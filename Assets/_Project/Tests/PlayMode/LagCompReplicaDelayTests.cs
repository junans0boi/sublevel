using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FishNet.Component.Transforming;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #396: how far behind the real body the copy of a strafing player is on another client, by NetworkTransform setting
    // (interpolation ticks, extrapolation ticks) and simulated round trip. A measurement, not a gate: writes a CSV and asserts
    // only that samples were taken. Run by name: Game.Tests.LagCompReplicaDelayTests
    [Category("Measure"), Explicit("measurement for docs/research/lagcomp_replica_delay.md; run by name")]
    public class LagCompReplicaDelayTests
    {
        const string LogDir = "Logs/net-multi";
        MultiHarness _net;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _net = new MultiHarness { Log = Debug.Log };
            yield return _net.StartHost("TestBox");
        }

        [UnityTearDown]
        public IEnumerator TearDown() { yield return _net.Teardown(); }

        static IEnumerator Frames(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        static float Percentile(List<float> sorted, float p) => sorted[Mathf.Clamp(Mathf.CeilToInt(p * sorted.Count) - 1, 0, sorted.Count - 1)];

        [UnityTest, Timeout(900000)]
        public IEnumerator CopyDelay_ByInterpolationExtrapolationAndRoundTrip()
        {
            RemoteClient a = null;
            yield return _net.AddClient(c => a = c);
            var hostCtl = _net.HostPlayer.GetComponent<PlayerController>();
            var seen = MultiHarness.ReplicaOf(a.Manager, _net.HostPlayer);
            var nt = seen.GetComponent<NetworkTransform>();
            Assert.IsNotNull(nt, "the copy has a NetworkTransform");
            // As LagCompPlayTests.Arm does before its duels: the client stands off in its lane, damage cannot end the run.
            _net.HostPlayer.ServerDamageFilter = _ => 0f;
            a.Controller.Teleport(new Vector3(-26f, 0.1f, -2f));
            a.Controller.LookAt(new Vector3(-17f, 1.7f, -2f));
            hostCtl.Teleport(new Vector3(-17f, 0.1f, -2f));
            hostCtl.LookAt(new Vector3(-26f, 1.7f, -2f));
            yield return Frames(3f);
            // The host cannot walk until its elevator has opened: push until it really moves, then put it back in the lane.
            var start = _net.HostPlayer.transform.position;
            _net.HostInput.Frame.Move = new Vector2(1f, 0f);
            Debug.Log($"host push: dead {_net.HostPlayer.Dead.Value} knocked {_net.HostPlayer.Knocked.Value} blocked {SpawnProtection.InputBlocked} input {PlayerController.InputOverride != null} last {hostCtl.LastInput.Move} enabled {hostCtl.isActiveAndEnabled}");
            yield return NetHarness.WaitUntil(() => (_net.HostPlayer.transform.position - start).magnitude > 1f, "host walks", Debug.Log, 20f);
            _net.HostInput.Frame.Move = Vector2.zero;
            hostCtl.Teleport(new Vector3(-17f, 0.1f, -2f));
            yield return Frames(1.5f);

            var csv = new StringBuilder("rtt_ms,interp,extrap,samples,mean_ms,p95_ms,max_ms,speed_mps\n");
            int configs = 0;
            foreach (long rtt in new long[] { 0, 100 })
            {
                if (rtt == 0) _net.ClearSimulation(a); else _net.Simulate(a, rtt, 0.0);
                yield return Frames(1.5f);
                foreach (ushort extrap in new ushort[] { 2, 0 })
                foreach (ushort interp in new ushort[] { 1, 2, 3 })
                {
                    nt.SetInterpolation(interp);
                    nt.SetExtrapolation(extrap);
                    yield return Frames(1f);
                    var delays = new List<float>();
                    float speedSum = 0f; int speedN = 0;
                    hostCtl.Teleport(new Vector3(-17f, 0.1f, -2f));
                    float dir = 1f;
                    for (int leg = 0; leg < 4; leg++)
                    {
                        _net.HostInput.Frame.Move = new Vector2(dir, 0f);
                        yield return Frames(0.7f); // the turn and the copy's catch-up are not sampled
                        // 0.1 s windows: at hundreds of frames a second one frame's step says nothing (the body moves in steps).
                        for (int w = 0; w < 10; w++)
                        {
                            var from = _net.HostPlayer.transform.position;
                            yield return Frames(0.1f);
                            var real = _net.HostPlayer.transform.position;
                            var step = real - from;
                            float v = step.magnitude / 0.1f;
                            if (v < 2f) continue; // a window at a standstill says nothing about the delay
                            speedSum += v; speedN++;
                            delays.Add(Vector3.Dot(real - seen.transform.position, step.normalized) / v * 1000f);
                        }
                        dir = -dir;
                    }
                    _net.HostInput.Frame.Move = Vector2.zero;
                    Assert.Greater(delays.Count, 10, "samples taken");
                    delays.Sort();
                    float mean = 0f; foreach (var d in delays) mean += d; mean /= delays.Count;
                    string row = $"{rtt},{interp},{extrap},{delays.Count},{mean:F0},{Percentile(delays, 0.95f):F0},{delays[delays.Count - 1]:F0},{speedSum / speedN:F2}";
                    csv.AppendLine(row);
                    Debug.Log("replica-delay " + row);
                    configs++;
                    yield return Frames(0.5f);
                }
            }
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "lagcomp-replica-delay.csv"), csv.ToString());
            Assert.AreEqual(12, configs);
        }
    }
}
