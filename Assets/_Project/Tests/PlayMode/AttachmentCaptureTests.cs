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
    // Review shots of the attachments (#129, #130): the inventory with parts fitted and in the bag, and the ground models.
    // Not part of the suite: run on purpose with
    //   ATT_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.AttachmentCaptureTests
    [Explicit, Category("Capture")]
    public class AttachmentCaptureTests
    {
        static readonly int[] AllIds = { 9, 10, 11, 12, 13, 14, 15, 16 };

        [UnityTest, Timeout(180000)]
        public IEnumerator CaptureAttachmentShots()
        {
            string folder = Environment.GetEnvironmentVariable("ATT_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/attshots";
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
            var gear = me.GetComponent<CombatantOptics>();
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 1, Amount = 12 });
            yield return new WaitForSeconds(0.3f);
            me.ServerGive(Optics.ToLoot(5));
            // Everything the rifle takes fits; the rest of each kind goes to the bag.
            foreach (int id in AllIds) me.ServerGive(Optics.ToLoot(id));
            foreach (int id in new[] { 12, 13, 14, 2, 1, 11 }) me.ServerGive(Optics.ToLoot(id));
            yield return new WaitForSeconds(0.5f);
            UiFocus.Set(UiScreen.Inventory);
            yield return new WaitForSeconds(1.0f);
            yield return Shot(folder, "att_01_inventory");
            UiFocus.Close();
            yield return null;

            // Ground models in a row in front of the player, from above and the side.
            Vector3 origin = me.transform.position + me.transform.forward * 2.2f;
            int n = 0;
            foreach (int id in AllIds)
                MatchDirector.Instance.ServerSpawnLoot(Optics.ToLoot(id), origin + me.transform.right * (n++ - 3.5f) * 0.45f + Vector3.up * 0.3f);
            yield return new WaitForSeconds(1.5f);
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            go.transform.position = origin + Vector3.up * 1.4f - me.transform.forward * 1.2f;
            go.transform.rotation = Quaternion.LookRotation(origin - go.transform.position, Vector3.up);
            yield return Render(cam, Path.Combine(folder, "att_02_ground_models.png"));
            UnityEngine.Object.Destroy(go);
            Debug.Log("[AttCapture] grip " + gear.AttachmentInSlot(me.ActiveSlot.Value, AttachSlot.Grip));
        }

        static IEnumerator Render(Camera cam, string path)
        {
            yield return null;
            const int w = 1920, h = 1080;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        static IEnumerator Shot(string folder, string name)
        {
            yield return null;
            var cam = PlayerController.Local != null && PlayerController.Local.Camera != null ? PlayerController.Local.Camera : Camera.main;
            var canvas = InventoryScreen.Instance.GetComponentInChildren<Canvas>(true);
            var mode = canvas.renderMode;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            yield return Render(cam, Path.Combine(folder, name + ".png"));
            canvas.renderMode = mode;
        }
    }
}
