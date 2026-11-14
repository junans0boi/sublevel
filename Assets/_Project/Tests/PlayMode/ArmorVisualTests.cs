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
    // #179: the helmet and vest the player wears are drawn on the character, by tier.
    public class ArmorVisualTests
    {
        static IEnumerator LoadTestBox()
        {
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.4f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UiFocus.Close();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator WornArmor_IsDrawnByTier_AndGoneWhenRemoved()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var gear = me.GetComponent<CombatantGear>();
            var visual = me.GetComponent<ArmorVisual>();
            Assert.IsNotNull(visual, "CombatantView adds the armour drawing");
            yield return null;
            Assert.IsFalse(visual.HelmetVisible, "bare head");
            Assert.IsFalse(visual.VestVisible, "bare chest");

            gear.ServerEquip(ArmorSlot.Helmet, 2);
            gear.ServerEquip(ArmorSlot.Vest, 3);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(2, visual.ShownHelmetTier);
            Assert.AreEqual(3, visual.ShownVestTier);
            Assert.IsTrue(visual.HelmetVisible, "helmet on the head");
            Assert.IsTrue(visual.VestVisible, "vest on the chest");
            var helmetFilter = visual.GetComponentInChildren<MeshFilter>(true);
            Assert.IsNotNull(helmetFilter);

            // The eye view hides them with the body, third person shows them again.
            visual.SetVisible(false);
            Assert.IsFalse(visual.HelmetVisible);
            visual.SetVisible(true);
            Assert.IsTrue(visual.HelmetVisible);

            gear.ServerRemove(ArmorSlot.Helmet);
            gear.ServerEquip(ArmorSlot.Vest, 1);
            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(visual.HelmetVisible, "no helmet worn, none drawn");
            Assert.AreEqual(1, visual.ShownVestTier, "the vest shows the new tier");
        }

        // Review shots (not part of the suite): ARMOR_SHOTS=<folder>.
        [Explicit, Category("Capture")]
        [UnityTest, Timeout(120000)]
        public IEnumerator CaptureWornArmor()
        {
            string folder = Environment.GetEnvironmentVariable("ARMOR_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/armorshots";
            Directory.CreateDirectory(folder);
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var gear = me.GetComponent<CombatantGear>();
            var visual = me.GetComponent<ArmorVisual>();
            int shot = 0;
            foreach (var tiers in new[] { new[] { 0, 0 }, new[] { 1, 1 }, new[] { 3, 3 } })
            {
                gear.ServerEquip(ArmorSlot.Helmet, tiers[0]);
                gear.ServerEquip(ArmorSlot.Vest, tiers[1]);
                yield return new WaitForSeconds(0.3f);
                visual.SetVisible(true);
                foreach (var view in new[] { new Vector3(0f, 1.2f, 2.2f), new Vector3(2.0f, 1.3f, 1.0f), new Vector3(0f, 1.3f, -2.2f) })
                {
                    yield return null;
                    var cam = new GameObject("ShotCam").AddComponent<Camera>();
                    var at = me.transform.position + Vector3.up * 1.25f;
                    cam.transform.position = me.transform.TransformPoint(view);
                    cam.transform.rotation = Quaternion.LookRotation(at - cam.transform.position);
                    cam.fieldOfView = 32f;
                    var rt = RenderTexture.GetTemporary(960, 960, 24, RenderTextureFormat.ARGB32);
                    cam.targetTexture = rt;
                    cam.Render();
                    var active = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(960, 960, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 960, 960), 0, 0);
                    tex.Apply();
                    RenderTexture.active = active;
                    RenderTexture.ReleaseTemporary(rt);
                    UnityEngine.Object.Destroy(cam.gameObject);
                    File.WriteAllBytes(Path.Combine(folder, $"armor_{shot++:00}_h{tiers[0]}v{tiers[1]}.png"), tex.EncodeToPNG());
                    UnityEngine.Object.Destroy(tex);
                }
            }
        }
    }
}
