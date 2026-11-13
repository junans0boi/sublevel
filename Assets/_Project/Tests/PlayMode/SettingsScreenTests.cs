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
    // The 환경 설정 screen (ADR-059) opened from the Esc menu in the TestBox scene: tabs swap the rows, changes preview
    // at once, 뒤로 가기 drops them, 적용 keeps and saves them, 기본 resets the current tab only.
    public class SettingsScreenTests
    {
        sealed class IdleInput : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        GameSettings.PrefsSnapshot _prefs;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        // The developer's own saved settings must survive a test run; every test starts from the defaults.
        [SetUp]
        public void RememberPrefs()
        {
            _prefs = GameSettings.CapturePrefs();
            foreach (var def in SettingsCatalog.All) PlayerPrefs.DeleteKey(def.Key);
            GameSettings.Load();
        }

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

        static IEnumerator OpenFromPauseMenu()
        {
            PlayerController.InputOverride = new IdleInput();
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null && PauseMenu.Instance != null,
                15f, "local player spawned");
            yield return null;
            UiFocus.Set(UiScreen.Menu);
            PauseMenu.Instance.ShowSettings();
            yield return null;
        }

        static SettingsScreen Shown => PauseMenu.Instance.Settings;

        [UnityTest]
        public IEnumerator Opens_FromThePauseMenu_OnTheGraphicsTab()
        {
            yield return OpenFromPauseMenu();
            Assert.IsTrue(PauseMenu.Instance.SettingsShown);
            Assert.IsNotNull(Shown);
            Assert.IsTrue(Shown.Root.gameObject.activeInHierarchy);
            Assert.AreEqual(SettingsTab.Graphics, Shown.CurrentTab);
            Assert.AreEqual(SettingsPage.Basic, Shown.CurrentPage);
            Assert.Greater(Shown.VisibleRows.Count, 0);
            Assert.IsNotNull(Shown.FocusedRow, "the first row is described");
            Assert.AreEqual(Shown.VisibleRows[0].Label, Shown.DescriptionTitle);
            Assert.IsNotEmpty(Shown.DescriptionBody);
            Assert.IsFalse(Shown.HasUnappliedChanges);

            Shown.Focus(Shown.Row(SettingId.Brightness));
            Assert.IsTrue(Shown.PreviewSwatchShown, "brightness shows its swatch");
            Shown.Focus(Shown.Row(SettingId.VSync));
            Assert.IsFalse(Shown.PreviewSwatchShown);
        }

        [UnityTest]
        public IEnumerator Tabs_SwapTheRows()
        {
            yield return OpenFromPauseMenu();
            var screen = Shown;

            screen.SelectTab(SettingsTab.Audio);
            Assert.AreEqual(4, screen.VisibleRows.Count);
            foreach (var row in screen.VisibleRows) Assert.AreEqual(SettingsTab.Audio, row.Def.Tab);
            Assert.IsTrue(screen.Row(SettingId.MasterVolume).gameObject.activeInHierarchy);
            Assert.IsFalse(screen.Row(SettingId.MouseSensitivity).gameObject.activeInHierarchy, "other tabs are hidden");

            screen.SelectTab(SettingsTab.Graphics);
            screen.SelectPage(SettingsPage.Advanced);
            Assert.IsTrue(screen.Row(SettingId.Shadows).gameObject.activeInHierarchy);
            Assert.IsFalse(screen.Row(SettingId.Brightness).gameObject.activeInHierarchy);

            screen.SelectTab(SettingsTab.Keys);
            Assert.AreEqual(SettingsCatalog.KeyBindings.Length, screen.VisibleRows.Count);
            foreach (var row in screen.VisibleRows) Assert.IsNotNull(row.Binding);
            StringAssert.Contains("추후", screen.DescriptionBody, "key rows say rebinding comes later");

            screen.SelectTab(SettingsTab.Controls);
            Assert.AreEqual(SettingsPage.Basic, screen.CurrentPage);
            screen.SelectPage(SettingsPage.Advanced);
            Assert.AreEqual(SettingsPage.Basic, screen.CurrentPage, "only graphics has an advanced page");
        }

        [UnityTest]
        public IEnumerator Sensitivity_Back_Reverts()
        {
            yield return OpenFromPauseMenu();
            var screen = Shown;
            float before = GameSettings.MouseSensitivity;
            float target = before > 0.2f ? 0.1f : 0.3f;

            screen.SelectTab(SettingsTab.Controls);
            screen.Row(SettingId.MouseSensitivity).Slider.value = target;
            Assert.AreEqual(target, GameSettings.MouseSensitivity, 0.0001f, "live preview");
            Assert.AreEqual(target, DeviceInput.MouseSensitivity, 0.0001f);
            Assert.IsTrue(screen.HasUnappliedChanges);
            Assert.IsFalse(PlayerPrefs.HasKey(GameSettings.MouseSensitivityKey), "nothing saved before 적용");

            screen.Back();
            Assert.AreEqual(before, GameSettings.MouseSensitivity, 0.0001f, "뒤로 가기 drops the change");
            Assert.AreEqual(before, DeviceInput.MouseSensitivity, 0.0001f);
            Assert.IsFalse(PlayerPrefs.HasKey(GameSettings.MouseSensitivityKey));
            Assert.IsFalse(PauseMenu.Instance.SettingsShown, "back to the menu column");
            Assert.IsTrue(PauseMenu.Instance.IsOpen);
        }

        [UnityTest]
        public IEnumerator Sensitivity_Apply_KeepsAndSaves()
        {
            yield return OpenFromPauseMenu();
            var screen = Shown;
            screen.SelectTab(SettingsTab.Controls);
            screen.Row(SettingId.MouseSensitivity).Slider.value = 0.25f;
            screen.Apply();
            Assert.IsFalse(screen.HasUnappliedChanges);
            Assert.AreEqual(0.25f, PlayerPrefs.GetFloat(GameSettings.MouseSensitivityKey), 0.0001f, "saved");

            screen.Back();
            Assert.AreEqual(0.25f, GameSettings.MouseSensitivity, 0.0001f, "kept after leaving");
            GameSettings.Load();
            Assert.AreEqual(0.25f, GameSettings.MouseSensitivity, 0.0001f, "read back");
        }

        [UnityTest]
        public IEnumerator ClosingTheMenu_DropsUnappliedChanges()
        {
            yield return OpenFromPauseMenu();
            float before = GameSettings.MasterVolume;
            Shown.SelectTab(SettingsTab.Audio);
            Shown.Row(SettingId.MasterVolume).Slider.value = 0.2f;
            Assert.AreEqual(0.2f, AudioListener.volume, 0.0001f);
            UiFocus.Close();
            Assert.AreEqual(before, GameSettings.MasterVolume, 0.0001f);
            Assert.AreEqual(before, AudioListener.volume, 0.0001f);
        }

        [UnityTest]
        public IEnumerator Defaults_ResetOnlyTheCurrentTab()
        {
            yield return OpenFromPauseMenu();
            var screen = Shown;
            screen.SelectTab(SettingsTab.Controls);
            screen.Row(SettingId.MouseSensitivity).Slider.value = 0.3f;
            screen.SelectTab(SettingsTab.Audio);
            screen.Row(SettingId.MasterVolume).Slider.value = 0.3f;
            screen.Row(SettingId.UiVolume).Slider.value = 0.5f;

            screen.ResetCurrentTab();
            Assert.AreEqual(SettingsCatalog.Get(SettingId.MasterVolume).Default, GameSettings.MasterVolume, 0.0001f);
            Assert.AreEqual(SettingsCatalog.Get(SettingId.UiVolume).Default, GameSettings.UiVolume, 0.0001f);
            Assert.AreEqual(0.3f, GameSettings.MouseSensitivity, 0.0001f, "the controls tab keeps its change");
            Assert.AreEqual(GameSettings.UiVolume, screen.Row(SettingId.UiVolume).Slider.value, 0.0001f, "the sliders show the reset");
            Assert.IsTrue(screen.HasUnappliedChanges, "기본 previews too");

            screen.Back();
            Assert.AreEqual(DeviceInput.DefaultMouseSensitivity, GameSettings.MouseSensitivity, 0.0001f);
        }

        [UnityTest]
        public IEnumerator Preset_DrivesAdvancedRows_AndAnAdvancedChangeMakesItCustom()
        {
            yield return OpenFromPauseMenu();
            var screen = Shown;
            screen.Step(SettingId.QualityPreset, -1); // 보통 → 낮음
            Assert.AreEqual(GraphicsPreset.Low, GraphicsOptions.Preset);
            Assert.AreEqual(GraphicsOptions.ShadowDistance(GraphicsPreset.Low), QualitySettings.shadowDistance, 0.001f, "applied live");
            Assert.AreEqual("낮음", screen.Row(SettingId.QualityPreset).Value.text);

            screen.SelectPage(SettingsPage.Advanced);
            Assert.AreEqual(SettingsCatalog.Get(SettingId.Shadows).Options[SettingsCatalog.PresetValue(SettingsCatalog.PresetLow, SettingId.Shadows)],
                screen.Row(SettingId.Shadows).Value.text, "the advanced page shows the preset's values");
            screen.Step(SettingId.Shadows, 1);
            Assert.AreEqual(GraphicsPreset.Custom, GraphicsOptions.Preset);
            screen.SelectPage(SettingsPage.Basic);
            Assert.AreEqual("사용자 설정", screen.Row(SettingId.QualityPreset).Value.text);

            screen.Back();
            Assert.AreEqual(GraphicsPreset.Medium, GraphicsOptions.Preset, "reverted");
        }

        [UnityTest]
        public IEnumerator ViewDistance_SetsTheFarClip()
        {
            yield return OpenFromPauseMenu();
            var cam = Camera.main;
            Assert.IsNotNull(cam);
            GameSettings.Preview(SettingId.ViewDistance, 0f);
            Assert.AreEqual(300f, cam.farClipPlane, 0.01f);
            GameSettings.Preview(SettingId.ViewDistance, 3f);
            Assert.AreEqual(900f, cam.farClipPlane, 0.01f);
            Shown.Cancel();
            Assert.AreEqual(GraphicsOptions.FarClip(SettingsCatalog.PresetValue(SettingsCatalog.PresetMedium, SettingId.ViewDistance)),
                cam.farClipPlane, 0.01f);
        }
    }
}
