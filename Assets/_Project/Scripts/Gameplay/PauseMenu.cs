using System.Globalization;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Esc pause menu. Created for the local player by PlayerController; shows itself while UiFocus says so.
    // The match keeps running behind it (this process hosts it): the menu only takes the mouse and keyboard,
    // which PlayerController already ignores while any screen is open.
    public class PauseMenu : MonoBehaviour
    {
        const int SortOrder = 100;
        const float MainWidth = 420f;
        const float SettingsWidth = 600f;
        const float Padding = 28f;
        const float ButtonHeight = 56f;
        const float ButtonGap = 12f;
        const float ControlRowHeight = 24f;

        static readonly Color Dim = new Color(0f, 0f, 0f, 0.6f);

        // Key list shown in the settings panel. Rebinding comes later; this is read-only.
        static readonly string[,] Controls =
        {
            { "W A S D", "이동" },
            { "Shift", "달리기" },
            { "Space", "점프" },
            { "좌클릭", "사격" },
            { "우클릭", "꾹: 견착 / 짧게: 조준" },
            { "Q / E", "기울이기" },
            { "F", "줍기" },
            { "R", "재장전" },
            { "1 / 2", "무기 전환" },
            { "H", "구급상자 사용" },
            { "V", "시점 전환" },
            { "Tab", "인벤토리" },
            { "Esc", "메뉴" },
        };

        public static PauseMenu Instance { get; private set; }

        public bool IsOpen => _root != null && _root.activeSelf;
        public bool SettingsShown => _settings != null && _settings.activeSelf;

        GameObject _root;
        GameObject _main;
        GameObject _settings;
        Slider _sensitivity;
        Slider _volume;
        Text _sensitivityValue;
        Text _volumeValue;

        void Awake()
        {
            Instance = this;
            Build();
            UiFocus.Changed += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            UiFocus.Changed -= Refresh;
            if (Instance == this) Instance = null;
        }

        // RESUME
        public void Resume() => UiFocus.Close();

        // RESTART: a fresh match. Only the server can reload the match scene; in solo this process is the host.
        public void Restart()
        {
            UiFocus.Close();
            var me = Combatant.LocalPlayer;
            bool isServer = me != null && me.IsServerInitialized;
            if (MatchDirector.Instance != null && isServer) MatchDirector.Instance.Restart();
            else Debug.LogWarning("PauseMenu: restart ignored, this process is not hosting a match.");
        }

        // SETTINGS
        public void ShowSettings()
        {
            SyncSettingsWidgets();
            ShowPanels(true);
        }

        // BACK
        public void Back() => ShowPanels(false);

        // QUIT: straight out, no confirmation.
        public void Quit() => Application.Quit();

        void Refresh()
        {
            // Scene unloads can raise the focus change after the canvas is already gone.
            if (_root == null) return;
            bool open = UiFocus.Current == UiScreen.Menu;
            // Every open starts on the main column, whatever was showing when the menu was closed.
            if (!open || !_root.activeSelf) ShowPanels(false);
            _root.SetActive(open);
        }

        void ShowPanels(bool settings)
        {
            if (_main == null || _settings == null) return;
            _main.SetActive(!settings);
            _settings.SetActive(settings);
        }

        void SyncSettingsWidgets()
        {
            _sensitivity.SetValueWithoutNotify(GameSettings.MouseSensitivity);
            _volume.SetValueWithoutNotify(GameSettings.MasterVolume);
            UpdateValueLabels();
        }

        void UpdateValueLabels()
        {
            _sensitivityValue.text = GameSettings.MouseSensitivity.ToString("0.00", CultureInfo.InvariantCulture);
            _volumeValue.text = Mathf.RoundToInt(GameSettings.MasterVolume * 100f).ToString(CultureInfo.InvariantCulture) + "%";
        }

        void OnSensitivityChanged(float value)
        {
            GameSettings.MouseSensitivity = value;
            UpdateValueLabels();
        }

        void OnVolumeChanged(float value)
        {
            GameSettings.MasterVolume = value;
            UpdateValueLabels();
        }

        void Build()
        {
            var canvas = UiKit.Canvas("PauseMenu", SortOrder, true);
            _root = canvas.gameObject;
            _root.transform.SetParent(transform, false);
            UiKit.Box(UiKit.Fill("Dim", _root.transform), Dim, true);
            BuildMain(_root.transform);
            BuildSettings(_root.transform);
        }

        void BuildMain(Transform parent)
        {
            var panel = Panel("Main", parent, MainWidth);
            _main = panel.gameObject;
            float y = Padding;
            y = Title(panel, "일시정지", MainWidth, y);
            y = MenuButton(panel, "계속하기", MainWidth, y, Resume);
            y = MenuButton(panel, "다시 시작", MainWidth, y, Restart);
            y = MenuButton(panel, "설정", MainWidth, y, ShowSettings);
            y = MenuButton(panel, "게임 종료", MainWidth, y, Quit);
            panel.sizeDelta = new Vector2(MainWidth, y - ButtonGap + Padding);
        }

        void BuildSettings(Transform parent)
        {
            var panel = Panel("Settings", parent, SettingsWidth);
            _settings = panel.gameObject;
            float inner = SettingsWidth - Padding * 2f;
            float y = Padding;
            y = Title(panel, "설정", SettingsWidth, y);

            _sensitivity = SliderRow(panel, "마우스 감도", inner, ref y, GameSettings.MinMouseSensitivity,
                GameSettings.MaxMouseSensitivity, GameSettings.MouseSensitivity, out _sensitivityValue);
            _volume = SliderRow(panel, "전체 음량", inner, ref y, 0f, 1f, GameSettings.MasterVolume, out _volumeValue);
            _sensitivity.onValueChanged.AddListener(OnSensitivityChanged);
            _volume.onValueChanged.AddListener(OnVolumeChanged);
            UpdateValueLabels();

            UiKit.Label(Row("ControlsHeader", panel, inner, 28f, y), "조작법", 18, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 34f;
            for (int i = 0; i < Controls.GetLength(0); i++)
            {
                var row = Row("Control" + i, panel, inner, ControlRowHeight, y);
                var key = UiKit.Rect("Key", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(140f, ControlRowHeight));
                UiKit.Label(key, Controls[i, 0], 18, UiKit.Accent);
                var action = UiKit.Rect("Action", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(150f, 0f), new Vector2(inner - 150f, ControlRowHeight));
                UiKit.Label(action, Controls[i, 1], 18, UiKit.Text);
                y += ControlRowHeight;
            }
            y += 20f;

            y = MenuButton(panel, "뒤로", SettingsWidth, y, Back);
            panel.sizeDelta = new Vector2(SettingsWidth, y - ButtonGap + Padding);
        }

        // A centred column; its height is set once the content is laid out.
        static RectTransform Panel(string name, Transform parent, float width)
        {
            var panel = UiKit.Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 100f));
            UiKit.Box(panel, UiKit.Panel, true);
            return panel;
        }

        // A row hung from the top of the panel, `y` reference pixels down.
        static RectTransform Row(string name, Transform panel, float width, float height, float y) =>
            UiKit.Rect(name, panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -y), new Vector2(width, height));

        static float Title(Transform panel, string text, float panelWidth, float y)
        {
            UiKit.Label(Row("Title", panel, panelWidth - Padding * 2f, 44f, y), text, 34, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            return y + 44f + 24f;
        }

        static float MenuButton(Transform panel, string text, float panelWidth, float y, UnityEngine.Events.UnityAction onClick)
        {
            UiKit.Button(Row(text, panel, panelWidth - Padding * 2f, ButtonHeight, y), text, 24, onClick);
            return y + ButtonHeight + ButtonGap;
        }

        // Caption on the left, current value on the right, slider underneath.
        static Slider SliderRow(Transform panel, string caption, float width, ref float y, float min, float max, float value, out Text valueLabel)
        {
            var header = Row(caption, panel, width, 26f, y);
            UiKit.Label(UiKit.Fill("Caption", header), caption, 18, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            valueLabel = UiKit.Label(UiKit.Fill("Value", header), "", 18, UiKit.Text, TextAnchor.MiddleRight);
            y += 30f;
            var slider = BuildSlider(Row(caption + " Slider", panel, width, 22f, y), min, max, value);
            y += 22f + 24f;
            return slider;
        }

        // A plain uGUI slider from kit boxes: track, accent fill and a block handle.
        static Slider BuildSlider(RectTransform rect, float min, float max, float value)
        {
            const float handleWidth = 14f;
            var track = UiKit.Fill("Track", rect);
            track.offsetMin = new Vector2(0f, 6f);
            track.offsetMax = new Vector2(0f, -6f);
            UiKit.Box(track, UiKit.PanelLight, true);

            var fillArea = UiKit.Fill("Fill Area", rect);
            fillArea.offsetMin = new Vector2(0f, 6f);
            fillArea.offsetMax = new Vector2(0f, -6f);
            var fill = UiKit.Fill("Fill", fillArea);
            UiKit.Box(fill, UiKit.Accent);

            var handleArea = UiKit.Fill("Handle Area", rect);
            handleArea.offsetMin = new Vector2(handleWidth * 0.5f, 0f);
            handleArea.offsetMax = new Vector2(-handleWidth * 0.5f, 0f);
            var handle = UiKit.Fill("Handle", handleArea);
            handle.sizeDelta = new Vector2(handleWidth, 0f);
            var handleImage = UiKit.Box(handle, UiKit.Text, true);

            var slider = rect.gameObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.targetGraphic = handleImage;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.wholeNumbers = false;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(value);
            return slider;
        }
    }
}
