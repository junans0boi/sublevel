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
    // #510 (ADR-562): review shots of the race HUD against docs/research/ui_mockups/race_room.html — standings with gaps, the gap to the
    // car ahead, the minimap with car dots, the best lap — around each car's own cluster (GoblinV12 = C, LT80 = D, #518). A solo race on
    // the snake circuit, my car driven by the bot driver; the HUD canvases are drawn through the player camera into a texture, no window.
    //   RACE_HUD_SHOTS=Logs/race_hud tools/unity.sh test playmode Game.Tests.RaceHudCaptureTests
    [Explicit, Category("Capture")]
    public class RaceHudCaptureTests
    {
        sealed class NoPlayerInput : IPlayerInput { public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 }; }
        sealed class CarKeys : IVehicleInput
        {
            public VehicleInputFrame Frame = new VehicleInputFrame { Seat = -1 };
            public VehicleInputFrame Read() => Frame;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MoveGuard.TrustTeleports = false;
            PlayerController.InputOverride = null;
            VehicleInput.InputOverride = null;
            PlayerPrefs.DeleteKey(UiFx.ReducedKey);
            RaceEvent.Starting = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static IEnumerator Until(Func<bool> ok, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!ok()) { Assert.Less(Time.realtimeSinceStartup, end, what); yield return null; }
        }

        [UnityTest, Timeout(600000)] public IEnumerator Capture_GoblinV12_ClusterC() => Race("GoblinV12", 1, "goblinv12");
        [UnityTest, Timeout(600000)] public IEnumerator Capture_LT80_ClusterD() => Race("FairheavenLT80", 4, "lt80");

        IEnumerator Race(string model, int paint, string tag)
        {
            string folder = Environment.GetEnvironmentVariable("RACE_HUD_SHOTS");
            folder = Path.GetFullPath(string.IsNullOrEmpty(folder) ? "Logs/race_hud" : folder);
            Directory.CreateDirectory(folder);
            PlayerPrefs.SetInt(UiFx.ReducedKey, 1);
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return Until(() => MainMenu.Instance != null, 20f, "main menu");
            MoveGuard.TrustTeleports = true;
            PlayerController.InputOverride = new NoPlayerInput();
            var keys = new CarKeys();
            VehicleInput.InputOverride = keys;
            var course = RaceEvent.ById("snake");
            MainMenu.Instance.StartEvent(course, laps: 2, bots: 7, car: model, paint: paint);
            yield return Until(() => RaceDirector.Current != null && Combatant.LocalPlayer != null, 60f, "the race scene and the player");
            yield return Until(() => RaceHud.Phase == RacePhase.Racing, 25f, "the race is on");
            var car = VehicleSeat.VehicleOf(Combatant.LocalPlayer);
            Assert.IsNotNull(car, "at the wheel");
            Assert.AreEqual(model, car.ModelName);

            // Leave the grid a few seconds late (so there is a car ahead), then drive with the bot driver's hands for a while: the field
            // spreads out and the gaps are known.
            yield return new WaitForSeconds(4f);
            float until = Time.realtimeSinceStartup + 22f;
            while (Time.realtimeSinceStartup < until)
            {
                var t = car.transform;
                var (throttle, steer) = RaceBotDriver.Drive(course.Course, t.position.x, t.position.z, t.eulerAngles.y, car.Speed, 0.85f, false);
                keys.Frame.Throttle = throttle; keys.Frame.Steer = steer;
                yield return null;
            }
            var m = RaceHud.Last;
            Assert.IsNotNull(m.Gaps, "the standings carry gaps");
            Assert.AreEqual(m.Keys.Length, m.Gaps.Length);
            Assert.AreEqual(0f, m.Gaps[0], "the leader");
            for (int i = 1; i < m.Gaps.Length; i++) Assert.GreaterOrEqual(m.Gaps[i], 0f, $"row {i}'s gap is known after 22 s");
            Assert.AreNotEqual(Combatant.LocalPlayer.ObjectId, m.Keys[0], "a car ahead of me (the gap line)");
            Snap(folder, $"race_hud_{tag}_1920x1080.png", 1920, 1080);
            Snap(folder, $"race_hud_{tag}_1728x1080.png", 1728, 1080);
            keys.Frame.Throttle = 0f; keys.Frame.Steer = 0f;
        }

        // The overlay canvases drawn through the player camera into a texture (as HudV2CaptureTests.Snap).
        static void Snap(string folder, string name, int w, int h)
        {
            var cam = PlayerController.Local.Camera;
            var rt = new RenderTexture(w, h, 24);
            var before = cam.targetTexture;
            cam.targetTexture = rt;
            var moved = new System.Collections.Generic.List<Canvas>();
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || !c.enabled || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = cam.nearClipPlane + 0.05f;
                Rescale(c);
                moved.Add(c);
            }
            Canvas.ForceUpdateCanvases();
            if (VehicleHud.Instance != null) VehicleHud.Instance.Relayout();
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(folder, name), tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = before;
            foreach (var c in moved) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; Rescale(c); }
            UnityEngine.Object.Destroy(tex); rt.Release(); UnityEngine.Object.Destroy(rt);
        }

        static void Rescale(Canvas c)
        {
            var scaler = c.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler == null) return;
            scaler.enabled = false;
            scaler.enabled = true;
        }
    }
}
