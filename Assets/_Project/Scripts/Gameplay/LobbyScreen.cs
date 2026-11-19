using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The main menu's multiplayer panels (#160): 방 만들기 (host) and 참가 (join by IP:port), the reasons a join failed,
    // and the lobby with its people, ready states, bot count and the start button. The network side is LobbyNet.
    public class LobbyScreen : MonoBehaviour
    {
        const float Width = 820f;
        const float Inner = Width - UiKit.MenuPadding * 2f;
        const float RowHeight = 34f;
        const int MaxRows = 24;

        public enum ViewKind { Hidden, Form, Connecting, Failed, Room }

        MainMenu _menu;
        LobbyNet _lobby;
        RectTransform _panel;
        GameObject _form, _connecting, _failed, _room;
        InputField _address;
        Text _failedText, _roomTitle, _roomAddress, _listHeader, _botsLabel, _statusLine;
        Text[] _names, _states;
        Button _readyButton, _startButton, _botMinus, _botPlus;
        Text _readyLabel, _startLabel;
        bool _dirty;
        bool _hostMode;

        public ViewKind View { get; private set; } = ViewKind.Hidden;
        public bool Shown => View != ViewKind.Hidden;
        public LobbyNet Lobby => _lobby;
        public string AddressText => _address != null ? _address.text : "";
        public string FailureText => _failedText != null ? _failedText.text : "";
        public string TitleText => _roomTitle != null ? _roomTitle.text : "";

        public static LobbyScreen Build(MainMenu menu, Transform parent)
        {
            var go = new GameObject("LobbyScreen");
            go.transform.SetParent(menu.transform, false);
            var screen = go.AddComponent<LobbyScreen>();
            screen._menu = menu;
            screen._lobby = go.AddComponent<LobbyNet>();
            screen._lobby.Changed += () => screen._dirty = true;
            screen._lobby.MatchStarting += () => menu.StartLobbyMatch(screen._lobby);
            screen._panel = UiKit.CenteredPanel("Lobby", parent, Width);
            screen.BuildForm();
            screen.BuildConnecting();
            screen.BuildFailed();
            screen.BuildRoom();
            screen.Show(ViewKind.Hidden);
            return screen;
        }

        // ---- Entry points (main menu buttons) ----

        public void OpenHost()
        {
            _hostMode = true;
            View = ViewKind.Connecting;
            Show(ViewKind.Connecting);
            if (!_lobby.StartHost()) Show(ViewKind.Failed);
            else _dirty = true;
        }

        public void OpenJoin()
        {
            _hostMode = false;
            Show(ViewKind.Form);
        }

        public void SetAddress(string text) => _address.text = text;

        // 참가 button.
        public void Join()
        {
            if (!LobbyRules.TryParseAddress(_address.text, LobbyNet.DefaultPort, out var host, out var port))
            {
                _failedText.text = "주소 형식이 맞지 않다. 예: 192.168.0.5:7770";
                Show(ViewKind.Failed);
                return;
            }
            Show(ViewKind.Connecting);
            if (!_lobby.Join(host, port)) Show(ViewKind.Failed);
            else _dirty = true;
        }

        // The backend assigned this server (#392): join it as the 참가 button would.
        public void JoinServer(string host, ushort port)
        {
            _hostMode = false;
            Show(ViewKind.Connecting);
            if (!_lobby.Join(host, port)) Show(ViewKind.Failed);
            else _dirty = true;
        }

        // The backend assigned this server (#392): connect without the custom room's panels; the lobby panel keeps showing the matching.
        public void JoinQuietly(string host, ushort port)
        {
            _hostMode = false;
            Show(ViewKind.Hidden);
            _lobby.Join(host, port);
        }

        // 나가기 / 뒤로 가기: leaves the room and shows the main column again.
        public void Leave()
        {
            _lobby.Shutdown();
            Show(ViewKind.Hidden);
            _menu.Back();
        }

        // Hides the room panels without leaving the room (the menu switched to another screen).
        public void HidePanel() { _lobby.Shutdown(); Show(ViewKind.Hidden); }

        // 확인 on a failure: back to the address form (join) or the main column (host).
        void AcknowledgeFailure()
        {
            _lobby.Shutdown();
            if (_hostMode) { Show(ViewKind.Hidden); _menu.Back(); }
            else Show(ViewKind.Form);
        }

        // ---- Building ----

        RectTransform Group(string name)
        {
            var g = UiKit.Fill(name, _panel);
            return g;
        }

        static void Title(RectTransform group, string text, ref float y)
        {
            var row = UiKit.HangRow("Title", group, Inner, 48f, y);
            UiKit.Label(row, text, 34, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 60f;
        }

        void BuildForm()
        {
            var g = Group("Form");
            _form = g.gameObject;
            float y = UiKit.MenuPadding;
            Title(g, "참가", ref y);
            var caption = UiKit.HangRow("Caption", g, Inner, 28f, y);
            UiKit.Label(caption, "호스트 주소 (IP:포트)", 20, UiKit.TextDim);
            y += 34f;
            var box = UiKit.HangRow("Address", g, Inner, 56f, y);
            var image = UiKit.Box(box, UiKit.PanelDeep, true);
            var text = UiKit.Label(UiKit.Fill("Text", box, 14f), "", 26, UiKit.Text, TextAnchor.MiddleLeft);
            _address = box.gameObject.AddComponent<InputField>();
            _address.targetGraphic = image;
            _address.textComponent = text;
            _address.characterLimit = 64;
            _address.text = "127.0.0.1:" + LobbyNet.DefaultPort;
            y += 76f;
            var hint = UiKit.HangRow("Hint", g, Inner, 28f, y);
            UiKit.Label(hint, "포트를 생략하면 " + LobbyNet.DefaultPort + "번으로 접속한다.", 18, UiKit.TextDim);
            y += 52f;
            UiKit.Button(UiKit.HangRow("Join", g, 300f, UiKit.MenuButtonHeight, y, -160f), "참가", 24, Join);
            UiKit.Button(UiKit.HangRow("Back", g, 300f, UiKit.MenuButtonHeight, y, 160f), "뒤로 가기", 24, Leave);
        }

        void BuildConnecting()
        {
            var g = Group("Connecting");
            _connecting = g.gameObject;
            float y = UiKit.MenuPadding;
            Title(g, "접속 중...", ref y);
            var line = UiKit.HangRow("Line", g, Inner, 32f, y);
            UiKit.Label(line, "호스트에 연결하고 있다.", 22, UiKit.TextDim);
            y += 60f;
            UiKit.Button(UiKit.HangRow("Cancel", g, 300f, UiKit.MenuButtonHeight, y), "취소", 24, Leave);
        }

        void BuildFailed()
        {
            var g = Group("Failed");
            _failed = g.gameObject;
            float y = UiKit.MenuPadding;
            Title(g, "접속하지 못했다", ref y);
            var line = UiKit.HangRow("Reason", g, Inner, 64f, y);
            _failedText = UiKit.Label(line, "", 24, UiKit.Danger);
            _failedText.horizontalOverflow = HorizontalWrapMode.Wrap;
            y += 90f;
            UiKit.Button(UiKit.HangRow("Ok", g, 300f, UiKit.MenuButtonHeight, y), "확인", 24, AcknowledgeFailure);
        }

        void BuildRoom()
        {
            var g = Group("Room");
            _room = g.gameObject;
            float y = UiKit.MenuPadding;
            var title = UiKit.HangRow("Title", g, Inner, 48f, y);
            _roomTitle = UiKit.Label(title, "", 34, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 54f;
            var address = UiKit.HangRow("Address", g, Inner, 28f, y);
            _roomAddress = UiKit.Label(address, "", 18, UiKit.TextDim);
            y += 40f;
            var header = UiKit.HangRow("Header", g, Inner, 30f, y);
            _listHeader = UiKit.Label(header, "", 20, UiKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 36f;
            _names = new Text[MaxRows];
            _states = new Text[MaxRows];
            for (int i = 0; i < MaxRows; i++)
            {
                var row = UiKit.HangRow("Row" + i, g, Inner, RowHeight - 2f, y);
                UiKit.Box(row, i % 2 == 0 ? UiKit.RowBand : Color.clear);
                _names[i] = UiKit.Label(UiKit.Fill("Name", row, 10f), "", 22, UiKit.Text, TextAnchor.MiddleLeft);
                _states[i] = UiKit.Label(UiKit.Fill("State", row, 10f), "", 22, UiKit.TextDim, TextAnchor.MiddleRight);
                row.gameObject.SetActive(false);
                _rowHolder[i] = row.gameObject;
                y += RowHeight;
            }
            _rowsEnd = y;
            // Placed in Layout() once the visible row count is known.
            _botsRow = UiKit.HangRow("Bots", g, Inner, 40f, 0f);
            _botsLabel = UiKit.Label(UiKit.Fill("Label", _botsRow, 0f), "", 22, UiKit.Text, TextAnchor.MiddleLeft);
            _botMinus = UiKit.Button(UiKit.Rect("Minus", _botsRow, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-70f, 0f), new Vector2(56f, 40f)), "-", 26, () => _lobby.SetBots(_lobby.Bots - 1));
            _botPlus = UiKit.Button(UiKit.Rect("Plus", _botsRow, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(56f, 40f)), "+", 26, () => _lobby.SetBots(_lobby.Bots + 1));
            _statusRow = UiKit.HangRow("Status", g, Inner, 30f, 0f);
            _statusLine = UiKit.Label(_statusRow, "", 19, UiKit.TextDim);
            _readyRow = UiKit.HangRow("Ready", g, 300f, UiKit.MenuButtonHeight, 0f, -160f);
            _readyButton = UiKit.Button(_readyRow, "준비", 24, () => _lobby.SetReady(!_lobby.IsReady));
            _readyLabel = _readyButton.GetComponentInChildren<Text>();
            _startRow = UiKit.HangRow("Start", g, 300f, UiKit.MenuButtonHeight, 0f, -160f);
            _startButton = UiKit.Button(_startRow, "시작", 24, () => _lobby.StartMatch());
            _startLabel = _startButton.GetComponentInChildren<Text>();
            _leaveRow = UiKit.HangRow("Leave", g, 300f, UiKit.MenuButtonHeight, 0f, 160f);
            UiKit.Button(_leaveRow, "나가기", 24, Leave);
        }

        RectTransform _botsRow, _statusRow, _readyRow, _startRow, _leaveRow;
        readonly GameObject[] _rowHolder = new GameObject[MaxRows];
        float _rowsEnd;

        // ---- Showing ----

        void Show(ViewKind view)
        {
            View = view;
            _panel.gameObject.SetActive(view != ViewKind.Hidden);
            _form.SetActive(view == ViewKind.Form);
            _connecting.SetActive(view == ViewKind.Connecting);
            _failed.SetActive(view == ViewKind.Failed);
            _room.SetActive(view == ViewKind.Room);
            float height = view == ViewKind.Form ? 370f : view == ViewKind.Connecting ? 230f : view == ViewKind.Failed ? 300f : _panel.sizeDelta.y;
            if (view != ViewKind.Room) _panel.sizeDelta = new Vector2(Width, height);
            if (view == ViewKind.Room) Refresh();
        }

        void Update()
        {
            if (!_dirty || View == ViewKind.Hidden) return;
            _dirty = false;
            Refresh();
        }

        // Follows the lobby's state: the right view, and in the room the people and buttons.
        void Refresh()
        {
            switch (_lobby.Phase)
            {
                case LobbyNet.PhaseKind.Failed:
                    _failedText.text = _lobby.FailureText;
                    if (View != ViewKind.Failed) Show(ViewKind.Failed);
                    return;
                case LobbyNet.PhaseKind.InLobby:
                    if (View != ViewKind.Room) { View = ViewKind.Room; _form.SetActive(false); _connecting.SetActive(false); _failed.SetActive(false); _room.SetActive(true); _panel.gameObject.SetActive(true); }
                    break;
                default:
                    return;
            }
            var roster = _lobby.Roster;
            bool host = _lobby.IsHost;
            _roomTitle.text = host ? "방 (호스트)" : "방";
            _roomAddress.text = host
                ? "내 주소: " + LocalAddresses(_lobby.Port) + "   ·   버전 " + _lobby.Version
                : "호스트: " + _lobby.Address + ":" + _lobby.Port + "   ·   버전 " + _lobby.Version;
            _listHeader.text = "플레이어 " + roster.Count + " / " + _lobby.MaxPlayers;
            for (int i = 0; i < MaxRows; i++)
            {
                bool used = i < roster.Count;
                _rowHolder[i].SetActive(used);
                if (!used) continue;
                var m = roster.Members[i];
                bool self = m.Id == _lobby.SelfId;
                _names[i].text = m.Name + (self ? " (나)" : "") + (m.Id == roster.HostId ? "  [호스트]" : "");
                bool ready = roster.IsReady(m.Id);
                _states[i].text = m.Id == roster.HostId ? "방장" : ready ? "준비 완료" : "대기 중";
                _states[i].color = ready || m.Id == roster.HostId ? UiKit.Accent : UiKit.TextDim;
            }
            float y = _rowsEnd - (MaxRows - roster.Count) * RowHeight + 10f;
            Place(_botsRow, y); y += 52f;
            _botsLabel.text = "봇 " + _lobby.Bots + "명   (전체 " + (roster.Count + _lobby.Bots) + "명)";
            _botMinus.gameObject.SetActive(host);
            _botPlus.gameObject.SetActive(host);
            Place(_statusRow, y); y += 40f;
            _statusLine.text = _lobby.HasStarted ? "곧 시작한다..." : host ? (roster.AllReady ? "모두 준비되었다. 시작할 수 있다." : "모두 준비하면 시작할 수 있다.") : (_lobby.IsReady ? "호스트가 시작하길 기다리는 중." : "준비를 누르자.");
            Place(_readyRow, y); Place(_startRow, y); Place(_leaveRow, y);
            _readyRow.gameObject.SetActive(!host);
            _startRow.gameObject.SetActive(host);
            _readyLabel.text = _lobby.IsReady ? "준비 취소" : "준비";
            _startButton.interactable = _lobby.CanStart;
            _startLabel.color = _lobby.CanStart ? UiKit.Text : UiKit.TextDim;
            y += UiKit.MenuButtonHeight;
            _panel.sizeDelta = new Vector2(Width, y + UiKit.MenuPadding);
        }

        static void Place(RectTransform row, float y) => row.anchoredPosition = new Vector2(row.anchoredPosition.x, -y);

        // The addresses friends can reach this machine on: its IPv4 addresses (read from the system, nothing is sent).
        static string LocalAddresses(ushort port)
        {
            var sb = new StringBuilder();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(ua.Address).Append(':').Append(port);
                    }
                }
            }
            catch (System.Exception) { }
            return sb.Length > 0 ? sb.ToString() : "127.0.0.1:" + port;
        }
    }
}
