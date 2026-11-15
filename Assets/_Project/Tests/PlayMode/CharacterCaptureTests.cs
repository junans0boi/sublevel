using System;
using System.Collections;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Character review shots (CHARACTER_AUDIT). Not part of the suite: run on purpose with
    //   CHAR_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.CharacterCaptureTests
    [Explicit, Category("Capture")]
    public class CharacterCaptureTests
    {
        [UnityTest, Timeout(1500000)]
        public IEnumerator CaptureCharacterShots()
        {
            string folder = Environment.GetEnvironmentVariable("CHAR_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/charshots";
            var capture = FeelCapture.StartCharShots(folder);
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 1300f;
            while (!FeelCapture.PoseShotsDone && Time.realtimeSinceStartup < end) yield return null;
            UnityEngine.Object.Destroy(capture.gameObject);
            PlayerController.InputOverride = null;
            Assert.IsTrue(FeelCapture.PoseShotsDone, "shots finished");
        }
    }
}
