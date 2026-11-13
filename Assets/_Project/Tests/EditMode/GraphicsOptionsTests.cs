using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // The graphics presets and the persisted settings: what each preset means, the level tables, clamping, and
    // PlayerPrefs round trips.
    public class GraphicsOptionsTests
    {
        GameSettings.PrefsSnapshot _prefs;

        [SetUp]
        public void RememberPrefs()
        {
            _prefs = GameSettings.CapturePrefs();
            foreach (var def in SettingsCatalog.All) PlayerPrefs.DeleteKey(def.Key);
            GameSettings.Load();
        }

        [TearDown]
        public void RestorePrefs() => GameSettings.RestorePrefs(_prefs);

        [Test]
        public void Presets_ScaleShadowsUpwards()
        {
            Assert.Less(GraphicsOptions.ShadowDistance(GraphicsPreset.Low), GraphicsOptions.ShadowDistance(GraphicsPreset.Medium));
            Assert.Less(GraphicsOptions.ShadowDistance(GraphicsPreset.Medium), GraphicsOptions.ShadowDistance(GraphicsPreset.High));
            Assert.AreEqual(50f, GraphicsOptions.ShadowDistance(GraphicsPreset.Medium), 0.001f, "보통 is the URP asset's own distance");
            Assert.AreEqual(0f, GraphicsOptions.ShadowDistanceFor(0), "그림자 끔");
        }

        [Test]
        public void Ssao_IsOffOnLowOnly()
        {
            Assert.IsFalse(GraphicsOptions.SsaoEnabled(GraphicsPreset.Low));
            Assert.IsTrue(GraphicsOptions.SsaoEnabled(GraphicsPreset.Medium));
            Assert.IsTrue(GraphicsOptions.SsaoEnabled(GraphicsPreset.High));
        }

        [Test]
        public void LevelTables_MatchTheSpec()
        {
            Assert.AreEqual(2, GraphicsOptions.TextureMipLimit(0));
            Assert.AreEqual(1, GraphicsOptions.TextureMipLimit(1));
            Assert.AreEqual(0, GraphicsOptions.TextureMipLimit(2));
            Assert.AreEqual(300f, GraphicsOptions.FarClip(0));
            Assert.AreEqual(500f, GraphicsOptions.FarClip(1));
            Assert.AreEqual(700f, GraphicsOptions.FarClip(2), "높음 = the World scene's own far clip");
            Assert.AreEqual(900f, GraphicsOptions.FarClip(3));
            Assert.AreEqual(-1, GraphicsOptions.FrameCap(0), "무제한");
            Assert.AreEqual(144, GraphicsOptions.FrameCap(3));
            Assert.AreEqual(FullScreenMode.Windowed, GraphicsOptions.ScreenMode(2));
            Assert.AreEqual(FullScreenMode.FullScreenWindow, GraphicsOptions.ScreenMode(1));
        }

        [Test]
        public void Labels_AreKorean()
        {
            Assert.AreEqual("낮음", GraphicsOptions.Label(GraphicsPreset.Low));
            Assert.AreEqual("보통", GraphicsOptions.Label(GraphicsPreset.Medium));
            Assert.AreEqual("높음", GraphicsOptions.Label(GraphicsPreset.High));
            Assert.AreEqual("사용자 설정", GraphicsOptions.Label(GraphicsPreset.Custom));
        }

        [Test]
        public void Preset_ClampsAndRoundTrips()
        {
            Assert.AreEqual(GraphicsPreset.Custom, GraphicsOptions.Clamp((GraphicsPreset)9));
            Assert.AreEqual(GraphicsPreset.Low, GraphicsOptions.Clamp((GraphicsPreset)(-3)));

            GraphicsOptions.Preset = GraphicsPreset.High;
            Assert.AreEqual((int)GraphicsPreset.High, PlayerPrefs.GetInt(GraphicsOptions.PresetKey));
            Assert.AreEqual(SettingsCatalog.PresetValue(SettingsCatalog.PresetHigh, SettingId.Shadows), PlayerPrefs.GetInt("settings.shadows"),
                "a preset saves the advanced rows it sets");
            GraphicsOptions.Preset = GraphicsPreset.Low;
            GraphicsOptions.Load();
            Assert.AreEqual(GraphicsPreset.Low, GraphicsOptions.Preset, "the saved preset is read back");

            PlayerPrefs.SetInt(GraphicsOptions.PresetKey, 77);
            GraphicsOptions.Load();
            Assert.AreEqual(GraphicsPreset.Custom, GraphicsOptions.Preset, "a corrupt saved value is clamped");
        }

        [Test]
        public void ResolutionList_IsNeverEmpty_AndRoundTripsAsText()
        {
            var def = SettingsCatalog.Get(SettingId.Resolution);
            Assert.Greater(def.OptionCount, 0);
            int index = GraphicsOptions.NativeResolutionIndex;
            string text = GraphicsOptions.ResolutionText(index);
            StringAssert.Contains("x", text);
            Assert.AreEqual(index, GraphicsOptions.ResolutionIndex(text));
            var size = GraphicsOptions.ResolutionSize(index);
            Assert.Greater(size.x, 0);
            Assert.Greater(size.y, 0);
        }

        [Test]
        public void AimSensitivityFactor_ClampsAndRoundTrips()
        {
            GameSettings.AimSensitivityFactor = 10f;
            Assert.AreEqual(1.5f, GameSettings.AimSensitivityFactor, 0.0001f);
            GameSettings.AimSensitivityFactor = 0.01f;
            Assert.AreEqual(0.3f, GameSettings.AimSensitivityFactor, 0.0001f);
            GameSettings.AimSensitivityFactor = 0.8f;
            GameSettings.Load();
            Assert.AreEqual(0.8f, GameSettings.AimSensitivityFactor, 0.0001f);
        }

        [Test]
        public void DefaultView_RoundTrips()
        {
            GameSettings.DefaultViewFirstPerson = true;
            GameSettings.Load();
            Assert.IsTrue(GameSettings.DefaultViewFirstPerson);
            GameSettings.DefaultViewFirstPerson = false;
            GameSettings.Load();
            Assert.IsFalse(GameSettings.DefaultViewFirstPerson);
        }

        [Test]
        public void GameplayReaders_FollowTheRows()
        {
            GameSettings.Preview(SettingId.CrosshairColor, 1f);
            Assert.Greater(GameSettings.CrosshairColor.g, GameSettings.CrosshairColor.r, "녹색");
            GameSettings.Preview(SettingId.KillFeed, 0f);
            Assert.IsFalse(GameSettings.ShowKillFeed);
            GameSettings.Preview(SettingId.ViewDistance, 3f);
            Assert.AreEqual(900f, GameSettings.ViewDistanceMeters);
            GameSettings.Preview(SettingId.Effects, 0f);
            Assert.IsFalse(GameSettings.HighEffects);
            GameSettings.Revert();
            Assert.IsTrue(GameSettings.ShowKillFeed);
        }
    }
}
