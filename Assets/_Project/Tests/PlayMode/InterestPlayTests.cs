using System.Collections;
using FishNet.Managing;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #167 (ADR-135): interest management, seen from a REMOTE client. The host's player and the remote's player are put
    // apart on a floor far from the TestBox (3000 m out), the box's loot, dummies and parked vehicles stay behind.
    public class InterestPlayTests : ReplicationTestBase
    {
        protected override string LogDir => "Logs/net-replication/interest";
        const float Wait = 8f;
        static readonly Vector3 HostSpot = new Vector3(3000f, 0.1f, 2000f);

        GameObject _floor;

        [UnitySetUp]
        public IEnumerator InterestSetUp()
        {
            InterestRules.Current = Game.Net.GameData.Rules.Interest ?? new InterestRules(); // the harness turns it off
            // 2.5 km of floor along z so a walk of any length stays on the ground.
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "InterestFloor";
            _floor.transform.position = new Vector3(3000f, -0.5f, 2900f);
            _floor.transform.localScale = new Vector3(80f, 1f, 2500f);
            Physics.SyncTransforms();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator InterestTearDown()
        {
            if (_floor != null) Object.Destroy(_floor);
            InterestRules.Current = Game.Net.GameData.Rules.Interest ?? new InterestRules();
            yield return null;
        }

        int Seen<T>(NetworkManager manager) where T : Component
        {
            int n = 0;
            foreach (var nob in manager.ClientManager.Objects.Spawned.Values)
                if (nob != null && nob.GetComponent<T>() != null) n++;
            return n;
        }

        // The remote's player `meters` south of the host's player in the open field (both on the floor).
        IEnumerator Apart(float meters)
        {
            HostPlayer.GetComponent<PlayerController>().Teleport(HostSpot);
            Remote.Controller.Teleport(HostSpot + new Vector3(0f, 0f, meters));
            yield return Seconds(0.3f);
        }

        [UnityTest]
        public IEnumerator FarPlayer_IsNotObserved_ComesInWithItsCurrentState_AndGoesAgain()
        {
            // Baseline in the box: both players are near each other and see each other.
            Assert.IsNotNull(HostOnRemote, "near: the remote holds a replica of the host's player the moment it has its own");

            // Walk the remote 600 m away: the host's player leaves its interest, and with it every box object.
            yield return Apart(600f);
            yield return Until(() => HostOnRemote == null, "the far host player is despawned on the remote", Wait);
            yield return Until(() => Seen<LootItem>(Net.Remote0()) == 0, "the box's loot is far from both: removed", Wait);
            yield return Until(() => Seen<Vehicle>(Net.Remote0()) == 0, "parked vehicles in the box: removed", Wait);
            Assert.IsTrue(Net.Host.ServerManager.Objects.Spawned.ContainsKey(Remote.Player.ObjectId), "the server still has everyone");
            Assert.IsFalse(HostPlayer.NetworkObject.Observers.Contains(Remote.Manager.ClientManager.Connection), "server side: the remote is no observer");

            // While unseen the host's player changes: a rifle in hand and some damage.
            HostPlayer.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return Until(() => HostPlayer.ActiveWeaponId == Rifle, "rifle in the host's hand");
            HostPlayer.ServerTakeDamage(35f);
            float hp = HostPlayer.Hp.Value;
            Assert.Less(hp, 100f);

            // Hysteresis: 330 m is outside 300 m (not seen yet, still not seen).
            yield return Apart(330f);
            yield return Seconds(1.5f);
            Assert.IsNull(HostOnRemote, "330 m: not inside the 300 m radius, so still not sent");

            // Into range: it appears at once with the state it has now.
            yield return Apart(150f);
            yield return Until(() => HostOnRemote != null, "the host player appears when in range", Wait);
            var onRemote = HostOnRemote;
            yield return Until(() => onRemote.ActiveWeaponId == Rifle, "the rifle is there on arrival");
            Assert.AreEqual(hp, onRemote.Hp.Value, 0.01f, "hit points as they are now");
            Assert.AreEqual(HostPlayer.TeamId.Value, onRemote.TeamId.Value);
            yield return Until(() => Vector3.Distance(onRemote.transform.position, HostPlayer.transform.position) < 1.5f, "the replica stands where the player is");

            // Hysteresis the other way: 330 m is still inside 360 m for something already seen.
            yield return Apart(330f);
            yield return Seconds(1.5f);
            Assert.IsNotNull(HostOnRemote, "330 m: already seen, stays until 360 m");

            // Out of range again: gone.
            yield return Apart(600f);
            yield return Until(() => HostOnRemote == null, "the player is removed after leaving the range", Wait);
        }

        [UnityTest]
        public IEnumerator Loot_ComesInRange_WithItsKindAndAmount_AndLeavesAgain()
        {
            yield return Apart(600f);
            yield return Until(() => Seen<LootItem>(Net.Remote0()) == 0, "no loot far from the box", Wait);

            // A pistol lies 20 m from the remote's player (server spawns it): it arrives with its data.
            var spot = Remote.Controller.transform.position + new Vector3(10f, 0.3f, 15f);
            MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = LootKind.Ammo, Amount = 37 }, spot);
            yield return Until(() => Seen<LootItem>(Net.Remote0()) == 1, "the near loot item is sent", Wait);
            LootItem onRemote = null;
            foreach (var nob in Net.Remote0().ClientManager.Objects.Spawned.Values)
                if (nob != null && nob.GetComponent<LootItem>() != null) onRemote = nob.GetComponent<LootItem>();
            Assert.AreEqual(37, onRemote.Amount.Value);
            Assert.AreEqual((int)LootKind.Ammo, onRemote.Kind.Value);

            // The player walks 150 m away: beyond the loot radius (100 m x 1.2) it is removed.
            yield return Apart(0f); // back together first, then the remote leaves
            Remote.Controller.Teleport(Remote.Controller.transform.position + new Vector3(0f, 0f, 250f));
            yield return Until(() => Seen<LootItem>(Net.Remote0()) == 0, "the item is removed beyond 120 m", Wait);
        }

        [UnityTest]
        public IEnumerator Switch_Off_SendsEverythingAgain()
        {
            InterestRules.Current = new InterestRules { Enabled = false };
            yield return Apart(600f);
            yield return Seconds(2f);
            Assert.IsNotNull(HostOnRemote, "rules off: far players are sent as before");
        }
    }
}
