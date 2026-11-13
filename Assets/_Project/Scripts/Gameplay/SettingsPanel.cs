using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The settings panel shared by the main menu and the Esc menu: mouse sensitivity, aim sensitivity factor, volume,
    // graphics preset, default view, and the read-only key list. Every change goes straight to GameSettings and
    // GraphicsOptions (which apply and save it).
    public class SettingsPanel
    {
        const float Width = 760f;
        const float ControlRowHeight = 24f;
        const float OptionHeight = 44f;
        const float OptionGap = 12f;

        // Key list shown at the bottom. Rebinding comes later; this is read-only.
        static readonly string[,] Controls =
        {
            { "W A S D", "이동" },
            { "Shift", "달리기" },
            { "Space", "점프" },
            { "좌클릭", "사격" },
            { "우클릭", "꾹: 견착 / 짧게: 조준" },
            { "C / Z", "앉기 / 엎드리기" },
            { "Q / E", "기울이기" },
            { "Alt", "둘러보기" },
            { "F", "줍기 / 열쇠로 통로 열기" },
            { "R", "재장전" },
            { "1 ~ 4", "무기 전환" },
            { "5", "투척 슬롯" },
            { "X", "무장 해제" },
            { "H", "회복 (붕대·키트·구급상자)" },
            { "G", "투척 (꾹: 궤적)" },
            { "T", "투척물 전환" },
            { "B", "발사 모드 전환" },
            { "V", "시점 전환" },
            { "Tab", "인벤토리" },
            { "M", "지도" },
            { "Esc", "메뉴" },
        };

        static readonly GraphicsPreset[] Presets = { GraphicsPreset.Low, GraphicsPreset.Medium, GraphicsPreset.High };

        public RectTransform Root { get; private set; }
        public Slider Sensitivity { get; private set; }
        public Slider AimFactor { get; private set; }
        public Slider Volume { get; private set; }
        public Button[] PresetButtons { get; private set; }
        public Button[] ViewButtons { get; private set; }

        Text _sensitivityValue, _aimFactorValue, _volumeValue;

        // Builds the panel under `parent`; `onBack` is the 뒤로 button.
        public RectTransform Build(Transform parent, string name, UnityAction onBack)
        {
            Root = UiKit.CenteredPanel(name, parent, Width);
            float pad = UiKit.MenuPadding;
            float inner = Width - pad * 2f;
            float y = pad;

            UiKit.Label(UiKit.HangRow("Title", Root, inner, 44f, y), "설정", 34, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            y += 44f + 20f;

            Sensitivity = UiKit.SliderRow(Root, "마우스 감도", inner, ref y, GameSettings.MinMouseSensitivity,
                GameSettings.MaxMouseSensitivity, GameSettings.MouseSensitivity, out _sensitivityValue);
            AimFactor = UiKit.SliderRow(Root, "조준 감도 배율", inner, ref y, GameSettings.MinAimSensitivityFactor,
                GameSettings.MaxAimSensitivityFactor, GameSettings.AimSensitivityFactor, out _aimFactorValue);
            Volume = UiKit.SliderRow(Root, "전체 음량", inner, ref y, 0f, 1f, GameSettings.MasterVolume, out _volumeValue);
            Sensitivity.onValueChanged.AddListener(v => { GameSettings.MouseSensitivity = v; UpdateValueLabels(); });
            AimFactor.onValueChanged.AddListener(v => { GameSettings.AimSensitivityFactor = v; UpdateValueLabels(); });
            Volume.onValueChanged.AddListener(v => { GameSettings.MasterVolume = v; UpdateValueLabels(); });

            UiKit.Label(UiKit.HangRow("GraphicsHeader", Root, inner, 26f, y), "그래픽 품질", 18, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 30f;
            PresetButtons = new Button[Presets.Length];
            for (int i = 0; i < Presets.Length; i++)
            {
                var preset = Presets[i];
                PresetButtons[i] = OptionButton(Root, GraphicsOptions.Label(preset), i, Presets.Length, inner, y, () => ChoosePreset(preset));
            }
            y += OptionHeight + 24f;

            UiKit.Label(UiKit.HangRow("ViewHeader", Root, inner, 26f, y), "기본 시점", 18, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 30f;
            ViewButtons = new Button[2];
            ViewButtons[0] = OptionButton(Root, "3인칭", 0, 2, inner, y, () => ChooseView(false));
            ViewButtons[1] = OptionButton(Root, "1인칭", 1, 2, inner, y, () => ChooseView(true));
            y += OptionHeight + 24f;

            UiKit.Label(UiKit.HangRow("ControlsHeader", Root, inner, 28f, y), "조작법", 18, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 34f;
            int perColumn = (Controls.GetLength(0) + 1) / 2;
            float columnWidth = inner * 0.5f;
            for (int i = 0; i < Controls.GetLength(0); i++)
            {
                int column = i / perColumn;
                var row = UiKit.HangRow("Control" + i, Root, columnWidth, ControlRowHeight, y + (i % perColumn) * ControlRowHeight,
                    (column == 0 ? -1f : 1f) * columnWidth * 0.5f);
                var key = UiKit.Rect("Key", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(110f, ControlRowHeight));
                UiKit.Label(key, Controls[i, 0], 18, UiKit.Accent);
                var action = UiKit.Rect("Action", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(116f, 0f), new Vector2(columnWidth - 116f, ControlRowHeight));
                UiKit.Label(action, Controls[i, 1], 18, UiKit.Text);
            }
            y += perColumn * ControlRowHeight + 20f;

            y = UiKit.MenuButton(Root, "뒤로", Width, y, onBack);
            Root.sizeDelta = new Vector2(Width, y - UiKit.MenuButtonGap + pad);
            Sync();
            return Root;
        }

        // Shows the current values (after anything else changed them).
        public void Sync()
        {
            Sensitivity.SetValueWithoutNotify(GameSettings.MouseSensitivity);
            AimFactor.SetValueWithoutNotify(GameSettings.AimSensitivityFactor);
            Volume.SetValueWithoutNotify(GameSettings.MasterVolume);
            UpdateValueLabels();
            for (int i = 0; i < PresetButtons.Length; i++) UiKit.SetSelected(PresetButtons[i], Presets[i] == GraphicsOptions.Preset);
            UiKit.SetSelected(ViewButtons[0], !GameSettings.DefaultViewFirstPerson);
            UiKit.SetSelected(ViewButtons[1], GameSettings.DefaultViewFirstPerson);
        }

        void ChoosePreset(GraphicsPreset preset)
        {
            GraphicsOptions.Preset = preset;
            Sync();
        }

        void ChooseView(bool firstPerson)
        {
            GameSettings.DefaultViewFirstPerson = firstPerson;
            Sync();
        }

        void UpdateValueLabels()
        {
            _sensitivityValue.text = GameSettings.MouseSensitivity.ToString("0.00", CultureInfo.InvariantCulture);
            _aimFactorValue.text = "x" + GameSettings.AimSensitivityFactor.ToString("0.00", CultureInfo.InvariantCulture);
            _volumeValue.text = Mathf.RoundToInt(GameSettings.MasterVolume * 100f).ToString(CultureInfo.InvariantCulture) + "%";
        }

        // One of `count` equal buttons side by side across `inner`.
        static Button OptionButton(Transform panel, string text, int index, int count, float inner, float y, UnityAction onClick)
        {
            float w = (inner - OptionGap * (count - 1)) / count;
            float x = -inner * 0.5f + w * 0.5f + index * (w + OptionGap);
            return UiKit.Button(UiKit.HangRow(text, panel, w, OptionHeight, y, x), text, 22, onClick);
        }
    }
}
