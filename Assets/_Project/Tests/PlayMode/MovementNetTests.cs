using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #161, ADR-126: movement of a remote player at 100 ms round trip and 2 % loss, in the real TestBox match.
    // The owner's own body is its prediction (nothing to wait for); the server only puts back a body that went where
    // none can (MoveGuard), and everybody else sees it through Fish-Net's interpolation. Numbers go to Logs/net-multi.
    public class MovementNetTests
    {
        const string LogDir = "Logs/net-multi";
        // Thresholds (ADR-126), measured at 100 ms / 2 % loss with 2 remote clients.
        public const int MaxFalseCorrections = 0;
        public const float MaxFreezeSeconds = 0.20f;   // a body running at full speed seen standing still this long = a stall
        public const float MinSpeedShare = 0.55f;      // 0.1 s windows of a run slower than this share of the true speed are counted...
        public const float MaxSlowWindowShare = 0.03f; // ...and may be at most this share of all windows (a lost packet shows as a short stall)
        public const float MaxSpeedShare = 1.6f;       // and the fastest (a catch-up after a lost packet)

        MultiHarness _net;
        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();

        void Log(string line)
        {
            Debug.Log(line);
            _log.AppendLine(line);
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _log.Clear();
            MoveGuard.ResetStats();
            _net = new MultiHarness { Log = Log };
            yield return _net.StartHost("TestBox");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "move-" + TestContext.CurrentContext.Test.Name + ".log"), _log.ToString());
            yield return _net.Teardown();
        }

        // Every rendered frame of one body, as one observer sees it.
        sealed class Trace
        {
            public readonly List<(double t, Vector3 p)> S = new List<(double, Vector3)>();
            // Frames this slow are the machine stalling (another Unity, GC, a screenshot), not the network: windows over them are not judged.
            public const double HitchSeconds = 0.10;
            public readonly List<double> Hitches = new List<double>();
            public void Add(Transform t)
            {
                if (Time.unscaledDeltaTime > HitchSeconds) Hitches.Add(Time.timeAsDouble);
                S.Add((Time.timeAsDouble, t.position));
            }
            // A stall at time h delays what the observer samples until about h + HitchSeconds * 2.
            public bool NearHitch(double t0, double t1) => Hitches.Exists(h => h >= t0 - 0.25 && h <= t1 + 0.25);

            Vector3 At(double t)
            {
                for (int i = 1; i < S.Count; i++)
                    if (S[i].t >= t) { float k = (float)((t - S[i - 1].t) / Math.Max(1e-6, S[i].t - S[i - 1].t)); return Vector3.Lerp(S[i - 1].p, S[i].p, Mathf.Clamp01(k)); }
                return S[S.Count - 1].p;
            }

            // Flat speed over 0.1 s windows that start where `use` says.
            public List<float> Speeds(double t0, double t1, Func<double, bool> use)
            {
                var list = new List<float>();
                for (double t = t0; t + 0.1 <= t1; t += 0.05)
                {
                    if (!use(t) || NearHitch(t, t + 0.1)) continue;
                    var d = At(t + 0.1) - At(t);
                    d.y = 0f;
                    list.Add(d.magnitude / 0.1f);
                }
                return list;
            }
        }

        // The test box's training dummies shoot whoever they see; a runner they kill stops running.
        IEnumerator AddRunner(Action<RemoteClient> done)
        {
            RemoteClient c = null;
            yield return _net.AddClient(x => c = x);
            MultiHarness.ReplicaOf(_net.Host, c.Player).ServerDamageFilter = _ => 0f;
            done(c);
        }

        static IEnumerator Frames(float seconds, Action each)
        {
            float end = Time.time + seconds;
            while (Time.time < end) { each?.Invoke(); yield return null; }
        }

        // Runs back and forth on a clear stretch of the floor at `speed` (sprint), recording what `observers` see of `body`.
        readonly List<double> _legStarts = new List<double>();
        const float LegSeconds = 2f, TurnSettle = 0.8f; // a turn round takes a stop and a restart: not network, so not measured

        bool OnALeg(double t)
        {
            foreach (var s in _legStarts)
                if (t >= s + TurnSettle && t + 0.1 <= s + LegSeconds) return true;
            return false;
        }

        IEnumerator Sprint(RemoteClient mover, float seconds, Action each)
        {
            var from = new Vector3(-27f, 0.1f, -2f);
            var to = new Vector3(-13f, 0.1f, -2f);
            mover.Controller.Teleport(from);
            yield return Frames(0.5f, each);
            bool forward = true;
            float end = Time.time + seconds, legEnd = 0f;
            while (Time.time < end)
            {
                if (Time.time >= legEnd)
                {
                    mover.Controller.LookAt((forward ? to : from) + Vector3.up * 1.6f);
                    forward = !forward;
                    legEnd = Time.time + LegSeconds;
                    _legStarts.Add(Time.timeAsDouble);
                }
                mover.Input.Frame.Move = new Vector2(0, 1);
                mover.Input.Frame.Sprint = true;
                each?.Invoke();
                yield return null;
            }
            mover.Input.Frame.Move = Vector2.zero;
            mover.Input.Frame.Sprint = false;
        }

        [UnityTest]
        public IEnumerator LegitMovement_At100msAnd2Percent_NeedsNoCorrection_AndSeenSmoothlyByAnotherClient()
        {
            RemoteClient a = null, b = null;
            yield return AddRunner(c => a = c);
            yield return AddRunner(c => b = c);
            _net.Simulate(a);
            _net.Simulate(b);
            var aOnB = MultiHarness.ReplicaOf(b.Manager, a.Player);
            var aOnHost = MultiHarness.ReplicaOf(_net.Host, a.Player);
            var seenByB = new Trace();
            var seenByHost = new Trace();
            double runFrom = 0, runTo = 0;

            // Sprinting back and forth.
            yield return Sprint(a, 0.1f, null); // positions it
            runFrom = Time.timeAsDouble + 1.0; // after the ramp and the first turn settles
            yield return Sprint(a, 8f, () => { seenByB.Add(aOnB.transform); seenByHost.Add(aOnHost.transform); });
            runTo = Time.timeAsDouble;

            // Jump, crouch, lie down, stand: all of them while walking, all on the same body.
            a.Input.Frame.Move = new Vector2(0, 1);
            a.Input.Frame.Jump = true;
            yield return Frames(1f, null);
            a.Input.Frame.Crouch = true; yield return null;
            yield return Frames(1.5f, null);
            a.Input.Frame.Prone = true; yield return null;
            yield return Frames(1.5f, null);
            a.Input.Frame.Jump = true; yield return null; // gets up
            yield return Frames(1f, null);
            a.Input.Frame.Move = Vector2.zero;
            yield return Frames(1f, null);

            // Both observers saw the run; numbers for the log and the thresholds.
            foreach (var (name, trace) in new[] { ("client B", seenByB), ("host", seenByHost) })
            {
                var speeds = trace.Speeds(runFrom, runTo, OnALeg);
                float truth = GameData.Rules.Locomotion.SprintSpeed;
                float min = float.MaxValue, max = 0f, sum = 0f; int slow = 0, longest = 0, run = 0;
                foreach (var v in speeds)
                {
                    min = Mathf.Min(min, v); max = Mathf.Max(max, v); sum += v;
                    bool stand = v < 0.5f;
                    run = stand ? run + 1 : 0;
                    if (stand) slow++;
                    longest = Mathf.Max(longest, run);
                }
                // Windows overlap by half, so a freeze of n windows lasts about (n + 1) * 0.05 s.
                float freeze = longest > 0 ? (longest + 1) * 0.05f : 0f;
                Log($"{name}: {trace.Hitches.Count} frames over {Trace.HitchSeconds * 1000:F0} ms (windows near them are skipped)");
                Log($"{name}: sprint {truth:F1} m/s true, windows {speeds.Count}, speed min {min:F2} mean {sum / speeds.Count:F2} max {max:F2}, stand-still windows {slow}, longest freeze {freeze:F2} s");
                Assert.LessOrEqual(freeze, MaxFreezeSeconds, name + " saw the runner freeze");
                int slowWindows = speeds.FindAll(v => v < MinSpeedShare * truth).Count;
                Log($"{name}: windows under {MinSpeedShare:P0} of the speed: {slowWindows} of {speeds.Count}");
                Assert.LessOrEqual(slowWindows, Mathf.CeilToInt(MaxSlowWindowShare * speeds.Count), name + " slow windows");
                Assert.Less(max, MaxSpeedShare * truth, name + " fastest window");
            }
            Log($"server corrections {MoveGuard.Corrections}, max error {MoveGuard.MaxError:F2} m");
            Assert.LessOrEqual(MoveGuard.Corrections, MaxFalseCorrections, "legit movement is never put back");
        }

        [UnityTest]
        public IEnumerator ABodyThatJumps100MetresByItself_IsPutBackByTheServer()
        {
            RemoteClient a = null;
            yield return AddRunner(c => a = c);
            _net.Simulate(a);
            a.Controller.Teleport(new Vector3(-20f, 0.1f, -2f));
            yield return Frames(1f, null);
            var at = a.Player.transform.position;
            Assert.AreEqual(0, MoveGuard.Corrections);

            // A cheat: move the body without telling anyone (Teleport would tell the server).
            var cc = a.Player.GetComponent<CharacterController>();
            cc.enabled = false;
            a.Player.transform.position = at + new Vector3(0f, 0f, 55f);
            cc.enabled = true;
            yield return Frames(2.5f, null);

            Log($"corrections {MoveGuard.Corrections}, max error {MoveGuard.MaxError:F1} m, body now {(a.Player.transform.position - at).magnitude:F2} m from where it was");
            Assert.AreEqual(1, MoveGuard.Corrections, "put back once, not again while it returns");
            Assert.Greater(MoveGuard.MaxError, 50f);
            Assert.Less((a.Player.transform.position - at).magnitude, 3f, "back where it was");
            Assert.Less((MultiHarness.ReplicaOf(_net.Host, a.Player).transform.position - at).magnitude, 3f, "and the server agrees");
        }

        [UnityTest]
        public IEnumerator ADeliberateTeleport_IsNotACheat()
        {
            RemoteClient a = null;
            yield return AddRunner(c => a = c);
            _net.Simulate(a);
            a.Controller.Teleport(new Vector3(-20f, 0.1f, -2f));
            yield return Frames(1f, null);
            a.Controller.Teleport(new Vector3(20f, 0.1f, 20f));
            yield return Frames(1.5f, null);
            Assert.AreEqual(0, MoveGuard.Corrections);
            Assert.Less((a.Player.transform.position - new Vector3(20f, 0.1f, 20f)).magnitude, 1f);
        }

        [UnityTest]
        public IEnumerator TheHostsOwnBody_IsNeverWatched()
        {
            Assert.IsNull(_net.HostPlayer.GetComponent<MoveGuard>());
            yield return null;
        }
    
        // The training range's stairs and swimming pit with a remote client at 100 ms / 2 %: a climb and swimming are not cheats.
        [UnityTest, Timeout(120000)]
        public IEnumerator StairsAndSwimming_At100ms_NeedNoCorrection()
        {
            yield return _net.Teardown();
            _net = new MultiHarness { Log = Log };
            yield return _net.StartHost("TrainingRange");
            MoveGuard.ResetStats();
            RemoteClient a = null;
            yield return AddRunner(c => a = c);
            _net.Simulate(a);
            var pc = a.Controller;
            pc.Teleport(new Vector3(50.5f, 0.1f, -14f));
            pc.LookAt(new Vector3(60f, 1.6f, -14f));
            yield return Frames(0.5f, null);
            a.Input.Frame.Move = new Vector2(0, 1);
            float end = Time.time + 6f;
            while (pc.transform.position.y < 1.4f && Time.time < end) yield return null;
            Assert.Greater(pc.transform.position.y, 1.4f, "up the stairs");

            pc.Teleport(new Vector3(75f, 0.5f, -14f));
            pc.LookAt(new Vector3(95f, 1.6f, -14f));
            end = Time.time + 4f;
            while (!pc.Swimming && Time.time < end) yield return null;
            Assert.IsTrue(pc.Swimming, "swimming");
            end = Time.time + 25f;
            while ((pc.Swimming || pc.transform.position.x < 93f) && Time.time < end) yield return null;
            Assert.IsFalse(pc.Swimming, "out of the pit");
            a.Input.Frame.Move = Vector2.zero;
            yield return Frames(1f, null);
            Log($"stairs + swim: corrections {MoveGuard.Corrections}, max error {MoveGuard.MaxError:F2} m");
            Assert.AreEqual(0, MoveGuard.Corrections);
        }
    }
}
