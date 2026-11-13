using System;
using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // G5 / ADR-093 in the TestBox scene as a host: the pickup list by the crosshair (wheel + F) and the optic
    // following the weapon that replaces its own.
    public class PickupListPlayTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.Interact = false;
                Frame.ZoomSteps = 0;
                return f;
            }
        }

        const int Ordo = 2, Rifle = 3, Breacher = 4, Sentinel = 5;
        const int X4 = 5;

        ScriptedInput _input;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        IEnumerator LoadTestBox()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null && PickupList.Instance != null, 15f, "local player spawned");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        // Three pickups 0.6 / 1.2 / 1.8 m in front: the list shows them nearest first.
        IEnumerator SpawnThree(LootItem[] into)
        {
            var me = Combatant.LocalPlayer;
            var entries = new[]
            {
                new LootEntry { Kind = LootKind.Medkit, Amount = 1 },
                new LootEntry { Kind = LootKind.Bandage, Amount = 2 },
                Optics.ToLoot(1),
            };
            for (int i = 0; i < 3; i++)
            {
                int before = LootItem.All.Count;
                MatchDirector.Instance.ServerSpawnLoot(entries[i], me.transform.position + me.transform.forward * (0.6f * (i + 1)) + Vector3.up * 0.3f);
                yield return WaitUntil(() => LootItem.All.Count > before, 2f, "pickup " + i + " spawned");
                into[i] = LootItem.All[LootItem.All.Count - 1];
            }
            yield return WaitUntil(() => PickupList.Instance.Rows.Count == 3, 2f, "three rows");
        }

        [UnityTest]
        public IEnumerator ThreePickups_WheelSelectsTheSecond_AndFTakesExactlyIt()
        {
            yield return LoadTestBox();
            var items = new LootItem[3];
            yield return SpawnThree(items);
            var list = PickupList.Instance;
            Assert.AreSame(items[0], list.Rows[0], "nearest first");
            Assert.AreSame(items[1], list.Rows[1]);
            Assert.AreSame(items[2], list.Rows[2]);
            Assert.AreEqual(0, list.Selected, "the nearest is selected first");
            yield return null;
            Assert.IsTrue(PickupList.Showing, "two or more pickups show the list");

            _input.Frame.ZoomSteps = -1; // one notch down
            yield return null;
            yield return null;
            Assert.AreEqual(1, list.Selected);
            Assert.AreSame(items[1], PlayerController.Local.NearbyLoot, "F now means the second row");

            yield return UiShot.Save("Logs/captures/g5_pickup_list.png", 1280, 720);

            _input.Frame.Interact = true;
            yield return null;
            yield return null;
            Assert.IsFalse(ContainsRow(list, items[1]), "the taken pickup leaves the list at once");
            yield return WaitUntil(() => items[1] == null || !items[1].IsSpawned, 2f, "the second pickup taken");
            yield return null;
            Assert.IsTrue(items[0] != null && items[0].IsSpawned, "the first stays on the ground");
            Assert.IsTrue(items[2] != null && items[2].IsSpawned, "the third stays on the ground");
            Assert.AreEqual(2, list.Rows.Count, string.Join(", ", System.Linq.Enumerable.Select(list.Rows, r => r == null ? "null" : InventoryBackend.LootName(r) + " spawned " + r.IsSpawned)));
        }

        static bool ContainsRow(PickupList list, LootItem item)
        {
            foreach (var row in list.Rows) if (row == item) return true;
            return false;
        }

        IEnumerator RifleWithX4InHand(Combatant me, CombatantOptics optics)
        {
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Sentinel, Amount = 5 });
            yield return WaitUntil(() => me.WeaponInSlot(0) != 0 && me.WeaponInSlot(1) != 0, 2f, "both main slots full");
            Assert.AreEqual(Rifle, me.ActiveWeaponId, "still the rifle in hand");
            Assert.IsTrue(me.ServerGive(Optics.ToLoot(X4)));
            yield return WaitUntil(() => optics.ActiveOptic == X4, 2f, "4x on the rifle");
        }

        [UnityTest]
        public IEnumerator ReplacingTheRifle_WithAGunThatTakesTheOptic_MovesItOver()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var optics = me.GetComponent<CombatantOptics>();
            yield return RifleWithX4InHand(me, optics);
            int slot = me.ActiveSlot.Value;

            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Ordo, Amount = 30 }));
            yield return WaitUntil(() => me.WeaponInSlot(slot) == Ordo, 2f, "the new gun in the rifle's slot");
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(X4, optics.OpticInSlot(slot), "the 4x moved to the new gun");
            Assert.AreEqual(0, optics.InBag(X4));
        }

        [UnityTest]
        public IEnumerator ReplacingTheRifle_WithAGunThatRefusesTheOptic_PutsItInTheBag()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var optics = me.GetComponent<CombatantOptics>();
            yield return RifleWithX4InHand(me, optics);
            int slot = me.ActiveSlot.Value;

            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Breacher, Amount = 5 }));
            yield return WaitUntil(() => me.WeaponInSlot(slot) == Breacher, 2f, "the shotgun in the rifle's slot");
            yield return WaitUntil(() => optics.InBag(X4) == 1, 2f, "the 4x in the bag (the shotgun takes up to 3x)");
            Assert.AreEqual(0, optics.OpticInSlot(slot));
        }
    }
}
