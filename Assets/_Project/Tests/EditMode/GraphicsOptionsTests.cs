using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // The graphics presets and the new persisted settings: what each preset means, clamping, and PlayerPrefs round trips.
    public class GraphicsOptionsTests
    {
        bool _hadPreset, _hadAim, _hadView;
        int _preset, _view;
        float _aim;

        [SetUp]
        public void RememberPrefs()
        {
            _hadPreset = PlayerPrefs.HasKey(GraphicsOptions.PresetKey);
            _hadAim = PlayerPrefs.HasKey(GameSettings.AimSensitivityKey);
            _hadView = PlayerPrefs.HasKey(GameSettings.DefaultViewKey);
            _preset = PlayerPrefs.GetInt(GraphicsOptions.PresetKey, 0);
            _aim = PlayerPrefs.GetFloat(GameSettings.AimSensitivityKey, 0f);
            _view = PlayerPrefs.GetInt(GameSettings.DefaultViewKey, 0);
        }

        [TearDown]
        public void RestorePrefs()
        {
            if (_hadPreset) PlayerPrefs.SetInt(GraphicsOptions.PresetKey, _preset); else PlayerPrefs.DeleteKey(GraphicsOptions.PresetKey);
            if (_hadAim) PlayerPrefs.SetFloat(GameSettings.AimSensitivityKey, _aim); else PlayerPrefs.DeleteKey(GameSettings.AimSensitivityKey);
            if (_hadView) PlayerPrefs.SetInt(GameSettings.DefaultViewKey, _view); else PlayerPrefs.DeleteKey(GameSettings.DefaultViewKey);
            PlayerPrefs.Save();
            GameSettings.Load();
        }

        [Test]
        public void Presets_ScaleShadowsAndResolutionUpwards()
        {
            Assert.Less(GraphicsOptions.ShadowDistance(GraphicsPreset.Low), GraphicsOptions.ShadowDistance(GraphicsPreset.Medium));
            Assert.Less(GraphicsOptions.ShadowDistance(GraphicsPreset.Medium), GraphicsOptions.ShadowDistance(GraphicsPreset.High));
            Assert.Less(GraphicsOptions.RenderScale(GraphicsPreset.Low), GraphicsOptions.RenderScale(GraphicsPreset.Medium));
            Assert.AreEqual(1f, GraphicsOptions.RenderScale(GraphicsPreset.High), 0.0001f);
        }

        [Test]
        public void Ssao_IsOffOnLowOnly()
        {
            Assert.IsFalse(GraphicsOptions.SsaoEnabled(GraphicsPreset.Low));
            Assert.IsTrue(GraphicsOptions.SsaoEnabled(GraphicsPreset.Medium));
            Assert.IsTrue(GraphicsOptions.SsaoEnabled(GraphicsPreset.High));
        }

        [Test]
        public void Labels_AreKorean()
        {
            Assert.AreEqual("낮음", GraphicsOptions.Label(GraphicsPreset.Low));
            Assert.AreEqual("보통", GraphicsOptions.Label(GraphicsPreset.Medium));
            Assert.AreEqual("높음", GraphicsOptions.Label(GraphicsPreset.High));
        }

        [Test]
        public void Preset_ClampsAndRoundTrips()
        {
            Assert.AreEqual(GraphicsPreset.High, GraphicsOptions.Clamp((GraphicsPreset)9));
            Assert.AreEqual(GraphicsPreset.Low, GraphicsOptions.Clamp((GraphicsPreset)(-3)));

            GraphicsOptions.Preset = GraphicsPreset.High;
            Assert.AreEqual((int)GraphicsPreset.High, PlayerPrefs.GetInt(GraphicsOptions.PresetKey));
            GraphicsOptions.Preset = GraphicsPreset.Low;
            GraphicsOptions.Load();
            Assert.AreEqual(GraphicsPreset.Low, GraphicsOptions.Preset, "the saved preset is read back");

            PlayerPrefs.SetInt(GraphicsOptions.PresetKey, 77);
            GraphicsOptions.Load();
            Assert.AreEqual(GraphicsPreset.High, GraphicsOptions.Preset, "a corrupt saved value is clamped");
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
    }
}
