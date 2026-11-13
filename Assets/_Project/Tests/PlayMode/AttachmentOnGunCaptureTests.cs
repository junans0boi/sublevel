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
    // Review shots of the attachments on the held guns (#154, ADR-120): each weapon bare and fitted, third person from the
    // side and the front, and the first-person view. Run on purpose with
    //   GUN_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.AttachmentOnGunCaptureTests
    [Explicit, Category("Capture")]
    public class AttachmentOnGunCaptureTests
    {
        sealed class Input : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.ToggleView = false;
                return f;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static void Save(Camera cam, string path, int w = 1280, int h = 720)
        {
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureGuns()
        {
            string folder = Environment.GetEnvironmentVariable("GUN_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/gunshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            var input = new Input();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;
            var weaponVisual = me.GetComponent<WeaponVisual>();
            var attachVisual = me.GetComponent<AttachmentVisual>();
            Assert.IsNotNull(attachVisual, "the view adds the attachment visual");
            var cam = new GameObject("GunShotCam").AddComponent<Camera>();
            cam.enabled = false;
            cam.CopyFrom(pc.Camera);
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.05f;
            var table = OpticRules.Current.AttachmentSlots;

            foreach (var weapon in new[] { 3, 2, 1, 4, 5 })
            {
                me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = weapon, Amount = 30 });
                for (int i = 0; i < 3; i++) if (me.WeaponInSlot(i) == weapon) me.ServerSelectSlot(i);
                float wait = Time.realtimeSinceStartup + 3f;
                while (me.ActiveWeaponId != weapon && Time.realtimeSinceStartup < wait) yield return null;
                Assert.AreEqual(weapon, me.ActiveWeaponId, "weapon " + weapon + " in hand");
                yield return new WaitForSeconds(0.7f);
                var model = weaponVisual.ModelFor(weapon).transform;
                IEnumerator Views(string tag)
                {
                    // Third person: from the right side, then from the front-left of the muzzle.
                    var c = model.position;
                    var right = me.transform.right;
                    var fwd = me.transform.forward;
                    var spot = c + right * 1.3f + Vector3.up * 0.12f;
                    cam.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(c - spot));
                    yield return null;
                    Save(cam, Path.Combine(folder, "gun_w" + weapon + "_" + tag + "_side.png"));
                    spot = c + fwd * 1.3f - right * 0.6f + Vector3.up * 0.2f;
                    cam.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(c - spot));
                    yield return null;
                    Save(cam, Path.Combine(folder, "gun_w" + weapon + "_" + tag + "_front.png"));
                }
                yield return Views("bare");
                foreach (var entry in table)
                    if (entry.WeaponId == weapon)
                        foreach (int id in entry.Ids)
                            if (id == 11 || id == 12 || id == 15 || id == 16) me.ServerGive(Optics.ToLoot(id)); // suppressor, vertical grip, stock, big mag
                yield return new WaitForSeconds(0.8f);
                yield return Views("fitted");
                if (weapon == 3 || weapon == 1)
                {
                    // First person (the weapon in front of the camera, with the HUD).
                    input.Frame.ToggleView = true;
                    yield return new WaitForSeconds(0.8f);
                    yield return UiShot.Save(Path.Combine(folder, "gun_w" + weapon + "_fitted_firstperson.png"), 1280, 720);
                    input.Frame.ToggleView = true;
                    yield return new WaitForSeconds(0.5f);
                }
            }
            Debug.Log("[GunCapture] shots in " + folder);
        }
    }
}
