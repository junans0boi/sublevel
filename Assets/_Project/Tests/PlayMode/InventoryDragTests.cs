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
    // Drag targets call these same public InventoryScreen paths; EventSystem pointer routing is not duplicated here.
    public class InventoryDragTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var frame = Frame;
                Frame.Inventory = Frame.Menu = false;
                return frame;
            }
        }

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        static IEnumerator LoadTestBox()
        {
            PlayerController.InputOverride = new ScriptedInput();
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

        [UnityTest]
        public IEnumerator GroundItem_DroppedOnBag_IsPickedUp()
        {
            yield return LoadTestBox();
            var screen = InventoryScreen.Instance;
            yield return WaitUntil(() => LootItem.All.Count > 0, 5f, "loot spawned");
            var target = LootItem.All[0];
            PlayerController.Local.Teleport(target.transform.position + Vector3.up * 0.1f);
            UiFocus.Set(UiScreen.Inventory);
            yield return WaitUntil(() => screen.VicinityCount > 0, 2f, "loot listed");

            int index = -1;
            for (int i = 0; i < screen.VicinityCount; i++)
                if (screen.VicinityItem(i) == target) index = i;
            Assert.GreaterOrEqual(index, 0, "target has a drag source");
            int before = LootItem.All.Count;

            Assert.IsTrue(screen.DropOnBag(screen.VicinitySource(index)));
            yield return WaitUntil(() => LootItem.All.Count == before - 1, 5f, "picked up from bag drop");
            Assert.IsFalse(LootItem.All.Contains(target));
        }

        [UnityTest]
        public IEnumerator BackpackAmmo_DroppedOnGround_CreatesLoot()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var screen = InventoryScreen.Instance;
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = InventoryBackend.AmmoValue(1), Amount = 40 });
            yield return WaitUntil(() => InventoryBackend.Reserve(me, 1) == 40, 2f, "ammo received");
            UiFocus.Set(UiScreen.Inventory);
            int before = LootItem.All.Count;

            Assert.IsTrue(screen.DropOnGround(screen.AmmoSource(1)));
            yield return WaitUntil(() => InventoryBackend.Reserve(me, 1) == 0, 2f, "ammo left backpack");
            yield return WaitUntil(() => LootItem.All.Count == before + 1, 2f, "ammo loot spawned");
            var dropped = LootItem.All[LootItem.All.Count - 1];
            Assert.AreEqual((int)LootKind.Ammo, dropped.Kind.Value);
            Assert.AreEqual(1, dropped.Ammo.Value);
            Assert.AreEqual(40, dropped.Amount.Value);
        }

        [UnityTest]
        public IEnumerator Medkit_DroppedOnWeaponSlot_DoesNothing()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var screen = InventoryScreen.Instance;
            me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 2 });
            yield return WaitUntil(() => me.Medkits.Value == 2, 2f, "medkits received");
            UiFocus.Set(UiScreen.Inventory);

            Assert.IsFalse(screen.DropOnWeaponSlot(screen.MedkitSource(), 0));
            yield return null;
            yield return null;
            Assert.AreEqual(2, me.Medkits.Value);
            Assert.AreEqual(0, me.Slot0.Value);
        }

        [UnityTest]
        public IEnumerator ClosingScreen_CancelsAnActiveDrag()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var screen = InventoryScreen.Instance;
            me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 1 });
            yield return WaitUntil(() => me.Medkits.Value == 1, 2f, "medkit received");
            UiFocus.Set(UiScreen.Inventory);

            Assert.IsTrue(screen.BeginDrag(screen.MedkitSource()));
            Assert.IsTrue(screen.Dragging);
            UiFocus.Close();
            Assert.IsFalse(screen.Dragging);
        }

        [UnityTest]
        public IEnumerator ClickingAnEmptyWeaponSlot_DoesNotChangeActiveSlot()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var screen = InventoryScreen.Instance;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            yield return WaitUntil(() => me.Slot0.Value == 3 && me.Slot1.Value == 0, 2f, "one weapon received");
            Assert.AreEqual(0, me.ActiveSlot.Value);
            UiFocus.Set(UiScreen.Inventory);

            screen.Activate(screen.WeaponSource(1), 1);
            yield return null;
            yield return null;
            Assert.AreEqual(0, me.ActiveSlot.Value);
        }

        [UnityTest]
        public IEnumerator RightClickReleaseAfterDragCancel_DoesNotDropItem()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var screen = InventoryScreen.Instance;
            me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 1 });
            yield return WaitUntil(() => me.Medkits.Value == 1, 2f, "medkit received");
            UiFocus.Set(UiScreen.Inventory);
            int before = LootItem.All.Count;

            Assert.IsTrue(screen.BeginDrag(screen.MedkitSource()));
            screen.CancelDragFromPointer();
            screen.QuickDrop(screen.MedkitSource());
            yield return null;
            yield return null;

            Assert.IsFalse(screen.Dragging);
            Assert.AreEqual(1, me.Medkits.Value);
            Assert.AreEqual(before, LootItem.All.Count);
        }

        [UnityTest]
        public IEnumerator WeaponSlot_DroppedOnOtherSlot_SwapsWeapons()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var screen = InventoryScreen.Instance;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 4, Amount = 6 });
            yield return WaitUntil(() => me.Slot0.Value == 3 && me.Slot1.Value == 4, 2f, "two weapons received");
            UiFocus.Set(UiScreen.Inventory);

            Assert.IsTrue(screen.DropOnWeaponSlot(screen.WeaponSource(0), 1));
            yield return WaitUntil(() => me.Slot0.Value == 4 && me.Slot1.Value == 3, 2f, "weapon slots swapped");
            Assert.AreEqual(4, me.Slot0.Value);
            Assert.AreEqual(3, me.Slot1.Value);
        }
    }
}
