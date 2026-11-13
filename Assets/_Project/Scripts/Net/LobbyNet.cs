using System;
using System.Reflection;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    public struct LobbyHello : IBroadcast { public string Version; public string Name; }
    public struct LobbyReject : IBroadcast { public byte Reason; }
    public struct LobbyReady : IBroadcast { public bool Ready; }
    public struct LobbyState : IBroadcast
    {
        public int[] Ids;
        public string[] Names;
        public bool[] Ready;
        public int HostId;
        public int Bots;
        public int MaxPlayers;
    }
    public struct LobbyStart : IBroadcast { public ushort MatchPort; public int Players; public int Bots; }

    // The room before a match (#160, ADR-124): its own small NetworkManager (a lobby needs no spawned objects, only
    // broadcasts), separate from the match's. The host runs server + client, a guest only a client. Joining checks the
    // version and the room's size; the host's start tells everyone the port the match will listen on and how many
    // people and bots it has, and the lobby is then shut down (Shutdown) while the match scene loads.
    public sealed class LobbyNet : MonoBehaviour
    {
        public enum PhaseKind { Idle, Connecting, InLobby, Failed }

        public const ushort DefaultPort = 7770;
        public const int Protocol = 1;
        const float ConnectSeconds = 6f;
        // The host waits this long before leaving the lobby itself, so its start message reaches the guests first.
        const float HostLeaveDelay = 0.6f;

        // Both sides must agree on this to play together. Tests may change an instance's copy.
        public static string CurrentVersion => Application.version + "/" + Protocol;

        public string Version;
        public PhaseKind Phase { get; private set; }
        public string FailureText { get; private set; } = "";
        public bool IsHost { get; private set; }
        // This room's own client id (known only once the connection finished).
        public int SelfId => _nm != null && _nm.ClientManager.Connection != null ? _nm.ClientManager.Connection.ClientId : -1;
        public string Address { get; private set; } = "127.0.0.1";
        public ushort Port { get; private set; }
        public int Bots { get; private set; }
        public int MaxPlayers { get; private set; } = 16;
        // What this room shows: on a guest and on the host's own client it is rebuilt from the host's LobbyState messages.
        public readonly LobbyRoster Roster = new LobbyRoster();
        // Host only: the truth the messages are made from (kept apart so a late message never overwrites it).
        readonly LobbyRoster _truth = new LobbyRoster();
        int _truthBots;
        bool _botsChosen;
        public bool HasStarted { get; private set; }
        public LobbyStart StartInfo { get; private set; }

        // Anything shown in the room changed.
        public event Action Changed;
        // The host started the match: set NetSession (NetSession.Begin(this)), load the match scene and call Shutdown.
        public event Action MatchStarting;

        NetworkManager _nm;
        SinglePrefabObjects _prefabs;
        bool _matchStarted;
        float _connectDeadline;
        float _hostLeaveAt = -1f;
        bool _helloPending;

        public NetworkManager Manager => _nm;
        public bool IsReady => Roster.IsReady(SelfId);
        public bool CanStart => IsHost && Phase == PhaseKind.InLobby && !HasStarted && _truth.AllReady;

        // ---- Entry points ----

        public bool StartHost(ushort port = DefaultPort)
        {
            if (!Begin(true, "127.0.0.1", port)) return false;
            MaxPlayers = Mathf.Max(1, (MatchRules.Current ?? new MatchRules()).MaxPlayers);
            _truthBots = Bots = LobbyRules.DefaultBots(1, (MatchRules.Current ?? new MatchRules()).ClampedBotCount + 1);
            RegisterServer();
            if (!_nm.ServerManager.StartConnection(port)) return Fail("방을 열지 못했다. 포트 " + port + "을(를) 다른 프로그램이 쓰고 있을 수 있다.");
            return StartClientSide();
        }

        public bool Join(string address, ushort port)
        {
            if (!Begin(false, address, port)) return false;
            return StartClientSide();
        }

        bool Begin(bool host, string address, ushort port)
        {
            Shutdown();
            Create();
            IsHost = host;
            Address = address;
            Port = port;
            FailureText = "";
            HasStarted = false;
            _matchStarted = false;
            _hostLeaveAt = -1f;
            _helloPending = false;
            _botsChosen = false;
            Roster.Clear();
            Roster.HostId = -1;
            _truth.Clear();
            _truth.HostId = -1;
            Phase = PhaseKind.Connecting;
            _connectDeadline = Time.unscaledTime + ConnectSeconds;
            return true;
        }

        bool StartClientSide()
        {
            _nm.ClientManager.OnClientConnectionState += OnClientState;
            _nm.ClientManager.RegisterBroadcast<LobbyState>(OnState);
            _nm.ClientManager.RegisterBroadcast<LobbyReject>(OnReject);
            _nm.ClientManager.RegisterBroadcast<LobbyStart>(OnStart);
            if (!_nm.ClientManager.StartConnection(Address, Port)) return Fail(LobbyRules.Unreachable);
            return true;
        }

        void RegisterServer()
        {
            _nm.ServerManager.RegisterBroadcast<LobbyHello>(OnHello, false);
            _nm.ServerManager.RegisterBroadcast<LobbyReady>(OnReady);
            _nm.ServerManager.OnRemoteConnectionState += OnRemoteState;
        }

        // Leaves the room: stops both sides and removes the lobby's NetworkManager (it must be gone before the match's own is created).
        public void Shutdown()
        {
            if (_nm != null)
            {
                _nm.ClientManager.OnClientConnectionState -= OnClientState;
                _nm.ClientManager.StopConnection();
                _nm.ServerManager.StopConnection(true);
                Destroy(_nm.gameObject);
                _nm = null;
            }
            if (_prefabs != null) { Destroy(_prefabs); _prefabs = null; }
            Phase = PhaseKind.Idle;
            Roster.Clear();
            _truth.Clear();
            Changed?.Invoke();
        }

        void Awake() { if (string.IsNullOrEmpty(Version)) Version = CurrentVersion; }

        void OnDestroy() => Shutdown();

        void Create()
        {
            var go = new GameObject("LobbyNetworkManager");
            go.transform.SetParent(transform, false);
            go.SetActive(false); // NetworkManager.Awake must wait until it is configured
            // The editor logs a missing prefab list on AddComponent (Reset); it is assigned right below.
            bool logging = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            try { _nm = go.AddComponent<NetworkManager>(); }
            finally { Debug.unityLogger.logEnabled = logging; }
            // No public setters: this manager lives and dies with the lobby and may stand beside another (tests, a second room).
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(NetworkManager).GetField("_persistence", flags)?.SetValue(_nm, NetworkManager.PersistenceType.AllowMultiple);
            typeof(NetworkManager).GetField("_dontDestroyOnLoad", flags)?.SetValue(_nm, false);
            _prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            _nm.SpawnablePrefabs = _prefabs;
            go.AddComponent<Tugboat>();
            go.SetActive(true);
        }

        // ---- Guest and host client side ----

        void OnClientState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                _helloPending = true; // sent from Update once the client id is known
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped && !HasStarted)
            {
                if (Phase == PhaseKind.Connecting) Fail(LobbyRules.Unreachable);
                else if (Phase == PhaseKind.InLobby) Fail(LobbyRules.Dropped);
            }
        }

        void OnState(LobbyState state, Channel channel)
        {
            Roster.Clear();
            Roster.HostId = state.HostId;
            for (int i = 0; i < state.Ids.Length; i++) Roster.Add(state.Ids[i], state.Names[i], state.Ready[i]);
            Bots = state.Bots;
            MaxPlayers = state.MaxPlayers;
            if (Phase == PhaseKind.Connecting) Phase = PhaseKind.InLobby;
            Changed?.Invoke();
        }

        void OnReject(LobbyReject reject, Channel channel) => Fail(LobbyRules.Reason((JoinResult)reject.Reason));

        void OnStart(LobbyStart start, Channel channel)
        {
            StartInfo = start;
            HasStarted = true;
            Changed?.Invoke();
            if (IsHost) _hostLeaveAt = Time.unscaledTime + HostLeaveDelay;
            else MatchStarting?.Invoke();
        }

        bool Fail(string text)
        {
            FailureText = text;
            Phase = PhaseKind.Failed;
            if (_nm != null)
            {
                _nm.ClientManager.StopConnection();
                _nm.ServerManager.StopConnection(true);
            }
            Changed?.Invoke();
            return false;
        }

        void Update()
        {
            if (_helloPending && SelfId >= 0)
            {
                _helloPending = false;
                _nm.ClientManager.Broadcast(new LobbyHello { Version = Version, Name = "" });
            }
            if (Phase == PhaseKind.Connecting && Time.unscaledTime > _connectDeadline) Fail(LobbyRules.Unreachable);
            if (_hostLeaveAt >= 0f && Time.unscaledTime >= _hostLeaveAt)
            {
                _hostLeaveAt = -1f;
                MatchStarting?.Invoke();
            }
        }

        // ---- Requests from the screen ----

        public void SetReady(bool ready)
        {
            if (Phase != PhaseKind.InLobby || IsHost) return;
            _nm.ClientManager.Broadcast(new LobbyReady { Ready = ready });
        }

        // Host: bots that fill the match (0 .. what fits beside the people).
        public void SetBots(int bots)
        {
            if (!IsHost || Phase != PhaseKind.InLobby || HasStarted) return;
            _truthBots = LobbyRules.ClampBots(bots, _truth.Count);
            _botsChosen = true;
            BroadcastState();
        }

        // Host: tells everyone the match begins. False while someone is not ready.
        public bool StartMatch()
        {
            if (!CanStart) return false;
            _matchStarted = true;
            // The match listens on its own port so the lobby's sockets need not be closed first.
            ushort matchPort = MatchDirector.FreePort((ushort)(Port + 1));
            _nm.ServerManager.Broadcast(new LobbyStart { MatchPort = matchPort, Players = _truth.Count, Bots = LobbyRules.ClampBots(_truthBots, _truth.Count) });
            return true;
        }

        // ---- Host (server) side ----

        void OnHello(NetworkConnection conn, LobbyHello hello, Channel channel)
        {
            if (_truth.Contains(conn.ClientId)) return;
            var result = LobbyRules.CheckJoin(Version, hello.Version, _truth.Count, MaxPlayers, _matchStarted);
            if (result != JoinResult.Ok)
            {
                _nm.ServerManager.Broadcast(conn, new LobbyReject { Reason = (byte)result }, false);
                conn.Disconnect(false);
                return;
            }
            if (IsHost && conn.ClientId == SelfId) _truth.HostId = conn.ClientId; // the host's own client
            _truth.Add(conn.ClientId, hello.Name);
            RefreshBots();
            BroadcastState();
        }

        void OnReady(NetworkConnection conn, LobbyReady ready, Channel channel)
        {
            if (_matchStarted) return;
            _truth.SetReady(conn.ClientId, ready.Ready);
            BroadcastState();
        }

        void OnRemoteState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped || !_truth.Contains(args.ConnectionId)) return;
            _truth.Remove(args.ConnectionId);
            RefreshBots();
            BroadcastState();
        }

        // Until the host picks a number, the bots fill the match up to the single-player size as people come and go.
        void RefreshBots()
        {
            int target = (MatchRules.Current ?? new MatchRules()).ClampedBotCount + 1;
            _truthBots = _botsChosen ? LobbyRules.ClampBots(_truthBots, _truth.Count) : LobbyRules.DefaultBots(_truth.Count, target);
        }

        void BroadcastState()
        {
            int n = _truth.Count;
            var state = new LobbyState { Ids = new int[n], Names = new string[n], Ready = new bool[n], HostId = _truth.HostId, Bots = _truthBots, MaxPlayers = MaxPlayers };
            for (int i = 0; i < n; i++)
            {
                var m = _truth.Members[i];
                state.Ids[i] = m.Id;
                state.Names[i] = m.Name;
                state.Ready[i] = m.Ready;
            }
            _nm.ServerManager.Broadcast(state, false);
        }
    }
}
