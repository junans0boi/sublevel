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
    public struct LobbyHello : IBroadcast { public string Version; public string Name; public string Token; public string SessionToken; }
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
        public MatchQueueState Queue; // matchmaking (ADR-249): who waits, how long is left
    }
    // A client asks for a place in the matchmaking queue (ADR-258): the mode and how many people its party has (1..team size).
    public struct LobbyQueueRequest : IBroadcast { public byte Mode; public byte PartySize; public long PartyId; }
    public struct LobbyQueueCancel : IBroadcast { }
    public struct LobbyStart : IBroadcast
    {
        public ushort MatchPort;
        public int Players;
        public int Bots;
        public byte Mode;
        public int[] PartySizes;

        public static LobbyStart FromPlan(ushort matchPort, TeamMode mode, MatchPlan plan)
        {
            var partySizes = new System.Collections.Generic.List<int>();
            foreach (var team in plan.Teams) if (!team.IsBot) partySizes.Add(team.Size);
            return new LobbyStart
            {
                MatchPort = matchPort,
                Players = plan.Humans,
                Bots = plan.Bots,
                Mode = (byte)mode,
                PartySizes = partySizes.ToArray()
            };
        }
    }

    // The room before a match (#160, ADR-124): its own small NetworkManager (a lobby needs no spawned objects, only
    // broadcasts), separate from the match's. The host runs server + client, a guest only a client. Joining checks the
    // version and the room's size; the host's start tells everyone the port, roster totals, and queued party sizes,
    // then the lobby shuts down while the match scene loads.
    public sealed class LobbyNet : MonoBehaviour
    {
        public enum PhaseKind { Idle, Connecting, InLobby, Failed }

        public const ushort DefaultPort = 7770;
        public const int Protocol = 2; // LobbyStart now carries queue party sizes.
        const float ConnectSeconds = 6f;
        // The host waits this long before leaving the lobby itself, so its start message reaches the guests first.
        const float HostLeaveDelay = 0.6f;

        // Both sides must agree on this to play together. Tests may change an instance's copy.
        public static string CurrentVersion => Application.version + "/" + Protocol;

        public string Version;

        // Login (#389). Client: the launcher's game token (SUBLEVEL_GAME_TOKEN) and the nickname shown for it, null without a login (dev run).
        // Server: the secret that checks tokens offline (SUBLEVEL_TOKEN_SECRET); without one nobody is asked to log in.
        public static string AuthToken, AuthNickname, ServerTokenSecret;
        // Client: the launcher's scoped API token ("sg_...", 12h) for the backend; it is only ever sent as a Bearer header, never stored or logged. When set, each join first trades it for a fresh 5-minute signed token (AuthToken).
        public static string ApiToken;
        public static long AccountId; // from /api/me, 0 until known
        public static IBackend Backend;
        // Client: the backend party this player queues with (0 = alone); the server turns one party's requests into one ticket.
        public static long QueuePartyId;
        // Server: set when the backend decides who plays here (BackendMatchPoller); null = anyone who logged in may join.
        public static System.Func<long, bool> AssignedCheck;
        // Server: the backend account behind each player's session token (NetSession.Token), which also names the player in the match (people without a login are absent).
        public static readonly System.Collections.Generic.Dictionary<string, long> AccountsBySession = new System.Collections.Generic.Dictionary<string, long>();
        public PhaseKind Phase { get; private set; }
        public string FailureText { get; private set; } = "";
        public bool IsHost { get; private set; }
        // Server only, without a local client of its own (#168).
        public bool Dedicated { get; private set; }
        // People in the room as the server counts them (a dedicated server has no roster of its own).
        public int PlayerCount => IsHost ? _truth.Count : Roster.Count;
        // This room's own client id (known only once the connection finished).
        public int SelfId => _nm != null && _nm.ClientManager.Connection != null ? _nm.ClientManager.Connection.ClientId : -1;
        public string Address { get; private set; } = "127.0.0.1";
        public ushort Port { get; private set; }
        public int Bots { get; private set; }
        public int MaxPlayers { get; private set; } = 16;
        // The queue as the server last told this client (ADR-249); on the server it is what ServerSetQueue stored.
        public MatchQueueState Queue { get; private set; }
        public void ServerSetQueue(MatchQueueState state) { Queue = state; }
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
            _truthBots = Bots = LobbyRules.DefaultBots(1, (MatchRules.Current ?? new MatchRules()).TargetParticipants);
            RegisterServer();
            if (!_nm.ServerManager.StartConnection(port)) return Fail("방을 열지 못했다. 포트 " + port + "을(를) 다른 프로그램이 쓰고 있을 수 있다.");
            return StartClientSide();
        }

        // A dedicated server's room (#168, ADR-126): the server side only, no local person. Nobody is the host, so
        // everyone who joins has to be ready (or the server's start timer runs out).
        public bool StartDedicated(ushort port)
        {
            if (!Begin(true, "127.0.0.1", port)) return false;
            Dedicated = true;
            MaxPlayers = Mathf.Max(1, (MatchRules.Current ?? new MatchRules()).MaxPlayers);
            _truthBots = Bots = LobbyRules.DefaultBots(1, (MatchRules.Current ?? new MatchRules()).TargetParticipants);
            RegisterServer();
            if (!_nm.ServerManager.StartConnection(port)) return Fail("포트 " + port + "을(를) 열지 못했다. 다른 프로그램이 쓰고 있을 수 있다.");
            Phase = PhaseKind.InLobby;
            Changed?.Invoke();
            return true;
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
            Dedicated = false;
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
            PingSimulation.Attach(_nm);
            if (!_nm.ClientManager.StartConnection(Address, Port)) return Fail(LobbyRules.Unreachable);
            return true;
        }

        void RegisterServer()
        {
            _nm.ServerManager.RegisterBroadcast<LobbyQueueRequest>(OnQueueRequest);
            _nm.ServerManager.RegisterBroadcast<LobbyQueueCancel>(OnQueueCancel);
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
            Queue = state.Queue;
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

        volatile bool _tokenAsked, _tokenAnswered;
        volatile string _freshToken;

        void Update()
        {
            if (_helloPending && SelfId >= 0 && !Dedicated && !string.IsNullOrEmpty(ApiToken) && Backend != null && !_tokenAnswered)
            {
                if (!_tokenAsked)
                {
                    _tokenAsked = true;
                    Backend.ServerToken().ContinueWith(t => { _freshToken = t.IsFaulted || !t.Result.Ok ? "" : t.Result.Value; _tokenAnswered = true; });
                }
            }
            else if (_helloPending && SelfId >= 0)
            {
                _helloPending = false;
                _tokenAsked = _tokenAnswered = false;
                if (!string.IsNullOrEmpty(_freshToken)) AuthToken = _freshToken;
                _freshToken = null;
                _nm.ClientManager.Broadcast(new LobbyHello { Version = Version, Name = AuthNickname ?? "", Token = AuthToken ?? "", SessionToken = NetSession.Token });
            }
            TickQueue();
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
        // `force`: a dedicated server's start timer, which does not wait for the people who are not ready.
        // Set by the dedicated server for --perf only: a forced start with nobody in the room (ADR-242).
        public static bool AllowEmptyStart;

        public bool StartMatch(bool force = false)
        {
            if (!(force ? IsHost && Phase == PhaseKind.InLobby && !HasStarted && LobbyRules.MayForceStart(_truth.Count, AllowEmptyStart) : CanStart)) return false;
            _matchStarted = true;
            // The match listens on its own port so the lobby's sockets need not be closed first.
            ushort matchPort = MatchDirector.FreePort((ushort)(Port + 1));
            var start = new LobbyStart { MatchPort = matchPort, Players = _truth.Count, Bots = LobbyRules.ClampBots(_truthBots, _truth.Count) };
            _nm.ServerManager.Broadcast(start);
            if (Dedicated)
            {
                // No client of its own to hear the message: the server leaves the lobby itself, after the guests got it.
                StartInfo = start;
                HasStarted = true;
                _hostLeaveAt = Time.unscaledTime + HostLeaveDelay;
                Changed?.Invoke();
            }
            return true;
        }

        // ---- Matchmaking queue (ADR-247, ADR-258) ----

        // Dedicated server: the queue of people who pressed start. Whoever asked is placed by the composer when the window ends;
        // clients that never asked stay in the room as before (ready/timer flow).
        MatchQueue _queue;
        readonly QueueParties _parties = new QueueParties();
        readonly System.Collections.Generic.Dictionary<int, string> _sessionOf = new System.Collections.Generic.Dictionary<int, string>();
        // Server: the session tokens of the people placed in the match, in team order (a party's members side by side).
        public System.Collections.Generic.List<string> PlannedSessions { get; } = new System.Collections.Generic.List<string>();
        TeamMode _queueMode;
        float _nextQueueState = -1f;
        public bool QueueActive => _queue != null && _queue.Tickets.Count > 0;
        public TeamMode QueueMode => _queueMode;

        // Client: ask for a place in the queue (one request per client; asking again changes nothing until it leaves).
        public bool RequestMatch(TeamMode mode, int partySize)
        {
            if (Phase != PhaseKind.InLobby || IsHost || _nm == null) return false;
            _nm.ClientManager.Broadcast(new LobbyQueueRequest { Mode = (byte)mode, PartySize = (byte)Mathf.Clamp(partySize, 1, 4), PartyId = QueuePartyId });
            return true;
        }

        public bool CancelMatch()
        {
            if (Phase != PhaseKind.InLobby || IsHost || _nm == null) return false;
            _nm.ClientManager.Broadcast(new LobbyQueueCancel());
            return true;
        }

        void OnQueueRequest(NetworkConnection conn, LobbyQueueRequest req, Channel channel)
        {
            if (!Dedicated || _matchStarted || !_truth.Contains(conn.ClientId)) return;
            var mode = (TeamMode)Mathf.Clamp(req.Mode, 0, 2);
            if (_queue == null) _queue = new MatchQueue(MatchRules.Current ?? new MatchRules());
            if (_queue.Tickets.Count == 0) _queueMode = mode; // the first one decides the mode of this match
            else if (mode != _queueMode) return;               // another mode waits for its own window (not queued here)
            int size = mode == TeamMode.Solo ? 1 : Mathf.Clamp(req.PartySize, 1, MatchComposer.TeamSizeOf(mode, TeamRules.Current));
            long party = mode == TeamMode.Solo ? 0 : req.PartyId; // in solo everybody is alone
            var added = _parties.Add(conn.ClientId, party);
            if (added == QueueParties.AddResult.Duplicate) return;
            if (added == QueueParties.AddResult.JoinedTicket) { PushQueueState(); return; } // the party's ticket is already waiting
            if (_queue.Enqueue(conn.ClientId, size, Time.unscaledTimeAsDouble)) PushQueueState();
            else _parties.Remove(conn.ClientId, out _);
        }

        void OnQueueCancel(NetworkConnection conn, LobbyQueueCancel cancel, Channel channel)
        {
            LeaveQueue(conn.ClientId);
        }

        // A connection leaves the queue (cancelled or dropped); its party's ticket goes only when nobody of the party is left.
        void LeaveQueue(int clientId)
        {
            if (_parties.Remove(clientId, out int ticket) && _queue != null && _queue.Leave(ticket, Time.unscaledTimeAsDouble)) PushQueueState();
        }

        void PushQueueState()
        {
            if (_queue == null) return;
            ServerSetQueue(_queue.State(Time.unscaledTimeAsDouble));
            BroadcastState();
        }

        // Server, every frame: the countdown shown to the clients (twice a second) and the start when the window ended or the room is full.
        void TickQueue()
        {
            if (!Dedicated || _queue == null || _matchStarted || Phase != PhaseKind.InLobby) return;
            double now = Time.unscaledTimeAsDouble;
            if (_queue.Tickets.Count > 0 && Time.unscaledTime >= _nextQueueState) { _nextQueueState = Time.unscaledTime + 0.5f; PushQueueState(); }
            if (_queue.ShouldStart(_queueMode, now, TeamRules.Current)) StartFromQueue(now);
        }

        // The window ended: the composer decides who plays; the placed clients are told the match port, the others stay.
        void StartFromQueue(double now)
        {
            var plan = _queue.Start(_queueMode, now, TeamRules.Current);
            if (plan.Humans == 0) return;
            _matchStarted = true;
            ushort matchPort = MatchDirector.FreePort((ushort)(Port + 1));
            var start = LobbyStart.FromPlan(matchPort, _queueMode, plan);
            PlannedSessions.Clear();
            foreach (int ticket in plan.Placed)
                foreach (int id in _parties.Members(ticket))
                {
                    if (_nm.ServerManager.Clients.TryGetValue(id, out var conn)) _nm.ServerManager.Broadcast(conn, start);
                    if (_sessionOf.TryGetValue(id, out var token)) PlannedSessions.Add(token);
                }
            StartInfo = start;
            StartMode = _queueMode;
            HasStarted = true;
            _hostLeaveAt = Time.unscaledTime + HostLeaveDelay;
            Debug.Log($"[Server] queue closed: {plan.Humans} people, {plan.Bots} bots, {plan.Teams.Count} teams, mode {_queueMode}");
            Changed?.Invoke();
        }

        // The mode the match starts in (set by the queue; the dedicated server applies it to TeamMatch before the scene loads).
        public TeamMode StartMode { get; private set; }

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
            var login = LobbyRules.CheckLogin(ServerTokenSecret, hello.Token, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out var claims);
            if (login != JoinResult.Ok)
            {
                _nm.ServerManager.Broadcast(conn, new LobbyReject { Reason = (byte)login }, false);
                conn.Disconnect(false);
                return;
            }
            if (!string.IsNullOrEmpty(ServerTokenSecret) && AssignedCheck != null && !AssignedCheck(claims.AccountId))
            {
                _nm.ServerManager.Broadcast(conn, new LobbyReject { Reason = (byte)JoinResult.NotAssigned }, false);
                conn.Disconnect(false);
                return;
            }
            string name = hello.Name;
            if (!string.IsNullOrEmpty(ServerTokenSecret)) { if (!string.IsNullOrEmpty(hello.SessionToken)) AccountsBySession[hello.SessionToken] = claims.AccountId; name = claims.Nickname; } // the backend's nickname, not the client's word
            if (IsHost && conn.ClientId == SelfId) _truth.HostId = conn.ClientId; // the host's own client
            _truth.Add(conn.ClientId, name);
            if (!string.IsNullOrEmpty(hello.SessionToken)) _sessionOf[conn.ClientId] = hello.SessionToken;
            RefreshBots();
            BroadcastState();
            if (Dedicated) Changed?.Invoke();
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
            _sessionOf.Remove(args.ConnectionId);
            LeaveQueue(args.ConnectionId);
            RefreshBots();
            BroadcastState();
            if (Dedicated) Changed?.Invoke();
        }

        // Until the host picks a number, the bots fill the match up to the single-player size as people come and go.
        void RefreshBots()
        {
            int target = (MatchRules.Current ?? new MatchRules()).TargetParticipants;
            _truthBots = _botsChosen ? LobbyRules.ClampBots(_truthBots, _truth.Count) : LobbyRules.DefaultBots(_truth.Count, target);
        }

        void BroadcastState()
        {
            int n = _truth.Count;
            var state = new LobbyState { Ids = new int[n], Names = new string[n], Ready = new bool[n], HostId = _truth.HostId, Bots = _truthBots, MaxPlayers = MaxPlayers, Queue = Queue };
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
