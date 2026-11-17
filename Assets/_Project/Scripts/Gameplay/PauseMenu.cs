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
                const float Padding = 28f;
        const float ButtonHeight = 56f;
        const float ButtonGap = 12f;

        static readonly Color Dim = new Color(UiPaint.Bg.r, UiPaint.Bg.g, UiPaint.Bg.b, 0.84f); // the lobby's scrim

        public static PauseMenu Instance { get; private set; }

        public bool IsOpen => _root != null && _root.activeSelf;
        public bool SettingsShown => _settings != null && _settings.activeSelf;
        public SettingsScreen Settings => _settingsScreen;

        GameObject _root;
        GameObject _main;
        GameObject _settings;
        SettingsScreen _settingsScreen;

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

        // MAIN MENU: leaves the match without asking and loads the main menu.
        public void ReturnToMainMenu() => MainMenu.LeaveMatch();

        // SETTINGS
        public void ShowSettings()
        {
            ShowPanels(true);
            _settingsScreen.Open();
        }

        // BACK (unapplied settings are dropped)
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
            // Leaving the settings any way but 적용 drops what was only previewed.
            if (!settings && _settings.activeSelf) _settingsScreen.Cancel();
            _main.SetActive(!settings);
            if (settings) UiFx.Show(_settings); else _settings.SetActive(false);
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
            y = MenuButton(panel, "메인 메뉴", MainWidth, y, ReturnToMainMenu);
            y = MenuButton(panel, "게임 종료", MainWidth, y, Quit);
            panel.sizeDelta = new Vector2(MainWidth, y - ButtonGap + Padding);
        }

        void BuildSettings(Transform parent)
        {
            _settingsScreen = new SettingsScreen();
            _settings = _settingsScreen.Build(parent, "Settings", Back).gameObject;
            _settings.SetActive(false);
        }

        // A centred column; its height is set once the content is laid out.
        static RectTransform Panel(string name, Transform parent, float width)
        {
            var panel = UiKit.Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 100f));
            UiPaint.Box(panel, UiPaint.PnSolid, 16, UiPaint.Ln).raycastTarget = true;
            return panel;
        }

        // A row hung from the top of the panel, `y` reference pixels down.
        static RectTransform Row(string name, Transform panel, float width, float height, float y) =>
            UiKit.Rect(name, panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -y), new Vector2(width, height));

        static float Title(Transform panel, string text, float panelWidth, float y)
        {
            UiKit.Label(Row("Title", panel, panelWidth - Padding * 2f, 44f, y), text, 32, UiPaint.Tx, TextAnchor.MiddleCenter, FontStyle.Bold);
            return y + 44f + 24f;
        }

        static float MenuButton(Transform panel, string text, float panelWidth, float y, UnityEngine.Events.UnityAction onClick)
        {
            // The first button of the column (계속하기) is the one accent action, the rest are outlined like the lobby's.
            bool primary = y < 100f;
            UiPaint.Btn(panel, text, Padding, y, panelWidth - Padding * 2f, ButtonHeight, text, 24, 14,
                primary ? UiPaint.Ac : UiPaint.Sf, primary ? UiPaint.Ac : UiPaint.Ln, primary ? UiPaint.Bg : UiPaint.Tx, onClick);
            return y + ButtonHeight + ButtonGap;
        }
    }
}
