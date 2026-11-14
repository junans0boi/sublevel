using System;
using System.Collections;
using System.IO;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Review shots of the inventory (#107 #110 #113): loose optics in the bag, the heal progress in the header, and a
    // dropped weapon beside its rounds in the ground list. Batch mode renders the player camera into a texture, so the
    // inventory canvas is switched to that camera for the shot. Not part of the suite: run on purpose with
    //   INV_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.InventoryCaptureTests
    [Explicit, Category("Capture")]
    public class InventoryCaptureTests
    {
        [UnityTest, Timeout(120000)]
        public IEnumerator CaptureInventoryShots()
        {
            string folder = Environment.GetEnvironmentVariable("INV_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/invshots";
            Directory.CreateDirectory(folder);
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var me = Combatant.LocalPlayer;
            var optics = me.GetComponent<CombatantOptics>();
            var healing = me.GetComponent<CombatantHealing>();
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            foreach (int id in new[] { 5, 7, 1, 3, 3 }) me.ServerGive(Optics.ToLoot(id)); // 4x fitted, the rest to the bag
            me.ServerGive(new LootEntry { Kind = HealRules.BandageLootKind, Amount = 5 });
            me.ServerGive(new LootEntry { Kind = HealRules.FirstAidLootKind, Amount = 2 });
            me.ServerTakeDamage(40f);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(healing.ServerBegin(HealKind.Bandage), "bandage use started");
            UiFocus.Set(UiScreen.Inventory);
            yield return new WaitForSeconds(1.0f);
            yield return Shot(folder, "inv_01_bag_optics_healing");
            Debug.Log($"[InvCapture] bag 8x {optics.InBag(7)} dot {optics.InBag(1)} 2x {optics.InBag(3)}, on rifle {optics.OpticInSlot(me.ActiveSlot.Value)}, healing {healing.Using}");

            // Swap from the bag: the red dot onto the rifle, the 4x back into the bag.
            optics.ServerAttachFromBag(1, me.ActiveSlot.Value);
            healing.ServerCancel();
            yield return new WaitForSeconds(0.6f);
            yield return Shot(folder, "inv_02_swapped_dot_on");

            // Drop the rifle: the weapon alone (0 rounds) and its 30 rounds beside it; the dot goes to the bag.
            me.CmdDropWeapon(me.ActiveSlot.Value);
            yield return new WaitForSeconds(0.8f);
            yield return Shot(folder, "inv_03_dropped_rifle");
            UiFocus.Close();
        }

        // #148: every bag row filled, boost items, flash and fire bottle included.
        [UnityTest, Timeout(120000)]
        public IEnumerator CaptureFullBag()
        {
            string folder = Environment.GetEnvironmentVariable("INV_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/invshots";
            Directory.CreateDirectory(folder);
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var me = Combatant.LocalPlayer;
            foreach (var k in new[] { LootKind.Bandage, LootKind.FirstAidKit, LootKind.Drink, LootKind.Pill, LootKind.Syringe, LootKind.Flash, LootKind.Molotov, LootKind.Smoke, LootKind.Grenade, LootKind.Medkit })
                me.ServerGive(new LootEntry { Kind = k, Amount = 2 });
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 90 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(Optics.ToLoot(5));
            yield return new WaitForSeconds(0.3f);
            UiFocus.Set(UiScreen.Inventory);
            yield return new WaitForSeconds(1.0f);
            yield return Shot(folder, "inv_04_full_bag");
            UiFocus.Close();
        }

        // #175: nothing picked up yet -> no rows, a hint; one pickup -> exactly that row.
        [UnityTest, Timeout(120000)]
        public IEnumerator CaptureEmptyBag()
        {
            string folder = Environment.GetEnvironmentVariable("INV_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/invshots";
            Directory.CreateDirectory(folder);
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var me = Combatant.LocalPlayer;
            UiFocus.Set(UiScreen.Inventory);
            yield return new WaitForSeconds(1.0f);
            yield return Shot(folder, "inv_06_empty_bag");
            var rows = InventoryScreen.Instance.GetComponentsInChildren<Transform>(false);
            int visibleBagRows = 0;
            foreach (var t in rows) if (t.name.StartsWith("Bag") && t.gameObject.activeInHierarchy) visibleBagRows++;
            Assert.AreEqual(0, visibleBagRows, "no bag row while nothing is carried");
            UiFocus.Close();
            me.ServerGive(new LootEntry { Kind = HealRules.BandageLootKind, Amount = 3 });
            yield return new WaitForSeconds(0.3f);
            UiFocus.Set(UiScreen.Inventory);
            yield return new WaitForSeconds(1.0f);
            yield return Shot(folder, "inv_07_one_item");
            visibleBagRows = 0;
            foreach (var t in InventoryScreen.Instance.GetComponentsInChildren<Transform>(false))
                if (t.name.StartsWith("Bag") && t.gameObject.activeInHierarchy) visibleBagRows++;
            Assert.AreEqual(1, visibleBagRows, "only the bandages");
            UiFocus.Close();
        }

        // #174: a rolled gun lies unloaded with its rounds as a separate item beside it.
        [UnityTest, Timeout(120000)]
        public IEnumerator CaptureRolledGunOnGround()
        {
            string folder = Environment.GetEnvironmentVariable("INV_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/invshots";
            Directory.CreateDirectory(folder);
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var me = Combatant.LocalPlayer;
            int before = LootItem.All.Count;
            var at = me.transform.position + me.transform.forward * 4f + Vector3.up * 0.3f;
            MatchDirector.Instance.ServerSpawnLootRoll(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 }, at);
            yield return new WaitForSeconds(1.0f);
            Assert.AreEqual(before + 2, LootItem.All.Count, "a gun and its ammo");
            LootItem gun = null, ammo = null;
            foreach (var item in LootItem.All)
            {
                if ((LootKind)item.Kind.Value == LootKind.Weapon && item.Amount.Value == 0 && item.WeaponId.Value == 3) gun = item;
                if ((LootKind)item.Kind.Value == LootKind.Ammo && item.Amount.Value == 30 && (at - item.transform.position).sqrMagnitude < 4f) ammo = item;
            }
            Assert.IsNotNull(gun, "the gun with an empty magazine");
            Assert.IsNotNull(ammo, "30 rounds as their own item");
            var src = PlayerController.Local.Camera != null ? PlayerController.Local.Camera : Camera.main;
            var cam = new GameObject("ShotCam").AddComponent<Camera>();
            cam.CopyFrom(src);
            cam.transform.position = at + Vector3.up * 1.4f - me.transform.forward * 1.3f + me.transform.right * 0.5f;
            cam.transform.rotation = Quaternion.LookRotation((at + me.transform.right * 0.2f - cam.transform.position).normalized);
            var rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.Destroy(cam.gameObject);
            File.WriteAllBytes(Path.Combine(folder, "inv_05_ground_gun_and_ammo.png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        static IEnumerator Shot(string folder, string name)
        {
            yield return null; // not WaitForEndOfFrame: it stalls in a batch mode editor
            var cam = PlayerController.Local != null && PlayerController.Local.Camera != null ? PlayerController.Local.Camera : Camera.main;
            var canvas = InventoryScreen.Instance.GetComponentInChildren<Canvas>(true);
            var mode = canvas.renderMode;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            const int w = 1920, h = 1080;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var before = cam.targetTexture;
            cam.targetTexture = rt;
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
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }
    }
}
