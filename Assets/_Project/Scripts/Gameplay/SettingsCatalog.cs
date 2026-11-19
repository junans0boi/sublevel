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
        MouseSensitivity, AimSensitivity, ScopeSens2x, ScopeSens3x, ScopeSens4x, ScopeSens6x, ScopeSens8x, ScopeSens15x, InvertMouseY,
        SightsStyle, ShoulderStyle,
        DefaultView, CrosshairColor, DamageDirection, KillFeed,
        BotDifficulty, // ADR-117
        DayTime, Weather, // ADR-119
        NetStats,         // ADR-128
        CameraShake, Blood, // ADR-470, ADR-471
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

    // One line of the key list: an action and the key it is on now (changeable, #140).
    public sealed class KeyBinding
    {
        public string Section;
        public string Label;   // the action, in Korean
        public BindAction Action;
        public string Description;
        public string Keys => KeyBinds.Name(Action);   // the key's name now
    }

    // What the settings screen offers, in screen order, and the pure rules between rows (quality presets, clamping,
    // defaults). No engine state here so it can be tested in edit mode; GameSettings holds the values and
    // GraphicsOptions applies them.
    public static class SettingsCatalog
    {
        public const int PresetLow = 0, PresetMedium = 1, PresetHigh = 2, PresetCustom = 3;
        public const string ResolutionKey = "settings.resolution";
        public const string KeysNote = "바꿀 줄을 누른 뒤 새 키를 누르면 바로 저장됩니다. Esc는 취소, 기본은 모든 키를 처음대로 되돌립니다.";

        // Magnifications with their own mouse factor (#139).
        public static readonly float[] ScopeZoomStops = { 2f, 3f, 4f, 6f, 8f, 15f };

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
            Add(Slider(SettingId.AimSensitivity, SettingsTab.Controls, SettingsPage.Basic, "마우스", "1배 조준 감도 (견착·반사 사이트)",
                "견착하거나 배율 없는 조준 사이트로 정조준할 때 마우스 감도에 곱해지는 값입니다.",
                GameSettings.AimSensitivityKey, GameSettings.MinAimSensitivityFactor, GameSettings.MaxAimSensitivityFactor, 0.01f,
                GameSettings.DefaultAimSensitivityFactor, SliderFormat.Factor2));
            for (int i = 0; i < ScopeZoomStops.Length; i++)
            {
                int zoom = (int)ScopeZoomStops[i];
                Add(Slider(SettingId.ScopeSens2x + i, SettingsTab.Controls, SettingsPage.Basic, "배율 조준경", zoom + "배 조준경 감도",
                    zoom + "배율 조준경을 볼 때 마우스 감도에 곱해지는 값입니다. 조준경 배율에 따른 기본 감속 위에 적용되고 다른 배율에는 영향이 없습니다.",
                    "settings.scopeSens." + zoom, GameSettings.MinAimSensitivityFactor, GameSettings.MaxAimSensitivityFactor, 0.01f,
                    1f, SliderFormat.Factor2));
            }
            Add(Choice(SettingId.InvertMouseY, SettingsTab.Controls, SettingsPage.Basic, "마우스", "마우스 상하 반전",
                "마우스를 위로 밀면 시점이 아래로 내려가게 합니다.",
                "settings.invertMouseY", OffOn, 0, SettingKind.Toggle));
            Add(Choice(SettingId.SightsStyle, SettingsTab.Controls, SettingsPage.Basic, "조준 방식", "정조준 방식",
                "탭 전환: 우클릭을 짧게 누를 때마다 정조준이 켜지고 꺼집니다. 꾹 누르기: 누르고 있는 동안만 정조준하고 견착은 쓰지 않습니다.",
                "settings.sightsStyle", new[] { "탭 전환", "꾹 누르기" }, 0));
            Add(Choice(SettingId.ShoulderStyle, SettingsTab.Controls, SettingsPage.Basic, "조준 방식", "견착 방식",
                "꾹 누르기: 우클릭을 길게 누르는 동안만 견착합니다. 탭 전환: 길게 눌러 견착하면 손을 떼도 유지되고, 다시 누르면 풀립니다. 정조준 방식이 탭 전환일 때만 적용됩니다.",
                "settings.shoulderStyle", new[] { "꾹 누르기", "탭 전환" }, 0));

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
            Add(Choice(SettingId.NetStats, SettingsTab.Gameplay, SettingsPage.Basic, "화면 표시", "네트워크 상태 표시",
                "핑, 패킷 손실, 서버 틱을 화면 오른쪽 위(킬 로그 아래)에 보여줍니다. 멀티플레이에서 끊김의 원인을 볼 때 켜세요. 경기 중에는 키 설정의 '네트워크 상태'(기본 F3)로도 켜고 끌 수 있습니다.",
                "settings.netStats", OffOn, 0, SettingKind.Toggle));
            Add(Choice(SettingId.BotDifficulty, SettingsTab.Gameplay, SettingsPage.Basic, "봇", "봇 난이도",
                "봇의 반응 속도, 조준 정확도, 시야와 청력을 정합니다. 쉬움은 늦게 알아채고 자주 빗맞히며, 어려움은 빨리 알아채고 정확하게 쏩니다. 다음 매치부터 적용됩니다.",
                "settings.botDifficulty", Game.Core.BotLevels.Labels, 1));
            Add(Choice(SettingId.DayTime, SettingsTab.Gameplay, SettingsPage.Basic, "환경", "시간대",
                "무작위: 매치마다 시작 시각이 달라지고 경기 중 시간이 흐릅니다. 그 밖의 값은 그 시간대로 고정합니다.",
                "settings.dayTime", new[] { "무작위", "낮", "저녁", "밤" }, 1)); // default 낮 (ADR-210)
            Add(Choice(SettingId.Weather, SettingsTab.Gameplay, SettingsPage.Basic, "환경", "날씨",
                "무작위: 매치마다 날씨가 달라집니다. 비와 안개는 시야를 줄입니다.",
                "settings.weather", new[] { "무작위", "맑음", "비", "안개" }, 1)); // default 맑음 (ADR-210)
            Add(Slider(SettingId.CameraShake, SettingsTab.Gameplay, SettingsPage.Basic, "효과", "화면 흔들림",
                "근처 폭발, 피격, 높은 곳 착지, 자기 사격 때 화면이 흔들리는 세기입니다. 0%면 흔들리지 않습니다. 조준점에는 영향이 없습니다.",
                "settings.cameraShake", 0f, 1f, 0.05f, 1f, SliderFormat.Unit01Percent));
            Add(Choice(SettingId.Blood, SettingsTab.Gameplay, SettingsPage.Basic, "효과", "피 효과",
                "피격 시 튀는 피와 바닥·벽의 핏자국입니다. 적게는 튀는 양이 줄고 벽 핏자국이 없으며, 끄면 피가 보이지 않습니다.",
                "settings.blood", new[] { "끔", "적게", "보통" }, 2));
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
            KeyBinding K(string section, string label, BindAction action, string description) =>
                new KeyBinding { Section = section, Label = label, Action = action, Description = description };
            return new[]
            {
                K(move, "앞으로", BindAction.MoveForward, "앞으로 걷습니다."),
                K(move, "뒤로", BindAction.MoveBack, "뒤로 걷습니다."),
                K(move, "왼쪽", BindAction.MoveLeft, "왼쪽으로 걷습니다."),
                K(move, "오른쪽", BindAction.MoveRight, "오른쪽으로 걷습니다."),
                K(move, "달리기", BindAction.Sprint, "누르고 있는 동안 달립니다. 달리는 중에는 사격·재장전·조준을 할 수 없습니다."),
                K(move, "점프·넘기", BindAction.Jump, "뛰어오릅니다. 앞에 장애물이 있으면 넘거나 위로 올라가고, 창문은 넘어 들어갑니다(작은 창문은 앉아서). 동작 초반에 뒤로 가기 키를 누르면 취소하고 제자리로 돌아옵니다."),
                K(move, "자동 달리기", BindAction.AutoRun, "앞으로 계속 걷습니다. 달리기 키를 누르면 달립니다. 앞뒤 키를 누르거나 조준하거나 화면을 열면 멈춥니다."),
                K(move, "천천히 걷기", BindAction.SlowWalk, "누르고 있는 동안 천천히 걸어 발소리가 작아집니다. 달릴 수 없습니다."),
                K(move, "앉기", BindAction.Crouch, "앉거나 일어섭니다. 앉으면 탄퍼짐이 줄고 몸이 작아집니다."),
                K(move, "엎드리기", BindAction.Prone, "엎드리거나 일어섭니다. 이동은 매우 느리지만 탄퍼짐이 가장 적습니다. 차에서는 시동을 겁니다."),
                K(move, "왼쪽으로 기울이기", BindAction.LeanLeft, "누르고 있는 동안 왼쪽으로 몸을 기울여 엄폐물 밖을 살핍니다."),
                K(move, "오른쪽으로 기울이기", BindAction.LeanRight, "누르고 있는 동안 오른쪽으로 몸을 기울여 엄폐물 밖을 살핍니다."),
                K(move, "자유 시점 360°", BindAction.FreeLook, "누르고 있는 동안 몸과 이동 방향은 그대로 두고 시점만 한 바퀴 돌릴 수 있습니다. 떼면 몸 방향으로 돌아옵니다. 자유 시점 중에는 사격할 수 없고, 조준하면 풀립니다."),
                K(fight, "사격", BindAction.Fire, "들고 있는 무기를 쏩니다. 맨손이면 주먹을 휘두릅니다. 차에서는 경적입니다."),
                K(fight, "견착·정조준", BindAction.Aim, "기본은 길게 누르면 어깨 너머 견착, 짧게 누르면 정조준 전환입니다. 방식은 컨트롤 탭에서 고릅니다."),
                K(fight, "재장전", BindAction.Reload, "탄창을 갈아 끼웁니다."),
                K(fight, "발사 모드 전환", BindAction.FireMode, "단발과 연발처럼 무기가 지원하는 발사 방식을 바꿉니다."),
                K(fight, "주무기 1", BindAction.Slot1, "첫 번째 주무기 슬롯을 듭니다."),
                K(fight, "주무기 2", BindAction.Slot2, "두 번째 주무기 슬롯을 듭니다."),
                K(fight, "권총", BindAction.Slot3, "권총 슬롯을 듭니다."),
                K(fight, "근접", BindAction.Slot4, "근접 무기 슬롯을 듭니다."),
                K(fight, "투척 슬롯", BindAction.Slot5, "투척 슬롯을 듭니다."),
                K(fight, "무장 해제", BindAction.Holster, "무기를 손에서 내려놓습니다. 맨손이면 조금 더 빨리 움직입니다."),
                K(fight, "투척", BindAction.Throw, "투척물을 던집니다. 누르고 있으면 궤적이 보입니다."),
                K(fight, "투척물 전환", BindAction.CycleThrowable, "가진 투척물 종류를 바꿉니다."),
                K(fight, "회복", BindAction.Heal, "붕대·구급 키트·구급상자 중 알맞은 것으로 체력을 회복합니다."),
                K(fight, "부스트", BindAction.Boost, "가진 부스트 아이템 가운데 가장 작은 것을 마십니다. 차에서는 연료통을 씁니다."),
                K(fight, "방독", BindAction.GasCover, "방독 필터나 해독제를 씁니다. 해독제가 있으면 먼저 쓰고, 최종 구역에서는 효과가 없습니다."),
                K(other, "줍기·상호작용", BindAction.Interact, "물건을 줍거나 열쇠로 통로 차단기를 엽니다."),
                K(other, "시점 전환", BindAction.ToggleView, "1인칭과 3인칭을 바꿉니다."),
                K(other, "인벤토리", BindAction.Inventory, "가방과 주변 물건을 엽니다."),
                K(other, "지도", BindAction.Map, "전체 지도를 엽니다. 지도를 클릭하면 마커를 찍고, 우클릭하면 지웁니다."),
                K(other, "네트워크 상태", BindAction.NetStats, "핑, 패킷 손실, 서버 틱 표시를 켜고 끕니다."),
                K(other, "핑", BindAction.Ping, "조준점이 가리키는 지점(최대 600m)에 마커를 찍습니다. 팀 매치에서는 길게 누르면 마커 휠이 열립니다. 방위 띠·미니맵·화면에 방향과 거리가 나오고, 10m 안으로 다가가면 사라집니다."),
                K(other, "팀원 업기", BindAction.Carry, "쓰러진 팀원 가까이에서 누르면 업습니다. 업은 채로는 느리게 걷고, 다시 누르면 내려놓습니다."),
                K(other, "감정 표현", BindAction.Emote, "누르고 있으면 감정 표현 휠이 열립니다. 마우스를 움직여 고르고 놓으면 표현합니다."),
                K(other, "전조등", BindAction.Headlights, "운전 중 전조등을 켜고 끕니다. 밤·저녁·비·안개에는 엔진이 켜져 있으면 저절로 켜집니다."),
                K(other, "메뉴", BindAction.Menu, "일시정지 메뉴를 열고 닫습니다. 설정에서 키를 고르는 중에는 Esc가 취소입니다."),
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
