using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Game.Net
{
    // Boots the match as a host and populates it on the server: players, loot (seeded), training dummies, bots.
    // On a map with elevators everyone (players and bots) starts in one, assigned by Deployment (ADR-055).
    public class MatchDirector : MonoBehaviour
    {
        [SerializeField] NetworkManager _networkManager;
        [SerializeField] NetworkObject _playerPrefab;
        [SerializeField] NetworkObject _lootPrefab;
        [SerializeField] NetworkObject _dummyPrefab;
        [SerializeField] Transform[] _playerSpawns = new Transform[0];
        [SerializeField] Transform[] _lootSpawns = new Transform[0];
        [SerializeField] Transform[] _dummySpawns = new Transform[0];
        // Bots (M2-2): one per spawn point, started with the match. Both empty = a match without bots.
        [SerializeField] NetworkObject _botPrefab;
        // Bots with the other roster models (ADR-230); bot n uses variant n mod length. Empty = every bot is _botPrefab.
        [SerializeField] NetworkObject[] _botVariants;
        // One player prefab per roster model, named Player_<ModelId> (ADR-255): the spawn picks the one the client asked for.
        [SerializeField] NetworkObject[] _playerVariants;
        // The local player's saved look as text; set by Game.Gameplay (PlayerAppearanceStore). Sent to the server on connect.
        public static System.Func<string> LocalLookSource;
        readonly System.Collections.Generic.Dictionary<int, string> _looks = new System.Collections.Generic.Dictionary<int, string>();
        [SerializeField] Transform[] _botSpawns = new Transform[0];
        [SerializeField] int _seed = 12345;
        [SerializeField] bool _startAsHost = true;
        // Elevator start (ADR-055): players expected at the start, counted into the assignment ahead of the bots
        // (more may join; they take elevators nobody holds). A fresh deployment every match unless switched off.
        [SerializeField] int _expectedPlayers = 1;
        [SerializeField] bool _randomDeployment = true;

        public static MatchDirector Instance { get; private set; }
        // Tests: forces the deployment seed of the next matches (null = as configured).
        public static int? DeploymentSeedOverride;
        // Tests: forces the lockdown seed of the next matches (null = random per match, or the scene seed when the
        // deployment is not random).
        public static int? LockdownSeedOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            DeploymentSeedOverride = null;
            LockdownSeedOverride = null;
        }

        public Transform[] LootSpawns => _lootSpawns;
        public Transform[] DummySpawns => _dummySpawns;
        public Transform[] BotSpawns => _botSpawns;
        public int Seed => _seed;
        // Seeds this match's lockdown plan (closing order, final region): new every match (#77).
        public int LockdownSeed { get; private set; }
        public ushort Port { get; private set; }
        // Score keeping for this match: alive count, kills, placement, winner.
        public MatchState State { get; private set; }
        // The elevators and their doors; null in scenes without elevators (players then use the spawn points in turn).
        public ElevatorDoors Elevators { get; private set; }
        public int DeploymentSeed { get; private set; }
        // Server: the elevator index each participant was given (players first, then bots); empty without elevators.
        public IReadOnlyList<int> ElevatorAssignment => _deployment;
        // Players counted ahead of the bots (participants 0..ExpectedPlayers-1, ADR-055); teams count the same way (ADR-072).
        // In a lobby's match (#160) it is the people the lobby counted.
        public int ExpectedPlayers => _playerPrefab != null ? Mathf.Max(0, NetSession.Role == NetSession.RoleKind.Host ? NetSession.Players : _expectedPlayers) : 0;

        const float BotSnapMeters = 2f;
        // Feet a little above the station floor, as the builders place spawn points.
        const float ElevatorSpawnLift = 0.1f;
        // The doors open on their own this long after the server started if no player ever arrives.
        const float PlayerWaitSeconds = 15f;
        // A lobby's match waits longer for its people (scene loads differ per machine); stragglers then arrive in an open station.
        const float LobbyWaitSeconds = 60f;
        // A guest keeps trying to reach the host's match this long (the host's scene may load later than its own).
        const float GuestConnectSeconds = 60f;

        int _nextPlayerSpawn;
        int _playersPlaced;
        int _expectedPlayersCounted;
        int[] _deployment = new int[0];
        int[] _elevatorLoad = new int[0];
        float _serverStartedAt = -1f;
        float _guestGiveUpAt = -1f;
        float _guestRetryAt = -1f;
        bool _guestWasUp;
        // Server: holds a dropped player's combatant for a reconnect, then hands it to a bot (#166).
        public SessionKeeper Sessions { get; private set; }

        void Awake()
        {
            Instance = this;
            LockdownSeed = LockdownSeedOverride ?? (_randomDeployment ? unchecked(_seed * 7919 ^ System.Environment.TickCount ^ (int)System.DateTime.Now.Ticks) : _seed);
            // A new match: the bots' stall counts start again.
            BotStats.Clear();
            // Scenes built before the match state existed get one here, so no scene has to be rebuilt for it.
            State = GetComponent<MatchState>();
            if (State == null) State = gameObject.AddComponent<MatchState>();
            if (GetComponent<PollutionDirector>() == null) gameObject.AddComponent<PollutionDirector>(); // the pollution runs the match (T10)
        }

        void Start()
        {
            State.Bind(_networkManager);
            // Every peer builds the same doors from the scene's elevators; scenes without elevators get none.
            Elevators = ElevatorDoors.TryCreate(gameObject);
            if (Elevators != null) Elevators.Bind(_networkManager);
            // Window panes (ADR-065 (4)): the server breaks them and tells everyone.
            var glass = GetComponent<GlassPanes>();
            if (glass == null) glass = gameObject.AddComponent<GlassPanes>();
            glass.Bind(_networkManager);
            // Doors of the map's buildings (ADR-104): the server keeps their state.
            var doors = GetComponent<DoorSystem>();
            if (doors == null) doors = gameObject.AddComponent<DoorSystem>();
            doors.Bind(_networkManager);
            // Time of day and weather: the server picks, every client derives (ADR-131).
            var sky = GetComponent<SkySync>();
            if (sky == null) sky = gameObject.AddComponent<SkySync>();
            sky.Bind(_networkManager);
            _networkManager.ServerManager.OnServerConnectionState += OnServerState;
            _networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
            _networkManager.ServerManager.RegisterBroadcast<LookBroadcast>(OnLook, false);
            _networkManager.ClientManager.OnClientConnectionState += SendLook;
            if (_startAsHost && NetSession.Role == NetSession.RoleKind.Client)
            {
                // A guest of a lobby's match (#160): no server here, only the connection to the host.
                _guestGiveUpAt = Time.time + GuestConnectSeconds;
                SessionHelloSender.Attach(_networkManager, NetSession.Token);
                _networkManager.ClientManager.OnClientConnectionState += OnGuestState;
                _networkManager.ClientManager.StartConnection(NetSession.Address, NetSession.Port);
            }
            else if (_startAsHost)
            {
                // Every run hosts its own match: never share (or fall through to) a port another process is serving (ADR-044).
                var transport = _networkManager.TransportManager.Transport;
                // A lobby's host keeps the port it told the guests.
                Port = FreePort(NetSession.Role == NetSession.RoleKind.Host && NetSession.Port != 0 ? NetSession.Port : transport.GetPort());
                transport.SetPort(Port);
                // Far combatants, loot, vehicles and thrown items are not sent (#167, ADR-135).
                InterestCondition.Install(_networkManager);
                _networkManager.ServerManager.StartConnection();
                if (!NetSession.Dedicated) _networkManager.ClientManager.StartConnection();
            }
        }

        // Close the sockets on the way out so nothing of this match outlives the window.
        void OnApplicationQuit()
        {
            if (_networkManager == null) return;
            _networkManager.ClientManager.StopConnection();
            _networkManager.ServerManager.StopConnection(true);
        }

        // The preferred port if nobody holds it, otherwise one the system hands out.
        public static ushort FreePort(ushort preferred)
        {
            if (CanBind(preferred, out _)) return preferred;
            if (CanBind(0, out ushort assigned)) return assigned;
            Debug.LogError($"[MatchDirector] no free port found; falling back to {preferred}, the match may fail to start");
            return preferred;
        }

        static bool CanBind(ushort port, out ushort bound)
        {
            bound = 0;
            try
            {
                using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    // Windows otherwise lets two sockets share a port; other platforms do not have the option.
                    try { probe.ExclusiveAddressUse = true; } catch (SocketException) { } catch (System.PlatformNotSupportedException) { }
                    probe.Bind(new IPEndPoint(IPAddress.Any, port));
                    bound = (ushort)((IPEndPoint)probe.LocalEndPoint).Port;
                    return true;
                }
            }
            catch (SocketException)
            {
                return false;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_networkManager == null) return;
            _networkManager.ServerManager.OnServerConnectionState -= OnServerState;
            _networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoaded;
            _networkManager.ServerManager.UnregisterBroadcast<LookBroadcast>(OnLook);
            _networkManager.ClientManager.OnClientConnectionState -= SendLook;
            _networkManager.ClientManager.OnClientConnectionState -= OnGuestState;
        }

        // The guest's first attempt may come before the host listens: try again until it is up or the time is over.
        void OnGuestState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Stopped && _guestWasUp)
            {
                // The line dropped mid-match: keep knocking for as long as the server holds our combatant (#166).
                _guestWasUp = false;
                _guestGiveUpAt = Time.time + (MatchRules.Current ?? new MatchRules()).ReconnectSeconds;
            }
            if (args.ConnectionState == LocalConnectionState.Stopped && Time.time < _guestGiveUpAt) _guestRetryAt = Time.time + 1f;
            else if (args.ConnectionState == LocalConnectionState.Started) { _guestGiveUpAt = -1f; _guestWasUp = true; }
        }

        // Roles and regions come from the spawn names the world builder gives them (LootSpawnName, ADR-256); a scene without them keeps the plain roll.
        LootEntry[] PlanLoot()
        {
            var roles = new char[_lootSpawns.Length];
            var regions = new int[_lootSpawns.Length];
            bool tagged = _lootSpawns.Length > 0;
            for (int i = 0; i < roles.Length; i++) tagged &= LootSpawnName.Parse(_lootSpawns[i].name, out roles[i], out regions[i]);
            if (!tagged) return GameData.Loot.Plan(_lootSpawns.Length, _seed);
            var plan = GameData.Loot.PlanByRole(roles, _seed);
            LootGuarantee.Apply(plan, regions, System.Linq.Enumerable.Max(regions) + 1, GameData.Loot, LootKind.Jerrycan, 1, _seed);
            return plan;
        }

        void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started) return;
            _serverStartedAt = Time.time;
            Sessions = SessionKeeper.Attach(_networkManager);
            NetPulseServer.Attach(_networkManager);
            ServerPlanDeployment();

            var plan = PlanLoot();
            for (int i = 0; i < plan.Length; i++)
            {
                ServerSpawnLootRoll(plan[i], _lootSpawns[i].position);
            }

            foreach (var point in _dummySpawns)
            {
                var dummy = Instantiate(_dummyPrefab, point.position, point.rotation);
                _networkManager.ServerManager.Spawn(dummy);
            }

            ServerSpawnBots();
        }

        // The bots of the match, put on the navmesh when there is one within reach. Bots are ordinary combatants:
        // MatchState counts them as they spawn (it recounts whenever the combatant list changes).
        // On a map with elevators there are MatchRules.BotCount of them, each in the elevator the deployment gave it
        // (the scene's bot spawn points are not needed); elsewhere one per scene spawn point.
        void ServerSpawnBots()
        {
            int count = BotCount;
            if (count <= 0) return;
            ServerGiveBotsPlacesToGo();
            int number = 0;
            int spawnPoints = _botSpawns != null ? _botSpawns.Length : 0;
            for (int i = 0; i < count; i++)
            {
                var point = spawnPoints > 0 ? _botSpawns[i % spawnPoints] : null;
                // With elevators a bot starts in the one the deployment gave it (bots follow the players in the
                // assignment), otherwise at its spawn point.
                bool inElevator = TryElevatorFor(_expectedPlayersCounted + number, out var position, out var rotation, out bool riding);
                if (!inElevator)
                {
                    if (point == null) continue;
                    position = point.position;
                    rotation = point.rotation;
                }
                // A bot in an elevator rides the car (the shaft has no navmesh); BotController finds the navmesh
                // once the doors open.
                if (!(inElevator && riding) && NavMesh.SamplePosition(position, out var hit, BotSnapMeters, NavMesh.AllAreas)) position = hit.position;
                var bot = Instantiate(_botVariants != null && _botVariants.Length > 0 ? _botVariants[number % _botVariants.Length] : _botPrefab, position, rotation);
                _networkManager.ServerManager.Spawn(bot);
                number++;
                var combatant = bot.GetComponent<Combatant>();
                if (combatant != null && State != null) State.ServerSetName(combatant, BotNames.For(Seed, number - 1));
                if (inElevator) ServerProtect(combatant);
            }
        }

        // How many bots this match has: by data on a map with elevators (16-24 combatants, #77), otherwise one per
        // scene spawn point.
        bool _slotWarned;
        public int BotCount
        {
            get
            {
                if (_botPrefab == null) return 0;
                if (HasElevators)
                {
                    int wanted = NetSession.Role == NetSession.RoleKind.Host && NetSession.Bots >= 0
                        ? LobbyRules.ClampBots(NetSession.Bots, NetSession.Players)
                        : (MatchRules.Current ?? new MatchRules()).BotsFor(Mathf.Max(1, NetSession.Players));
                    // One car per team (ADR-260): the map's elevators are the slots, so a match never has more bots than they hold.
                    int slots = Elevators.Sites.Count * Mathf.Max(1, TeamMatch.TeamSize) - Mathf.Max(1, NetSession.Players);
                    if (wanted > slots && !_slotWarned) { _slotWarned = true; Debug.LogWarning($"[MatchDirector] {wanted} bots wanted but the {Elevators.Sites.Count} elevators hold {slots} beside the people: capped"); }
                    return Mathf.Max(0, Mathf.Min(wanted, slots));
                }
                if (_botSpawns == null) return 0;
                int count = 0;
                foreach (var point in _botSpawns) if (point != null) count++;
                return count;
            }
        }

        bool HasElevators => Elevators != null && Elevators.Sites.Count > 0;

        // ADR-055 (1)(4): one assignment for everyone who starts the match, players first, then bots.
        void ServerPlanDeployment()
        {
            if (Elevators == null || Elevators.Sites.Count == 0) return;
            DeploymentSeed = DeploymentSeedOverride ?? (_randomDeployment ? unchecked(_seed * 397 ^ System.Environment.TickCount) : _seed);
            _expectedPlayersCounted = ExpectedPlayers;
            var slots = new ElevatorSlot[Elevators.Sites.Count];
            for (int i = 0; i < slots.Length; i++) slots[i] = new ElevatorSlot(Elevators.Sites[i].Region);
            // A team starts together in one elevator (ADR-072); solo is the plain assignment.
            _deployment = Game.Core.Teams.Deploy(_expectedPlayersCounted + BotCount, TeamMatch.TeamSize, slots, DeploymentSeed);
            _elevatorLoad = new int[slots.Length];
            Debug.Log($"[MatchDirector] elevator start: {_deployment.Length} participants over {slots.Length} elevators, seed {DeploymentSeed}");
        }

        // Where participant `index` starts: its assigned elevator, or (a player beyond the expected ones) the least
        // occupied elevator. False when the scene has no elevators. `riding`: in the car under the station.
        bool TryElevatorFor(int index, out Vector3 position, out Quaternion rotation, out bool riding)
        {
            position = default;
            rotation = Quaternion.identity;
            riding = false;
            if (Elevators == null || Elevators.Sites.Count == 0 || _elevatorLoad.Length != Elevators.Sites.Count) return false;
            int elevator;
            if (index >= 0 && index < _deployment.Length) elevator = _deployment[index];
            else
            {
                elevator = 0;
                for (int i = 1; i < _elevatorLoad.Length; i++) if (_elevatorLoad[i] < _elevatorLoad[elevator]) elevator = i;
            }
            int sharing = _elevatorLoad[elevator]++;
            var site = Elevators.Sites[elevator];
            rotation = Quaternion.Euler(0f, site.YawDeg, 0f);
            // With a car under the station (ADR-065 (1)) the start is in the car, wherever it is right now: at the
            // bottom of the shaft before the doors are scheduled, at the floor for a late joiner.
            riding = Elevators.CarOf(elevator) != null;
            float lift = riding ? ElevatorCar.RideLift - Elevators.CarOffsetNow : ElevatorSpawnLift;
            // More participants than elevators (ADR-055: then elevators are shared): side by side in the bay.
            position = site.Spawn + rotation * SharedOffset(sharing) + Vector3.up * lift;
            return true;
        }

        static Vector3 SharedOffset(int occupant)
        {
            var (x, z) = Deployment.BayOffset(occupant);
            return new Vector3(x, 0f, z);
        }

        void ServerProtect(Combatant combatant)
        {
            if (combatant == null || Elevators == null) return;
            var protection = combatant.GetComponent<SpawnProtection>();
            if (protection == null) protection = combatant.gameObject.AddComponent<SpawnProtection>();
            protection.ServerBegin(Elevators);
        }

        void Update()
        {
            // Nobody came to play (a server without its own client): the bots are not kept in the dark for ever.
            float wait = NetSession.Role == NetSession.RoleKind.Host ? LobbyWaitSeconds : PlayerWaitSeconds;
            if (Elevators != null && _serverStartedAt >= 0f && !Elevators.ServerScheduled && Time.time - _serverStartedAt > wait)
                Elevators.ServerScheduleOpen(DeploymentRules.Current.DoorDelaySeconds);
            if (_guestRetryAt >= 0f && Time.time >= _guestRetryAt)
            {
                _guestRetryAt = -1f;
                _networkManager.ClientManager.StartConnection(NetSession.Address, NetSession.Port);
            }
        }

        // The places bots wander between: loot, dummies and where bots start. Game.AI sits above this assembly, so
        // its entry point is called by name; a build without it just leaves bots on their own defaults.
        void ServerGiveBotsPlacesToGo()
        {
            var method = System.Type.GetType("Game.AI.BotController, Game.AI")?.GetMethod("SetPointsOfInterest",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null) return;
            var points = new List<Vector3>();
            foreach (var group in new[] { _lootSpawns, _dummySpawns, _botSpawns })
                if (group != null)
                    foreach (var t in group)
                        if (t != null) points.Add(t.position);
            method.Invoke(null, new object[] { points });
        }

        // ADR-136: a rolled gun lies unloaded with its ammo as a separate item 0.4 m beside it.
        public void ServerSpawnLootRoll(LootEntry roll, Vector3 position)
        {
            var entries = Loadout.SpawnEntries(GameData.Weapons, roll);
            for (int i = 0; i < entries.Length; i++) ServerSpawnLoot(entries[i], position + Vector3.right * (0.4f * i));
        }

        public void ServerSpawnLoot(LootEntry entry, Vector3 position)
        {
            var item = Instantiate(_lootPrefab, position, Quaternion.identity);
            item.GetComponent<LootItem>().Init(entry);
            _networkManager.ServerManager.Spawn(item);
        }

        void OnLook(NetworkConnection connection, LookBroadcast message, FishNet.Transporting.Channel channel) => _looks[connection.ClientId] = message.Look;

        void SendLook(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started || LocalLookSource == null) return;
            _networkManager.ClientManager.Broadcast(new LookBroadcast { Look = LocalLookSource() });
        }

        // The prefab for this connection's chosen model (the default player prefab when it sent none or the model has no prefab) and its look text.
        NetworkObject PlayerPrefabFor(NetworkConnection connection, out string look)
        {
            look = "";
            if (_playerVariants == null || _playerVariants.Length == 0 || !_looks.TryGetValue(connection.ClientId, out look)) return _playerPrefab;
            string model = look.Split(':')[0];
            foreach (var variant in _playerVariants)
                if (variant != null && variant.name == "Player_" + model) return variant;
            return _playerPrefab;
        }

        static void SetLook(NetworkObject player, string look)
        {
            var component = player.GetComponent<CombatantLook>();
            if (component != null) component.ServerSet(look);
        }

        void OnClientLoaded(NetworkConnection connection, bool asServer)
        {
            if (!asServer) return;
            // A returning player gets its held combatant back instead of a new one (#166).
            if (Sessions == null || !Sessions.Intercept(connection, () => ServerSpawnPlayer(connection))) ServerSpawnPlayer(connection);
            // Interest management (#167): the client sees nothing until its body exists, so give it its view now, not at the next timed check.
            _networkManager.ServerManager.Objects.RebuildObservers(connection);
        }

        void ServerSpawnPlayer(NetworkConnection connection)
        {
            // Scenes with elevators: the deployment decides (ADR-055). Others: the spawn points in turn.
            // Players beyond the expected ones (-1) take the least occupied elevator instead of a bot's place.
            int index = _playersPlaced < _expectedPlayersCounted ? _playersPlaced : -1;
            if (TryElevatorFor(index, out var position, out var rotation, out _))
            {
                _playersPlaced++;
                var deployed = Instantiate(PlayerPrefabFor(connection, out var deployedLook), position, rotation);
                SetLook(deployed, deployedLook);
                _networkManager.ServerManager.Spawn(deployed, connection);
                ServerProtect(deployed.GetComponent<Combatant>());
                // The match starts with its first player: the doors open DoorDelaySeconds later, for everyone.
                // A lobby's match waits until all its people stand in their elevators (#160); Update covers the ones who never come.
                if (NetSession.Role != NetSession.RoleKind.Host || _playersPlaced >= _expectedPlayersCounted)
                    Elevators.ServerScheduleOpen(DeploymentRules.Current.DoorDelaySeconds);
                return;
            }
            var point = _playerSpawns[_nextPlayerSpawn++ % _playerSpawns.Length];
            var player = Instantiate(PlayerPrefabFor(connection, out var look), point.position, point.rotation);
            SetLook(player, look);
            _networkManager.ServerManager.Spawn(player, connection);
        }

        // Server-side. Solo has no revival: restarting reloads the match scene.
        public void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
