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
    // Shots of the 환경 설정 screen: every tab x resolution, over the 3D main menu and over the match (Esc menu).
    // No sound. Run on purpose with  UI_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.SettingsCaptureTests
    [Explicit, Category("Capture")]
    public class SettingsCaptureTests
    {
        static readonly int[][] Sizes = { new[] { 1280, 720 }, new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 1920, 1200 } };

        sealed class IdleInput : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        GameSettings.PrefsSnapshot _prefs;

        [SetUp]
        public void Remember() => _prefs = GameSettings.CapturePrefs();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameSettings.RestorePrefs(_prefs);
            UiFocus.Close();
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static string Folder()
        {
            string folder = Environment.GetEnvironmentVariable("UI_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/uishots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            return folder;
        }

        static IEnumerator AllTabs(SettingsScreen s, string folder, string prefix)
        {
            var steps = new (string name, SettingsTab tab, SettingsPage page)[]
            {
                ("gfx", SettingsTab.Graphics, SettingsPage.Basic), ("gfxadv", SettingsTab.Graphics, SettingsPage.Advanced),
                ("audio", SettingsTab.Audio, SettingsPage.Basic), ("controls", SettingsTab.Controls, SettingsPage.Basic),
                ("keys", SettingsTab.Keys, SettingsPage.Basic), ("gameplay", SettingsTab.Gameplay, SettingsPage.Basic),
            };
            foreach (var st in steps)
            {
                s.SelectTab(st.tab);
                s.SelectPage(st.page);
                if (s.VisibleRows.Count > 1) s.Focus(s.VisibleRows[1]);
                foreach (var size in Sizes)
                    yield return UiShot.Save(Path.Combine(folder, prefix + "_" + st.name + "_" + size[0] + "x" + size[1] + ".png"), size[0], size[1]);
            }
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Settings_OverMainMenu()
        {
            string folder = Folder();
            PlayerPrefs.SetInt(UiFx.ReducedKey, 1);
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return NetHarness.WaitUntil(() => MainMenu.Instance != null, "main menu", null, 20f);
            yield return null;
            Camera.main.cullingMask = -1;
            var menu = MainMenu.Instance;
            menu.ShowSettings();
            yield return AllTabs(menu.Settings, folder, "menu");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Settings_OverMatch()
        {
            string folder = Folder();
            PlayerPrefs.SetInt(UiFx.ReducedKey, 1);
            PlayerController.InputOverride = new IdleInput();
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 20f;
            while (!(PlayerController.Local != null && Combatant.LocalPlayer != null && PauseMenu.Instance != null))
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("player not spawned");
                yield return null;
            }
            yield return null;
            UiFocus.Set(UiScreen.Menu);
            PauseMenu.Instance.ShowSettings();
            yield return null;
            yield return AllTabs(PauseMenu.Instance.Settings, folder, "pause");
        }
    }
}
