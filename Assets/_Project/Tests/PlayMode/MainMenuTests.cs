using System;
using System.Collections;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // The main menu scene: it shows, opens the settings and starts a match; the Esc menu leads back to it and a second
    // start hosts a fresh match again (one local player, the same combatants). Needs MainMenu in the build
    // (run MainMenuBuilder.Build once).
    public class MainMenuTests
    {
        sealed class IdleInput : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        // The developer's own saved settings must survive a test run.
        float _sensitivity, _volume, _aim;
        int _preset, _view;
        bool _hadSensitivity, _hadVolume, _hadAim, _hadPreset, _hadView;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        [SetUp]
        public void RememberPrefs()
        {
            _hadSensitivity = PlayerPrefs.HasKey(GameSettings.MouseSensitivityKey);
            _hadVolume = PlayerPrefs.HasKey(GameSettings.MasterVolumeKey);
            _hadAim = PlayerPrefs.HasKey(GameSettings.AimSensitivityKey);
            _hadPreset = PlayerPrefs.HasKey(GraphicsOptions.PresetKey);
            _hadView = PlayerPrefs.HasKey(GameSettings.DefaultViewKey);
            _sensitivity = PlayerPrefs.GetFloat(GameSettings.MouseSensitivityKey, 0f);
            _volume = PlayerPrefs.GetFloat(GameSettings.MasterVolumeKey, 0f);
            _aim = PlayerPrefs.GetFloat(GameSettings.AimSensitivityKey, 0f);
            _preset = PlayerPrefs.GetInt(GraphicsOptions.PresetKey, 0);
            _view = PlayerPrefs.GetInt(GameSettings.DefaultViewKey, 0);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Restore(GameSettings.MouseSensitivityKey, _hadSensitivity, _sensitivity);
            Restore(GameSettings.MasterVolumeKey, _hadVolume, _volume);
            Restore(GameSettings.AimSensitivityKey, _hadAim, _aim);
            if (_hadPreset) PlayerPrefs.SetInt(GraphicsOptions.PresetKey, _preset); else PlayerPrefs.DeleteKey(GraphicsOptions.PresetKey);
            if (_hadView) PlayerPrefs.SetInt(GameSettings.DefaultViewKey, _view); else PlayerPrefs.DeleteKey(GameSettings.DefaultViewKey);
            PlayerPrefs.Save();
            GameSettings.Load();

            UiFocus.Close();
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static void Restore(string key, bool had, float value)
        {
            if (had) PlayerPrefs.SetFloat(key, value); else PlayerPrefs.DeleteKey(key);
        }

        static IEnumerator LoadMainMenu()
        {
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return WaitUntil(() => MainMenu.Instance != null, 10f, "the main menu");
            yield return null;
        }

        [UnityTest]
        public IEnumerator MainMenu_ShowsTheMainColumn_AndSettingsSwapWithIt()
        {
            yield return LoadMainMenu();
            var menu = MainMenu.Instance;
            Assert.IsFalse(menu.SettingsShown);
            Assert.IsFalse(menu.IsLoading);

            menu.ShowSettings();
            Assert.IsTrue(menu.SettingsShown);
            menu.Back();
            Assert.IsFalse(menu.SettingsShown);
        }

        [UnityTest]
        public IEnumerator Settings_ClampAndSave()
        {
            yield return LoadMainMenu();

            GameSettings.AimSensitivityFactor = 9f;
            Assert.AreEqual(GameSettings.MaxAimSensitivityFactor, GameSettings.AimSensitivityFactor, 0.0001f);
            GameSettings.AimSensitivityFactor = 0f;
            Assert.AreEqual(GameSettings.MinAimSensitivityFactor, GameSettings.AimSensitivityFactor, 0.0001f);
            GameSettings.AimSensitivityFactor = 0.7f;
            Assert.AreEqual(0.7f, PlayerPrefs.GetFloat(GameSettings.AimSensitivityKey), 0.0001f);

            GameSettings.DefaultViewFirstPerson = true;
            Assert.AreEqual(1, PlayerPrefs.GetInt(GameSettings.DefaultViewKey));
            GameSettings.Load();
            Assert.IsTrue(GameSettings.DefaultViewFirstPerson, "the saved view comes back");
            Assert.AreEqual(0.7f, GameSettings.AimSensitivityFactor, 0.0001f);

            GraphicsOptions.Preset = GraphicsPreset.Low;
            Assert.AreEqual((int)GraphicsPreset.Low, PlayerPrefs.GetInt(GraphicsOptions.PresetKey));
            Assert.AreEqual(GraphicsOptions.ShadowDistance(GraphicsPreset.Low), QualitySettings.shadowDistance, 0.001f);
            GraphicsOptions.Preset = GraphicsPreset.Medium;
            Assert.AreEqual(GraphicsOptions.ShadowDistance(GraphicsPreset.Medium), QualitySettings.shadowDistance, 0.001f);
        }

        [UnityTest]
        public IEnumerator Start_Menu_Start_HostsAFreshMatchEachTime()
        {
            PlayerController.InputOverride = new IdleInput();
            yield return LoadMainMenu();

            MainMenu.Instance.StartGame();
            Assert.IsTrue(MainMenu.Instance.IsLoading);
            Assert.IsNotEmpty(MainMenu.Instance.LoadingText, "a loading line while the match scene loads");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null && PauseMenu.Instance != null, 40f, "the first match");
            yield return new WaitForSeconds(1f);
            int combatants = Combatant.All.Count;
            Assert.Greater(combatants, 1, "the player and the match's bots and dummies");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None).Length, "one local player");
            var first = Combatant.LocalPlayer;

            PauseMenu.Instance.ReturnToMainMenu();
            yield return WaitUntil(() => MainMenu.Instance != null && Combatant.All.Count == 0, 15f, "back at the main menu with the match gone");
            Assert.AreEqual(UiScreen.None, UiFocus.Current);
            yield return null;

            MainMenu.Instance.StartGame();
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null
                && !ReferenceEquals(Combatant.LocalPlayer, first) && Combatant.All.Count == combatants, 40f, "the second match with the same combatants");
            yield return null;
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None).Length, "still one local player");
            Assert.AreEqual(100f, Combatant.LocalPlayer.Hp.Value, "a fresh player");
        }
    }
}
