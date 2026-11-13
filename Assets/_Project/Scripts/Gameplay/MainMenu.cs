using FishNet;
using Game.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The first screen of a normal launch (the MainMenu scene is first in the build): title, 시작(솔로), 설정, 종료.
    // 시작 loads the match scene, whose MatchDirector hosts a fresh match on a free port (ADR-044). The dev scene picker
    // is not here: `-scene <name>` on the command line is handled by StartScene.
    public class MainMenu : MonoBehaviour
    {
        public const string SceneName = "MainMenu";
        // The scene 시작 loads.
        public const string MatchScene = "World";

        const int SortOrder = 100;
        const float MainWidth = 460f;
        const string Title = "SUBLEVEL";
        const string Studio = "STEADY TO VIVID STUDIO";
        const string ControlsHint = "WASD 이동  ·  마우스 조준  ·  좌클릭 사격  ·  Tab 인벤토리  ·  Esc 메뉴";

        static readonly Color Background = new Color(0.035f, 0.04f, 0.05f, 1f);

        public static MainMenu Instance { get; private set; }

        public bool IsLoading { get; private set; }
        public bool SettingsShown => _settings != null && _settings.activeSelf;
        public string LoadingText => _loading != null ? _loading.text : "";

        GameObject _root, _main, _settings;
        SettingsPanel _settingsPanel;
        Text _loading;
        Button[] _buttons;

        // Leaves the running match (this process hosts it) and opens the main menu. Both network sides are stopped
        // first so the sockets are closed before the scene, and its NetworkManager, goes away; the next 시작 then
        // hosts its own match.
        public static void LeaveMatch()
        {
            UiFocus.Close();
            if (MatchDirector.Instance != null)
            {
                var network = InstanceFinder.NetworkManager;
                if (network != null)
                {
                    network.ClientManager.StopConnection();
                    network.ServerManager.StopConnection(true);
                }
            }
            SceneManager.LoadScene(SceneName);
        }

        void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
            // The cursor may still be locked by the match we came from.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            GameSettings.Load();
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && SettingsShown) Back();
        }

        // 시작(솔로): loads the match scene in the background and shows a loading line.
        public void StartGame()
        {
            if (IsLoading) return;
            if (!Application.CanStreamedLevelBeLoaded(MatchScene))
            {
                Debug.LogError("[MainMenu] match scene is not in the build: " + MatchScene);
                return;
            }
            IsLoading = true;
            _loading.text = "불러오는 중...";
            _loading.gameObject.SetActive(true);
            for (int i = 0; i < _buttons.Length; i++) _buttons[i].interactable = false;
            SceneManager.LoadSceneAsync(MatchScene);
        }

        public void ShowSettings()
        {
            if (IsLoading) return;
            _settingsPanel.Sync();
            ShowPanels(true);
        }

        public void Back() => ShowPanels(false);

        public void Quit()
        {
            if (IsLoading) return;
            Application.Quit();
        }

        void ShowPanels(bool settings)
        {
            _main.SetActive(!settings);
            _settings.SetActive(settings);
        }

        void Build()
        {
            var canvas = UiKit.Canvas("MainMenu", SortOrder, true);
            _root = canvas.gameObject;
            _root.transform.SetParent(transform, false);
            UiKit.Box(UiKit.Fill("Background", _root.transform), Background, true);

            BuildMain(_root.transform);
            _settingsPanel = new SettingsPanel();
            _settings = _settingsPanel.Build(_root.transform, "Settings", Back).gameObject;
            _settings.SetActive(false);

            var hint = UiKit.Rect("ControlsHint", _root.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(1400f, 30f));
            UiKit.Label(hint, ControlsHint, 20, UiKit.TextDim, TextAnchor.MiddleCenter);
            var version = UiKit.Rect("Version", _root.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 20f), new Vector2(400f, 26f));
            UiKit.Label(version, "v" + Application.version, 18, UiKit.TextDim, TextAnchor.MiddleRight);
        }

        void BuildMain(Transform parent)
        {
            var panel = UiKit.CenteredPanel("Main", parent, MainWidth);
            _main = panel.gameObject;
            _buttons = new Button[3];
            float y = UiKit.MenuPadding;
            _buttons[0] = AddButton(panel, "시작 (솔로)", ref y, StartGame);
            _buttons[1] = AddButton(panel, "설정", ref y, ShowSettings);
            _buttons[2] = AddButton(panel, "종료", ref y, Quit);
            panel.sizeDelta = new Vector2(MainWidth, y - UiKit.MenuButtonGap + UiKit.MenuPadding);
            panel.anchoredPosition = new Vector2(0f, -60f);

            // Title block above the button column (children of the column, so it goes away with it when settings show).
            var title = UiKit.Rect("Title", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 310f), new Vector2(1000f, 120f));
            UiKit.Label(title, Title, 96, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            var studio = UiKit.Rect("Studio", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 230f), new Vector2(1000f, 36f));
            UiKit.Label(studio, Studio, 24, UiKit.Accent, TextAnchor.MiddleCenter);

            var loading = UiKit.Rect("Loading", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -250f), new Vector2(600f, 40f));
            _loading = UiKit.Label(loading, "", 28, UiKit.Text, TextAnchor.MiddleCenter);
            loading.gameObject.SetActive(false);
        }

        static Button AddButton(RectTransform panel, string text, ref float y, UnityEngine.Events.UnityAction onClick)
        {
            var button = UiKit.Button(UiKit.HangRow(text, panel, MainWidth - UiKit.MenuPadding * 2f, UiKit.MenuButtonHeight, y), text, 24, onClick);
            y += UiKit.MenuButtonHeight + UiKit.MenuButtonGap;
            return button;
        }
    }
}
