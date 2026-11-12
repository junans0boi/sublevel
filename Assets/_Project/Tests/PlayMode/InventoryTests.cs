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
    // The Tab inventory in the real TestBox scene: it follows UiFocus, shows the server's loadout, and its
    // buttons (called directly here) pick up, drop and equip through the server.
    public class InventoryTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                // One-shot presses last a single frame.
                Frame.Inventory = Frame.Menu = false;
                return f;
            }
        }

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
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return WaitUntil(() => InventoryScreen.Instance != null, 2f, "inventory screen created");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UiFocus.Close();
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        IEnumerator GiveRifle()
        {
            Combatant.LocalPlayer.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            yield return WaitUntil(() => Combatant.LocalPlayer.Slot0.Value == 3, 2f, "rifle in slot 1");
        }

        [UnityTest]
        public IEnumerator OpensAndCloses_WithUiFocus()
        {
            yield return LoadTestBox();
            var screen = InventoryScreen.Instance;
            Assert.IsFalse(screen.IsOpen, "closed when the match starts");

            UiFocus.Set(UiScreen.Inventory);
            Assert.IsTrue(screen.IsOpen);
            yield return null;
            Assert.IsTrue(screen.IsOpen, "stays open");

            UiFocus.Set(UiScreen.Menu);
            Assert.IsFalse(screen.IsOpen, "another screen takes its place");

            UiFocus.Set(UiScreen.Inventory);
            Assert.IsTrue(screen.IsOpen);
            UiFocus.Close();
            Assert.IsFalse(screen.IsOpen);
            Assert.AreEqual(0, screen.VicinityCount);
        }

        [UnityTest]
        public IEnumerator TabKey_OpensAndClosesIt()
        {
            yield return LoadTestBox();
            var screen = InventoryScreen.Instance;

            _input.Frame.Inventory = true;
            yield return WaitUntil(() => screen.IsOpen, 2f, "opened by the inventory key");
            _input.Frame.Inventory = true;
            yield return WaitUntil(() => !screen.IsOpen, 2f, "closed by the inventory key");
        }

        [UnityTest]
        public IEnumerator WeaponCard_ShowsTheWeaponInTheSlot()
        {
            yield return LoadTestBox();
            var screen = InventoryScreen.Instance;
            UiFocus.Set(UiScreen.Inventory);
            yield return null;
            StringAssert.Contains("비어 있음", screen.WeaponCardText(0));
            StringAssert.Contains("비어 있음", screen.WeaponCardText(1));

            yield return GiveRifle();
            string rifle = GameData.Weapons.Find(3).Name;
            yield return WaitUntil(() => screen.WeaponCardText(0).Contains(rifle), 2f, "rifle shown on the first card");
            StringAssert.Contains("30 / 0", screen.WeaponCardText(0));
            StringAssert.Contains("비어 있음", screen.WeaponCardText(1));
        }

        [UnityTest]
        public IEnumerator Backpack_ShowsAmmoAndMedkits_AndDropsThem()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var screen = InventoryScreen.Instance;
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Medium, Amount = 40 });
            me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 2 });
            UiFocus.Set(UiScreen.Inventory);
            yield return WaitUntil(() => screen.BackpackText(1).Contains("40") && screen.BackpackText(3).Contains("2"), 2f, "ammo and medkits listed");

            int lootBefore = LootItem.All.Count;
            screen.ClickDropAmmo(1);
            yield return WaitUntil(() => me.ReserveMedium.Value == 0, 2f, "ammo left the backpack");
            yield return WaitUntil(() => LootItem.All.Count == lootBefore + 1, 2f, "ammo on the ground");
            var ammo = LootItem.All[LootItem.All.Count - 1];
            Assert.AreEqual((int)LootKind.Ammo, ammo.Kind.Value);
            Assert.AreEqual((int)AmmoType.Medium, ammo.Ammo.Value);
            Assert.AreEqual(40, ammo.Amount.Value, "the whole stack is dropped");

            screen.ClickDropMedkit();
            yield return WaitUntil(() => me.Medkits.Value == 1, 2f, "one medkit left the backpack");
            yield return WaitUntil(() => LootItem.All.Count == lootBefore + 2, 2f, "medkit on the ground");
            var medkit = LootItem.All[LootItem.All.Count - 1];
            Assert.AreEqual((int)LootKind.Medkit, medkit.Kind.Value);
            Assert.AreEqual(1, medkit.Amount.Value);

            // Nothing of that type left: the request changes nothing.
            screen.ClickDropAmmo(1);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(lootBefore + 2, LootItem.All.Count);
        }

        [UnityTest]
        public IEnumerator DropWeapon_EmptiesTheSlot_AndPutsItOnTheGroundNearby()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var screen = InventoryScreen.Instance;
            yield return GiveRifle();
            UiFocus.Set(UiScreen.Inventory);
            yield return null;

            int lootBefore = LootItem.All.Count;
            screen.ClickDropWeapon(0);
            yield return WaitUntil(() => me.Slot0.Value == 0, 2f, "slot emptied");
            yield return WaitUntil(() => LootItem.All.Count == lootBefore + 1, 2f, "dropped weapon on the ground");

            var dropped = LootItem.All[LootItem.All.Count - 1];
            Assert.AreEqual((int)LootKind.Weapon, dropped.Kind.Value);
            Assert.AreEqual(3, dropped.WeaponId.Value);
            Assert.AreEqual(30, dropped.Amount.Value, "keeps what was in the magazine");
            Assert.Less(Vector3.Distance(dropped.transform.position, me.transform.position), 3f, "within reach of the player");
            Assert.AreEqual(0, me.ActiveWeaponId, "hands are empty");

            yield return WaitUntil(() => screen.WeaponCardText(0).Contains("비어 있음"), 2f, "card shows the empty slot");
            // The dropped weapon is listed as nearby loot straight away.
            yield return WaitUntil(() => screen.VicinityCount > 0, 2f, "dropped weapon listed in vicinity");
            bool listed = false;
            for (int i = 0; i < screen.VicinityCount; i++) listed |= screen.VicinityItem(i) == dropped;
            Assert.IsTrue(listed);
        }

        [UnityTest]
        public IEnumerator Equip_SwitchesTheActiveSlot()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var screen = InventoryScreen.Instance;
            yield return GiveRifle();
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 4, Amount = 6 });
            yield return WaitUntil(() => me.Slot1.Value == 4, 2f, "shotgun in slot 2");
            Assert.AreEqual(0, me.ActiveSlot.Value);
            UiFocus.Set(UiScreen.Inventory);

            screen.ClickEquip(1);
            yield return WaitUntil(() => me.ActiveSlot.Value == 1, 2f, "second slot in hand");
            Assert.AreEqual(4, me.ActiveWeaponId);
        }

        [UnityTest]
        public IEnumerator Vicinity_ListsLootInReach_AndClickPicksItUp()
        {
            yield return LoadTestBox();
            var pc = PlayerController.Local;
            var screen = InventoryScreen.Instance;
            yield return WaitUntil(() => LootItem.All.Count > 0, 5f, "loot spawned");
            var target = LootItem.All[0];
            pc.Teleport(target.transform.position + Vector3.up * 0.1f);
            UiFocus.Set(UiScreen.Inventory);
            yield return WaitUntil(() => screen.VicinityCount > 0, 2f, "loot listed in vicinity");

            bool listed = false;
            for (int i = 0; i < screen.VicinityCount; i++) listed |= screen.VicinityItem(i) == target;
            Assert.IsTrue(listed, "the loot we stand on is listed");
            Assert.Greater(screen.VicinityText(0).Trim().Length, 0, "rows carry a label");

            var first = screen.VicinityItem(0);
            int lootBefore = LootItem.All.Count;
            screen.ClickVicinity(0);
            yield return WaitUntil(() => LootItem.All.Count == lootBefore - 1, 5f, "picked-up loot removed from the ground");
            Assert.IsFalse(LootItem.All.Contains(first));
            yield return null;
            for (int i = 0; i < screen.VicinityCount; i++)
                Assert.AreNotEqual(first, screen.VicinityItem(i), "and from the list");
        }
    }
}
