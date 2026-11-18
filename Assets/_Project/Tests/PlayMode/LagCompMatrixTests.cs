using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Game.Tests
{
    // #398 (S4): the acceptance matrix of docs: lag compensation under ping 0/50/80/100/150 ms, jitter +-20 ms and 1 % loss, against a
    // standing, a strafing (measured speed, ~4.7 m/s) and a 35 m strafing target, N repetitions each. One command runs it all:
    //   tools/unity.sh test playmode Game.Tests.LagCompMatrixTests
    // Environment: LAGCOMP_MODEL=before|after (before = the model of ADR-141: view 2 ticks, cap 0.25 s; default after = ADR-301/302),
    // LAGCOMP_CONDS=clean|all|<comma list of names> (default all), LAGCOMP_REPS (default 5), LAGCOMP_ALLOW_SHARED=1 (skip the
    // exclusive-window check). Output: Logs/net-multi/lagcomp-matrix-<model>.csv and .txt (PASS/FAIL table, run time).
    // Pass rule (spec Q6; the shots per scenario are scaled by the fractions): ping <= 80: static all hit, moving >= 90 %;
    // ping >= 100 (150 is the spec's one): static >= 95 %, moving >= 80 %. 35 m is judged like moving.
    [Category("Measure"), Explicit("the matrix takes ~45 min and needs an exclusive window; run by name")]
    public class LagCompMatrixTests
    {
        const string LogDir = "Logs/net-multi";
        const int Rifle = 3;

        struct Cond
        {
            public string Name; public long Rtt; public int JitterMs; public double Loss;
            public bool Tight => Rtt <= 80; // the spec's 0~80 ms tier; above it the looser 150 ms tier
        }

        static List<Cond> Conditions(string filter)
        {
            var all = new List<Cond>();
            foreach (long p in new long[] { 0, 50, 80, 100, 150 }) all.Add(new Cond { Name = "ping" + p, Rtt = p });
            foreach (long p in new long[] { 80, 150 }) all.Add(new Cond { Name = "ping" + p + "+jitter20", Rtt = p, JitterMs = 20 });
            foreach (long p in new long[] { 80, 150 }) all.Add(new Cond { Name = "ping" + p + "+loss1", Rtt = p, Loss = 0.01 });
            if (string.IsNullOrEmpty(filter) || filter == "all") return all;
            var keep = new List<Cond>();
            var names = new HashSet<string>(filter == "clean" ? new[] { "ping0", "ping50", "ping80", "ping100", "ping150" } : filter.Split(','));
            foreach (var c in all) if (names.Contains(c.Name)) keep.Add(c);
            return keep;
        }

        // Per-packet delay wander for the jitter conditions: Fish-Net's simulator has none, so the delay is redrawn every frame.
        sealed class JitterPump : MonoBehaviour
        {
            public FishNet.Managing.NetworkManager[] Managers = new FishNet.Managing.NetworkManager[0];
            public long OneWayMs; public int JitterMs;
            void Update()
            {
                foreach (var nm in Managers)
                    nm.TransportManager.LatencySimulator.SetLatency(Math.Max(0, OneWayMs + (long)Mathf.Round((UnityEngine.Random.value * 2f - 1f) * JitterMs)));
            }
        }

        MultiHarness _net;
        float _spreadBefore;
        WeaponDef _def;
        float _capBefore;
        int _viewDelayBefore;
        GameObject _pump;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _viewDelayBefore = LagComp.ViewDelayTicks;
            _capBefore = GameData.Rules.Ballistics.MaxRewindSeconds;
            _net = new MultiHarness { Log = Debug.Log };
            yield return _net.StartHost("TestBox");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LagComp.ViewDelayTicks = _viewDelayBefore;
            GameData.Rules.Ballistics.MaxRewindSeconds = _capBefore;
            LagCompensation.Probe = null;
            if (_def != null) _def.SpreadDeg = _spreadBefore;
            if (_pump != null) UnityEngine.Object.Destroy(_pump);
            yield return _net.Teardown();
        }

        static IEnumerator Frames(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        static string Inv(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

        // The server time at which the body stood where `copy` is (its recorded history): the time the copy shows.
        static bool TimeShownBy(PositionHistory h, Vector3 copy, out double time)
        {
            time = 0; float best = float.MaxValue;
            double newest = h.Newest;
            for (double t = newest; t > newest - 0.8; t -= 0.002)
            {
                if (!h.TryAt(t, out float x, out float y, out float z)) break;
                float d = new Vector2(x - copy.x, z - copy.z).sqrMagnitude;
                if (d < best) { best = d; time = t; }
            }
            return best < 0.5f * 0.5f;
        }

        static int OtherEditorsOrServers()
        {
            int n = 0;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string name = p.ProcessName;
                    if (name == "Unity" || name.StartsWith("Sublevel")) n++;
                }
                catch (InvalidOperationException) { } // exited while we looked
                finally { p.Dispose(); }
            }
            return n - 1; // this editor itself
        }

        [UnityTest, Timeout(7200000)]
        public IEnumerator Matrix()
        {
            string model = Environment.GetEnvironmentVariable("LAGCOMP_MODEL") ?? "after";
            Assert.IsTrue(model == "before" || model == "after", "LAGCOMP_MODEL is before or after");
            int reps = int.TryParse(Environment.GetEnvironmentVariable("LAGCOMP_REPS"), out int r) && r > 0 ? r : 5;
            var conds = Conditions(Environment.GetEnvironmentVariable("LAGCOMP_CONDS"));
            if (Environment.GetEnvironmentVariable("LAGCOMP_ALLOW_SHARED") != "1")
                Assert.AreEqual(0, OtherEditorsOrServers(), "another Unity or Sublevel process is running: measure in an exclusive window (LAGCOMP_ALLOW_SHARED=1 to override)");
            if (model == "before") { LagComp.ViewDelayTicks = LagComp.InterpolationTicks; GameData.Rules.Ballistics.MaxRewindSeconds = 0.25f; }
            else { LagComp.ViewDelayTicks = LagComp.InterpolationTicks + LagComp.ReplicaExtraTicks; GameData.Rules.Ballistics.MaxRewindSeconds = LagComp.MaxRewindSeconds; }

            var clock = Stopwatch.StartNew();
            RemoteClient a = null;
            yield return _net.AddClient(c => a = c);
            var hostCtl = _net.HostPlayer.GetComponent<PlayerController>();
            var seen = MultiHarness.ReplicaOf(a.Manager, _net.HostPlayer);
            _def = GameData.Weapons.Find(Rifle);
            _spreadBefore = _def.SpreadDeg; _def.SpreadDeg = 0f; // a miss is the compensation's, not the cone's
            var onServer = MultiHarness.ReplicaOf(_net.Host, a.Player);
            Assert.IsTrue(onServer.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 }));
            _net.HostPlayer.ServerDamageFilter = _ => 0f;
            onServer.ServerDamageFilter = _ => 0f;
            foreach (var name in new[] { "Cover10", "Cover11" }) { var cover = GameObject.Find(name); if (cover != null) cover.SetActive(false); } // 35 m lane
            yield return NetHarness.WaitUntil(() => a.Player.ActiveWeaponId == Rifle, "weapon in hand", Debug.Log, 10f);

            // Where each scenario stands: (shooter, target lane).
            var near = (shooter: new Vector3(-26f, 0.1f, -2f), lane: new Vector3(-17f, 0.1f, -2f));
            var far = (shooter: new Vector3(-27f, 0.1f, 21f), lane: new Vector3(8f, 0.1f, 21f));
            a.Controller.Teleport(near.shooter);
            a.Controller.LookAt(near.lane + Vector3.up * 1.6f);
            hostCtl.Teleport(near.lane);
            hostCtl.LookAt(near.shooter + Vector3.up * 1.6f);
            yield return NetHarness.WaitUntil(() => (onServer.transform.position - near.shooter).magnitude < 0.3f, "shooter synced", Debug.Log, 10f);
            // The host cannot walk for a while after the scene loads: push until it moves.
            var start = _net.HostPlayer.transform.position;
            _net.HostInput.Frame.Move = new Vector2(1f, 0f);
            yield return NetHarness.WaitUntil(() => (_net.HostPlayer.transform.position - start).magnitude > 1f, "host walks", Debug.Log, 30f);
            _net.HostInput.Frame.Move = Vector2.zero;
            a.Input.Frame.AimHeld = true;

            var history = _net.HostPlayer.GetComponent<PositionRecorder>().History;
            double probeNow = 0, probeAt = 0; int probeCalls = 0;
            LagCompensation.Probe = (shooter, now, at) => { if (shooter != onServer) return; probeNow = now; probeAt = at; probeCalls++; };
            var shotsCsv = new StringBuilder("model,condition,rep,scenario,shot,hit,model_ms,copy_delay_ms,dt_ms\n");
            string curCond = "", curScenario = ""; int curRep = 0, curShot = 0;
            bool resolved = false, hit = false;
            void On(in Combatant.ShotResult s) { if (s.Phase == Combatant.ShotPhase.Launch) return; resolved = true; hit = s.Victim != null; }
            a.Player.ShotResolved += On;

            // One aimed shot at what the shooter sees (compensated); reports hit/miss.
            IEnumerator Fire(Action<bool> done)
            {
                if (a.Player.ActiveMag < 3) { a.Player.CmdReload(); yield return Frames(3.5f); }
                resolved = false; probeCalls = 0;
                var copyPos = seen.transform.position;
                var aim = copyPos + Vector3.up * 0.9f;
                double callT = Time.timeAsDouble;
                a.Player.CmdFire((aim - a.Player.EyePosition).normalized, a.Player.ViewTick);
                float giveUp = Time.time + 3f;
                while (!resolved && Time.time < giveUp) yield return null;
                // The model the server used and how late the copy was (dt = rewound-to minus the time the copy showed, + = too little rewind).
                string extra = ",,";
                if (probeCalls > 0 && TimeShownBy(history, copyPos, out double tShown))
                    extra = $",{Inv((probeNow - probeAt) * 1000)},{Inv((callT - tShown) * 1000)},{Inv((probeAt - tShown) * 1000)}";
                else if (probeCalls > 0) extra = $",{Inv((probeNow - probeAt) * 1000)},,";
                shotsCsv.AppendLine($"{model},{curCond},{curRep},{curScenario},{curShot++},{(resolved && hit ? 1 : 0)}" + (extra == ",," ? ",,," : extra));
                done(resolved && hit);
            }

            var csv = new StringBuilder("model,condition,rtt_ms,jitter_ms,loss,status_ping_ms,rep,scenario,hits,shots,mean_speed_mps,pass\n");
            var summary = new StringBuilder();
            var failures = new List<string>();
            foreach (var cond in conds)
            {
                _net.ClearSimulation(a);
                if (_pump != null) { UnityEngine.Object.Destroy(_pump); _pump = null; }
                _net.Simulate(a, cond.Rtt, cond.Loss);
                if (cond.JitterMs > 0)
                {
                    _pump = new GameObject("JitterPump");
                    var pump = _pump.AddComponent<JitterPump>();
                    pump.Managers = new[] { a.Manager, _net.Host };
                    pump.OneWayMs = cond.Rtt / 2; pump.JitterMs = cond.JitterMs;
                }
                yield return Frames(2f);
                int status = NetStatusClient.Attach(a.Manager).PingMs;
                var results = new Dictionary<string, List<(int hits, int shots)>> { ["static"] = new(), ["moving"] = new(), ["far35"] = new() };
                for (int rep = 0; rep < reps; rep++)
                {
                    onServer.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 200 });
                    foreach (var scenario in new[] { "static", "moving", "far35" })
                    {
                        var place = scenario == "far35" ? far : near;
                        curCond = cond.Name; curScenario = scenario; curRep = rep; curShot = 0;
                        // Both bodies must stand where the test put them, on the server and in the shooter's copy, before a shot is fired:
                        // a shooter teleported back from 35 m was held at the old spot for seconds (every shot of the rep missed).
                        bool placed = false;
                        for (int attempt = 0; attempt < 3 && !placed; attempt++)
                        {
                            a.Controller.Teleport(place.shooter);
                            a.Controller.LookAt(place.lane + Vector3.up * 1.6f);
                            hostCtl.Teleport(place.lane);
                            hostCtl.LookAt(place.shooter + Vector3.up * 1.6f);
                            float until = Time.time + 6f;
                            while (Time.time < until && !((onServer.transform.position - place.shooter).magnitude < 0.3f && (seen.transform.position - place.lane).magnitude < 0.3f)) yield return null;
                            placed = (onServer.transform.position - place.shooter).magnitude < 0.3f && (seen.transform.position - place.lane).magnitude < 0.3f;
                            if (!placed) Debug.Log($"placement retry {attempt}: {cond.Name} {scenario} rep {rep}");
                        }
                        Assert.IsTrue(placed, "both bodies placed and synced");
                        yield return Frames(0.5f);
                        int shots = scenario == "far35" ? 10 : 20, hits = 0, taken = 0, speedN = 0; float speedSum = 0f;
                        if (scenario == "static")
                        {
                            for (; taken < shots; taken++) { bool h = false; yield return Fire(x => h = x); if (h) hits++; yield return Frames(0.4f); }
                        }
                        else
                        {
                            float dir = 1f;
                            int perLeg = scenario == "far35" ? 1 : 2; // the far lane is short (covers, walls): one shot per short leg
                            float settle = scenario == "far35" ? 0.5f : 0.6f;
                            while (taken < shots)
                            {
                                _net.HostInput.Frame.Move = new Vector2(dir, 0f);
                                yield return Frames(settle);
                                for (int k = 0; k < perLeg && taken < shots; k++)
                                {
                                    var from = _net.HostPlayer.transform.position;
                                    yield return Frames(0.1f);
                                    float speed = (_net.HostPlayer.transform.position - from).magnitude / 0.1f;
                                    if (speed > 2f) { speedSum += speed; speedN++; }
                                    bool h = false; yield return Fire(x => h = x);
                                    if (h) hits++;
                                    taken++;
                                    yield return Frames(0.3f);
                                }
                                dir = -dir;
                            }
                            _net.HostInput.Frame.Move = Vector2.zero;
                        }
                        results[scenario].Add((hits, shots));
                        float mean = speedN > 0 ? speedSum / speedN : 0f;
                        csv.AppendLine(string.Join(",", model, cond.Name, cond.Rtt, cond.JitterMs, Inv(cond.Loss), status, rep, scenario, hits, shots, Inv(mean), Passes(cond, scenario, hits, shots) ? 1 : 0));
                    }
                }
                foreach (var scenario in new[] { "static", "moving", "far35" })
                {
                    var list = results[scenario];
                    int pass = 0, h = 0, s = 0; int worst = int.MaxValue;
                    foreach (var (hits, shots) in list) { if (Passes(cond, scenario, hits, shots)) pass++; h += hits; s += shots; worst = Math.Min(worst, hits); }
                    string line = $"{model,-6} {cond.Name,-18} status-ping {status,3} ms  {scenario,-6} {pass}/{list.Count} reps pass, hits {h}/{s} ({100.0 * h / Math.Max(s, 1):F0}%), worst rep {worst}/{list[0].shots}";
                    summary.AppendLine(line); Debug.Log("matrix " + line);
                    if (pass < list.Count) failures.Add($"{cond.Name}/{scenario}");
                }
                Directory.CreateDirectory(LogDir);
                File.WriteAllText(Path.Combine(LogDir, $"lagcomp-matrix-{model}.csv"), csv.ToString());
                File.WriteAllText(Path.Combine(LogDir, $"lagcomp-matrix-shots-{model}.csv"), shotsCsv.ToString());
            }
            a.Player.ShotResolved -= On;
            summary.AppendLine($"\n{(failures.Count == 0 ? "PASS" : "FAIL")}: {failures.Count} of {conds.Count * 3} condition/scenario cells fail" + (failures.Count > 0 ? ": " + string.Join(", ", failures) : ""));
            summary.AppendLine($"elapsed {clock.Elapsed.TotalMinutes:F1} min, reps {reps}, model {model} (view delay {LagComp.ViewDelayTicks} ticks, cap {GameData.Rules.Ballistics.MaxRewindSeconds:F2} s)");
            File.WriteAllText(Path.Combine(LogDir, $"lagcomp-matrix-{model}.txt"), summary.ToString());
            Debug.Log("matrix summary\n" + summary);
        }

        // The spec's rule, by fractions so the 10 shot far scenario is judged alike: tight tier (ping <= 80): static all, moving 90 %;
        // looser tier: static 95 %, moving 80 %.
        static bool Passes(Cond c, string scenario, int hits, int shots)
        {
            double f = hits / (double)shots;
            if (scenario == "static") return c.Tight ? hits == shots : f >= 0.95;
            return c.Tight ? f >= 0.9 : f >= 0.8;
        }
    }
}
