using System;
using System.Collections;
using System.IO;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Review shots of the network status line (#171) in the TestBox match, good / fair / bad. Run on purpose:
    //   NET_SHOTS=<folder> tools/unity.sh run-gfx ... (or test playmode Game.Tests.NetStatusCaptureTests)
    [Explicit, Category("Capture")]
    public class NetStatusCaptureTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameSettings.SetAndSave(SettingId.NetStats, 0f);
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator CaptureLine()
        {
            string folder = Environment.GetEnvironmentVariable("NET_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/netshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            PlayerController.InputOverride = new HarnessInput();
            GameSettings.SetAndSave(SettingId.NetStats, 1f);
            SceneManager.LoadScene("TestBox");
            yield return NetHarness.WaitUntil(() => PlayerController.Local != null && NetStatusView.Instance != null, "player and line", null, 30f);
            var view = NetStatusView.Instance;
            yield return new WaitForSeconds(1.5f);
            // Live numbers first (a host: no loss, no ping), then chosen ones for the two worse grades.
            yield return UiShot.Save(Path.Combine(folder, "net_1_live.png"), 1920, 1080);
            view.enabled = false; // keep the chosen numbers on screen
            view.Apply(48, 0.005f, true, 48211, 60);
            yield return UiShot.Save(Path.Combine(folder, "net_2_good.png"), 1920, 1080);
            view.Apply(85, 0.02f, true, 48211, 60);
            yield return UiShot.Save(Path.Combine(folder, "net_3_fair.png"), 1920, 1080);
            view.Apply(190, 0.07f, true, 48211, 60);
            yield return UiShot.Save(Path.Combine(folder, "net_4_bad.png"), 1920, 1080);
            view.Apply(0, 0f, false, 0, 60);
            yield return UiShot.Save(Path.Combine(folder, "net_5_nopulses.png"), 1280, 720);
        }
    }
}
