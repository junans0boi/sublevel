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
    // Review shots of swimming (#138, ADR-112): the lying swim pose at the surface from the side and behind, the diver
    // under water, and the player's own view with the breath bar and the under-water wash. Run on purpose with
    //   SWIM_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.SwimCaptureTests
    [Explicit, Category("Capture")]
    public class SwimCaptureTests
    {
        sealed class Input : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read() => Frame;
        }

        [DefaultExecutionOrder(30000)]
        sealed class LateShot : MonoBehaviour
        {
            public string Path;
            public Camera Cam;

            void LateUpdate()
            {
                if (Path == null || Cam == null) return;
                const int w = 1280, h = 720;
                // The HUD canvas of the swimmer is drawn through this camera for the picture.
                foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (canvas.name != "SwimHud") continue;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = Cam;
                    canvas.planeDistance = 1f;
                }
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                Cam.targetTexture = rt;
                Cam.Render();
                Cam.targetTexture = null;
                var active = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(Path, tex.EncodeToPNG());
                Path = null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            EnvironmentView.ForceTime = null;
            EnvironmentView.ForceWeather = null;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator CaptureSwim()
        {
            string folder = Environment.GetEnvironmentVariable("SWIM_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/swimshots";
            folder = System.IO.Path.GetFullPath(folder);
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
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            yield return new WaitForSeconds(0.6f);
            var c = new Vector3(3000f, 0f, 3000f);
            TestPool.Create(c, 12f, 2.0f, out _);
            Physics.SyncTransforms();
            pc.Teleport(c + new Vector3(0f, 0.1f, 0f));
            pc.LookAt(c + new Vector3(0f, 2f, 20f));

            var go = new GameObject("SwimShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            if (pc.Camera != null) cam.CopyFrom(pc.Camera);
            cam.fieldOfView = 55f;
            var shot = go.AddComponent<LateShot>();
            shot.Cam = cam;

            // Floating, still, then swimming forward.
            yield return new WaitForSeconds(1.6f);
            var at = pc.transform.position;
            cam.transform.SetPositionAndRotation(c + new Vector3(4.5f, 2.6f, 0.5f), Quaternion.LookRotation(at + Vector3.up * 0.3f - (c + new Vector3(4.5f, 2.6f, 0.5f))));
            shot.Path = System.IO.Path.Combine(folder, "swim_01_float_side.png");
            yield return null; yield return null;
            input.Frame.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(1.0f);
            at = pc.transform.position;
            var side0 = at + new Vector3(3.2f, 0.9f, 0f);
            cam.transform.SetPositionAndRotation(side0, Quaternion.LookRotation(at + Vector3.up * 0.1f - side0));
            shot.Path = System.IO.Path.Combine(folder, "swim_02a_stroke_side.png");
            yield return null; yield return null;
            yield return new WaitForSeconds(0.37f);
            at = pc.transform.position;
            side0 = at + new Vector3(3.2f, 0.9f, 0f);
            cam.transform.SetPositionAndRotation(side0, Quaternion.LookRotation(at + Vector3.up * 0.1f - side0));
            shot.Path = System.IO.Path.Combine(folder, "swim_02b_stroke_side_later.png");
            yield return null; yield return null;
            at = pc.transform.position;
            var from = at + new Vector3(2.8f, 1.2f, -1.6f);
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at + Vector3.up * 0.2f - from));
            shot.Path = System.IO.Path.Combine(folder, "swim_02_swimming_front3q.png");
            yield return null; yield return null;
            from = at + new Vector3(0f, 1.3f, -3.0f);
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at + Vector3.up * 0.2f - from));
            shot.Path = System.IO.Path.Combine(folder, "swim_03_swimming_behind.png");
            yield return null; yield return null;
            input.Frame.Move = Vector2.zero;

            // Diving: seen from beside, under the surface, and through the player's own camera with the HUD.
            input.Frame.SlowWalk = true;
            yield return new WaitForSeconds(1.6f);
            at = pc.transform.position;
            from = at + new Vector3(3.2f, 0.4f, 0.4f);
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at + Vector3.up * 0.2f - from));
            shot.Path = System.IO.Path.Combine(folder, "swim_04_dive_side.png");
            yield return null; yield return null;
            if (pc.Camera != null) cam.CopyFrom(pc.Camera);
            cam.transform.SetPositionAndRotation(pc.Camera.transform.position, pc.Camera.transform.rotation);
            yield return new WaitForSeconds(0.3f);
            shot.Path = System.IO.Path.Combine(folder, "swim_05_dive_own_view.png");
            yield return null; yield return null;
            input.Frame.SlowWalk = false;
            yield return new WaitForSeconds(1.6f);
            cam.transform.SetPositionAndRotation(pc.Camera.transform.position, pc.Camera.transform.rotation);
            shot.Path = System.IO.Path.Combine(folder, "swim_06_surface_own_view.png");
            yield return null; yield return null;
            Debug.Log("[SwimCapture] shots in " + folder);
        }

        // The sea at a beach of the real map (#146): the player walks in, swims in the band before the wall.
        [UnityTest, Timeout(240000)]
        public IEnumerator CaptureCoast()
        {
            string folder = Environment.GetEnvironmentVariable("SWIM_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/swimshots";
            folder = System.IO.Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            var input = new Input();
            PlayerController.InputOverride = input;
            EnvironmentView.ForceTime = DayTimeChoice.Day;
            EnvironmentView.ForceWeather = WeatherChoice.Clear;
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 40f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            end = Time.realtimeSinceStartup + 30f;
            while (SpawnProtection.InputBlocked && Time.realtimeSinceStartup < end) yield return null;
            yield return new WaitForSeconds(0.5f);
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(WorldSwimPlayTests.FindShore(out var land, out var outward));
            pc.Teleport(land);
            pc.LookAt(land + outward * 50f + Vector3.up * land.y);
            var go = new GameObject("SwimShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            if (pc.Camera != null) cam.CopyFrom(pc.Camera);
            cam.fieldOfView = 55f;
            var shot = go.AddComponent<LateShot>();
            shot.Cam = cam;
            yield return new WaitForSeconds(0.5f);
            // On the beach, then wading, then swimming.
            var at = pc.transform.position;
            var side = Vector3.Cross(Vector3.up, outward);
            var from = at - outward * 5f + side * 4f + Vector3.up * 2.5f;
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at + outward * 4f - from));
            shot.Path = System.IO.Path.Combine(folder, "coast_01_beach.png");
            yield return null; yield return null;
            input.Frame.Move = new Vector2(0f, 1f);
            float until = Time.realtimeSinceStartup + 40f;
            while (!pc.Swimming && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(pc.Swimming);
            yield return new WaitForSeconds(2.5f);
            at = pc.transform.position;
            from = at - outward * 3.5f + side * 3f + Vector3.up * 1.3f;
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at + outward * 1f - from));
            shot.Path = System.IO.Path.Combine(folder, "coast_02_swimming_behind.png");
            yield return null; yield return null;
            from = at + outward * 3.5f + side * 3.5f + Vector3.up * 1.2f;
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from + Vector3.up * 0.1f));
            shot.Path = System.IO.Path.Combine(folder, "coast_03_swimming_front.png");
            yield return null; yield return null;
            Debug.Log($"[SwimCapture] own cam {pc.Camera.transform.position} fwd {pc.Camera.transform.forward} body {pc.transform.position} eye {me.EyePosition} sea {WorldInfo.Current.SeaLevel} headUnder {me.GetComponent<CombatantSwim>().HeadUnder}");
            cam.CopyFrom(pc.Camera);
            cam.transform.SetPositionAndRotation(pc.Camera.transform.position, pc.Camera.transform.rotation);
            shot.Path = System.IO.Path.Combine(folder, "coast_04_own_view.png");
            yield return null; yield return null;
            // From above, the whole strip.
            from = at - outward * 22f + Vector3.up * 24f + side * 8f;
            cam.fieldOfView = 60f;
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            shot.Path = System.IO.Path.Combine(folder, "coast_05_overview.png");
            yield return null; yield return null;
            input.Frame.Move = Vector2.zero;
            Debug.Log("[SwimCapture] coast shots in " + folder);
        }
    }
}
