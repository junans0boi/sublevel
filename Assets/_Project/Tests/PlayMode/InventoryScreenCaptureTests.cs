using System;
using System.Collections;
using System.IO;
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
    // #179 review shots of the rebuilt inventory screen, every state at 1920x1080 and 1280x720. Not part of the suite:
    //   INV_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.InventoryScreenCaptureTests
    [Explicit, Category("Capture")]
    public class InventoryScreenCaptureTests
    {
        static readonly int[][] Sizes = { new[] { 1920, 1080 }, new[] { 1280, 720 } };

        static string Folder()
        {
            string folder = Environment.GetEnvironmentVariable("INV_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/invshots179";
            Directory.CreateDirectory(folder);
            return folder;
        }

        static IEnumerator LoadTestBox()
        {
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.6f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UiFocus.Close();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static void Gear(Combatant me)
        {
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 2, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 1, Amount = 12 });
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 90 });
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Pistol9, Amount = 48 });
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureEmptyBag()
        {
            string folder = Folder();
            yield return LoadTestBox();
            UiFocus.Set(UiScreen.Inventory);
            yield return new WaitForSeconds(0.6f);
            foreach (var size in Sizes) yield return Shot(folder, "empty_bag", size[0], size[1]);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureFullBagAndWeapons()
        {
            string folder = Environment.GetEnvironmentVariable("INV_SHOTS");
            folder = Folder();
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Gear(me);
            foreach (var k in new[] { LootKind.Bandage, LootKind.FirstAidKit, LootKind.Drink, LootKind.Pill, LootKind.Syringe, LootKind.Flash, LootKind.Molotov, LootKind.Smoke, LootKind.Grenade, LootKind.Medkit, LootKind.Jerrycan })
                me.ServerGive(new LootEntry { Kind = k, Amount = 2 });
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Shotgun12, Amount = 20 });
            me.ServerGive(new LootEntry { Kind = LootKind.Helmet, Tier = 3 });
            me.ServerGive(new LootEntry { Kind = LootKind.Vest, Tier = 2 });
            me.ServerGive(new LootEntry { Kind = LootKind.Backpack, Tier = 2 });
            foreach (int id in new[] { 5, 1, 12, 9, 15, 16, 11 }) me.ServerGive(Optics.ToLoot(id)); // fitted where they fit
            foreach (int id in new[] { 12, 13, 11, 11, 7, 3 }) me.GetComponent<CombatantOptics>().ServerStash(id); // and loose ones
            yield return new WaitForSeconds(0.4f);
            UiFocus.Set(UiScreen.Inventory);
            yield return new WaitForSeconds(0.6f);
            foreach (var size in Sizes) yield return Shot(folder, "full_bag_weapons", size[0], size[1]);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureDragAndTooltip()
        {
            string folder = Folder();
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Gear(me);
            me.ServerGive(new LootEntry { Kind = LootKind.Helmet, Tier = 2 });
            me.ServerGive(new LootEntry { Kind = LootKind.Vest, Tier = 3 });
            foreach (int id in new[] { 4, 9, 13 }) me.ServerGive(Optics.ToLoot(id)); // ADR-493: fitted, for the gun tooltip
            foreach (int id in new[] { 5, 12, 11, 16 }) me.GetComponent<CombatantOptics>().ServerStash(id); // loose in the bag
            yield return new WaitForSeconds(0.4f);
            UiFocus.Set(UiScreen.Inventory);
            yield return new WaitForSeconds(0.6f);
            var screen = InventoryScreen.Instance;
            // A suppressor from the bag in hand: muzzle slots light green, the others pulse red.
            Assert.IsTrue(screen.BeginDrag(screen.BagOpticSource(11)), "drag a suppressor");
            var cam = Camera.main != null ? Camera.main : PlayerController.Local.Camera;
            foreach (var size in Sizes)
            {
                var at = Screen(screen, "Weapons/Slot1/Attach1", size[0], size[1]);
                screen.SetPointerForTests(at);
                yield return Shot(folder, "drag_attachment", size[0], size[1], at, true);
            }
            screen.SetPointerForTests(null);
            screen.CancelDrag();
            // Hover over the first card's muzzle slot and a bag row.
            foreach (var size in Sizes)
            {
                var at = Screen(screen, "Weapons/Slot1/Attach0", size[0], size[1]);
                screen.SetPointerForTests(at);
                screen.ShowTooltip(screen.AttachSlotSource(0, AttachSlot.Optic), at, null);
                yield return new WaitForSeconds(0.3f);
                yield return Shot(folder, "hover_weapon_slot", size[0], size[1], at, false);
                screen.HideTooltip(true);
            }
            // ADR-493: the gun card's stats panel (class, modes, bars, fitted parts) and a gun on the ground.
            foreach (var size in Sizes)
            {
                var at = Screen(screen, "Weapons/Slot1", size[0], size[1]);
                screen.SetPointerForTests(at);
                screen.ShowTooltip(screen.WeaponSource(0), at, null);
                yield return new WaitForSeconds(0.3f);
                yield return Shot(folder, "hover_weapon_card", size[0], size[1], at, false);
                screen.HideTooltip(true);
                at = Screen(screen, "Weapons/Slot2", size[0], size[1]);
                screen.SetPointerForTests(at);
                screen.ShowTooltip(screen.WeaponSource(1), at, null);
                yield return new WaitForSeconds(0.3f);
                yield return Shot(folder, "hover_weapon_card2", size[0], size[1], at, false);
                screen.HideTooltip(true);
            }
            foreach (var size in Sizes)
            {
                var at = Screen(screen, "Pack/Bag4", size[0], size[1]);
                screen.SetPointerForTests(at);
                screen.ShowTooltip(screen.AmmoSource(1), at, null);
                yield return new WaitForSeconds(0.3f);
                yield return Shot(folder, "hover_bag_row", size[0], size[1], at, false);
                screen.HideTooltip(true);
            }
            // Drag a bag row (bandage) over the throwable slot.
            screen.SetPointerForTests(null);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureThrowables()
        {
            string folder = Folder();
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Gear(me);
            var user = me.GetComponent<ThrowableUser>();
            user.ServerAdd(ThrowableKind.Frag, 2);
            user.ServerAdd(ThrowableKind.Smoke, 1);
            user.ServerAdd(ThrowableKind.Flash, 2);
            user.ServerAdd(ThrowableKind.Molotov, 1);
            user.ServerSetThrowable(ThrowableKind.Smoke, true);
            yield return new WaitForSeconds(0.5f);
            UiFocus.Set(UiScreen.Inventory);
            yield return new WaitForSeconds(0.6f);
            foreach (var size in Sizes) yield return Shot(folder, "throwables_smoke_selected", size[0], size[1]);
            var screen = InventoryScreen.Instance;
            Assert.IsTrue(screen.BeginDrag(screen.ExtraSource(InventorySourceKind.Molotov)));
            foreach (var size in Sizes)
            {
                var at = Screen(screen, "Weapons/Slot5", size[0], size[1]);
                screen.SetPointerForTests(at);
                yield return Shot(folder, "throwables_drag_to_slot5", size[0], size[1], at, true);
            }
            screen.SetPointerForTests(null);
            screen.CancelDrag();
        }

        // Hover and press feedback of a row, through the EventSystem handlers.
        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureRowHoverAndPress()
        {
            string folder = Folder();
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Gear(me);
            me.ServerGive(new LootEntry { Kind = HealRules.BandageLootKind, Amount = 3 });
            me.ServerGive(new LootEntry { Kind = LootKind.Helmet, Tier = 1 });
            yield return new WaitForSeconds(0.4f);
            UiFocus.Set(UiScreen.Inventory);
            yield return new WaitForSeconds(0.6f);
            var screen = InventoryScreen.Instance;
            var row = screen.ScreenCanvas.transform.Find("InventoryPanel/Pack/Bag" + InventoryBackend.BandageRow).gameObject;
            foreach (var size in Sizes)
            {
                var at = Screen(screen, "Pack/Bag" + InventoryBackend.BandageRow, size[0], size[1]);
                screen.SetPointerForTests(at);
                var data = new PointerEventData(EventSystem.current) { position = at, button = PointerEventData.InputButton.Left };
                ExecuteEvents.Execute(row, data, ExecuteEvents.pointerEnterHandler);
                yield return new WaitForSeconds(0.4f);
                yield return Shot(folder, "row_hover", size[0], size[1], at, false);
                ExecuteEvents.Execute(row, data, ExecuteEvents.pointerDownHandler);
                yield return new WaitForSeconds(0.3f);
                yield return Shot(folder, "row_press", size[0], size[1], at, false);
                ExecuteEvents.Execute(row, data, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(row, data, ExecuteEvents.pointerExitHandler);
            }
            screen.SetPointerForTests(null);
        }

        static Vector2 Screen(InventoryScreen screen, string path, int w, int h)
        {
            var rect = (RectTransform)screen.ScreenCanvas.transform.Find("InventoryPanel/" + path);
            Assert.IsNotNull(rect, path);
            // The canvas scales to the shot's size (match height), so convert through its reference size.
            var canvasRect = (RectTransform)screen.ScreenCanvas.transform;
            Vector3 world = rect.TransformPoint(rect.rect.center);
            Vector2 local = canvasRect.InverseTransformPoint(world);
            float scale = h / 1080f;
            return new Vector2(w * 0.5f + local.x * scale, h * 0.5f + local.y * scale);
        }

        // The canvas is drawn by the player camera into a texture of the wanted size.
        static IEnumerator Shot(string folder, string name, int w, int h, Vector2? pointer = null, bool dragging = false)
        {
            yield return null; // not WaitForEndOfFrame: it stalls in a batch mode editor
            var screen = InventoryScreen.Instance;
            var cam = PlayerController.Local != null && PlayerController.Local.Camera != null ? PlayerController.Local.Camera : Camera.main;
            var canvas = screen.ScreenCanvas;
            var mode = canvas.renderMode;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            screen.RefreshNow();
            screen.Preview.RenderNow();
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var before = cam.targetTexture;
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            screen.RefreshPointerNow(); // with the camera on the shot's texture, so pointer coordinates are the shot's
            Canvas.ForceUpdateCanvases();
            cam.Render();
            cam.targetTexture = before;
            canvas.renderMode = mode;
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(folder, $"{name}_{w}x{h}.png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }
    }
}
