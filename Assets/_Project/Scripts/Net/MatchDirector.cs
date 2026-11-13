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
        public int ExpectedPlayers => _playerPrefab != null ? Mathf.Max(0, _expectedPlayers) : 0;

        const float BotSnapMeters = 2f;
        // Feet a little above the station floor, as the builders place spawn points.
        const float ElevatorSpawnLift = 0.1f;
        // The doors open on their own this long after the server started if no player ever arrives.
        const float PlayerWaitSeconds = 15f;

        int _nextPlayerSpawn;
        int _playersPlaced;
        int _expectedPlayersCounted;
        int[] _deployment = new int[0];
        int[] _elevatorLoad = new int[0];
        float _serverStartedAt = -1f;

        void Awake()
        {
            Instance = this;
            LockdownSeed = LockdownSeedOverride ?? (_randomDeployment ? unchecked(_seed * 7919 ^ System.Environment.TickCount ^ (int)System.DateTime.Now.Ticks) : _seed);
            // A new match: the bots' stall counts start again.
            BotStats.Clear();
            // Scenes built before the match state existed get one here, so no scene has to be rebuilt for it.
            State = GetComponent<MatchState>();
            if (State == null) State = gameObject.AddComponent<MatchState>();
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
            _networkManager.ServerManager.OnServerConnectionState += OnServerState;
            _networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
            if (_startAsHost)
            {
                // Every run hosts its own match: never share (or fall through to) a port another process is serving (ADR-044).
                var transport = _networkManager.TransportManager.Transport;
                Port = FreePort(transport.GetPort());
                transport.SetPort(Port);
                _networkManager.ServerManager.StartConnection();
                _networkManager.ClientManager.StartConnection();
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
        }

        void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started) return;
            _serverStartedAt = Time.time;
            ServerPlanDeployment();

            var plan = GameData.Loot.Plan(_lootSpawns.Length, _seed);
            for (int i = 0; i < plan.Length; i++)
            {
                ServerSpawnLoot(plan[i], _lootSpawns[i].position);
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
                var bot = Instantiate(_botPrefab, position, rotation);
                _networkManager.ServerManager.Spawn(bot);
                number++;
                var combatant = bot.GetComponent<Combatant>();
                if (combatant != null && State != null) State.ServerSetName(combatant, "봇 " + number);
                if (inElevator) ServerProtect(combatant);
            }
        }

        // How many bots this match has: by data on a map with elevators (16-24 combatants, #77), otherwise one per
        // scene spawn point.
        public int BotCount
        {
            get
            {
                if (_botPrefab == null) return 0;
                if (HasElevators) return (MatchRules.Current ?? new MatchRules()).ClampedBotCount;
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
            _expectedPlayersCounted = _playerPrefab != null ? Mathf.Max(0, _expectedPlayers) : 0;
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

        // Where the n-th occupant of one elevator stands, relative to its spawn point (x right, z out of the door).
        // The bay is Buildings.ElevatorBay (3 m) square; a body is 0.7 m wide.
        static Vector3 SharedOffset(int occupant)
        {
            switch (occupant)
            {
                case 0: return Vector3.zero;
                case 1: return new Vector3(0.8f, 0f, 0f);
                case 2: return new Vector3(-0.8f, 0f, 0f);
                case 3: return new Vector3(0.8f, 0f, -0.8f);
                default: return new Vector3(-0.8f, 0f, -0.8f);
            }
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
            if (Elevators != null && _serverStartedAt >= 0f && !Elevators.ServerScheduled && Time.time - _serverStartedAt > PlayerWaitSeconds)
                Elevators.ServerScheduleOpen(DeploymentRules.Current.DoorDelaySeconds);
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

        public void ServerSpawnLoot(LootEntry entry, Vector3 position)
        {
            var item = Instantiate(_lootPrefab, position, Quaternion.identity);
            item.GetComponent<LootItem>().Init(entry);
            _networkManager.ServerManager.Spawn(item);
        }

        void OnClientLoaded(NetworkConnection connection, bool asServer)
        {
            if (!asServer) return;
            // Scenes with elevators: the deployment decides (ADR-055). Others: the spawn points in turn.
            // Players beyond the expected ones (-1) take the least occupied elevator instead of a bot's place.
            int index = _playersPlaced < _expectedPlayersCounted ? _playersPlaced : -1;
            if (TryElevatorFor(index, out var position, out var rotation, out _))
            {
                _playersPlaced++;
                var deployed = Instantiate(_playerPrefab, position, rotation);
                _networkManager.ServerManager.Spawn(deployed, connection);
                ServerProtect(deployed.GetComponent<Combatant>());
                // The match starts with its first player: the doors open DoorDelaySeconds later, for everyone.
                Elevators.ServerScheduleOpen(DeploymentRules.Current.DoorDelaySeconds);
                return;
            }
            var point = _playerSpawns[_nextPlayerSpawn++ % _playerSpawns.Length];
            var player = Instantiate(_playerPrefab, point.position, point.rotation);
            _networkManager.ServerManager.Spawn(player, connection);
        }

        // Server-side. Solo has no revival: restarting reloads the match scene.
        public void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
