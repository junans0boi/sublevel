using System;
using System.Collections;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Review shots of the vehicle models (#89) taken in the editor (batch mode renders the player camera into a texture,
    // so this works with the screen locked). Not part of the suite: run on purpose with
    //   VEH_SHOTS=<folder> VEH_SCENE=TestBox|World tools/unity.sh test playmode Game.Tests.VehicleCaptureTests
    [Explicit, Category("Capture")]
    public class VehicleCaptureTests
    {
        [UnityTest, Timeout(900000)]
        public IEnumerator CaptureVehicleShots()
        {
            string folder = Environment.GetEnvironmentVariable("VEH_SHOTS");
            string scene = Environment.GetEnvironmentVariable("VEH_SCENE");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/vehshots";
            if (string.IsNullOrEmpty(scene)) scene = "TestBox";
            SceneManager.LoadScene(scene);
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Game.Net.Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            var capture = FeelCapture.StartVehicleShots(folder + "/" + scene);
            end = Time.realtimeSinceStartup + 840f;
            while (!FeelCapture.VehicleShotsDone && Time.realtimeSinceStartup < end) yield return null;
            UnityEngine.Object.Destroy(capture.gameObject);
            PlayerController.InputOverride = null;
            VehicleInput.InputOverride = null;
            Assert.IsTrue(FeelCapture.VehicleShotsDone, "shots finished");
        }
    }
}
