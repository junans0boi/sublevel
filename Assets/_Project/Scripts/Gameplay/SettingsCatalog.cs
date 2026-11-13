using System;
using System.Globalization;
using UnityEngine;

namespace Game.Gameplay
{
    // The top tabs of the settings screen (ADR-059).
    public enum SettingsTab { Graphics = 0, Audio = 1, Controls = 2, Keys = 3, Gameplay = 4 }

    // Sub-tabs; only the graphics tab has an advanced page, every other row is on Basic.
    public enum SettingsPage { Basic = 0, Advanced = 1 }

    // Choice = a list stepped with arrows (value = option index), Slider = a number in [Min, Max] snapped to Step,
    // Toggle = off/on (value 0 or 1; Options[0] is the off label).
    public enum SettingKind { Choice, Slider, Toggle }

    // How a slider shows its value.
    public enum SliderFormat { Integer, Percent, Unit01Percent, Decimal2, Factor2 }

    // Every persisted setting. The order is the storage order of the value arrays, not the screen order.
    public enum SettingId
    {
        ScreenMode, Resolution, FpsLimit, VSync, Brightness, RenderScale, FirstPersonFov, QualityPreset,
        AntiAliasing, Shadows, Textures, Effects, ViewDistance, AmbientOcclusion,
        MasterVolume, EffectsVolume, AmbientVolume, UiVolume,
        MouseSensitivity, AimSensitivity, ScopeSensitivity, InvertMouseY,
        DefaultView, CrosshairColor, DamageDirection, KillFeed,
    }

    // One row of the settings screen: where it shows, what it says and how its value is stored.
    public sealed class SettingDef
    {
        public SettingId Id;
        public SettingsTab Tab;
        public SettingsPage Page;
        public string Section;      // small header the row sits under
        public string Label;
        public string Description;  // one or two sentences for the description panel
        public SettingKind Kind;
        public string Key;          // PlayerPrefs key (int for choices and toggles, float for sliders, "WxH" string for the resolution)
        public string[] Options;    // choice and toggle labels
        public float Min, Max, Step, Default;
        public SliderFormat Format;
        public bool ShowsPreview;   // the description panel shows a brightness swatch for this row
        public bool AppliesOnCommit; // screen mode and resolution change the display only when 적용 is pressed

        public int OptionCount => Options != null ? Options.Length : 0;
        // Number of distinct values a slider can take.
        public int StepCount => Kind == SettingKind.Slider ? Mathf.RoundToInt((Max - Min) / Step) + 1 : OptionCount;
    }

    // One read-only line of the key list.
    public sealed class KeyBinding
    {
        public string Section;
        public string Label;   // the action, in Korean
        public string Keys;    // the key names
        public string Description;
    }

    // What the settings screen offers, in screen order, and the pure rules between rows (quality presets, clamping,
    // defaults). No engine state here so it can be tested in edit mode; GameSettings holds the values and
    // GraphicsOptions applies them.
    public static class SettingsCatalog
    {
        public const int PresetLow = 0, PresetMedium = 1, PresetHigh = 2, PresetCustom = 3;
        public const string ResolutionKey = "settings.resolution";
        public const string KeysNote = "키 변경은 추후 지원 예정입니다. 지금은 아래 배치만 확인할 수 있습니다.";

        public static readonly string[] TabLabels = { "그래픽", "오디오", "컨트롤", "키 설정", "게임 플레이" };
        public static readonly string[] PageLabels = { "기본", "고급" };

        static readonly string[] OffOn = { "비활성화", "활성화" };

        // The rows a quality preset sets, and what each preset gives them (rows = 낮음/보통/높음).
        public static readonly SettingId[] AdvancedRows =
        {
            SettingId.AntiAliasing, SettingId.Shadows, SettingId.Textures, SettingId.Effects, SettingId.ViewDistance, SettingId.AmbientOcclusion,
        };
        static readonly int[,] PresetTable =
        {
            // AA  shadows  textures  effects  distance  AO
            { 1,   1,       1,        0,       0,        0 }, // 낮음
            { 1,   2,       2,        1,       2,        1 }, // 보통 = the shipped look (URP asset as built) plus FXAA
            { 2,   3,       2,        1,       3,        1 }, // 높음
        };

        public static readonly SettingDef[] All = Build();
        public static readonly KeyBinding[] KeyBindings = BuildKeys();
        public static int Count => All.Length;

        public static SettingDef Get(SettingId id) => All[(int)id];

        static SettingDef[] Build()
        {
            var rows = new SettingDef[Enum.GetValues(typeof(SettingId)).Length];
            void Add(SettingDef def) => rows[(int)def.Id] = def;

            // ---- graphics / basic ----
            Add(Choice(SettingId.ScreenMode, SettingsTab.Graphics, SettingsPage.Basic, "화면", "화면 모드",
                "게임 창을 전체 화면, 테두리 없는 창, 창 모드 중 하나로 띄웁니다. 적용을 눌러야 바뀝니다.",
                "settings.screenMode", new[] { "전체 화면", "테두리 없는 창", "창 모드" }, 1, onCommit: true));
            Add(Choice(SettingId.Resolution, SettingsTab.Graphics, SettingsPage.Basic, "화면", "해상도",
                "화면에 그리는 가로·세로 픽셀 수입니다. 모니터가 지원하는 크기만 고를 수 있고, 적용을 눌러야 바뀝니다.",
                ResolutionKey, new[] { "1920 × 1080" }, 0, onCommit: true));
            Add(Choice(SettingId.FpsLimit, SettingsTab.Graphics, SettingsPage.Basic, "화면", "인게임 FPS 제한",
                "초당 프레임 수의 상한입니다. 수직 동기화가 켜져 있으면 모니터 주사율을 따르므로 이 값은 무시됩니다.",
                "settings.fpsLimit", new[] { "무제한", "60", "120", "144", "240" }, 0));
            Add(Choice(SettingId.VSync, SettingsTab.Graphics, SettingsPage.Basic, "화면", "수직 동기화",
                "프레임을 모니터 주사율에 맞춰 화면 찢김을 없앱니다. 켜면 입력 지연이 조금 늘 수 있습니다.",
                "settings.vsync", OffOn, 1, SettingKind.Toggle));
            var brightness = Slider(SettingId.Brightness, SettingsTab.Graphics, SettingsPage.Basic, "화질", "밝기",
                "화면 전체의 밝기를 조절합니다. 오른쪽 견본의 가장 어두운 칸이 겨우 보일 때가 알맞습니다.",
                "settings.brightness", 0f, 100f, 1f, 50f, SliderFormat.Integer);
            brightness.ShowsPreview = true;
            Add(brightness);
            Add(Slider(SettingId.RenderScale, SettingsTab.Graphics, SettingsPage.Basic, "화질", "렌더링 크기",
                "화면 해상도 대비 실제로 그리는 크기입니다. 낮추면 프레임이 오르지만 화면이 흐려집니다.",
                "settings.renderScale", 50f, 100f, 5f, 100f, SliderFormat.Percent));
            Add(Slider(SettingId.FirstPersonFov, SettingsTab.Graphics, SettingsPage.Basic, "화질", "1인칭 시야 (FOV)",
                "1인칭 시점에서 보이는 세로 시야각입니다. 넓히면 주변이 더 보이지만 멀리 있는 것은 작아집니다.",
                "settings.firstPersonFov", 60f, 100f, 1f, 70f, SliderFormat.Integer));
            Add(Choice(SettingId.QualityPreset, SettingsTab.Graphics, SettingsPage.Basic, "품질", "전반적인 그래픽 품질",
                "고급 탭의 항목을 한 번에 정합니다. 고급 항목을 직접 바꾸면 사용자 설정이 됩니다.",
                GraphicsOptions.PresetKey, new[] { "낮음", "보통", "높음", "사용자 설정" }, PresetMedium));

            // ---- graphics / advanced (defaults = the 보통 preset) ----
            Add(Choice(SettingId.AntiAliasing, SettingsTab.Graphics, SettingsPage.Advanced, "렌더링", "안티 앨리어싱",
                "물체 가장자리의 계단 현상을 줄입니다. FXAA는 가볍고, SMAA는 더 깔끔하지만 조금 무겁습니다.",
                "settings.antiAliasing", new[] { "끔", "FXAA", "SMAA" }, PresetTable[PresetMedium, 0]));
            Add(Choice(SettingId.Shadows, SettingsTab.Graphics, SettingsPage.Advanced, "렌더링", "그림자",
                "그림자가 그려지는 거리와 선명도입니다. 높을수록 멀리까지 또렷하지만 프레임이 떨어집니다.",
                "settings.shadows", new[] { "끔", "낮음", "보통", "높음" }, PresetTable[PresetMedium, 1]));
            Add(Choice(SettingId.Textures, SettingsTab.Graphics, SettingsPage.Advanced, "렌더링", "텍스처",
                "표면 그림의 해상도입니다. 낮추면 그래픽 메모리를 덜 쓰고 표면이 흐려집니다.",
                "settings.textures", new[] { "낮음", "보통", "높음" }, PresetTable[PresetMedium, 2]));
            Add(Choice(SettingId.Effects, SettingsTab.Graphics, SettingsPage.Advanced, "렌더링", "이펙트",
                "총구 화염, 탄착 먼지, 연기 같은 효과의 양입니다. 낮음이면 입자 수가 줄어듭니다.",
                "settings.effects", new[] { "낮음", "높음" }, PresetTable[PresetMedium, 3]));
            Add(Choice(SettingId.ViewDistance, SettingsTab.Graphics, SettingsPage.Advanced, "거리·조명", "거리 보기",
                "카메라가 그리는 최대 거리입니다. 안개도 함께 맞춰져 경계가 갑자기 끊기지 않습니다.",
                "settings.viewDistance", new[] { "낮음", "보통", "높음", "울트라" }, PresetTable[PresetMedium, 4]));
            Add(Choice(SettingId.AmbientOcclusion, SettingsTab.Graphics, SettingsPage.Advanced, "거리·조명", "앰비언트 오클루전",
                "구석과 틈에 은은한 그늘을 넣어 입체감을 줍니다. 끄면 프레임이 조금 오릅니다.",
                "settings.ambientOcclusion", new[] { "끄기", "켜기" }, PresetTable[PresetMedium, 5], SettingKind.Toggle));

            // ---- audio (0..1, shown as percent) ----
            Add(Slider(SettingId.MasterVolume, SettingsTab.Audio, SettingsPage.Basic, "음량", "전체 음량",
                "게임의 모든 소리에 곱해지는 음량입니다.",
                GameSettings.MasterVolumeKey, 0f, 1f, 0.01f, 1f, SliderFormat.Unit01Percent));
            Add(Slider(SettingId.EffectsVolume, SettingsTab.Audio, SettingsPage.Basic, "음량", "효과음",
                "총성, 발소리, 재장전, 폭발 같은 전투 소리의 음량입니다.",
                "settings.effectsVolume", 0f, 1f, 0.01f, 1f, SliderFormat.Unit01Percent));
            Add(Slider(SettingId.AmbientVolume, SettingsTab.Audio, SettingsPage.Basic, "음량", "환경음",
                "바람, 설비 소음, 지역 경보처럼 주변에서 들리는 소리의 음량입니다.",
                "settings.ambientVolume", 0f, 1f, 0.01f, 1f, SliderFormat.Unit01Percent));
            Add(Slider(SettingId.UiVolume, SettingsTab.Audio, SettingsPage.Basic, "음량", "UI 소리",
                "명중·처치 확인음과 메뉴 소리의 음량입니다.",
                "settings.uiVolume", 0f, 1f, 0.01f, 1f, SliderFormat.Unit01Percent));

            // ---- controls ----
            Add(Slider(SettingId.MouseSensitivity, SettingsTab.Controls, SettingsPage.Basic, "마우스", "마우스 감도",
                "마우스를 움직일 때 시점이 도는 빠르기입니다.",
                GameSettings.MouseSensitivityKey, GameSettings.MinMouseSensitivity, GameSettings.MaxMouseSensitivity, 0.01f,
                DeviceInput.DefaultMouseSensitivity, SliderFormat.Decimal2));
            Add(Slider(SettingId.AimSensitivity, SettingsTab.Controls, SettingsPage.Basic, "마우스", "조준 감도 배율",
                "견착하거나 정조준할 때 마우스 감도에 곱해지는 값입니다.",
                GameSettings.AimSensitivityKey, GameSettings.MinAimSensitivityFactor, GameSettings.MaxAimSensitivityFactor, 0.01f,
                GameSettings.DefaultAimSensitivityFactor, SliderFormat.Factor2));
            Add(Slider(SettingId.ScopeSensitivity, SettingsTab.Controls, SettingsPage.Basic, "마우스", "배율 조준경 감도 배율",
                "2배율 이상 조준경을 볼 때 추가로 곱해지는 값입니다. 조준경 배율에 따른 기본 감속 위에 적용됩니다.",
                "settings.scopeSensitivityFactor", GameSettings.MinAimSensitivityFactor, GameSettings.MaxAimSensitivityFactor, 0.01f,
                1f, SliderFormat.Factor2));
            Add(Choice(SettingId.InvertMouseY, SettingsTab.Controls, SettingsPage.Basic, "마우스", "마우스 상하 반전",
                "마우스를 위로 밀면 시점이 아래로 내려가게 합니다.",
                "settings.invertMouseY", OffOn, 0, SettingKind.Toggle));

            // ---- gameplay ----
            Add(Choice(SettingId.DefaultView, SettingsTab.Gameplay, SettingsPage.Basic, "시점", "기본 시점",
                "매치를 시작할 때의 시점입니다. 경기 중에는 V 키로 언제든 바꿀 수 있습니다.",
                GameSettings.DefaultViewKey, new[] { "3인칭", "1인칭" }, 0));
            Add(Choice(SettingId.CrosshairColor, SettingsTab.Gameplay, SettingsPage.Basic, "화면 표시", "조준선 색",
                "화면 가운데 조준선의 색입니다. 배경에 묻히지 않는 색을 고르세요.",
                "settings.crosshairColor", new[] { "흰색", "녹색", "노랑", "청록" }, 0));
            Add(Choice(SettingId.DamageDirection, SettingsTab.Gameplay, SettingsPage.Basic, "화면 표시", "피격 방향 표시",
                "공격받은 방향을 화면 가운데 둘레에 표시합니다.",
                "settings.damageDirection", OffOn, 1, SettingKind.Toggle));
            Add(Choice(SettingId.KillFeed, SettingsTab.Gameplay, SettingsPage.Basic, "화면 표시", "킬 로그 표시",
                "누가 누구를 쓰러뜨렸는지 화면 오른쪽 위에 보여줍니다.",
                "settings.killFeed", OffOn, 1, SettingKind.Toggle));
            return rows;
        }

        static SettingDef Choice(SettingId id, SettingsTab tab, SettingsPage page, string section, string label, string description,
            string key, string[] options, int defaultIndex, SettingKind kind = SettingKind.Choice, bool onCommit = false) =>
            new SettingDef
            {
                Id = id, Tab = tab, Page = page, Section = section, Label = label, Description = description, Kind = kind, Key = key,
                Options = options, Min = 0f, Max = options.Length - 1, Step = 1f, Default = defaultIndex, AppliesOnCommit = onCommit,
            };

        static SettingDef Slider(SettingId id, SettingsTab tab, SettingsPage page, string section, string label, string description,
            string key, float min, float max, float step, float defaultValue, SliderFormat format) =>
            new SettingDef
            {
                Id = id, Tab = tab, Page = page, Section = section, Label = label, Description = description, Kind = SettingKind.Slider,
                Key = key, Min = min, Max = max, Step = step, Default = defaultValue, Format = format,
            };

        static KeyBinding[] BuildKeys()
        {
            const string move = "이동", fight = "전투", other = "화면·기타";
            KeyBinding K(string section, string label, string keys, string description) =>
                new KeyBinding { Section = section, Label = label, Keys = keys, Description = description };
            return new[]
            {
                K(move, "이동", "W A S D", "앞뒤와 좌우로 걷습니다."),
                K(move, "달리기", "Shift", "누르고 있는 동안 달립니다. 달리는 중에는 사격·재장전·조준을 할 수 없습니다."),
                K(move, "점프", "Space", "제자리에서 뛰어오르거나 낮은 장애물을 넘습니다."),
                K(move, "앉기", "C", "앉거나 일어섭니다. 앉으면 탄퍼짐이 줄고 몸이 작아집니다."),
                K(move, "엎드리기", "Z", "엎드리거나 일어섭니다. 이동은 매우 느리지만 탄퍼짐이 가장 적습니다."),
                K(move, "기울이기", "Q / E", "왼쪽·오른쪽으로 몸을 기울여 엄폐물 밖을 살핍니다."),
                K(move, "둘러보기", "Alt", "누르고 있는 동안 몸은 그대로 두고 시점만 돌립니다."),
                K(fight, "사격", "좌클릭", "들고 있는 무기를 쏩니다. 맨손이면 주먹을 휘두릅니다."),
                K(fight, "견착", "우클릭 (꾹)", "누르고 있는 동안 3인칭 그대로 어깨 너머로 조준합니다."),
                K(fight, "정조준", "우클릭 (짧게)", "짧게 누르면 조준기를 들여다보고, 다시 누르면 풀립니다."),
                K(fight, "재장전", "R", "탄창을 갈아 끼웁니다."),
                K(fight, "발사 모드 전환", "B", "단발과 연발처럼 무기가 지원하는 발사 방식을 바꿉니다."),
                K(fight, "무기 슬롯", "1 ~ 5", "1·2 주무기, 3 권총, 4 근접, 5 투척 슬롯을 듭니다."),
                K(fight, "무장 해제", "X", "무기를 손에서 내려놓습니다. 맨손이면 조금 더 빨리 움직입니다."),
                K(fight, "투척", "G", "투척물을 던집니다. 누르고 있으면 궤적이 보입니다."),
                K(fight, "투척물 전환", "T", "가진 투척물 종류를 바꿉니다."),
                K(fight, "회복", "H", "붕대·구급 키트·구급상자 중 알맞은 것으로 체력을 회복합니다."),
                K(other, "줍기·상호작용", "F", "물건을 줍거나 열쇠로 통로 차단기를 엽니다."),
                K(other, "시점 전환", "V", "1인칭과 3인칭을 바꿉니다."),
                K(other, "인벤토리", "Tab", "가방과 주변 물건을 엽니다."),
                K(other, "지도", "M", "전체 지도를 엽니다."),
                K(other, "메뉴", "Esc", "일시정지 메뉴를 열고 닫습니다."),
            };
        }

        // ---- value rules ----

        // The value a row starts with when nothing is saved and after 기본.
        public static float[] Defaults()
        {
            var values = new float[Count];
            for (int i = 0; i < Count; i++) values[i] = All[i].Default;
            return values;
        }

        // Clamps to the row's range and snaps sliders to their step and choices to an index.
        public static float Clamp(SettingDef def, float value)
        {
            if (float.IsNaN(value)) value = def.Default;
            if (def.Kind != SettingKind.Slider)
                return Mathf.Clamp(Mathf.RoundToInt(value), 0, Mathf.Max(0, def.OptionCount - 1));
            value = Mathf.Clamp(value, def.Min, def.Max);
            return Mathf.Clamp(def.Min + Mathf.Round((value - def.Min) / def.Step) * def.Step, def.Min, def.Max);
        }

        // Which of the slider's StepCount values `value` is (0 = Min).
        public static int StepIndex(SettingDef def, float value) =>
            Mathf.Clamp(Mathf.RoundToInt((value - def.Min) / def.Step), 0, Mathf.Max(0, def.StepCount - 1));

        public static bool IsAdvanced(SettingId id) => Array.IndexOf(AdvancedRows, id) >= 0;

        // What preset `preset` (낮음/보통/높음) gives the advanced row `id`.
        public static int PresetValue(int preset, SettingId id)
        {
            int column = Array.IndexOf(AdvancedRows, id);
            if (column < 0) throw new ArgumentException(id + " is not set by the quality preset");
            return PresetTable[Mathf.Clamp(preset, PresetLow, PresetHigh), column];
        }

        // Sets the advanced rows to the preset's values (사용자 설정 leaves them alone).
        public static void ApplyPreset(float[] values, int preset)
        {
            if (preset < PresetLow || preset > PresetHigh) return;
            for (int i = 0; i < AdvancedRows.Length; i++) values[(int)AdvancedRows[i]] = PresetTable[preset, i];
        }

        // Changes one row and the rows tied to it: a preset sets the advanced rows, an advanced row makes the preset 사용자 설정.
        public static void Change(float[] values, SettingId id, float value)
        {
            var def = Get(id);
            float clamped = Clamp(def, value);
            values[(int)id] = clamped;
            if (id == SettingId.QualityPreset) ApplyPreset(values, (int)clamped);
            else if (IsAdvanced(id)) values[(int)SettingId.QualityPreset] = PresetCustom;
        }

        // 기본: every row of `tab` back to its default (`resolutionDefault` is the screen's native size, found at run time).
        public static void ResetTab(float[] values, SettingsTab tab, int resolutionDefault = 0)
        {
            for (int i = 0; i < Count; i++)
            {
                if (All[i].Tab != tab) continue;
                values[i] = All[i].Id == SettingId.Resolution ? Clamp(All[i], resolutionDefault) : All[i].Default;
            }
            if (tab == SettingsTab.Graphics) ApplyPreset(values, (int)values[(int)SettingId.QualityPreset]);
        }

        // The resolution list is the monitor's; GraphicsOptions fills it in before the first scene.
        public static void SetResolutionOptions(string[] labels)
        {
            if (labels == null || labels.Length == 0) return;
            var def = Get(SettingId.Resolution);
            def.Options = labels;
            def.Max = labels.Length - 1;
        }

        // Exposure offset (EV) of the 밝기 slider: 50 = none, 0 = one stop darker, 100 = one stop brighter.
        public static float BrightnessToExposure(float brightness) => (Mathf.Clamp(brightness, 0f, 100f) - 50f) / 50f;

        // The text a slider value shows. Allocates: the screen builds every step's text once.
        public static string FormatSlider(SettingDef def, float value)
        {
            var c = CultureInfo.InvariantCulture;
            switch (def.Format)
            {
                case SliderFormat.Percent: return Mathf.RoundToInt(value).ToString(c) + "%";
                case SliderFormat.Unit01Percent: return Mathf.RoundToInt(value * 100f).ToString(c) + "%";
                case SliderFormat.Decimal2: return value.ToString("0.00", c);
                case SliderFormat.Factor2: return "x" + value.ToString("0.00", c);
                default: return Mathf.RoundToInt(value).ToString(c);
            }
        }

        // The texts of every step of a slider, index = StepIndex.
        public static string[] SliderTexts(SettingDef def)
        {
            var texts = new string[def.StepCount];
            for (int i = 0; i < texts.Length; i++) texts[i] = FormatSlider(def, def.Min + i * def.Step);
            return texts;
        }
    }
}
