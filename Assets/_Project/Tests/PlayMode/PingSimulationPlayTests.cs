using System.Collections;
using Game.Core;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #399, ADR-300: `--simulate-ping 100` on a client makes its real ping ~100 ms (what the status line shows), and play still works.
    public class PingSimulationPlayTests
    {
        MultiHarness _net;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _net = new MultiHarness { Log = Debug.Log };
            yield return _net.StartHost("TestBox");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PingSimulation.Use(null);
            yield return _net.Teardown();
        }

        static IEnumerator Frames(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        int MeasuredPing(RemoteClient c) => NetStatusClient.Attach(c.Manager).PingMs;

        [UnityTest, Timeout(120000)]
        public IEnumerator SimulatePing100_ShowsAbout100ms_AndShootingStillWorks()
        {
            PingSimulation.Use(SimulatedPing.Parse(new[] { "--simulate-ping", "100" }));
            RemoteClient a = null;
            yield return _net.AddClient(c => a = c, null, nm => PingSimulation.Attach(nm));
            yield return Frames(4f);
            int ping = MeasuredPing(a);
            Debug.Log($"simulate-ping 100: status line ping {ping} ms");
            Assert.That(ping, Is.InRange(85, 160), "the ping the status line shows");

            var onServer = MultiHarness.ReplicaOf(_net.Host, a.Player);
            Assert.IsTrue(onServer.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 }));
            Assert.IsTrue(onServer.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 60 }));
            yield return NetHarness.WaitUntil(() => a.Player.ActiveWeaponId == 3, "weapon in hand over the delayed line", Debug.Log, 8f);
            int resolved = 0;
            void On(in Combatant.ShotResult s) { if (s.Phase != Combatant.ShotPhase.Launch) resolved++; }
            a.Player.ShotResolved += On;
            a.Player.CmdFire(a.Player.transform.forward, uint.MaxValue);
            yield return Frames(1.5f);
            a.Player.ShotResolved -= On;
            Assert.Greater(resolved, 0, "a shot fired over the simulated line is resolved by the server");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator NoFlag_LeavesTheLineAlone()
        {
            PingSimulation.Use(SimulatedPing.Parse(new string[0]));
            RemoteClient a = null;
            yield return _net.AddClient(c => a = c, null, nm => PingSimulation.Attach(nm));
            yield return Frames(3f);
            Assert.IsFalse(a.Manager.TransportManager.LatencySimulator.GetEnabled(), "the latency simulator stays off");
            Assert.IsNull(a.Manager.GetComponent<PingSimulation>());
            Assert.Less(MeasuredPing(a), 40, "localhost ping without the flag");
        }
    }
}
