using System;
using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // ADR-075 in the real TestBox scene as a host: an empty tank stops the car, a jerrycan (J) puts 60% back after
    // five seconds, the vehicle HUD shows speed / fuel / condition, the horn sounds and the engine plays.
    // Set FUEL_SHOT=<png path> to also save one frame of the HUD while driving.
    public class VehicleFuelTests
    {
        sealed class NoPlayerInput : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        sealed class ScriptedVehicleInput : IVehicleInput
        {
            public VehicleInputFrame Frame = new VehicleInputFrame { Seat = -1 };

            public VehicleInputFrame Read()
            {
                var f = Frame;
                Frame.Interact = Frame.EngineToggle = Frame.ToggleView = Frame.Jerrycan = false;
                Frame.Seat = -1;
                return f;
            }
        }

        ScriptedVehicleInput _keys;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        IEnumerator LoadAndDrive()
        {
            _keys = new ScriptedVehicleInput();
            PlayerController.InputOverride = new NoPlayerInput();
            VehicleInput.InputOverride = _keys;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return WaitUntil(() => Car != null, 10f, "the test box's car");
            yield return new WaitForSeconds(1f);
            Assert.IsNotNull(Car.GetComponent<VehicleFuel>(), "the car prefab has a tank (VehicleBuilder)");
            var me = Combatant.LocalPlayer;
            PlayerController.Local.Teleport(Car.DoorPosition(0) + Vector3.up * 0.05f);
            yield return null;
            yield return null;
            _keys.Frame.Interact = true;
            yield return WaitUntil(() => VehicleSeat.TryGet(me, out _, out int seat) && seat == 0, 3f, "in the driver's seat");
            yield return new WaitForSeconds(Car.Rules.EnterSeconds + 0.3f);
        }

        static Vehicle Car
        {
            get
            {
                foreach (var v in Vehicle.All)
                    if (v != null && v.Kind == VehicleKind.Car) return v;
                return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            VehicleInput.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator EmptyTank_StopsTheEngine_JerrycanRefuelsSixtyPercent()
        {
            yield return LoadAndDrive();
            var car = Car;
            var tank = car.GetComponent<VehicleFuel>();
            var me = Combatant.LocalPlayer;
            tank.ServerSetFuel(0f);
            yield return WaitUntil(() => !car.EngineOn.Value, 2f, "the engine stops on an empty tank");
            var start = car.transform.position;
            _keys.Frame.Throttle = 1f;
            yield return new WaitForSeconds(1.5f);
            _keys.Frame.Throttle = 0f;
            Assert.Less(Vector3.Distance(car.transform.position, start), 1f, "no pull without fuel");
            Assert.IsFalse(car.EngineOn.Value, "W does not restart a dry engine");
            Assert.IsTrue(VehicleHud.Instance != null && VehicleHud.Instance.PlateShown, "the vehicle plate is up");
            Assert.AreEqual(0, VehicleHud.Instance.FuelShown);
            Assert.IsTrue(VehicleHud.Instance.FuelLowShown);

            var cans = me.GetComponent<CombatantJerrycan>();
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Jerrycan, Amount = 1 }), "a jerrycan in the bag");
            Assert.AreEqual(1, cans.Count.Value);
            yield return WaitUntil(() => VehicleHud.Instance.JerrycanShown, 2f, "the jerrycan prompt");
            StringAssert.Contains("J", VehicleHud.Instance.JerrycanText);

            _keys.Frame.Jerrycan = true;
            yield return WaitUntil(() => cans.Using, 1f, "pouring");
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(0f, tank.Fuel01, 1e-4f, "nothing goes in before the time is out");
            yield return WaitUntil(() => !cans.Using, FuelRules.Current.JerrycanSeconds + 2f, "the pour finishes");
            Assert.AreEqual(FuelRules.Current.JerrycanAmount, tank.Fuel01, 0.01f);
            Assert.AreEqual(0, cans.Count.Value, "the can is used up");
            yield return WaitUntil(() => VehicleHud.Instance.FuelShown == 60, 1f, "the gauge reads 60%");

            _keys.Frame.Throttle = 1f; // W starts the engine again
            yield return WaitUntil(() => car.EngineOn.Value, 2f, "the engine restarts with fuel");
            yield return new WaitForSeconds(1.5f);
            Assert.Greater(Vector3.Distance(car.transform.position, start), 2f, "it drives again");
            Assert.Greater(VehicleHud.Instance.SpeedShown, 0, "the speed readout moves");
            Assert.Less(tank.Fuel01, FuelRules.Current.JerrycanAmount, "driving burns fuel");
            _keys.Frame.Throttle = -1f;

            string shot = Environment.GetEnvironmentVariable("FUEL_SHOT");
            if (!string.IsNullOrEmpty(shot)) Shot(shot);
        }

        [UnityTest]
        public IEnumerator Horn_SoundsWhileHeld_EnginePlays()
        {
            yield return LoadAndDrive();
            var car = Car;
            var horn = car.GetComponent<VehicleHorn>();
            var audio = car.GetComponent<VehicleAudio>();
            Assert.IsNotNull(horn);
            Assert.IsNotNull(audio);
            yield return WaitUntil(() => audio.EnginePlaying, 2f, "the engine sound");
            float idle = audio.EnginePitch;

            _keys.Frame.Horn = true;
            yield return WaitUntil(() => horn.Sounding && audio.HornPlaying, 1f, "the horn");
            Assert.AreEqual(1, horn.ServerPresses);
            _keys.Frame.Horn = false;
            yield return WaitUntil(() => !horn.Sounding && !audio.HornPlaying, 1f, "the horn stops");

            _keys.Frame.Throttle = 1f;
            yield return new WaitForSeconds(1.5f);
            Assert.Greater(audio.EnginePitch, idle, "the note rises with speed");
            _keys.Frame.Throttle = -1f;
        }

        // The overlay canvases onto the player's camera for one rendered frame.
        static void Shot(string path)
        {
            var cam = PlayerController.Local.Camera;
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (var c in canvases)
            {
                if (!c.isRootCanvas || !c.enabled) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = cam.nearClipPlane + 0.05f;
            }
            Canvas.ForceUpdateCanvases();
            const int w = 1920, h = 1080;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var before = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = before;
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }
    }
}
