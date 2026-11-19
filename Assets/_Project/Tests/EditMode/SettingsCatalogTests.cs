using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // The settings catalog (ADR-059): every row speaks Korean, keys are unique, presets drive the advanced rows and an
    // advanced change turns the preset into 사용자 설정; plus the preview / apply / revert contract of GameSettings.
    public class SettingsCatalogTests
    {
        GameSettings.PrefsSnapshot _prefs;

        [SetUp]
        public void RememberPrefs()
        {
            _prefs = GameSettings.CapturePrefs();
            // Start every test from the defaults, whatever the developer has saved.
            foreach (var def in SettingsCatalog.All) PlayerPrefs.DeleteKey(def.Key);
            GameSettings.Load();
        }

        [TearDown]
        public void RestorePrefs() => GameSettings.RestorePrefs(_prefs);

        static bool HasHangul(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
                if (c >= '가' && c <= '힣') return true;
            return false;
        }

        [Test]
        public void Rows_AreInIdOrder()
        {
            Assert.AreEqual(System.Enum.GetValues(typeof(SettingId)).Length, SettingsCatalog.Count);
            for (int i = 0; i < SettingsCatalog.Count; i++)
                Assert.AreEqual((SettingId)i, SettingsCatalog.All[i].Id, "row " + i);
        }

        [Test]
        public void EveryRow_HasAKoreanLabelSectionAndDescription()
        {
            foreach (var def in SettingsCatalog.All)
            {
                Assert.IsTrue(HasHangul(def.Label), def.Id + " label");
                Assert.IsTrue(HasHangul(def.Section), def.Id + " section");
                Assert.IsTrue(HasHangul(def.Description), def.Id + " description");
                if (def.Kind != SettingKind.Slider)
                {
                    Assert.Greater(def.OptionCount, 0, def.Id + " options");
                    foreach (var option in def.Options) Assert.IsNotEmpty(option, def.Id + " option text");
                }
                if (def.Kind == SettingKind.Toggle) Assert.AreEqual(2, def.OptionCount, def.Id + " toggles are off/on");
            }
            foreach (var key in SettingsCatalog.KeyBindings)
            {
                Assert.IsTrue(HasHangul(key.Label), key.Keys + " label");
                Assert.IsTrue(HasHangul(key.Description), key.Keys + " description");
                Assert.IsTrue(HasHangul(key.Section), key.Keys + " section");
                Assert.IsNotEmpty(key.Keys);
            }
            foreach (var label in SettingsCatalog.TabLabels) Assert.IsTrue(HasHangul(label));
            foreach (var label in SettingsCatalog.PageLabels) Assert.IsTrue(HasHangul(label));
            Assert.IsTrue(HasHangul(SettingsCatalog.KeysNote));
        }

        [Test]
        public void KeyList_HasARowForEveryAction_WithItsDefaultKey()
        {
            var seen = new HashSet<BindAction>();
            foreach (var k in SettingsCatalog.KeyBindings) Assert.IsTrue(seen.Add(k.Action), "duplicate row " + k.Action);
            foreach (BindAction a in System.Enum.GetValues(typeof(BindAction))) Assert.IsTrue(seen.Contains(a), "missing row " + a);
            var saved = KeyBinds.SnapshotPrefs();
            KeyBinds.ResetAll();
            var names = new HashSet<string>();
            foreach (var k in SettingsCatalog.KeyBindings) names.Add(k.Keys);
            KeyBinds.RestorePrefs(saved);
            string[] expected = { "W", "A", "S", "D", "Shift", "Space", "C", "Z", "X", "B", "R", "F", "H", "G", "T", "Q", "E", "V", "M", "Tab", "Esc", "1", "5", "Alt", "좌클릭", "우클릭", "휠 클릭" };
            foreach (var key in expected) Assert.IsTrue(names.Contains(key), "missing key " + key);
        }

        [Test]
        public void PlayerPrefsKeys_AreUnique()
        {
            var seen = new HashSet<string>();
            foreach (var def in SettingsCatalog.All)
            {
                Assert.IsFalse(string.IsNullOrEmpty(def.Key), def.Id + " has a key");
                Assert.IsTrue(def.Key.StartsWith("settings."), def.Id + " key prefix");
                Assert.IsTrue(seen.Add(def.Key), "duplicate key " + def.Key);
            }
        }

        [Test]
        public void EveryTabAndPage_HasRows()
        {
            int Count(SettingsTab tab, SettingsPage page)
            {
                int n = 0;
                foreach (var def in SettingsCatalog.All) if (def.Tab == tab && def.Page == page) n++;
                return n;
            }
            Assert.AreEqual(8, Count(SettingsTab.Graphics, SettingsPage.Basic));
            Assert.AreEqual(6, Count(SettingsTab.Graphics, SettingsPage.Advanced));
            Assert.AreEqual(4, Count(SettingsTab.Audio, SettingsPage.Basic));
            Assert.AreEqual(11, Count(SettingsTab.Controls, SettingsPage.Basic));
            Assert.AreEqual(10, Count(SettingsTab.Gameplay, SettingsPage.Basic)); // + time of day and weather (ADR-119), network status (ADR-128), shake and blood (ADR-470/471)
            Assert.AreEqual(0, Count(SettingsTab.Audio, SettingsPage.Advanced), "only graphics has an advanced page");
            Assert.Greater(SettingsCatalog.KeyBindings.Length, 15);
            foreach (var id in SettingsCatalog.AdvancedRows)
                Assert.AreEqual(SettingsPage.Advanced, SettingsCatalog.Get(id).Page, id + " lives on the advanced page");
        }

        [Test]
        public void Presets_SetTheAdvancedRows()
        {
            var values = SettingsCatalog.Defaults();
            for (int preset = SettingsCatalog.PresetLow; preset <= SettingsCatalog.PresetHigh; preset++)
            {
                SettingsCatalog.Change(values, SettingId.QualityPreset, preset);
                Assert.AreEqual(preset, (int)values[(int)SettingId.QualityPreset]);
                foreach (var id in SettingsCatalog.AdvancedRows)
                    Assert.AreEqual(SettingsCatalog.PresetValue(preset, id), (int)values[(int)id], preset + " " + id);
            }
            Assert.Less(SettingsCatalog.PresetValue(SettingsCatalog.PresetLow, SettingId.Shadows),
                SettingsCatalog.PresetValue(SettingsCatalog.PresetHigh, SettingId.Shadows));
            Assert.Less(SettingsCatalog.PresetValue(SettingsCatalog.PresetLow, SettingId.ViewDistance),
                SettingsCatalog.PresetValue(SettingsCatalog.PresetHigh, SettingId.ViewDistance));
            Assert.AreEqual(0, SettingsCatalog.PresetValue(SettingsCatalog.PresetLow, SettingId.AmbientOcclusion), "SSAO off on 낮음");
        }

        [Test]
        public void ChoosingCustom_KeepsTheAdvancedRows()
        {
            var values = SettingsCatalog.Defaults();
            SettingsCatalog.Change(values, SettingId.QualityPreset, SettingsCatalog.PresetHigh);
            SettingsCatalog.Change(values, SettingId.QualityPreset, SettingsCatalog.PresetCustom);
            foreach (var id in SettingsCatalog.AdvancedRows)
                Assert.AreEqual(SettingsCatalog.PresetValue(SettingsCatalog.PresetHigh, id), (int)values[(int)id], id.ToString());
        }

        [Test]
        public void AdvancedChange_MakesThePresetCustom()
        {
            foreach (var id in SettingsCatalog.AdvancedRows)
            {
                var values = SettingsCatalog.Defaults();
                int current = (int)values[(int)id];
                SettingsCatalog.Change(values, id, current == 0 ? 1 : 0);
                Assert.AreEqual(SettingsCatalog.PresetCustom, (int)values[(int)SettingId.QualityPreset], id + " → 사용자 설정");
            }
            var other = SettingsCatalog.Defaults();
            SettingsCatalog.Change(other, SettingId.Brightness, 80f);
            Assert.AreEqual(SettingsCatalog.PresetMedium, (int)other[(int)SettingId.QualityPreset], "a basic row leaves the preset alone");
        }

        [Test]
        public void AdvancedDefaults_AreTheDefaultPreset()
        {
            var def = SettingsCatalog.Get(SettingId.QualityPreset);
            Assert.AreEqual(SettingsCatalog.PresetMedium, (int)def.Default);
            foreach (var id in SettingsCatalog.AdvancedRows)
                Assert.AreEqual(SettingsCatalog.PresetValue(SettingsCatalog.PresetMedium, id), (int)SettingsCatalog.Get(id).Default, id.ToString());
        }

        [Test]
        public void Clamp_SnapsSlidersAndChoices()
        {
            Assert.AreEqual(GameSettings.MaxMouseSensitivity, SettingsCatalog.Clamp(SettingsCatalog.Get(SettingId.MouseSensitivity), 5f), 0.0001f);
            Assert.AreEqual(75f, SettingsCatalog.Clamp(SettingsCatalog.Get(SettingId.RenderScale), 73f), 0.0001f);
            Assert.AreEqual(50f, SettingsCatalog.Clamp(SettingsCatalog.Get(SettingId.RenderScale), 10f), 0.0001f);
            Assert.AreEqual(3f, SettingsCatalog.Clamp(SettingsCatalog.Get(SettingId.CrosshairColor), 99f));
            Assert.AreEqual(0f, SettingsCatalog.Clamp(SettingsCatalog.Get(SettingId.CrosshairColor), -4f));
            Assert.AreEqual(1f, SettingsCatalog.Clamp(SettingsCatalog.Get(SettingId.VSync), 0.7f));
        }

        [Test]
        public void ResetTab_TouchesOnlyThatTab()
        {
            var values = SettingsCatalog.Defaults();
            SettingsCatalog.Change(values, SettingId.MasterVolume, 0.3f);
            SettingsCatalog.Change(values, SettingId.MouseSensitivity, 0.3f);
            SettingsCatalog.Change(values, SettingId.Shadows, 0f);
            SettingsCatalog.ResetTab(values, SettingsTab.Audio);
            Assert.AreEqual(1f, values[(int)SettingId.MasterVolume], 0.0001f);
            Assert.AreEqual(0.3f, values[(int)SettingId.MouseSensitivity], 0.0001f, "controls untouched");
            Assert.AreEqual(SettingsCatalog.PresetCustom, (int)values[(int)SettingId.QualityPreset], "graphics untouched");

            SettingsCatalog.ResetTab(values, SettingsTab.Graphics);
            Assert.AreEqual(SettingsCatalog.PresetMedium, (int)values[(int)SettingId.QualityPreset]);
            Assert.AreEqual(SettingsCatalog.PresetValue(SettingsCatalog.PresetMedium, SettingId.Shadows), (int)values[(int)SettingId.Shadows]);
        }

        [Test]
        public void SliderTexts_CoverEveryStep()
        {
            var volume = SettingsCatalog.Get(SettingId.MasterVolume);
            var texts = SettingsCatalog.SliderTexts(volume);
            Assert.AreEqual(101, texts.Length);
            Assert.AreEqual("0%", texts[0]);
            Assert.AreEqual("100%", texts[100]);
            var aim = SettingsCatalog.Get(SettingId.AimSensitivity);
            Assert.AreEqual("x1.00", SettingsCatalog.SliderTexts(aim)[SettingsCatalog.StepIndex(aim, 1f)]);
            Assert.AreEqual("100%", SettingsCatalog.SliderTexts(SettingsCatalog.Get(SettingId.RenderScale))[10]);
        }

        [Test]
        public void Brightness_MapsToOneStopEachWay()
        {
            Assert.AreEqual(0f, SettingsCatalog.BrightnessToExposure(50f), 0.0001f);
            Assert.AreEqual(-1f, SettingsCatalog.BrightnessToExposure(0f), 0.0001f);
            Assert.AreEqual(1f, SettingsCatalog.BrightnessToExposure(100f), 0.0001f);
        }

        [Test]
        public void Preview_ThenRevert_SavesNothing()
        {
            GameSettings.MouseSensitivity = 0.1f;
            GameSettings.Preview(SettingId.MouseSensitivity, 0.3f);
            Assert.AreEqual(0.3f, GameSettings.MouseSensitivity, 0.0001f, "the preview applies at once");
            Assert.IsTrue(GameSettings.HasUnappliedChanges);
            Assert.AreEqual(0.1f, PlayerPrefs.GetFloat(GameSettings.MouseSensitivityKey), 0.0001f, "not saved yet");
            GameSettings.Revert();
            Assert.AreEqual(0.1f, GameSettings.MouseSensitivity, 0.0001f);
            Assert.IsFalse(GameSettings.HasUnappliedChanges);
        }

        [Test]
        public void Preview_ThenCommit_SavesEveryRow()
        {
            GameSettings.Preview(SettingId.EffectsVolume, 0.4f);
            GameSettings.Preview(SettingId.CrosshairColor, 2f);
            GameSettings.Preview(SettingId.Shadows, 0f);
            GameSettings.Commit();
            Assert.IsFalse(GameSettings.HasUnappliedChanges);
            Assert.AreEqual(0.4f, PlayerPrefs.GetFloat("settings.effectsVolume"), 0.0001f);
            Assert.AreEqual(2, PlayerPrefs.GetInt("settings.crosshairColor"));
            Assert.AreEqual(SettingsCatalog.PresetCustom, PlayerPrefs.GetInt(GraphicsOptions.PresetKey));
            GameSettings.Load();
            Assert.AreEqual(0.4f, GameSettings.EffectsVolume, 0.0001f, "read back");
            Assert.AreEqual(0, GameSettings.GetIndex(SettingId.Shadows), "a custom preset keeps its own rows");
            Assert.AreEqual(GraphicsPreset.Custom, GraphicsOptions.Preset);
        }

        [Test]
        public void SavedPreset_WinsOverStaleAdvancedRows()
        {
            PlayerPrefs.SetInt(GraphicsOptions.PresetKey, SettingsCatalog.PresetLow);
            PlayerPrefs.SetInt("settings.shadows", 3);
            GameSettings.Load();
            Assert.AreEqual(SettingsCatalog.PresetValue(SettingsCatalog.PresetLow, SettingId.Shadows), GameSettings.GetIndex(SettingId.Shadows));
        }

        [Test]
        public void AdjustLook_AppliesFactorsAndInvert()
        {
            GameSettings.AimSensitivityFactor = 0.5f;
            Assert.AreEqual(new Vector2(1f, 1f), GameSettings.AdjustLook(new Vector2(1f, 1f), false, 1f), "hip, no scope: unchanged");
            Assert.AreEqual(0.5f, GameSettings.AdjustLook(new Vector2(1f, 0f), true, 1f).x, 0.0001f, "aim factor while aiming");
            GameSettings.Preview(SettingId.ScopeSens4x, 0.5f);
            Assert.AreEqual(0.5f * 0.5f, GameSettings.AdjustLook(new Vector2(1f, 0f), true, 0.5f, 4f).x, 0.0001f, "magnified: scope factor × that zoom's setting");
            GameSettings.Preview(SettingId.InvertMouseY, 1f);
            Assert.AreEqual(-1f, GameSettings.AdjustLook(new Vector2(0f, 1f), false, 1f).y, 0.0001f, "inverted");
            GameSettings.Revert();
        }

        [Test]
        public void ScopeSensitivity_IsPerMagnification()
        {
            var look = new Vector2(1f, 0f);
            float f2 = Optics.SensitivityFor(2f), f4 = Optics.SensitivityFor(4f);
            float before2 = GameSettings.AdjustLook(look, true, f2, 2f).x;
            float before4 = GameSettings.AdjustLook(look, true, f4, 4f).x;
            float hip = GameSettings.AdjustLook(look, false, 1f).x;
            GameSettings.Preview(SettingId.ScopeSens4x, 1.5f);
            Assert.AreEqual(before2, GameSettings.AdjustLook(look, true, f2, 2f).x, 0.0001f, "2x unchanged");
            Assert.AreEqual(hip, GameSettings.AdjustLook(look, false, 1f).x, 0.0001f, "hip unchanged");
            Assert.AreEqual(before4 * 1.5f, GameSettings.AdjustLook(look, true, f4, 4f).x, 0.0001f, "4x scaled");
            Assert.AreEqual(Mathf.Lerp(1f, 1.5f, 0.5f), GameSettings.ScopeFactorAt(5f), 0.0001f, "5x lies between 4x (1.5) and 6x (1)");
            GameSettings.Revert();
        }

        [Test]
        public void AimStyles_AreSavedAndSurviveReload()
        {
            var snap = GameSettings.CapturePrefs();
            try
            {
                GameSettings.SetAndSave(SettingId.SightsStyle, 1f);
                GameSettings.SetAndSave(SettingId.ShoulderStyle, 1f);
                GameSettings.SetAndSave(SettingId.ScopeSens8x, 0.8f);
                GameSettings.Load();
                Assert.IsTrue(GameSettings.SightsHold);
                Assert.IsTrue(GameSettings.ShoulderToggle);
                Assert.AreEqual(0.8f, GameSettings.Get(SettingId.ScopeSens8x), 0.0001f);
            }
            finally { GameSettings.RestorePrefs(snap); }
        }
    }
}
