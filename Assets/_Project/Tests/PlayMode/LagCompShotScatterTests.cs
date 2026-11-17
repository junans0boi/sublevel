using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FishNet.Component.Transforming;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #401 (S2b): why a lag-compensated shot at a strafing player hits in one run and misses in the next. For every shot the
    // test writes one CSV row: what the shooter's copy showed (time the copy was at), what the server rewound to, the body's
    // real speed, and where the bullet passed the rewound body. tools/lagcomp_scatter.py reads the CSV and splits the scatter.
    // Zero spread, 9 m, so every miss is the time model's. Run by name: Game.Tests.LagCompShotScatterTests
    [Category("Measure"), Explicit("measurement for docs/research/lagcomp_shot_scatter.md; run by name")]
    public class LagCompShotScatterTests
    {
        const string LogDir = "Logs/net-multi";
        const int Rifle = 3;
        // Test knobs. The environment variables LAGCOMP_REPS / LAGCOMP_SHOTS / LAGCOMP_RTTS ("0,100,150") override them for a short check.
        static int Env(string name, int fallback) => int.TryParse(System.Environment.GetEnvironmentVariable(name), out int v) && v > 0 ? v : fallback;
        MultiHarness _net;
        float _spreadBefore;
        WeaponDef _def;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _net = new MultiHarness { Log = Debug.Log };
            yield return _net.StartHost("TestBox");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LagCompensation.Probe = null;
            if (_def != null) _def.SpreadDeg = _spreadBefore;
            yield return _net.Teardown();
        }

        static IEnumerator Frames(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        static string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);

        // The server time at which the body stood where `copy` is (its history is the server's own): the time the copy shows.
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
            return best < 0.5f * 0.5f; // the copy is somewhere on the path
        }

        [UnityTest, Timeout(3600000)]
        public IEnumerator ShotByShot_AtAStrafingTarget()
        {
            int reps = Env("LAGCOMP_REPS", 5), shotsPerRep = Env("LAGCOMP_SHOTS", 12);
            var rtts = new List<long>();
            foreach (var t in (System.Environment.GetEnvironmentVariable("LAGCOMP_RTTS") ?? "0,100,150").Split(',')) rtts.Add(long.Parse(t));

            RemoteClient a = null;
            yield return _net.AddClient(c => a = c);
            var hostCtl = _net.HostPlayer.GetComponent<PlayerController>();
            var seen = MultiHarness.ReplicaOf(a.Manager, _net.HostPlayer);
            var nt = seen.GetComponent<NetworkTransform>();
            var history = _net.HostPlayer.GetComponent<PositionRecorder>().History;
            _def = GameData.Weapons.Find(Rifle);
            _spreadBefore = _def.SpreadDeg; _def.SpreadDeg = 0f;
            var onServer = MultiHarness.ReplicaOf(_net.Host, a.Player);
            Assert.IsTrue(onServer.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 }));
            Assert.IsTrue(onServer.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 400 }));
            _net.HostPlayer.ServerDamageFilter = _ => 0f;
            onServer.ServerDamageFilter = _ => 0f;
            var shooterAt = new Vector3(-26f, 0.1f, -2f);
            var lane = new Vector3(-17f, 0.1f, -2f);
            a.Controller.Teleport(shooterAt);
            a.Controller.LookAt(lane + Vector3.up * 1.6f);
            hostCtl.Teleport(lane);
            hostCtl.LookAt(shooterAt + Vector3.up * 1.6f);
            yield return NetHarness.WaitUntil(() => a.Player.ActiveWeaponId == Rifle, "weapon in hand", Debug.Log, 10f);
            yield return NetHarness.WaitUntil(() => (onServer.transform.position - shooterAt).magnitude < 0.3f, "shooter synced", Debug.Log, 10f);
            // The host cannot walk for a while after the match scene loads; push until it moves.
            var start = _net.HostPlayer.transform.position;
            _net.HostInput.Frame.Move = new Vector2(1f, 0f);
            yield return NetHarness.WaitUntil(() => (_net.HostPlayer.transform.position - start).magnitude > 1f, "host walks", Debug.Log, 20f);
            _net.HostInput.Frame.Move = Vector2.zero;
            a.Input.Frame.AimHeld = true;

            // One Rewind call per shot (9 m is inside the instant range): its (server now, rewound-to) pair.
            double probeNow = 0, probeAt = 0; uint probeTick = 0; int probeCalls = 0;
            LagCompensation.Probe = (shooter, now, at) => { if (shooter != onServer) return; probeNow = now; probeAt = at; probeTick = _net.Host.TimeManager.Tick; probeCalls++; };
            bool resolvedFlag = false, hit = false; Vector3 origin = default, end = default;
            void On(in Combatant.ShotResult s) { if (s.Phase == Combatant.ShotPhase.Launch) return; resolvedFlag = true; hit = s.Victim != null; origin = s.Origin; end = s.End; }
            a.Player.ShotResolved += On;

            var csv = new StringBuilder("rtt_ms,interp,rep,shot,hit,speed_mps,call_t,exec_t,rewound_to,copy_shown_t,view_tick,tick_call,tick_exec,call_to_exec_ms,model_ms,copy_delay_ms,dt_ms,along_ms,lateral_m,line_miss_m\n");
            int total = 0, taken = 0;
            foreach (long rtt in rtts)
            {
                if (rtt == 0) _net.ClearSimulation(a); else _net.Simulate(a, rtt, 0.0);
                yield return Frames(1.5f);
                foreach (ushort interp in new ushort[] { 1, 2, 3 })
                {
                    nt.SetInterpolation(interp);
                    onServer.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 200 });
                    yield return Frames(1f);
                    for (int rep = 0; rep < reps; rep++)
                    {
                        hostCtl.Teleport(lane);
                        yield return Frames(0.4f);
                        float dir = 1f; int shot = 0;
                        while (shot < shotsPerRep)
                        {
                            _net.HostInput.Frame.Move = new Vector2(dir, 0f);
                            yield return Frames(0.6f); // up to a steady strafe
                            for (int k = 0; k < 2 && shot < shotsPerRep; k++)
                            {
                                if (a.Player.ActiveMag < 3) { a.Player.CmdReload(); yield return Frames(3.5f); _net.HostInput.Frame.Move = new Vector2(dir, 0f); }
                                var from = _net.HostPlayer.transform.position;
                                yield return Frames(0.1f);
                                var real = _net.HostPlayer.transform.position;
                                float speed = (real - from).magnitude / 0.1f;
                                var copyPos = seen.transform.position;
                                var eye = a.Player.EyePosition;
                                uint view = a.Player.ViewTick;
                                uint tickCall = _net.Host.TimeManager.Tick;
                                double callT = Time.timeAsDouble;
                                resolvedFlag = false; probeCalls = 0;
                                var aim = copyPos + Vector3.up * 0.9f;
                                a.Player.CmdFire((aim - eye).normalized, view);
                                float giveUp = Time.time + 3f;
                                while (!resolvedFlag && Time.time < giveUp) yield return null;
                                total++;
                                if (!resolvedFlag) { Debug.Log($"shot {shot} not resolved (mag {a.Player.ActiveMag}): skipped"); shot++; yield return Frames(0.4f); continue; }
                                bool moving = speed > 2f;
                                double tShown = 0;
                                bool shown = moving && TimeShownBy(history, copyPos, out tShown);
                                bool probed = probeCalls > 0;
                                if (moving && shown && probed)
                                {
                                    taken++;
                                    history.TryAt(probeAt, out float rx, out float ry, out float rz);
                                    var rewound = new Vector3(rx, ry, rz);
                                    var motion = (real - from).normalized;
                                    var err = rewound - copyPos; // where the server judged the body minus where the shooter saw it
                                    float along = Vector3.Dot(err, motion), lateral = (err - motion * along).magnitude;
                                    var ray = (end - origin).normalized;
                                    float lineMiss = Vector3.Cross(ray, rewound + Vector3.up * 0.9f - origin).magnitude;
                                    csv.AppendLine(string.Join(",", rtt, interp, rep, shot, hit ? 1 : 0, F(speed), F(callT), F(probeNow), F(probeAt), F(tShown), view, tickCall, probeTick,
                                        F((probeNow - callT) * 1000), F((probeNow - probeAt) * 1000), F((callT - tShown) * 1000), F((probeAt - tShown) * 1000), F(along / speed * 1000), F(lateral), F(lineMiss)));
                                }
                                shot++;
                                yield return Frames(0.4f);
                            }
                            dir = -dir;
                        }
                        _net.HostInput.Frame.Move = Vector2.zero;
                    }
                    Debug.Log($"scatter rtt {rtt} interp {interp}: {taken}/{total} shots logged so far");
                    Directory.CreateDirectory(LogDir);
                    File.WriteAllText(Path.Combine(LogDir, "lagcomp-shot-scatter.csv"), csv.ToString());
                }
            }
            _net.HostInput.Frame.Move = Vector2.zero;
            a.Player.ShotResolved -= On;
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "lagcomp-shot-scatter.csv"), csv.ToString());
            Assert.Greater(taken, total / 2, "most shots were logged with a rewind and a moving target");
        }
    }
}
