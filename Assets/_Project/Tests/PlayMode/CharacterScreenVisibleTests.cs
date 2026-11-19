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
    // 외형 tab: the real equipped 3D character is on screen (r5.1 playtest: the tab showed no character).
    // Writes Logs/charshot/appearance_1920x1080.png (CHAR_SHOT=<folder> overrides) for a look by eye.
    public class CharacterScreenVisibleTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerPrefs.DeleteKey(UiFx.ReducedKey);
            PlayerAppearanceStore.Clear();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AppearanceTab_ShowsTheEquippedCharacter()
        {
            PlayerPrefs.SetInt(UiFx.ReducedKey, 1);
            PlayerAppearanceStore.Save(PlayerAppearance.Parse("MixamoSwat:0:0:0:0")); // 준환's saved look in the r5.1 playtest
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return NetHarness.WaitUntil(() => MainMenu.Instance != null && MainMenu.Instance.Panel != null, "main menu", null, 20f);
            var menu = MainMenu.Instance;
            menu.ShowCharacter();
            menu.Character.SetGear(0, 2); // a helmet, so the worn gear is part of what must be visible
            for (float t = Time.realtimeSinceStartup + 1.5f; Time.realtimeSinceStartup < t;) yield return null; // camera glide

            Assert.IsTrue(menu.CharacterShown);
            AssertVisible(menu, "saved look");
            var armor = menu.Stage.CharacterOf(menu.Stage.LocalSlot).GetComponent<ArmorVisual>();
            Assert.IsTrue(armor != null && armor.HelmetVisible, "the equipped helmet is drawn on the character");
            for (int i = 0; i < PlayerAppearance.ModelIds.Length; i++)
            {
                menu.Character.SelectModel(i);
                yield return null;
                AssertVisible(menu, PlayerAppearance.ModelIds[i]);
            }
            menu.Character.SelectModel(Array.IndexOf(PlayerAppearance.ModelIds, "MixamoSwat"));
            yield return null;

            string folder = Environment.GetEnvironmentVariable("CHAR_SHOT");
            folder = Path.GetFullPath(string.IsNullOrEmpty(folder) ? "Logs/charshot" : folder);
            Directory.CreateDirectory(folder);
            yield return UiShot.Save(Path.Combine(folder, "appearance_1920x1080.png"), 1920, 1080);
            menu.Back();
        }

        static void AssertVisible(MainMenu menu, string what)
        {
            var cam = menu.Stage.Camera;
            Assert.IsTrue(cam != null && cam.enabled && cam.gameObject.activeInHierarchy, what + ": the stage camera renders");
            var ch = menu.Stage.CharacterOf(menu.Stage.LocalSlot);
            Assert.IsTrue(ch != null && ch.activeInHierarchy, what + ": the local character is in the closeup");
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            int visible = 0;
            foreach (var r in ch.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.sharedMaterial == null || r is SpriteRenderer) continue;
                if ((cam.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                if (r is SkinnedMeshRenderer s && s.sharedMesh == null) continue;
                if (r is MeshRenderer && (r.GetComponent<MeshFilter>() == null || r.GetComponent<MeshFilter>().sharedMesh == null)) continue;
                visible++;
            }
            Assert.Greater(visible, 0, what + ": a renderer of the local character is in the stage camera's view");
            var mid = cam.WorldToViewportPoint(ch.transform.position + Vector3.up * 1.0f);
            Assert.IsTrue(mid.z > 0f && mid.x > 0f && mid.x < 1f && mid.y > 0f && mid.y < 1f, what + ": the character's middle is inside the view " + mid);
        }
    }
}
