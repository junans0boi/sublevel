using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Game.Gameplay
{
    // The 전반적인 그래픽 품질 row: three presets and 사용자 설정 (an advanced row was changed by hand).
    public enum GraphicsPreset { Low = 0, Medium = 1, High = 2, Custom = 3 }

    // Pushes the graphics rows of GameSettings to the engine: display mode and resolution (only on 적용 / at startup),
    // frame cap, vsync, textures, the URP asset (shadows, render scale, SSAO feature), the main camera (anti-aliasing,
    // far clip with the scene's linear fog scaled to match) and a runtime global volume for 밝기. Works in player
    // builds; nothing touches the engine outside play mode. Camera-bound rows are pushed again after every scene load.
    public static class GraphicsOptions
    {
        public const string PresetKey = "settings.graphicsPreset";
        public const GraphicsPreset DefaultPreset = GraphicsPreset.Medium;
        // Above the scene's own post volume, so its exposure is replaced by (scene exposure + brightness offset).
        public const float BrightnessVolumePriority = 1000f;

        static readonly float[] ShadowDistances = { 0f, 30f, 50f, 80f };   // 끔/낮음/보통/높음; 보통 = the URP asset's 50 m
        static readonly int[] ShadowCascades = { 1, 1, 1, 2 };
        static readonly int[] ShadowResolutions = { 1024, 1024, 2048, 2048 };
        static readonly int[] TextureMipLimits = { 2, 1, 0 };            // 낮음/보통/높음
        static readonly float[] FarClips = { 300f, 500f, 700f, 900f };   // 낮음/보통/높음(the World scene's own)/울트라
        static readonly int[] FrameCaps = { -1, 60, 120, 144, 240 };

        static bool _initialized;
        static readonly List<Vector2Int> Resolutions = new List<Vector2Int>(16);
        static string[] _resolutionTexts = new string[0];

        // SSAO features of the active pipeline asset, found by type name. Looked up again when the asset changes.
        static readonly List<ScriptableRendererFeature> SsaoFeatures = new List<ScriptableRendererFeature>(2);
        static UniversalRenderPipelineAsset _ssaoAsset;

        // What the loaded scene had before any setting touched it.
        static float _sceneFar;
        static bool _sceneFogLinear;
        static float _sceneFogStart, _sceneFogEnd;
        static float _sceneExposure;

        static Volume _brightnessVolume;
        static VolumeProfile _brightnessProfile;
        static ColorAdjustments _brightnessAdjust;

        public static GraphicsPreset Preset
        {
            get => (GraphicsPreset)GameSettings.GetIndex(SettingId.QualityPreset);
            set => GameSettings.SetAndSave(SettingId.QualityPreset, (int)Clamp(value));
        }

        public static GraphicsPreset Clamp(GraphicsPreset preset) =>
            (GraphicsPreset)Mathf.Clamp((int)preset, (int)GraphicsPreset.Low, (int)GraphicsPreset.Custom);

        // The Korean name of a preset.
        public static string Label(GraphicsPreset preset) => SettingsCatalog.Get(SettingId.QualityPreset).Options[(int)Clamp(preset)];

        // Metres the shadows reach with a preset (사용자 설정: the current 그림자 row).
        public static float ShadowDistance(GraphicsPreset preset) => ShadowDistanceFor(Level(preset, SettingId.Shadows));
        public static bool SsaoEnabled(GraphicsPreset preset) => Level(preset, SettingId.AmbientOcclusion) > 0;

        static int Level(GraphicsPreset preset, SettingId row) =>
            preset == GraphicsPreset.Custom ? GameSettings.GetIndex(row) : SettingsCatalog.PresetValue((int)preset, row);

        public static float ShadowDistanceFor(int level) => ShadowDistances[Mathf.Clamp(level, 0, ShadowDistances.Length - 1)];
        public static int TextureMipLimit(int level) => TextureMipLimits[Mathf.Clamp(level, 0, TextureMipLimits.Length - 1)];
        public static float FarClip(int level) => FarClips[Mathf.Clamp(level, 0, FarClips.Length - 1)];
        public static int FrameCap(int index) => FrameCaps[Mathf.Clamp(index, 0, FrameCaps.Length - 1)];

        public static AntialiasingMode AntiAliasing(int index) =>
            index <= 0 ? AntialiasingMode.None : index == 1 ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        // 전체 화면 is exclusive on Windows; macOS has no exclusive mode, so it is the full-screen window there too.
        public static FullScreenMode ScreenMode(int index)
        {
            if (index <= 0)
                return Application.platform == RuntimePlatform.WindowsPlayer ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.FullScreenWindow;
            return index == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }

        // Reads every saved setting again (kept for callers of the old API).
        public static void Load() => GameSettings.Load();

        // Builds the resolution list and hooks scene loads. Called by GameSettings.Load before the first scene.
        public static void Initialize()
        {
            BuildResolutions();
            if (_initialized) return;
            _initialized = true;
            // A scene brings its own camera, fog and post volume; the camera-bound rows are pushed again after every load.
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (Application.isPlaying) CaptureScene();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) CaptureScene();
            Apply();
        }

        // ---- resolutions ----

        static void BuildResolutions()
        {
            Resolutions.Clear();
            var modes = Screen.resolutions;
            for (int i = 0; i < modes.Length; i++)
            {
                var size = new Vector2Int(modes[i].width, modes[i].height);
                if (size.x > 0 && size.y > 0 && !Resolutions.Contains(size)) Resolutions.Add(size);
            }
            if (Resolutions.Count == 0)
            {
                // Batch mode and some editors report nothing: offer the current size.
                bool valid = Screen.width > 0 && Screen.height > 0;
                Resolutions.Add(valid ? new Vector2Int(Screen.width, Screen.height) : new Vector2Int(1920, 1080));
            }
            Resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            _resolutionTexts = new string[Resolutions.Count];
            var labels = new string[Resolutions.Count];
            for (int i = 0; i < Resolutions.Count; i++)
            {
                var c = CultureInfo.InvariantCulture;
                _resolutionTexts[i] = Resolutions[i].x.ToString(c) + "x" + Resolutions[i].y.ToString(c);
                labels[i] = Resolutions[i].x.ToString(c) + " × " + Resolutions[i].y.ToString(c);
            }
            SettingsCatalog.SetResolutionOptions(labels);
        }

        // The largest size the monitor offers (기본 of the 해상도 row).
        public static int NativeResolutionIndex => Mathf.Max(0, Resolutions.Count - 1);

        // The list entry closest to the current window size.
        public static int CurrentResolutionIndex() => Nearest(Screen.width, Screen.height);

        // A saved "WxH" back to a list index; a size this monitor does not offer falls back to the current one.
        public static int ResolutionIndex(string text)
        {
            for (int i = 0; i < _resolutionTexts.Length; i++)
                if (_resolutionTexts[i] == text) return i;
            return CurrentResolutionIndex();
        }

        public static string ResolutionText(int index) =>
            _resolutionTexts.Length == 0 ? "" : _resolutionTexts[Mathf.Clamp(index, 0, _resolutionTexts.Length - 1)];

        public static Vector2Int ResolutionSize(int index) =>
            Resolutions.Count == 0 ? new Vector2Int(1920, 1080) : Resolutions[Mathf.Clamp(index, 0, Resolutions.Count - 1)];

        static int Nearest(int width, int height)
        {
            int best = NativeResolutionIndex;
            long bestScore = long.MaxValue;
            for (int i = 0; i < Resolutions.Count; i++)
            {
                long dx = Resolutions[i].x - width, dy = Resolutions[i].y - height;
                long score = dx * dx + dy * dy;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        public static int CurrentScreenModeIndex()
        {
            switch (Screen.fullScreenMode)
            {
                case FullScreenMode.ExclusiveFullScreen: return 0;
                case FullScreenMode.Windowed: return 2;
                default: return 1;
            }
        }

        // ---- applying ----

        // Screen mode and resolution. Only in a player: the editor's game view ignores them.
        public static void ApplyDisplay()
        {
            if (!Application.isPlaying || Application.isEditor) return;
            var size = ResolutionSize(GameSettings.GetIndex(SettingId.Resolution));
            var mode = ScreenMode(GameSettings.GetIndex(SettingId.ScreenMode));
            if (Screen.width == size.x && Screen.height == size.y && Screen.fullScreenMode == mode) return;
            Screen.SetResolution(size.x, size.y, mode);
        }

        // Everything but the display. Cheap enough to run on every slider step: values are only written when they change.
        public static void Apply()
        {
            if (!Application.isPlaying) return;
            int vsync = GameSettings.GetToggle(SettingId.VSync) ? 1 : 0;
            if (QualitySettings.vSyncCount != vsync) QualitySettings.vSyncCount = vsync;
            // Ignored by Unity while vsync is on.
            Application.targetFrameRate = FrameCap(GameSettings.GetIndex(SettingId.FpsLimit));
            int mip = TextureMipLimit(GameSettings.GetIndex(SettingId.Textures));
            if (QualitySettings.globalTextureMipmapLimit != mip) QualitySettings.globalTextureMipmapLimit = mip;

            int shadows = GameSettings.GetIndex(SettingId.Shadows);
            float shadowDistance = ShadowDistanceFor(shadows);
            QualitySettings.shadowDistance = shadowDistance;
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset != null)
            {
                asset.shadowDistance = shadowDistance;
                int cascades = ShadowCascades[Mathf.Clamp(shadows, 0, ShadowCascades.Length - 1)];
                if (asset.shadowCascadeCount != cascades) asset.shadowCascadeCount = cascades;
                int resolution = ShadowResolutions[Mathf.Clamp(shadows, 0, ShadowResolutions.Length - 1)];
                if (asset.mainLightShadowmapResolution != resolution) asset.mainLightShadowmapResolution = resolution;
                asset.renderScale = GameSettings.Get(SettingId.RenderScale) / 100f;
                FindSsao(asset);
                bool ao = GameSettings.GetToggle(SettingId.AmbientOcclusion);
                for (int i = 0; i < SsaoFeatures.Count; i++)
                    if (SsaoFeatures[i] != null && SsaoFeatures[i].isActive != ao) SsaoFeatures[i].SetActive(ao);
            }
            ApplyCamera();
            ApplyBrightness();
        }

        // Anti-aliasing and far clip on the scene's main camera; the scene's linear fog is scaled with the far clip so
        // the edge of the drawn world stays inside the fog.
        static void ApplyCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            float far = FarClip(GameSettings.GetIndex(SettingId.ViewDistance));
            cam.farClipPlane = far;
            var data = cam.GetUniversalAdditionalCameraData();
            var aa = AntiAliasing(GameSettings.GetIndex(SettingId.AntiAliasing));
            if (data != null && data.antialiasing != aa) data.antialiasing = aa;
            if (_sceneFogLinear && _sceneFar > 0f)
            {
                float ratio = far / _sceneFar;
                RenderSettings.fogStartDistance = _sceneFogStart * ratio;
                RenderSettings.fogEndDistance = _sceneFogEnd * ratio;
            }
        }

        static void CaptureScene()
        {
            var cam = Camera.main;
            _sceneFar = cam != null ? cam.farClipPlane : 0f;
            _sceneFogLinear = RenderSettings.fog && RenderSettings.fogMode == FogMode.Linear && RenderSettings.fogEndDistance > 0f;
            _sceneFogStart = RenderSettings.fogStartDistance;
            _sceneFogEnd = RenderSettings.fogEndDistance;
            _sceneExposure = SceneExposure();
        }

        // Post exposure of the strongest other global volume that sets one (the scene's look), 0 when none does.
        static float SceneExposure()
        {
            float exposure = 0f, priority = float.NegativeInfinity;
            var volumes = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
            for (int i = 0; i < volumes.Length; i++)
            {
                var v = volumes[i];
                if (v == _brightnessVolume || !v.isActiveAndEnabled || !v.isGlobal || v.sharedProfile == null) continue;
                if (!v.sharedProfile.TryGet(out ColorAdjustments adjust) || !adjust.active || !adjust.postExposure.overrideState) continue;
                if (v.priority <= priority) continue;
                priority = v.priority;
                exposure = adjust.postExposure.value;
            }
            return exposure;
        }

        // 밝기 as a global volume above the scene's: the profile is made at run time (no asset, no shader lookups).
        static void ApplyBrightness()
        {
            float offset = SettingsCatalog.BrightnessToExposure(GameSettings.Get(SettingId.Brightness));
            if (_brightnessVolume == null)
            {
                // Nothing to do at the default until the player first moves the slider.
                if (Mathf.Approximately(offset, 0f)) return;
                CreateBrightnessVolume();
            }
            _brightnessAdjust.postExposure.Override(_sceneExposure + offset);
        }

        static void CreateBrightnessVolume()
        {
            var go = new GameObject("SettingsBrightness");
            go.SetActive(false);
            Object.DontDestroyOnLoad(go);
            if (_brightnessProfile == null)
            {
                _brightnessProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                _brightnessProfile.name = "SettingsBrightness";
                _brightnessAdjust = _brightnessProfile.Add<ColorAdjustments>(false);
            }
            _brightnessVolume = go.AddComponent<Volume>();
            _brightnessVolume.isGlobal = true;
            _brightnessVolume.priority = BrightnessVolumePriority;
            _brightnessVolume.weight = 1f;
            _brightnessVolume.sharedProfile = _brightnessProfile;
            go.SetActive(true);
        }

        // True when an SSAO feature was found on the active pipeline asset (so the setting can switch it).
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
        // setting without the SSAO part.
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
