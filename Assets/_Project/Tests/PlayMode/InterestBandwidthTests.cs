using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FishNet.Transporting;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #167 (ADR-135): bytes per second the server sends each remote client in the World with 20 bots, interest management
    // off (everything sent, as before) and on. The clients stand where the bots are, spread over the map. The count of
    // clients is INTEREST_BW_CLIENTS (default 6; one process cannot hold 16 with their replicas); docs/PERF_BUDGET.md
    // extrapolates to 16 humans + 20 bots. Counted: payload bytes the client's transport received (no UDP/IP headers).
    [Category("Bandwidth")]
    public class InterestBandwidthTests
    {
        const string LogDir = "Logs/net-interest";
        const int Bots = 20;
        static readonly int Humans = int.TryParse(Environment.GetEnvironmentVariable("INTEREST_BW_CLIENTS"), out int n) ? n : 6;
        const float SettleSeconds = 6f, MeasureSeconds = 30f;

        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();
        MultiHarness _net;
        BotRules _botRules;
        MatchRules _matchRules;
        InterestRules _interest;

        void Log(string line) { Debug.Log(line); _log.AppendLine(line); }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _log.Clear();
            _botRules = BotRules.Current;
            _matchRules = MatchRules.Current;
            _interest = InterestRules.Current;
            BotRules.Current = new BotRules { TargetsPlayers = false }; // the clients must live: a dead client spectates and is sent everything
            MatchRules.Current = new MatchRules { BotCount = Bots, MaxPlayers = 16 };
            _net = new MultiHarness { Log = Log };
            yield return _net.StartHost("World");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "bandwidth.log"), _log.ToString());
            BotRules.Current = _botRules;
            MatchRules.Current = _matchRules;
            InterestRules.Current = _interest;
            yield return _net.Teardown();
        }

        static IEnumerator Seconds(float s)
        {
            float end = Time.time + s;
            while (Time.time < end) yield return null;
        }

        sealed class Meter
        {
            public long Bytes;
        }

        static int Seen<T>(RemoteClient c) where T : Component
        {
            int count = 0;
            foreach (var nob in c.Manager.ClientManager.Objects.Spawned.Values)
                if (nob != null && nob.GetComponent<T>() != null) count++;
            return count;
        }

        IEnumerator Measure(string label, bool enabled, List<Meter> meters, Action<string> result)
        {
            InterestRules.Current = new InterestRules
            {
                Enabled = enabled,
                CombatantMeters = _interest.CombatantMeters, VehicleMeters = _interest.VehicleMeters,
                LootMeters = _interest.LootMeters, ThrownMeters = _interest.ThrownMeters, HideExtraFraction = _interest.HideExtraFraction,
            };
            yield return Seconds(SettleSeconds);
            foreach (var m in meters) m.Bytes = 0;
            float t0 = Time.time;
            yield return Seconds(MeasureSeconds);
            float seconds = Time.time - t0;
            double total = 0;
            int combatants = 0, loot = 0, vehicles = 0;
            for (int i = 0; i < _net.Clients.Count; i++)
            {
                total += meters[i].Bytes / seconds;
                combatants += Seen<Combatant>(_net.Clients[i]);
                loot += Seen<LootItem>(_net.Clients[i]);
                vehicles += Seen<Vehicle>(_net.Clients[i]);
            }
            int n = _net.Clients.Count;
            string line = $"{label}: {total / n:F0} B/s per client (average of {n}), sees on average {combatants / (float)n:F1} combatants, {loot / (float)n:F0} loot items, {vehicles / (float)n:F1} vehicles; window {seconds:F1} s";
            Log(line);
            result(line + $"|{total / n:F0}");
        }

        [UnityTest, Timeout(10 * 60 * 1000)]
        public IEnumerator BytesPerSecondPerClient_WithAndWithoutInterestManagement()
        {
            var meters = new List<Meter>();
            for (int i = 0; i < Humans; i++)
            {
                var meter = new Meter();
                meters.Add(meter);
                yield return _net.AddClient(null, null, nm => nm.TransportManager.Transport.OnClientReceivedData += (ClientReceivedDataArgs a) => meter.Bytes += a.Data.Count);
            }
            yield return NetHarness.WaitUntil(() => !SpawnProtection.InputBlocked, "elevator doors open", Log, 90f);
            yield return Seconds(8f); // the bots scatter

            // Put each client where a bot is, picking bots far from the ones already taken (spread over the map).
            var bots = new List<Combatant>();
            foreach (var c in Combatant.All) if (c != null && c.IsServerInitialized && !c.IsPlayer) bots.Add(c);
            Assert.GreaterOrEqual(bots.Count, Bots - 1, "the bots are there");
            var taken = new List<Vector3>();
            var players = new List<Combatant> { _net.HostPlayer };
            foreach (var c in _net.Clients) players.Add(c.Player);
            foreach (var p in players)
            {
                Combatant best = null; float bestScore = -1f;
                foreach (var b in bots)
                {
                    float score = float.MaxValue;
                    foreach (var t in taken) score = Mathf.Min(score, Vector3.Distance(t, b.transform.position));
                    if (taken.Count == 0) score = 0f;
                    if (score > bestScore) { bestScore = score; best = b; }
                }
                taken.Add(best.transform.position);
                var spot = best.transform.position + new Vector3(3f, 0.2f, 0f);
                (p == _net.HostPlayer ? _net.HostPlayer.GetComponent<PlayerController>() : _net.Clients.Find(c => c.Player == p).Controller).Teleport(spot);
            }
            Log($"{players.Count} humans + {bots.Count} bots in World, clients spread over the map");

            string off = null, on = null;
            yield return Measure("interest OFF", false, meters, s => off = s);
            yield return Measure("interest ON ", true, meters, s => on = s);
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "bandwidth.json"),
                "{\"humans\":" + players.Count + ",\"bots\":" + bots.Count + ",\"off\":\"" + off + "\",\"on\":\"" + on + "\"}");
            double offBps = double.Parse(off.Split('|')[1]), onBps = double.Parse(on.Split('|')[1]);
            Assert.Less(onBps, offBps * 0.8, "interest management saves at least 20 % of the traffic per client");
        }
    }
}
