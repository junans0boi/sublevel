using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FishNet.Object;
using FishNet.Transporting;
using Game.Core;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #423 grilling: what moving cars on the waiting island cost while people wait there. Host + RACE_COST_CLIENTS remote clients
    // (default 3) on the island of the World scene, interest management on (as in a real match). Phases of 0, 6 and 12 cars
    // moving at race speed (25 m/s); per phase: payload bytes each remote client receives per second and the host's frame time.
    // The cars are moved by the server (no driver), which sends the same NetworkTransform updates a driver's copy would.
    [Category("Bandwidth")]
    public class RaceSyncCostTests
    {
        const string LogDir = "Logs/net-race";
        static readonly int Humans = int.TryParse(Environment.GetEnvironmentVariable("RACE_COST_CLIENTS"), out int n) ? n : 3;
        const float SettleSeconds = 4f, MeasureSeconds = 20f, Speed = 25f;

        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();
        MultiHarness _net;
        bool _oldFlag;
        InterestRules _interest;

        void Log(string line) { Debug.Log(line); _log.AppendLine(line); }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _oldFlag = MatchRules.Current.UseWaitingIsland;
            _interest = InterestRules.Current;
            MatchRules.Current.UseWaitingIsland = true;
            WaitingRoom.ForceEnabled = true;
            DedicatedServer.StartSecondsOverride = 600f;
            _net = new MultiHarness { Log = Log };
            yield return _net.StartHost("World");
            yield return NetHarness.WaitUntil(() => WaitingRoom.Current != null, "waiting room open", Log, 60f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "sync_cost.log"), _log.ToString());
            MatchRules.Current.UseWaitingIsland = _oldFlag;
            WaitingRoom.ForceEnabled = false;
            DedicatedServer.StartSecondsOverride = -1f;
            yield return _net.Teardown();
            InterestRules.Current = _interest;
        }

        sealed class Meter { public long Bytes; }

        // Moves the server copy round a 20 m circle 30 m over the deck (nobody under it: no roadkill), after physics.
        [DefaultExecutionOrder(10000)]
        sealed class Circler : MonoBehaviour
        {
            public float Phase, Y;
            void LateUpdate()
            {
                var body = GetComponent<Rigidbody>(); body.isKinematic = true;
                float a = Phase + Time.time * Speed / 20f;
                transform.SetPositionAndRotation(new Vector3(WaitingIsland.CentreX + Mathf.Cos(a) * 20f, Y, WaitingIsland.CentreZ + Mathf.Sin(a) * 20f),
                    Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a))));
            }
        }

        IEnumerator Phase(string label, List<Meter> meters, List<string> rows, int cars)
        {
            float t = Time.realtimeSinceStartup + SettleSeconds;
            while (Time.realtimeSinceStartup < t) yield return null;
            foreach (var m in meters) m.Bytes = 0;
            var frames = new List<float>();
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < MeasureSeconds) { frames.Add(Time.unscaledDeltaTime * 1000f); yield return null; }
            float seconds = Time.realtimeSinceStartup - t0;
            double total = 0; foreach (var m in meters) total += m.Bytes / seconds;
            frames.Sort();
            float mean = 0f; foreach (var f in frames) mean += f; mean /= Mathf.Max(1, frames.Count);
            float p99 = frames.Count > 0 ? frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.99f))] : 0f;
            int seen = 0; foreach (var c in _net.Clients) foreach (var nob in c.Manager.ClientManager.Objects.Spawned.Values) if (nob != null && nob.GetComponent<Vehicle>() != null) seen++;
            string line = $"{label}: cars {cars}, {total / meters.Count:F0} B/s per client (avg of {meters.Count}), clients see {seen / (float)meters.Count:F1} vehicles, host frame mean {mean:F2} ms p99 {p99:F2} ms ({frames.Count} frames)";
            Log(line);
            rows.Add($"{{\"cars\":{cars},\"bytesPerClient\":{total / meters.Count:F0},\"frameMeanMs\":{mean:F2},\"frameP99Ms\":{p99:F2}}}");
        }

        [UnityTest, Explicit, Timeout(10 * 60 * 1000)]
        public IEnumerator BytesPerClient_AndFrameTime_WithRaceCars()
        {
            var meters = new List<Meter>();
            for (int i = 0; i < Humans; i++)
            {
                var meter = new Meter(); meters.Add(meter);
                yield return _net.AddClient(null, null, nm => nm.TransportManager.Transport.OnClientReceivedData += (ClientReceivedDataArgs a) => meter.Bytes += a.Data.Count);
            }
            // As in a real match: far objects (the map 1.8 km away) are not sent.
            InterestRules.Current = _interest;
            Assert.IsTrue(InterestRules.Current.Enabled, "interest management is on in the data");

            var spawner = VehicleSpawner.Instance;
            Assert.IsNotNull(spawner, "the World has a vehicle spawner");
            var carPrefabs = spawner.CarPrefabs();
            Assert.IsNotEmpty(carPrefabs, "car models");
            float y = GameObject.Find("WaitingIsland").transform.position.y + 30f;

            var rows = new List<string>();
            yield return Phase("no cars ", meters, rows, 0);
            var spawned = new List<NetworkObject>();
            foreach (int target in new[] { 6, 12 })
            {
                while (spawned.Count < target)
                {
                    var car = UnityEngine.Object.Instantiate(carPrefabs[spawned.Count % carPrefabs.Count], new Vector3(WaitingIsland.CentreX, y, WaitingIsland.CentreZ), Quaternion.identity);
                    var c = car.gameObject.AddComponent<Circler>(); c.Phase = spawned.Count * Mathf.PI * 2f / 12f; c.Y = y;
                    _net.Host.ServerManager.Spawn(car);
                    spawned.Add(car);
                }
                yield return Phase($"{target} cars", meters, rows, target);
            }
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "sync_cost.json"), "{\"clients\":" + Humans + ",\"speed\":" + Speed + ",\"phases\":[" + string.Join(",", rows) + "]}");
            foreach (var car in spawned) if (car != null) _net.Host.ServerManager.Despawn(car);
        }
    }
}
