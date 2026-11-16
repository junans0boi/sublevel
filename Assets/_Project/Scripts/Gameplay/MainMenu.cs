using FishNet;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The first screen of a normal launch (the MainMenu scene is first in the build): title, 모드(솔로/듀오/스쿼드, ADR-072),
    // 시작, 설정, 크레딧(#89), 종료.
    // 시작 loads the match scene, whose MatchDirector hosts a fresh match on a free port (ADR-044). The dev scene picker
    // is not here: `-scene <name>` on the command line is handled by StartScene.
    public class MainMenu : MonoBehaviour
    {
        public const string SceneName = "MainMenu";
        // The scene 시작 loads.
        public const string MatchScene = "World";
        public const string TrainingScene = TrainingRange.SceneName;

        const int SortOrder = 100;
        const float MainWidth = 460f;
        const string Title = "SUBLEVEL";
        const string Studio = "STEADY TO VIVID STUDIO";
        const string ControlsHint = "WASD 이동  ·  마우스 조준  ·  좌클릭 사격  ·  Tab 인벤토리  ·  Esc 메뉴";

        static readonly Color Background = new Color(0.035f, 0.04f, 0.05f, 1f);

        public static MainMenu Instance { get; private set; }

        public bool IsLoading { get; private set; }
        public bool SettingsShown => _settings != null && _settings.activeSelf;
        public SettingsScreen Settings => _settingsScreen;
        public string LoadingText => _loading != null ? _loading.text : "";
        // The team mode 시작 will host (ADR-072); the last choice of this session, solo at first.
        public TeamMode Mode { get; private set; } = TeamMatch.Mode;
        public LobbyScreen Lobby => _lobbyScreen;
        public bool LobbyShown => _lobbyScreen != null && _lobbyScreen.Shown;
        public string ModeText => _modeLabel != null ? _modeLabel.text : "";

        GameObject _root, _main, _settings, _hint, _credits;
        SettingsScreen _settingsScreen;
        Text _loading;
        Text _modeLabel, _startLabel;
        LobbyScreen _lobbyScreen;
        LobbyPanel _lobbyPanel;
        GameObject _custom;
        public LobbyPanel Panel => _lobbyPanel;
        public bool CustomShown => _custom != null && _custom.activeSelf;

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
            // The next match is solo again unless a lobby decides otherwise (#160).
            NetSession.Reset();
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
            if (SettingsScreen.KeyCaptureActive) return; // Esc while choosing a key only cancels that
            // Esc in the settings = 뒤로 가기 (drops unapplied changes).
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && SettingsShown) _settingsScreen.Back();
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && CreditsShown) Back();
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && CustomShown) Back();
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && LobbyShown) _lobbyScreen.Leave();
        }

        // 시작(솔로): loads the match scene in the background and shows a loading line.
        public void StartGame() => Load(MatchScene, Mode);

        // 훈련장 (#153): the same hosted match without bots and lockdown, always solo.
        public void StartTraining() => Load(TrainingScene, TeamMode.Solo);

        void Load(string scene, TeamMode mode)
        {
            if (IsLoading) return;
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogError("[MainMenu] scene is not in the build: " + scene);
                return;
            }
            IsLoading = true;
            TeamMatch.Mode = mode;
            _loading.text = "불러오는 중...";
            _loading.gameObject.SetActive(true);
            if (_lobbyPanel != null) _lobbyPanel.Root.gameObject.AddComponent<CanvasGroup>().interactable = false;
            SceneManager.LoadSceneAsync(scene);
        }

        // 방 만들기 / 참가 (#160): the lobby panels; 시작 above stays the single-player path.
        public void OpenHost()
        {
            if (IsLoading) return;
            _main.SetActive(false);
            _custom.SetActive(false);
            if (_hint != null) _hint.SetActive(false);
            _lobbyScreen.OpenHost();
        }

        public void OpenJoin()
        {
            if (IsLoading) return;
            _main.SetActive(false);
            _custom.SetActive(false);
            if (_hint != null) _hint.SetActive(false);
            _lobbyScreen.OpenJoin();
        }

        // The host started the lobby's match: everyone loads the match scene now (NetSession tells it who it is).
        public void StartLobbyMatch(LobbyNet lobby)
        {
            if (IsLoading) return;
            NetSession.Begin(lobby);
            lobby.Shutdown();
            _lobbyScreen.gameObject.SetActive(false);
            Load(MatchScene, TeamMode.Solo);
        }

        // 모드 button: 솔로 -> 듀오 -> 스쿼드 -> 솔로.
        public void CycleMode()
        {
            if (IsLoading) return;
            SetMode(Teams.Next(Mode));
        }

        public void SetMode(TeamMode mode)
        {
            Mode = mode;
            string name = Teams.ModeName(mode);
            if (_modeLabel != null) _modeLabel.text = "모드: " + name;
            if (_startLabel != null) _startLabel.text = "시작 (" + name + ")";
        }

        // 커스텀 모드 tab: 방 만들기 / 참가 (#160); normal play has no room list.
        public void ShowCustom()
        {
            if (IsLoading) return;
            _main.SetActive(false);
            if (_hint != null) _hint.SetActive(false);
            _custom.SetActive(true);
        }

        public void ShowSettings()
        {
            if (IsLoading) return;
            ShowPanels(true);
            _settingsScreen.Open();
        }

        public void Back() => ShowPanels(false);

        // 크레딧 (#89): the assets that ask for credit.
        public bool CreditsShown => _credits != null && _credits.activeSelf;

        public void ShowCredits()
        {
            if (IsLoading) return;
            _main.SetActive(false);
            if (_hint != null) _hint.SetActive(false);
            _credits.SetActive(true);
        }

        public void Quit()
        {
            if (IsLoading) return;
            Application.Quit();
        }

        void ShowPanels(bool settings)
        {
            // Leaving the settings any way but 적용 drops what was only previewed.
            if (!settings && _settings.activeSelf) _settingsScreen.Cancel();
            _main.SetActive(!settings);
            // The bottom hint line would sit on the settings footer.
            if (_hint != null) _hint.SetActive(!settings);
            _settings.SetActive(settings);
            if (_credits != null) _credits.SetActive(false);
            if (_custom != null) _custom.SetActive(false);
        }

        void Build()
        {
            var canvas = UiKit.Canvas("MainMenu", SortOrder, true);
            _root = canvas.gameObject;
            _root.transform.SetParent(transform, false);
            UiKit.Box(UiKit.Fill("Background", _root.transform), Background, true);

            BuildMain(_root.transform);
            _settingsScreen = new SettingsScreen();
            _settings = _settingsScreen.Build(_root.transform, "Settings", Back).gameObject;
            _settings.SetActive(false);
            _credits = new CreditsScreen().Build(_root.transform, Back).gameObject;
            _credits.SetActive(false);
            _lobbyScreen = LobbyScreen.Build(this, _root.transform);

            var hint = UiKit.Rect("ControlsHint", _root.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 352f), new Vector2(1400f, 30f));
            _hint = hint.gameObject;
            UiKit.Label(hint, ControlsHint, 20, UiKit.TextDim, TextAnchor.MiddleCenter);
            var version = UiKit.Rect("Version", _root.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 20f), new Vector2(400f, 26f));
            UiKit.Label(version, "v" + Application.version, 18, UiKit.TextDim, TextAnchor.MiddleRight);
        }

        void BuildMain(Transform parent)
        {
            // The lobby is the entry screen (ADR-245): tabs on top, party above, control panel below.
            _lobbyPanel = LobbyPanel.Build(this, parent, StartTraining, ShowCustom, ShowSettings, ShowCredits, Quit);
            _main = _lobbyPanel.Root.gameObject;
            var studio = UiKit.Rect("Studio", _lobbyPanel.Root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(1000f, 30f));
            UiKit.Label(studio, Studio, 20, UiKit.TextDim, TextAnchor.MiddleCenter);

            // 커스텀 모드: the host/join entries of the room lobby.
            var panel = UiKit.CenteredPanel("Custom", parent, MainWidth);
            _custom = panel.gameObject;
            float y = UiKit.MenuPadding;
            AddButton(panel, "방 만들기", ref y, OpenHost);
            AddButton(panel, "참가", ref y, OpenJoin);
            AddButton(panel, "뒤로 가기", ref y, Back);
            panel.sizeDelta = new Vector2(MainWidth, y - UiKit.MenuButtonGap + UiKit.MenuPadding);
            _custom.SetActive(false);

            var loading = UiKit.Rect("Loading", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(600f, 40f));
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
