using System;
using System.Collections;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Review shots of the character holding a rifle (#111, ADR-083), taken in the editor like VehicleCaptureTests.
    // Not part of the suite: run on purpose with
    //   POSE_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.PoseCaptureTests
    [Explicit, Category("Capture")]
    public class PoseCaptureTests
    {
        [UnityTest, Timeout(600000)]
        public IEnumerator CapturePoseShots()
        {
            string folder = Environment.GetEnvironmentVariable("POSE_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/poseshots";
            var capture = FeelCapture.StartPoseShots(folder);
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 400f;
            while (!FeelCapture.PoseShotsDone && Time.realtimeSinceStartup < end) yield return null;
            UnityEngine.Object.Destroy(capture.gameObject);
            PlayerController.InputOverride = null;
            Assert.IsTrue(FeelCapture.PoseShotsDone, "shots finished");
        }
    }
}
