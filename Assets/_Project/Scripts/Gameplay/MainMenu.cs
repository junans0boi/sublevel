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
        RaceLobbyScreen _eventScreen;
        GameObject _event;
        public RaceLobbyScreen EventEntry => _eventScreen;
        public bool EventEntryShown => _event != null && _event.activeSelf;
        public bool TrainingEntryShown => _training != null && _training.activeSelf;
        CustomScreen _customScreen;
        CharacterScreen _charScreen;
        GameObject _char;
        public CharacterScreen Character => _charScreen;
        public bool CharacterShown => _char != null && _char.activeSelf;
        public CustomScreen CustomPanel => _customScreen;
        SettingsScreen _settingsScreen;
        Text _loading;
        AsyncOperation _loadOp;
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
            // #491: the World's door plan and pollution grid (seconds of pure computation) build on worker threads while the menu is up.
            DoorSystem.PrepareLayout();
            PollutionWorld.Prepare();
            // The cursor may still be locked by the match we came from.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // The next match is solo again unless a lobby decides otherwise (#160).
            NetSession.Reset();
            GameSettings.Load();
            _stage = FindFirstObjectByType<LobbyStage>();
            Build();
            // Back from a race started in the race room (#511): the room again, everyone unready.
            if (RaceLobbyScreen.ReturnPending) { RaceLobbyScreen.ReturnPending = false; ShowEventEntry(); _eventScreen.BackToRoom(); }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Backend matchmaking and friends (#391/#392), only with a launcher login (LobbyNet.Backend).
        SocialClient _social;
        BackendMatchQueue _backendQueue;
        (TeamMode mode, int size)? _afterJoin;
        public SocialClient Social => _social;
        FriendsPanel _friends;

        void StartBackend()
        {
            if (LobbyNet.Backend == null || _social != null) return;
            _social = new SocialClient(LobbyNet.Backend);
            _backendQueue = new BackendMatchQueue(new MatchTicketClient(LobbyNet.Backend), _social, (host, port, size) =>
            {
                LobbyNet.QueuePartyId = _social.Party != null ? _social.Party.id : 0;
                _afterJoin = (size > 1 ? TeamMode.Squad : TeamMode.Solo, size);
                _quietJoin = true;
                _lobbyScreen.JoinQuietly(host, (ushort)port);
            });
        }

        string _partyShown;

        // The stage shows the logged-in player and the backend party (#389): the login's nickname instead of "플레이어", the party's members
        // with its leader, squad when more than one. The backend has no ready step, so members count as ready. Left alone while matching.
        void SyncBackendParty()
        {
            if (_lobbyPanel == null || LobbyNet.AccountId == 0 || _lobbyPanel.Party.Matching) return;
            var party = _social?.Party;
            string key = LobbyNet.AuthNickname + "|" + (party == null ? "" : party.leader_id + ":" + string.Join(",", System.Array.ConvertAll(party.members, m => m.account_id + m.nickname)));
            if (key == _partyShown) return;
            _partyShown = key;
            int self = (int)LobbyNet.AccountId;
            var old = _lobbyPanel.Party;
            PartyLobby next;
            if (party == null || party.members.Length == 0) next = new PartyLobby(self, LobbyNet.AuthNickname ?? "플레이어");
            else
            {
                var leader = System.Array.Find(party.members, m => m.account_id == party.leader_id) ?? party.members[0];
                next = new PartyLobby((int)leader.account_id, leader.nickname);
                foreach (var m in party.members) if (m != leader) { next.Join((int)m.account_id, m.nickname); next.SetReady((int)m.account_id, true); }
                if (party.members.Length > 1) next.SetMode(next.LeaderId, TeamMode.Squad);
            }
            next.SetView(next.LeaderId, old.View);
            _lobbyPanel.SetParty(next, self);
        }

        void Update()
        {
            // The read phase reports 0..0.9; the scene's activation (meshes, colliders) runs in one frame after it.
            if (_loadOp != null) _loading.text = _loadOp.progress < 0.9f ? $"불러오는 중... {Mathf.RoundToInt(_loadOp.progress / 0.9f * 100f)}%" : "맵 준비 중...";
            if (_social == null) StartBackend();
            if (_social != null && _friends == null && _root != null) _friends = FriendsPanel.Build(_root.transform, _social);
            if (_social != null) { _social.Tick(Time.unscaledTimeAsDouble); _backendQueue.Tick(Time.unscaledTimeAsDouble); }
            SyncBackendParty();
            SyncQueue();
            if (CharacterShown && !ModalShown) _charScreen.Tick();
            if (EventEntryShown && !IsLoading) _eventScreen.Tick();
            if (_toastScreen != null && Time.unscaledTime > _toastUntil) { Destroy(_toastScreen.Root.gameObject); _toastScreen = null; }
            if (_modal != null && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { CloseModal(); return; }
            if (_lobbyPanel != null && !IsLoading && _main != null && _main.activeSelf) _lobbyPanel.Tick(Time.unscaledDeltaTime);
            var keyboard = Keyboard.current;
            if (SettingsScreen.KeyCaptureActive) return; // Esc while choosing a key only cancels that
            // Esc in the settings = 뒤로 가기 (drops unapplied changes).
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && SettingsShown) _settingsScreen.Back();
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && CreditsShown) Back();
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && CustomShown) Back();
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && EventEntryShown) _eventScreen.Escape();
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && LobbyShown) _lobbyScreen.Leave();
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && _main != null && _main.activeSelf && !IsLoading) ShowQuit();
        }

        // 시작(솔로): loads the match scene in the background and shows a loading line.
        // Solo from the menu goes through the waiting island like a server match (ADR-561).
        public void StartGame() { WaitingRoom.PracticeNext = true; Load(MatchScene, Mode); }

        // 훈련장 (#153): the same hosted match without bots and lockdown, always solo.
        public void StartTraining() => Load(TrainingScene, TeamMode.Solo);

        // 이벤트 (ADR-512): a local race on the chosen course, always solo (the event's bots fill the grid).
        // The solo race (#508, ADR-562): the local choice as the race's start config. Defaults = today's race (course laps, bots to Racers,
        // a slot car).
        public void StartEvent(Game.Core.RaceEvent.CourseInfo course, int laps = 0, int bots = -1, string car = null, int paint = Game.Core.VehicleColors.None)
        {
            var start = new Game.Core.RaceStart { CourseId = course.Id, Laps = laps, Bots = bots };
            if (car != null || paint != Game.Core.VehicleColors.None) start.Racers.Add(new Game.Core.RaceStart.Entry { Car = car, Paint = paint });
            Game.Core.RaceEvent.Starting = start;
            Load(course.Scene, TeamMode.Solo);
        }

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
            if (_lobbyPanel != null)
            {
                // The lobby root already carries a CanvasGroup (fade/screen switch); AddComponent would return null and abort the load.
                if (!_lobbyPanel.Root.gameObject.TryGetComponent(out CanvasGroup group)) group = _lobbyPanel.Root.gameObject.AddComponent<CanvasGroup>(); // ?? would not catch the editor's fake-null from GetComponent
                group.interactable = false;
            }
            LoadTrace.Mark("load start " + scene);
            _loadOp = SceneManager.LoadSceneAsync(scene);
        }

        // 방 만들기 / 참가 (#160): the lobby panels; 시작 above stays the single-player path.
        public void OpenHost()
        {
            if (IsLoading) return;
            ShowOnly(null);
            _lobbyScreen.OpenHost();
        }

        public void OpenJoin()
        {
            if (IsLoading) return;
            ShowOnly(null);
            _lobbyScreen.OpenJoin();
        }

        // The host started the lobby's match: everyone loads the match scene now (NetSession tells it who it is).
        public void StartLobbyMatch(LobbyNet lobby)
        {
            if (IsLoading) return;
            NetSession.Begin(lobby);
            lobby.Shutdown();
            _lobbyScreen.gameObject.SetActive(false);
            // The host may have started the event race instead (ADR-512, #448): everyone goes to that course's scene.
            string race = Game.Core.RaceEvent.SceneForLobby(lobby.StartInfo.Event);
            if (race != null) { Game.Core.RaceEvent.Starting = new Game.Core.RaceStart { CourseId = Game.Core.RaceEvent.IdForLobby(lobby.StartInfo.Event) }; Load(race, TeamMode.Solo); return; }
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
            ShowOnly(_custom);
        }

        public void ShowSettings()
        {
            if (IsLoading) return;
            ShowOnly(_settings);
            _settingsScreen.Open();
        }

        public void Back() => ShowOnly(_main);

        // 크레딧 (#89): the assets that ask for credit.
        CreditsScreen _creditsScreen;
        public CreditsScreen CreditsScreen => _creditsScreen;
        public int CreditsRows => _creditsScreen != null ? _creditsScreen.RowCount : 0;
        public bool LobbyHomeShown => _main != null && _main.activeSelf;
        public bool CreditsShown => _credits != null && _credits.activeSelf;

        public void ShowCredits()
        {
            if (IsLoading) return;
            ShowOnly(_credits);
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
            ShowOnly(_char);
            _charScreen.Open();
        }

        // 훈련장 tab: the entry screen (사격장 / 봇 연습 매치).
        public void ShowTrainingEntry()
        {
            if (IsLoading) return;
            ShowOnly(_training);
        }

        // 이벤트 tab: the race room (#511, spec #505).
        public void ShowEventEntry()
        {
            if (IsLoading) return;
            ShowOnly(_event);
            _eventScreen.Refresh();
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

        // 파티 시작 uses the server's queue while this menu is connected to a server as a client (LobbyNet in a lobby, not hosting);
        // otherwise it is the local start (solo against bots).
        LobbyNet _queueLobby;
        bool _backendAttached;
        bool _quietJoin; // connected to the backend's server without the room panels: its failure is shown here
        void SyncQueue()
        {
            if (_lobbyPanel == null || _lobbyScreen == null) return;
            var lobby = _lobbyScreen.Lobby;
            if (_quietJoin && lobby != null && lobby.Phase == LobbyNet.PhaseKind.Failed)
            {
                _quietJoin = false;
                _afterJoin = null;
                _lobbyPanel.MatchingFailed(lobby.FailureText);
            }
            bool client = lobby != null && lobby.Phase == LobbyNet.PhaseKind.InLobby && !lobby.IsHost;
            if (client && _queueLobby != lobby)
            {
                _queueLobby = lobby;
                var server = new MatchQueueAdapter(new MatchmakingClient(lobby));
                _lobbyPanel.AttachQueue(server);
                // Assigned by the backend: ask the server's queue for a place at once (the party's size, as the backend counted it).
                if (_afterJoin.HasValue) { server.RequestStart(_afterJoin.Value.mode, _afterJoin.Value.size); _afterJoin = null; }
            }
            else if (!client && _queueLobby != null) { _queueLobby = null; _lobbyPanel.AttachQueue(_backendQueue); }
            else if (!client && _queueLobby == null && _backendQueue != null && !_backendAttached) { _backendAttached = true; _lobbyPanel.AttachQueue(_backendQueue); }
        }

        public void Quit()
        {
            if (IsLoading) return;
            Application.Quit();
        }

        // The one place screens change (every tab, 뒤로 가기, Esc): exactly one screen root is active afterwards (`target`, or none
        // for the room panels), the other roots are off, the modal is closed. The 3D lobby behind is shared and never switched.
        public int ActiveScreenRoots
        {
            get
            {
                int n = 0;
                foreach (var r in ScreenRoots()) if (r != null && r.activeSelf) n++;
                if (_lobbyScreen != null && _lobbyScreen.Shown) n++;
                return n;
            }
        }

        GameObject[] ScreenRoots() => new[] { _main, _settings, _credits, _custom, _char, _training, _event };

        void ShowOnly(GameObject target)
        {
            CloseModal();
            // Leaving the settings any way but 적용 drops what was only previewed; leaving 외형 returns the camera to the party.
            if (_settings != null && _settings.activeSelf && target != _settings) _settingsScreen.Cancel();
            if (_char != null && _char.activeSelf && target != _char) _charScreen.Close();
            if (_lobbyScreen != null && _lobbyScreen.Shown && target != null) _lobbyScreen.HidePanel();
            foreach (var r in ScreenRoots()) if (r != null && r != target && r.activeSelf) r.SetActive(false);
            if (_hint != null) _hint.SetActive(false);
            if (target != null && !target.activeSelf) UiFx.Show(target);
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
            _lobbyPanel = LobbyPanel.Build(this, parent, ShowTrainingEntry, ShowEventEntry, ShowCustom, ShowSettings, ShowCredits, ShowQuit, ShowCharacter, () => { if (_friends != null) _friends.Toggle(); else Toast("새 알림이 없습니다. (로그인하면 친구·파티를 쓸 수 있습니다.)"); });
            _main = _lobbyPanel.Root.gameObject;

            // 커스텀 모드 (adopted mockup): room rules, 방 만들기, 코드로 참가.
            _customScreen = new CustomScreen();
            _custom = _customScreen.Build(parent, this).gameObject;
            _custom.SetActive(false);
            _trainingScreen = new TrainingEntryScreen();
            _training = _trainingScreen.Build(parent, this).gameObject;
            _training.SetActive(false);
            _eventScreen = new RaceLobbyScreen();
            _event = _eventScreen.Build(parent, this).gameObject;
            _event.SetActive(false);
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
