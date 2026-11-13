using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Game.Gameplay
{
    public enum GraphicsPreset { Low = 0, Medium = 1, High = 2 }

    // The three graphics presets of the settings screen (낮음 / 보통 / 높음): shadow distance, render scale and the
    // SSAO pass. Saved in PlayerPrefs and applied through the active URP asset, so it works in player builds.
    // The Medium preset equals what the URP asset ships with, so a default install changes nothing.
    public static class GraphicsOptions
    {
        public const string PresetKey = "settings.graphicsPreset";
        public const GraphicsPreset DefaultPreset = GraphicsPreset.Medium;

        static GraphicsPreset _preset = DefaultPreset;
        static bool _hooked;
        // SSAO features of the active pipeline asset, found by type name. Looked up again when the asset changes.
        static readonly List<ScriptableRendererFeature> SsaoFeatures = new List<ScriptableRendererFeature>(2);
        static UniversalRenderPipelineAsset _ssaoAsset;

        public static GraphicsPreset Preset
        {
            get => _preset;
            set
            {
                _preset = Clamp(value);
                PlayerPrefs.SetInt(PresetKey, (int)_preset);
                PlayerPrefs.Save();
                Apply();
            }
        }

        // Metres the shadows reach. Medium is the URP asset's own value.
        public static float ShadowDistance(GraphicsPreset preset) =>
            preset == GraphicsPreset.Low ? 30f : preset == GraphicsPreset.High ? 80f : 50f;

        // Multiplier of the screen resolution the scene is rendered at.
        public static float RenderScale(GraphicsPreset preset) => preset == GraphicsPreset.Low ? 0.75f : 1f;

        public static bool SsaoEnabled(GraphicsPreset preset) => preset != GraphicsPreset.Low;

        // The Korean name shown on the buttons.
        public static string Label(GraphicsPreset preset) =>
            preset == GraphicsPreset.Low ? "낮음" : preset == GraphicsPreset.High ? "높음" : "보통";

        public static GraphicsPreset Clamp(GraphicsPreset preset) =>
            (GraphicsPreset)Mathf.Clamp((int)preset, (int)GraphicsPreset.Low, (int)GraphicsPreset.High);

        // Reads the saved preset (without writing) and applies it. Called by GameSettings.Load before the first scene.
        public static void Load()
        {
            _preset = Clamp((GraphicsPreset)PlayerPrefs.GetInt(PresetKey, (int)DefaultPreset));
            if (!_hooked)
            {
                _hooked = true;
                // A scene may bring its own pipeline settings; the preset is pushed again after every load.
                SceneManager.sceneLoaded += OnSceneLoaded;
            }
            Apply();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

        // Pushes the current preset to the URP asset and the quality settings.
        public static void Apply()
        {
            QualitySettings.shadowDistance = ShadowDistance(_preset);
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return;
            asset.shadowDistance = ShadowDistance(_preset);
            asset.renderScale = RenderScale(_preset);
            FindSsao(asset);
            bool on = SsaoEnabled(_preset);
            for (int i = 0; i < SsaoFeatures.Count; i++)
                if (SsaoFeatures[i] != null) SsaoFeatures[i].SetActive(on);
        }

        // True when an SSAO feature was found on the active pipeline asset (so the preset can switch it).
        public static bool SsaoToggleAvailable
        {
            get
            {
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (asset == null) return false;
                FindSsao(asset);
                return SsaoFeatures.Count > 0;
            }
        }

        // True when any SSAO feature is currently switched on.
        public static bool SsaoActive
        {
            get
            {
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (asset == null) return false;
                FindSsao(asset);
                for (int i = 0; i < SsaoFeatures.Count; i++)
                    if (SsaoFeatures[i] != null && SsaoFeatures[i].isActive) return true;
                return false;
            }
        }

        // The asset keeps its renderer data in a private serialized array (kept in player builds); the features of each
        // renderer data are the same instances the renderers run. Matched by type name; any failure just leaves the
        // preset without the SSAO part.
        static void FindSsao(UniversalRenderPipelineAsset asset)
        {
            if (_ssaoAsset == asset && SsaoFeatures.Count > 0) return;
            SsaoFeatures.Clear();
            _ssaoAsset = asset;
            try
            {
                var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null || !(field.GetValue(asset) is ScriptableRendererData[] datas)) return;
                for (int i = 0; i < datas.Length; i++)
                {
                    var features = datas[i] != null ? datas[i].rendererFeatures : null;
                    if (features == null) continue;
                    for (int f = 0; f < features.Count; f++)
                        if (features[f] != null && features[f].GetType().Name == "ScreenSpaceAmbientOcclusion") SsaoFeatures.Add(features[f]);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[GraphicsOptions] SSAO toggle unavailable: " + e.Message);
            }
        }
    }
}
