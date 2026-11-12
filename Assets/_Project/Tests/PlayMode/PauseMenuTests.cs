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
    // The Esc menu in the real TestBox scene with injected input: it shows with the menu focus, holds the
    // player still while open, swaps to settings and back, saves settings, and restarts into a fresh match.
    public class PauseMenuTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = false;
                return f;
            }
        }

        ScriptedInput _input;
        bool _hadSensitivity, _hadVolume;
        float _savedSensitivity, _savedVolume;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        IEnumerator LoadTestBox()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
        }

        // The developer's own saved settings must survive a test run.
        [SetUp]
        public void RememberPrefs()
        {
            _hadSensitivity = PlayerPrefs.HasKey(GameSettings.MouseSensitivityKey);
            _hadVolume = PlayerPrefs.HasKey(GameSettings.MasterVolumeKey);
            _savedSensitivity = PlayerPrefs.GetFloat(GameSettings.MouseSensitivityKey, 0f);
            _savedVolume = PlayerPrefs.GetFloat(GameSettings.MasterVolumeKey, 0f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_hadSensitivity) PlayerPrefs.SetFloat(GameSettings.MouseSensitivityKey, _savedSensitivity);
            else PlayerPrefs.DeleteKey(GameSettings.MouseSensitivityKey);
            if (_hadVolume) PlayerPrefs.SetFloat(GameSettings.MasterVolumeKey, _savedVolume);
            else PlayerPrefs.DeleteKey(GameSettings.MasterVolumeKey);
            PlayerPrefs.Save();
            GameSettings.Load();

            UiFocus.Close();
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static Vector2 Ground(Vector3 position) => new Vector2(position.x, position.z);

        [UnityTest]
        public IEnumerator Menu_IsHiddenAtSpawn_AndShowsWithTheMenuFocus()
        {
            yield return LoadTestBox();
            Assert.IsNotNull(PauseMenu.Instance, "the local player gets a pause menu");
            Assert.IsFalse(PauseMenu.Instance.IsOpen, "hidden at spawn");

            UiFocus.Set(UiScreen.Menu);
            Assert.IsTrue(PauseMenu.Instance.IsOpen, "shown with the menu focus");
            Assert.IsFalse(PauseMenu.Instance.SettingsShown);

            UiFocus.Set(UiScreen.Inventory);
            Assert.IsFalse(PauseMenu.Instance.IsOpen, "another screen is not the menu");
        }

        [UnityTest]
        public IEnumerator OpenMenu_HoldsThePlayerStill_UntilResume()
        {
            yield return LoadTestBox();
            var pc = PlayerController.Local;

            UiFocus.Set(UiScreen.Menu);
            _input.Frame.Move = new Vector2(0, 1);
            yield return null;
            Vector2 start = Ground(pc.transform.position);
            yield return new WaitForSeconds(0.5f);
            Assert.Less(Vector2.Distance(Ground(pc.transform.position), start), 0.05f, "input is ignored while the menu is open");

            PauseMenu.Instance.Resume();
            Assert.AreEqual(UiScreen.None, UiFocus.Current);
            Assert.IsFalse(PauseMenu.Instance.IsOpen);
            yield return null;
            start = Ground(pc.transform.position);
            yield return new WaitForSeconds(0.5f);
            Assert.Greater(Vector2.Distance(Ground(pc.transform.position), start), 0.5f, "moving again after resume");
        }

        [UnityTest]
        public IEnumerator Settings_SwapWithTheMainColumn_AndReopenOnTheMainColumn()
        {
            yield return LoadTestBox();
            var menu = PauseMenu.Instance;
            UiFocus.Set(UiScreen.Menu);

            menu.ShowSettings();
            Assert.IsTrue(menu.SettingsShown);
            menu.Back();
            Assert.IsFalse(menu.SettingsShown);
            Assert.IsTrue(menu.IsOpen, "back stays in the menu");

            menu.ShowSettings();
            UiFocus.Close();
            Assert.IsFalse(menu.IsOpen);
            UiFocus.Set(UiScreen.Menu);
            Assert.IsTrue(menu.IsOpen);
            Assert.IsFalse(menu.SettingsShown, "reopening starts on the main column");
        }

        [UnityTest]
        public IEnumerator Settings_ApplyClampAndSave()
        {
            yield return LoadTestBox();

            GameSettings.MouseSensitivity = 0.2f;
            Assert.AreEqual(0.2f, DeviceInput.MouseSensitivity, 0.0001f);
            Assert.AreEqual(0.2f, PlayerPrefs.GetFloat(GameSettings.MouseSensitivityKey), 0.0001f);
            GameSettings.MouseSensitivity = 5f;
            Assert.AreEqual(0.4f, DeviceInput.MouseSensitivity, 0.0001f, "clamped to the slider range");

            GameSettings.MasterVolume = 0.5f;
            Assert.AreEqual(0.5f, AudioListener.volume, 0.0001f);
            GameSettings.MasterVolume = -1f;
            Assert.AreEqual(0f, AudioListener.volume, 0.0001f);
        }

        [UnityTest]
        public IEnumerator Restart_StartsAFreshMatch()
        {
            yield return LoadTestBox();
            var before = Combatant.LocalPlayer;
            before.ServerTakeDamage(40);
            yield return WaitUntil(() => Mathf.Approximately(before.Hp.Value, 60f), 2f, "damage applied");

            UiFocus.Set(UiScreen.Menu);
            PauseMenu.Instance.Restart();
            Assert.AreEqual(UiScreen.None, UiFocus.Current, "restart closes the menu");

            // The old player is destroyed with the scene (and compares equal to null from then on).
            yield return WaitUntil(() => Combatant.LocalPlayer != null && !ReferenceEquals(Combatant.LocalPlayer, before)
                && PlayerController.Local != null && PauseMenu.Instance != null, 15f, "a new local player after the restart");
            yield return null;
            Assert.AreEqual(100f, Combatant.LocalPlayer.Hp.Value, "full health in the new match");
            Assert.IsFalse(PauseMenu.Instance.IsOpen, "the new match starts with the menu closed");
        }
    }
}
