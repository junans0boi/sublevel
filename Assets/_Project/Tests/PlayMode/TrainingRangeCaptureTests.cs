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
    // Review shots of the training range (#153): overview, shooting lane, item table, movement course, vehicle pad and
    // door house, the checklist HUD and the main menu entry. Not part of the suite: run on purpose with
    //   TRAINING_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.TrainingRangeCaptureTests
    [Explicit, Category("Capture")]
    public class TrainingRangeCaptureTests
    {
        sealed class Input : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = Frame.Interact = Frame.Reload = Frame.Crouch = Frame.Prone = false;
                return f;
            }
        }

        static string Folder()
        {
            string folder = Environment.GetEnvironmentVariable("TRAINING_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/trainingshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            return folder;
        }

        static void Render(Camera cam, string path)
        {
            const int w = 1600, h = 900;
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
            Debug.Log("[TrainingCapture] " + path);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator CaptureRange()
        {
            string folder = Folder();
            var input = new Input();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene(TrainingRange.SceneName);
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null || TrainingRange.Instance == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(2f);
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            var go = new GameObject("ShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.CopyFrom(pc.Camera);
            cam.farClipPlane = 400f;
            cam.fieldOfView = 60f;

            IEnumerator Shot(string name, Vector3 from, Vector3 at, float fov = 60f)
            {
                cam.fieldOfView = fov;
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
                yield return null;
                Render(cam, Path.Combine(folder, name + ".png"));
            }

            // Overviews.
            yield return Shot("range_01_overview_south", new Vector3(10f, 40f, -85f), new Vector3(10f, 0f, 10f), 70f);
            yield return Shot("range_02_overview_lane_from_air", new Vector3(0f, 30f, -20f), new Vector3(0f, 0f, 90f), 60f);
            yield return Shot("range_03_overview_west_south", new Vector3(-60f, 22f, -30f), new Vector3(0f, 0f, -20f), 70f);
            // Shooting lane, at the firing line.
            yield return Shot("lane_01_firing_line", new Vector3(0f, 1.7f, 1f), new Vector3(0f, 1.5f, 120f), 50f);
            yield return Shot("lane_02_near_targets", new Vector3(0f, 2.2f, -1f), new Vector3(0f, 1.2f, 28f), 60f);
            yield return Shot("lane_03_far_targets_200m", new Vector3(0f, 1.7f, 150f), new Vector3(0f, 1.5f, 200f), 35f);
            // Table.
            yield return Shot("table_01_items", new Vector3(-9f, 2.6f, -3f), new Vector3(-14.5f, 1f, 0.5f), 70f);
            yield return Shot("table_02_close", new Vector3(-11.5f, 1.9f, -4f), new Vector3(-14.5f, 1f, -3f), 60f);
            // Course.
            yield return Shot("course_01_start_to_ledge", new Vector3(12f, 4f, -14f), new Vector3(45f, 1f, -14f), 60f);
            yield return Shot("course_02_stairs_pool", new Vector3(46f, 9f, -14f), new Vector3(75f, -1f, -14f), 60f);
            yield return Shot("course_03_pool_water", new Vector3(72f, 3.5f, -9f), new Vector3(84f, -1f, -16f), 65f);
            yield return Shot("course_04_gap_and_vault", new Vector3(20f, 1.6f, -14f), new Vector3(34f, 1.2f, -14f), 60f);
            // Vehicles and house.
            yield return Shot("pad_01_vehicles", new Vector3(0f, 5f, -22f), new Vector3(0f, 0.8f, -33f), 60f);
            yield return Shot("house_01_doors", new Vector3(-28f, 5f, -26f), new Vector3(-36f, 1f, -36f), 60f);

            // The player's own view with the checklist: move, sprint, crouch, shoot a few targets.
            input.Frame.Move = new Vector2(0f, 1f);
            input.Frame.Sprint = true;
            yield return new WaitForSeconds(0.8f);
            input.Frame.Move = Vector2.zero;
            input.Frame.Sprint = false;
            input.Frame.Crouch = true;
            yield return new WaitForSeconds(0.5f);
            input.Frame.Crouch = true;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 90 });
            yield return new WaitForSeconds(0.8f);
            pc.Teleport(new Vector3(0f, 0.1f, 2f));
            for (int i = 0; i < 25; i++)
            {
                pc.LookAt(new Vector3(0f, 1.2f, 25f));
                input.Frame.FireHeld = true;
                yield return null;
            }
            input.Frame.FireHeld = false;
            pc.LookAt(new Vector3(0f, 1.8f, 60f));
            yield return new WaitForSeconds(0.3f);
            yield return UiShot.Save(Path.Combine(folder, "hud_01_checklist.png"), 1920, 1080);
            Debug.Log($"[TrainingCapture] done {TrainingRange.Instance.Checklist.DoneCount}/{TrainingChecklist.Count}, hits {TrainingRange.Instance.TargetHits}");
            UnityEngine.Object.Destroy(go);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator CaptureMainMenu()
        {
            SceneManager.LoadScene(MainMenu.SceneName);
            float end = Time.realtimeSinceStartup + 20f;
            while (MainMenu.Instance == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no main menu");
                yield return null;
            }
            yield return null;
            // The menu camera draws nothing itself (culling mask 0); the shot's copy must still draw the UI layer.
            Camera.main.cullingMask = -1;
            yield return UiShot.Save(Path.Combine(Folder(), "menu_01_main.png"), 1920, 1080);
        }
    }
}
