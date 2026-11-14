using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #162, ADR-141: a remote client aims at what it SEES of a strafing player (the copy it holds, ~120 ms behind the
    // real one at 100 ms round trip) and the server judges the shot against where that player stood then.
    public class LagCompPlayTests
    {
        const string LogDir = "Logs/net-multi";
        const int Rifle = 3;
        MultiHarness _net;
        readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();

        void Log(string line) { Debug.Log(line); _log.AppendLine(line); }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _log.Clear();
            _net = new MultiHarness { Log = Log };
            yield return _net.StartHost("TestBox");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "lagcomp-" + TestContext.CurrentContext.Test.Name + ".log"), _log.ToString());
            yield return _net.Teardown();
        }

        static IEnumerator Frames(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        // The host player strafes across A's sights; A shoots `shots` times at the copy it sees. Returns hits seen by A.
        IEnumerator Duel(RemoteClient a, int shots, bool compensated, Vector3 targetAt, System.Action<int> result, float gap = 0f)
        {
            var hostCtl = _net.HostPlayer.GetComponent<PlayerController>();
            var seen = MultiHarness.ReplicaOf(a.Manager, _net.HostPlayer);
            int hits = 0;
            int resolved = 0;
            void On(in Combatant.ShotResult s) { if (s.Phase == Combatant.ShotPhase.Launch) return; resolved++; if (s.Victim != null) hits++; else Log($"shot ended on {s.Hit} at {s.End} (target seen at {seen.transform.position}, real {_net.HostPlayer.transform.position})"); }
            a.Player.ShotResolved += On;
            a.Input.Frame.AimHeld = true;
            hostCtl.Teleport(targetAt);
            hostCtl.LookAt(a.Player.transform.position + Vector3.up * 1.6f);
            yield return Frames(1f);
            float dir = 1f, lastShot = -99f;
            for (int i = 0; i < shots; i++)
            {
                do
                {
                    _net.HostInput.Frame.Move = new Vector2(dir, 0f);
                    yield return Frames(0.5f); // up to a steady strafe (short legs: the target stays in the clear lane)
                    if (Time.time - lastShot >= gap) break;
                    dir = -dir;
                } while (true);
                lastShot = Time.time;
                var eye = a.Player.EyePosition;
                var aim = seen.transform.position + Vector3.up * 0.9f;
                uint view = compensated ? a.Player.ViewTick : uint.MaxValue; // MaxValue: a claim from the future = no rewind
                a.Player.CmdFire((aim - eye).normalized, view);
                dir = -dir;
            }
            _net.HostInput.Frame.Move = Vector2.zero;
            yield return Frames(0.5f);
            a.Player.ShotResolved -= On;
            Log($"duel compensated={compensated}: {resolved} shots resolved, {hits} on the target, ammo left {a.Player.ActiveMag}");
            result(hits);
        }

        IEnumerator Arm(System.Action<RemoteClient> done, Vector3 shooterAt, Vector3 lookAt, int weapon = Rifle, int mag = 30, AmmoType ammo = AmmoType.Rifle556)
        {
            RemoteClient a = null;
            yield return _net.AddClient(c => a = c);
            var onServer = MultiHarness.ReplicaOf(_net.Host, a.Player);
            Assert.IsTrue(onServer.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = weapon, Amount = mag }));
            Assert.IsTrue(onServer.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = ammo, Amount = 200 }));
            _net.HostPlayer.ServerDamageFilter = _ => 0.5f; // survives the whole duel
            onServer.ServerDamageFilter = _ => 0f;
            a.Controller.Teleport(shooterAt);
            a.Controller.LookAt(lookAt);
            yield return NetHarness.WaitUntil(() => a.Player.ActiveWeaponId == weapon, "weapon in hand", Log, 5f);
            yield return Frames(1.5f);
            _net.Simulate(a);
            yield return Frames(1.5f);

            done(a);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator AimedShotAt100ms_HitsAStrafingTarget_AndMissesWithoutCompensation()
        {
            RemoteClient a = null;
            yield return Arm(c => a = c, new Vector3(-26f, 0.1f, -2f), new Vector3(-17f, 1.7f, -2f));
            const int shots = 6;
            var near = new Vector3(-17f, 0.1f, -2f);
            int withComp = 0, without = 0;
            int rewindsBefore = LagCompensation.Rewinds;
            yield return Duel(a, shots, true, near, h => withComp = h);
            int rewinds = LagCompensation.Rewinds - rewindsBefore;
            yield return Duel(a, shots, false, near, h => without = h);
            Log($"strafing target at 100 ms / 2 %: {withComp}/{shots} hits with compensation (server rewound bodies in {rewinds} ray tests, rewind cap {LagComp.MaxRewindSeconds:F2} s), {without}/{shots} without");
            Assert.GreaterOrEqual(withComp, shots - 1, "an aimed shot at what the shooter sees hits");
            Assert.Greater(rewinds, 0, "the server really rewound");
            Assert.Less(without, withComp, "without compensation the same shots mostly miss (the test can tell the difference)");
        }

        // Past the instant range (25 m) the bullet flies on the server for a few ticks; it is judged against the same
        // rewound bodies (ADR-141). The bolt-action Sentinel has almost no spread, so every miss is the compensation's.
        [UnityTest, Timeout(180000)]
        public IEnumerator AimedShotAt35m_ABulletInFlight_HitsAStrafingTarget()
        {
            RemoteClient a = null;
            yield return Arm(c => a = c, new Vector3(-27f, 0.1f, 21f), new Vector3(8f, 1.7f, 21f), 5, 5, AmmoType.Rifle762);
            const int shots = 5;
            int withComp = 0;
            yield return Duel(a, shots, true, new Vector3(8f, 0.1f, 21f), h => withComp = h, 1.7f);
            Log($"strafing target at 35 m, bullet in flight: {withComp}/{shots} hits with compensation");
            Assert.GreaterOrEqual(withComp, shots - 1);
        }
    }
}
