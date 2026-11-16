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
        LobbyStage _stage;
        public LobbyStage Stage => _stage;
        TrainingEntryScreen _trainingScreen;
        GameObject _training;
        public TrainingEntryScreen TrainingEntry => _trainingScreen;
        public bool TrainingEntryShown => _training != null && _training.activeSelf;
        CustomScreen _customScreen;
        CharacterScreen _charScreen;
        GameObject _char;
        public CharacterScreen Character => _charScreen;
        public bool CharacterShown => _char != null && _char.activeSelf;
        public CustomScreen CustomPanel => _customScreen;
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
            _stage = FindFirstObjectByType<LobbyStage>();
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (CharacterShown && !ModalShown) _charScreen.Tick();
            if (_toastScreen != null && Time.unscaledTime > _toastUntil) { Destroy(_toastScreen.Root.gameObject); _toastScreen = null; }
            if (_modal != null && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { CloseModal(); return; }
            if (_lobbyPanel != null && !IsLoading && _main != null && _main.activeSelf) _lobbyPanel.Tick(Time.unscaledDeltaTime);
            var keyboard = Keyboard.current;
            if (SettingsScreen.KeyCaptureActive) return; // Esc while choosing a key only cancels that
            // Esc in the settings = 뒤로 가기 (drops unapplied changes).
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && SettingsShown) _settingsScreen.Back();
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && CreditsShown) Back();
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && CustomShown) Back();
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && LobbyShown) _lobbyScreen.Leave();
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && _main != null && _main.activeSelf && !IsLoading) ShowQuit();
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
            Load(MatchScene, (TeamMode)Mathf.Clamp(lobby.StartInfo.Mode, 0, 2)); // a queue match carries its mode
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
            UiFx.Show(_custom);
        }

        public void ShowSettings()
        {
            if (IsLoading) return;
            ShowPanels(true);
            _settingsScreen.Open();
        }

        public void Back() => ShowPanels(false);

        // 크레딧 (#89): the assets that ask for credit.
        CreditsScreen _creditsScreen;
        public CreditsScreen CreditsScreen => _creditsScreen;
        public int CreditsRows => _creditsScreen != null ? _creditsScreen.RowCount : 0;
        public bool CreditsShown => _credits != null && _credits.activeSelf;

        public void ShowCredits()
        {
            if (IsLoading) return;
            _main.SetActive(false);
            if (_hint != null) _hint.SetActive(false);
            UiFx.Show(_credits);
        }

        // ---- Mockup-driven screens: shared router, toast and modal ----
        MockScreen _modal;
        MockScreen _toastScreen;
        float _toastUntil;
        public bool ModalShown => _modal != null;
        public MockScreen ModalScreen => _modal;
        public string ToastText { get; private set; } = "";

        // Actions of the mockup's clickable items (data-go / data-act): screen changes, toasts, modals.
        public void OnMock(MockScreen.Item it)
        {
            string a = !string.IsNullOrEmpty(it.go) ? it.go : it.act;
            if (it.id == "back" || a == "lobby") { Back(); return; }
            if (string.IsNullOrEmpty(a)) return;
            if (a.StartsWith("toast:")) { Toast(a.Substring(6)); return; }
            if (a.StartsWith("mode:") || a.StartsWith("pv:"))
            {
                var party = _lobbyPanel.Party;
                if (a == "mode:solo") party.SetMode(_lobbyPanel.Self, TeamMode.Solo);
                else if (a == "mode:squad") party.SetMode(_lobbyPanel.Self, TeamMode.Squad);
                else party.SetView(_lobbyPanel.Self, a == "pv:1인칭" ? Perspective.First : Perspective.Third);
                _lobbyPanel.Refresh();
                ShowModeModal(); // redraw with the new selection
                return;
            }
            switch (a)
            {
                case "settings": ShowSettings(); break;
                case "tutorial": ShowTrainingEntry(); break;
                case "char": ShowCharacter(); break;
                case "custom": ShowCustom(); break;
                case "credits": ShowCredits(); break;
                case "!quit": ShowQuit(); break;
                case "quit": Quit(); break;
                case "modal:": CloseModal(); break;
            }
        }

        public void Toast(string text)
        {
            ToastText = text;
            if (_toastScreen != null) Destroy(_toastScreen.Root.gameObject);
            var s = MockScreen.Build(_root.transform, "toast_blank", null, null, true);
            _toastScreen = s;
            _toastUntil = Time.unscaledTime + 2.6f;
            var r = UiPaint.At(s.Stage, "Toast", 700, 940, 520, 64);
            UiPaint.Box(r, UiPaint.Pn, 16, UiPaint.Ln);
            UiPaint.Box(UiPaint.At(r, "Edge", 0, 0, 8, 64), UiPaint.Ac, 4);
            UiPaint.Label(r, "Text", 28, 0, 480, 64, text, 20, UiPaint.Tx, TextAnchor.MiddleLeft);
            UiFx.Show(s.Root.gameObject, UiFx.Normal, r, 12f);
        }

        public void Invite()
        {
            GUIUtility.systemCopyBuffer = "CH-7K2Q";
            Toast("초대 코드 CH-7K2Q를 복사했습니다.");
        }

        // 외형: the character screen.
        public void ShowCharacter()
        {
            if (IsLoading) return;
            _main.SetActive(false);
            if (_hint != null) _hint.SetActive(false);
            if (_credits != null) _credits.SetActive(false);
            if (_custom != null) _custom.SetActive(false);
            UiFx.Show(_char);
            _charScreen.Open();
        }

        // 훈련장 tab: the entry screen (사격장 / 봇 연습 매치).
        public void ShowTrainingEntry()
        {
            if (IsLoading) return;
            _main.SetActive(false);
            if (_hint != null) _hint.SetActive(false);
            if (_credits != null) _credits.SetActive(false);
            if (_custom != null) _custom.SetActive(false);
            if (_char != null && _char.activeSelf) { _charScreen.Close(); _char.SetActive(false); }
            UiFx.Show(_training);
        }

        public void ShowQuit() => ShowModal("pop_quit");

        // The map window: 게임 모드 선택 창 (mode, view and the map card), the variant for the current choice.
        public void ShowModeModal()
        {
            var p = _lobbyPanel.Party;
            ShowModal("lobby_mode_" + (p.Mode == TeamMode.Solo ? "solo" : "squad") + "_" + (p.View == Perspective.Third ? "tpp" : "fpp"));
        }

        public void ShowModal(string name)
        {
            CloseModal();
            _modal = MockScreen.Build(_root.transform, name, OnMock, null, true);
            UiFx.Show(_modal.Root.gameObject, UiFx.Normal);
            // The scrim and card are one measured set; Esc closes, as the mockup's 취소 shows.
        }

        public void CloseModal()
        {
            if (_modal != null) Destroy(_modal.Root.gameObject);
            _modal = null;
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
            if (settings) _main.SetActive(false); else if (!_main.activeSelf) UiFx.Show(_main);
            // The bottom hint line would sit on the settings footer.
            if (_hint != null) _hint.SetActive(!settings);
            if (settings && !_settings.activeSelf) UiFx.Show(_settings); else if (!settings) _settings.SetActive(false);
            if (_credits != null) _credits.SetActive(false);
            if (_custom != null) _custom.SetActive(false);
            if (_char != null && _char.activeSelf) { _charScreen.Close(); _char.SetActive(false); }
            if (_training != null) _training.SetActive(false);
        }

        void Build()
        {
            var canvas = UiKit.Canvas("MainMenu", SortOrder, true);
            _root = canvas.gameObject;
            _root.transform.SetParent(transform, false);

            BuildMain(_root.transform);
            _settingsScreen = new SettingsScreen();
            _settings = _settingsScreen.Build(_root.transform, "Settings", Back, OnMock).gameObject;
            _settings.SetActive(false);
            _creditsScreen = new CreditsScreen();
            _credits = _creditsScreen.Build(_root.transform, OnMock).gameObject;
            _credits.SetActive(false);
            _lobbyScreen = LobbyScreen.Build(this, _root.transform);

        }

        void BuildMain(Transform parent)
        {
            // The lobby is the entry screen (ADR-245): tabs on top, party above, control panel below.
            _lobbyPanel = LobbyPanel.Build(this, parent, ShowTrainingEntry, ShowCustom, ShowSettings, ShowCredits, ShowQuit, ShowCharacter, () => Toast("새 알림이 없습니다."));
            _main = _lobbyPanel.Root.gameObject;

            // 커스텀 모드 (adopted mockup): room rules, 방 만들기, 코드로 참가.
            _customScreen = new CustomScreen();
            _custom = _customScreen.Build(parent, this).gameObject;
            _custom.SetActive(false);
            _trainingScreen = new TrainingEntryScreen();
            _training = _trainingScreen.Build(parent, this).gameObject;
            _training.SetActive(false);
            _charScreen = new CharacterScreen();
            _char = _charScreen.Build(parent, this).gameObject;
            _char.SetActive(false);

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
