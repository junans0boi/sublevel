using System;
using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #179: the rebuilt inventory screen's flows through its public paths (the EventSystem handlers call the same ones):
    // right click attach/detach, drag onto an attachment slot, throwable selection, the live character, the sort toggle.
    public class InventoryFlowTests
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

        static IEnumerator TwoRifles(Combatant me)
        {
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 2, Amount = 30 });
            yield return WaitUntil(() => me.Slot0.Value != 0 && me.Slot1.Value != 0, 2f, "two primaries");
        }

        [UnityTest]
        public IEnumerator RightClick_LooseAttachment_FitsTheGunInHand_ThenSwapsAndOldGoesToBag()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var optics = me.GetComponent<CombatantOptics>();
            var screen = InventoryScreen.Instance;
            yield return TwoRifles(me);
            int hand = me.ActiveSlot.Value;
            optics.ServerStash(12); // a vertical grip, loose in the bag
            UiFocus.Set(UiScreen.Inventory);
            yield return WaitUntil(() => InventoryBackend.BagOpticCount(me, 12) == 1, 2f, "grip in the bag list");

            screen.QuickDrop(screen.BagOpticSource(12));
            yield return WaitUntil(() => optics.AttachmentInSlot(hand, AttachSlot.Grip) == 12, 3f, "grip on the gun in hand");
            Assert.AreEqual(0, optics.InBag(12), "left the bag");

            // A second grip: the gun in hand again, the first one goes back to the bag.
            optics.ServerStash(13);
            yield return WaitUntil(() => InventoryBackend.BagOpticCount(me, 13) == 1, 2f, "second grip in the bag");
            screen.QuickDrop(screen.BagOpticSource(13));
            yield return WaitUntil(() => optics.AttachmentInSlot(hand, AttachSlot.Grip) == 13, 3f, "swapped on the gun in hand");
            yield return WaitUntil(() => optics.InBag(12) == 1, 2f, "the old grip is in the bag");
            Assert.AreEqual(0, optics.AttachmentInSlot(1 - hand, AttachSlot.Grip), "the other gun was left alone");
        }

        [UnityTest]
        public IEnumerator RightClick_NoGunTakesIt_DoesNothing_AndFittedOneGoesToTheBag()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var optics = me.GetComponent<CombatantOptics>();
            var screen = InventoryScreen.Instance;
            UiFocus.Set(UiScreen.Inventory);
            optics.ServerStash(12);
            yield return WaitUntil(() => InventoryBackend.BagOpticCount(me, 12) == 1, 2f, "grip in the bag list");
            Assert.IsFalse(screen.AutoEquipFromBag(12), "no gun at all");
            screen.QuickDrop(screen.BagOpticSource(12));
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(1, optics.InBag(12), "still in the bag");

            yield return TwoRifles(me);
            Assert.IsTrue(screen.AutoEquipFromBag(12));
            int slot = -1;
            yield return WaitUntil(() => (slot = optics.AttachmentInSlot(0, AttachSlot.Grip) == 12 ? 0 : optics.AttachmentInSlot(1, AttachSlot.Grip) == 12 ? 1 : -1) >= 0, 3f, "grip fitted");
            // Right click on the fitted one: back to the bag.
            screen.QuickDrop(screen.AttachedSource(slot, AttachSlot.Grip));
            yield return WaitUntil(() => optics.InBag(12) == 1, 3f, "detached to the bag");
            Assert.AreEqual(0, optics.AttachmentInSlot(slot, AttachSlot.Grip));
        }

        [UnityTest]
        public IEnumerator Drag_OntoAttachmentSlot_FitsOnlyTheRightPlace_AndTheOthersShowRed()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var optics = me.GetComponent<CombatantOptics>();
            var screen = InventoryScreen.Instance;
            yield return TwoRifles(me);
            optics.ServerStash(11); // a suppressor
            UiFocus.Set(UiScreen.Inventory);
            yield return WaitUntil(() => InventoryBackend.BagOpticCount(me, 11) == 1, 2f, "suppressor in the bag list");
            yield return null;
            Assert.IsTrue(screen.AttachSlotVisible(0, AttachSlot.Muzzle), "the rifle card shows a muzzle slot");
            Assert.IsTrue(screen.AttachSlotIsGhost(0, AttachSlot.Muzzle), "empty slot shows the type picture");

            Assert.IsTrue(screen.BeginDrag(screen.BagOpticSource(11)));
            Assert.IsTrue(screen.AttachSlotShowsValid(0, AttachSlot.Muzzle), "muzzle slot is green");
            Assert.IsTrue(screen.AttachSlotShowsInvalid(0, AttachSlot.Grip), "grip slot is red");
            Assert.IsTrue(screen.AttachSlotShowsInvalid(0, AttachSlot.Optic), "sights slot is red");
            Assert.IsFalse(screen.DropOnAttachSlot(screen.BagOpticSource(11), Attachments.GearIndex(0, AttachSlot.Grip)), "wrong place refused");
            screen.CancelDrag();
            Assert.IsFalse(screen.AttachSlotShowsInvalid(0, AttachSlot.Grip), "red gone after the drag");

            Assert.IsTrue(screen.DropOnAttachSlot(screen.BagOpticSource(11), Attachments.GearIndex(0, AttachSlot.Muzzle)));
            yield return WaitUntil(() => optics.AttachmentInSlot(0, AttachSlot.Muzzle) == 11, 3f, "suppressor on the muzzle");
            yield return null;
            Assert.IsFalse(screen.AttachSlotIsGhost(0, AttachSlot.Muzzle), "filled slot shows the item");
            Assert.AreEqual("att_supp", screen.AttachSlotIconKey(0, AttachSlot.Muzzle));

            // Another muzzle device over the full slot swaps: the suppressor goes to the bag.
            optics.ServerStash(9);
            yield return WaitUntil(() => InventoryBackend.BagOpticCount(me, 9) == 1, 2f, "compensator in the bag list");
            Assert.IsTrue(screen.DropOnAttachSlot(screen.BagOpticSource(9), Attachments.GearIndex(0, AttachSlot.Muzzle)));
            yield return WaitUntil(() => optics.AttachmentInSlot(0, AttachSlot.Muzzle) == 9 && optics.InBag(11) == 1, 3f, "swap");
        }

        [UnityTest]
        public IEnumerator Throwables_AreSelectedFromTheInventory_AndTheSlotShowsKindAndCount()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var user = me.GetComponent<ThrowableUser>();
            var screen = InventoryScreen.Instance;
            user.ServerAdd(ThrowableKind.Frag, 1);
            user.ServerAdd(ThrowableKind.Smoke, 2);
            user.ServerAdd(ThrowableKind.Flash, 1);
            user.ServerAdd(ThrowableKind.Molotov, 1);
            UiFocus.Set(UiScreen.Inventory);
            yield return WaitUntil(() => screen.ThrowChipVisible(ThrowableKind.Molotov), 2f, "chips for the four kinds");
            for (var k = ThrowableKind.Frag; k <= ThrowableKind.Molotov; k++) Assert.IsTrue(screen.ThrowChipVisible(k), k.ToString());

            // Drag a smoke onto slot 5.
            Assert.IsTrue(screen.DropOnThrowableSlot(screen.ExtraSource(InventorySourceKind.Smoke)));
            yield return WaitUntil(() => user.SelectedKind == ThrowableKind.Smoke, 2f, "smoke selected");
            yield return null;
            StringAssert.Contains("연막탄", screen.WeaponCardText(InventoryBackend.ThrowableSlot));
            StringAssert.Contains("x2", screen.WeaponCardText(InventoryBackend.ThrowableSlot));
            Assert.IsTrue(screen.ThrowChipSelected(ThrowableKind.Smoke));
            Assert.IsFalse(screen.ThrowChipSelected(ThrowableKind.Frag));

            // Right click on the flash row: set as throwable (not dropped).
            int loot = LootItem.All.Count;
            screen.QuickDrop(screen.ExtraSource(InventorySourceKind.Flash));
            yield return WaitUntil(() => user.SelectedKind == ThrowableKind.Flash, 2f, "flash selected by right click");
            Assert.AreEqual(1, user.Flashes.Value, "still carried");
            Assert.AreEqual(loot, LootItem.All.Count, "nothing dropped");

            // A double click on the molotov also takes slot 5 out.
            screen.Activate(screen.ExtraSource(InventorySourceKind.Molotov), 2);
            yield return WaitUntil(() => user.SelectedKind == ThrowableKind.Molotov && me.ActiveSlot.Value == InventoryBackend.ThrowableSlot && !me.Holstered.Value, 2f, "molotov out");

            // Cycling still works from the slot card: the next kind that is carried.
            screen.ClickEquip(InventoryBackend.ThrowableSlot);
            yield return WaitUntil(() => user.SelectedKind != ThrowableKind.Molotov, 2f, "cycled");

            // A kind that is not carried (or does not exist) cannot be set.
            user.ServerSelect(ThrowableKind.Flash);
            Assert.IsFalse(user.ServerSetThrowable((ThrowableKind)9, false));
            Assert.AreEqual(ThrowableKind.Flash, user.SelectedKind);
            user.ServerStrip(null);
            Assert.IsFalse(user.ServerSetThrowable(ThrowableKind.Smoke, false), "none carried");
        }

        [UnityTest]
        public IEnumerator LiveCharacter_IsDrawnWhileOpen_WithTheWornPieces()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var gear = me.GetComponent<CombatantGear>();
            var screen = InventoryScreen.Instance;
            UiFocus.Set(UiScreen.Inventory);
            yield return null;
            yield return null;
            Assert.IsTrue(screen.Preview.HasModel, "a copy of the model stands in the preview");
            int bare = screen.Preview.RendererCount;
            Assert.Greater(bare, 0);
            gear.ServerEquip(ArmorSlot.Helmet, 3);
            gear.ServerEquip(ArmorSlot.Vest, 2);
            yield return new WaitForSeconds(0.4f);
            Assert.Greater(screen.Preview.RendererCount, bare, "helmet and vest parts are drawn on the copy");
            UiFocus.Close();
            yield return null;
            Assert.IsFalse(screen.Preview.HasModel, "gone when the screen closes");
        }

        [UnityTest]
        public IEnumerator CarriedList_SortsByKindOrByRecent()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var screen = InventoryScreen.Instance;
            UiFocus.Set(UiScreen.Inventory);
            yield return null;
            me.ServerGive(new LootEntry { Kind = HealRules.BandageLootKind, Amount = 2 });
            yield return WaitUntil(() => screen.PackRowVisible(InventoryBackend.BandageRow), 2f, "bandage row");
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 30 });
            yield return WaitUntil(() => screen.PackRowVisible(1), 2f, "ammo row");
            yield return null;
            Assert.Less(screen.PackRowTop(1), screen.PackRowTop(InventoryBackend.BandageRow), "by kind: ammo before bandages");
            screen.SetSort(true);
            yield return null;
            Assert.IsTrue(screen.SortByRecent);
            Assert.Less(screen.PackRowTop(1), screen.PackRowTop(InventoryBackend.BandageRow), "by recent: the ammo was picked up last, so it is first");
            me.ServerGive(new LootEntry { Kind = HealRules.BandageLootKind, Amount = 1 });
            yield return WaitUntil(() => InventoryBackend.Bandages(me) == 3, 2f, "another bandage");
            yield return null;
            yield return null;
            Assert.Less(screen.PackRowTop(InventoryBackend.BandageRow), screen.PackRowTop(1), "by recent: the bandages grew last");
            screen.SetSort(false);
            yield return null;
            Assert.Less(screen.PackRowTop(1), screen.PackRowTop(InventoryBackend.BandageRow), "back to kind order");
        }

        // ---- The same flows through the real EventSystem handlers on the built objects ----

        static GameObject Find(InventoryScreen screen, string path)
        {
            var t = screen.ScreenCanvas.transform.Find("InventoryPanel/" + path);
            Assert.IsNotNull(t, path);
            return t.gameObject;
        }

        static PointerEventData Data(PointerEventData.InputButton button, int clicks = 1) => new PointerEventData(EventSystem.current)
        {
            button = button, clickCount = clicks, position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), pressPosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
        };

        [UnityTest]
        public IEnumerator PointerEvents_RightClickOnTheCell_FitsIt_AndHoverShowsTheTooltip()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var optics = me.GetComponent<CombatantOptics>();
            var screen = InventoryScreen.Instance;
            yield return TwoRifles(me);
            optics.ServerStash(12);
            UiFocus.Set(UiScreen.Inventory);
            yield return WaitUntil(() => InventoryBackend.BagOpticCount(me, 12) == 1, 2f, "grip cell listed");
            yield return null;
            var cell = Find(screen, "Pack/Optic0");
            Assert.IsTrue(cell.activeInHierarchy);

            ExecuteEvents.Execute(cell, Data(PointerEventData.InputButton.Left), ExecuteEvents.pointerEnterHandler);
            Assert.IsTrue(screen.TooltipVisible, "hover shows the tooltip");
            StringAssert.Contains("수직 손잡이", screen.TooltipTitle);
            StringAssert.Contains("우클릭", screen.TooltipBody);
            ExecuteEvents.Execute(cell, Data(PointerEventData.InputButton.Left), ExecuteEvents.pointerExitHandler);
            Assert.IsFalse(screen.TooltipVisible, "leaving hides it");

            ExecuteEvents.Execute(cell, Data(PointerEventData.InputButton.Right), ExecuteEvents.pointerClickHandler);
            yield return WaitUntil(() => optics.AttachmentInSlot(me.ActiveSlot.Value, AttachSlot.Grip) == 12, 3f, "right click fitted the grip");
            Assert.AreEqual(0, optics.InBag(12));

            // Right click on the fitted grip's slot: back to the bag.
            var slot = Find(screen, "Weapons/Slot" + (me.ActiveSlot.Value + 1) + "/Attach" + (int)AttachSlot.Grip);
            yield return null;
            ExecuteEvents.Execute(slot, Data(PointerEventData.InputButton.Right), ExecuteEvents.pointerClickHandler);
            yield return WaitUntil(() => optics.InBag(12) == 1, 3f, "right click on the slot detached it");
        }

        [UnityTest]
        public IEnumerator PointerEvents_DragTheCellOntoASlot_FitsIt_AndAWrongSlotTakesNothing()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var optics = me.GetComponent<CombatantOptics>();
            var screen = InventoryScreen.Instance;
            yield return TwoRifles(me);
            optics.ServerStash(11);
            UiFocus.Set(UiScreen.Inventory);
            yield return WaitUntil(() => InventoryBackend.BagOpticCount(me, 11) == 1, 2f, "suppressor cell listed");
            yield return null;
            var cell = Find(screen, "Pack/Optic0");
            var left = Data(PointerEventData.InputButton.Left);
            ExecuteEvents.Execute(cell, left, ExecuteEvents.beginDragHandler);
            Assert.IsTrue(screen.Dragging, "drag started on the cell");

            // Over the grip slot: refused, nothing changes, the drag ends.
            ExecuteEvents.Execute(Find(screen, "Weapons/Slot1/Attach" + (int)AttachSlot.Grip), left, ExecuteEvents.dropHandler);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(1, optics.InBag(11), "wrong place: still in the bag");
            Assert.IsFalse(screen.Dragging, "a drop ends the drag");

            ExecuteEvents.Execute(cell, left, ExecuteEvents.beginDragHandler);
            Assert.IsTrue(screen.Dragging);
            ExecuteEvents.Execute(Find(screen, "Weapons/Slot1/Attach" + (int)AttachSlot.Muzzle), left, ExecuteEvents.dropHandler);
            ExecuteEvents.Execute(cell, left, ExecuteEvents.endDragHandler);
            yield return WaitUntil(() => optics.AttachmentInSlot(0, AttachSlot.Muzzle) == 11, 3f, "dropped on the muzzle slot");
            Assert.IsFalse(screen.Dragging);
        }

        [UnityTest]
        public IEnumerator PointerEvents_ClickOnAThrowableRow_SelectsIt_DoubleClickTakesItOut()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var user = me.GetComponent<ThrowableUser>();
            var screen = InventoryScreen.Instance;
            user.ServerAdd(ThrowableKind.Frag, 1);
            user.ServerAdd(ThrowableKind.Flash, 1);
            UiFocus.Set(UiScreen.Inventory);
            yield return WaitUntil(() => screen.PackRowVisible(InventoryBackend.FlashRow), 2f, "flash row");
            yield return null;
            var row = Find(screen, "Pack/Bag" + InventoryBackend.FlashRow);
            ExecuteEvents.Execute(row, Data(PointerEventData.InputButton.Left), ExecuteEvents.pointerClickHandler);
            yield return WaitUntil(() => user.SelectedKind == ThrowableKind.Flash, 2f, "one click selects the flash");
            Assert.AreNotEqual(InventoryBackend.ThrowableSlot, me.ActiveSlot.Value, "not taken out by a single click");
            var frag = Find(screen, "Pack/Bag" + InventoryBackend.GrenadeRow);
            ExecuteEvents.Execute(frag, Data(PointerEventData.InputButton.Left, 2), ExecuteEvents.pointerClickHandler);
            yield return WaitUntil(() => user.SelectedKind == ThrowableKind.Frag && me.ActiveSlot.Value == InventoryBackend.ThrowableSlot, 2f, "double click takes the grenade out");
        }
    }
}
