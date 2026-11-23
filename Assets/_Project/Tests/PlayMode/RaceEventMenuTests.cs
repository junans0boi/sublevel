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
    // #445 (ADR-512): 이벤트 in the lobby's top bar opens the event screen; 시작하기 starts the chosen course's race scene.
    public class RaceEventMenuTests
    {
        static IEnumerator LoadMenu()
        {
            PlayerPrefs.SetInt(UiFx.ReducedKey, 1);
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return NetHarness.WaitUntil(() => MainMenu.Instance != null, "main menu", null, 20f);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerPrefs.DeleteKey(UiFx.ReducedKey);
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator EventScreen_StartsTheChosenCourse()
        {
            yield return LoadMenu();
            var menu = MainMenu.Instance;
            menu.ShowEventEntry();
            Assert.IsTrue(menu.EventEntryShown);
            Assert.AreEqual(1, menu.ActiveScreenRoots);
            menu.EventEntry.Select(0);
            menu.EventEntry.Start();
            yield return NetHarness.WaitUntil(() => RaceDirector.Current != null && Combatant.LocalPlayer != null, "the race scene and the player", null, 60f);
            Assert.AreEqual(RaceEvent.Courses[0].Scene, SceneManager.GetActiveScene().name);
            Assert.IsTrue(Vehicle.EventRace);
        }

        // RACE_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.RaceEventMenuTests.CaptureEventScreen
        [UnityTest, Explicit, Category("Capture"), Timeout(120000)]
        public IEnumerator CaptureEventScreen()
        {
            string folder = System.Environment.GetEnvironmentVariable("RACE_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/lookdev";
            Directory.CreateDirectory(folder);
            yield return LoadMenu();
            yield return UiShot.Save(Path.Combine(folder, "event_lobby_tab.png"), 1920, 1080);
            MainMenu.Instance.ShowEventEntry();
            yield return new WaitForSeconds(0.5f);
            yield return UiShot.Save(Path.Combine(folder, "event_screen.png"), 1920, 1080);
            // Other screen shapes: a Mac (16:10), an old 4:3-ish monitor and an ultrawide.
            foreach (var (w, h) in new[] { (1440, 900), (1280, 1024), (2560, 1080) })
                yield return UiShot.Save(Path.Combine(folder, $"event_screen_{w}x{h}.png"), w, h);
        }
    }
}
