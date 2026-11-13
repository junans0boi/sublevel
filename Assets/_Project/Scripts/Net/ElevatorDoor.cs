using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // When the elevator doors open, sent by the server to every client (and again to each client that finishes
    // loading). Times travel as "seconds from now" so client and server clocks never have to agree.
    public struct ElevatorDoorStatus : IBroadcast
    {
        public bool Scheduled;          // false while the match waits for its first player (doors closed)
        public float OpensIn;           // seconds from when this was sent; 0 or less = already open
        public float ProtectionSeconds; // spawn protection after the doors open (ADR-055 (3))
    }

    // A combatant gave up its spawn protection early (it fired or threw).
    public struct SpawnProtectionEnded : IBroadcast
    {
        public int ObjectId;
    }

    // The front of one elevator station (ADR-055 (2)); the car behind it comes up from underground (ElevatorCar,
    // ADR-065 (1)). A box that fills the opening and, once told when, sinks into
    // the floor. Not a network object: every peer has its own, driven by ElevatorDoors from one replicated time, so
    // 13 stations cost no network objects. While it stands, its collider stops movement and shots like a wall.
    // WorldBuilder may place it (any GameObject with a collider and renderers; set Station); otherwise
    // ElevatorDoors.Create builds a plain one at runtime.
    public class ElevatorDoor : MonoBehaviour
    {
        [SerializeField] int _station = -1;     // index into the scene's elevators (WorldInfo order)
        [SerializeField] float _drop = 3.1f;    // metres it sinks: its own height and a little more

        Collider[] _colliders;
        Renderer[] _renderers;
        Vector3 _closed;
        double _openAt = double.PositiveInfinity;
        float _slideSeconds = 1f;
        bool _gone;

        public int Station => _station;
        // 0 = closed, 1 = fully sunk (then it neither blocks nor shows).
        public float Opened { get; private set; }
        public bool Blocking => !_gone;

        void Awake()
        {
            _colliders = GetComponentsInChildren<Collider>();
            _renderers = GetComponentsInChildren<Renderer>();
            _closed = transform.position;
        }

        // For doors built in code: which station, and how far it sinks.
        public void Configure(int station, float drop)
        {
            _station = station;
            _drop = drop;
            _closed = transform.position;
        }

        // Local clock (Time.timeAsDouble). PositiveInfinity = closed until told otherwise.
        public void SetOpenAt(double localTime, float slideSeconds)
        {
            _openAt = localTime;
            _slideSeconds = Mathf.Max(0.01f, slideSeconds);
            Tick();
        }

        void Update() => Tick();

        void Tick()
        {
            if (_gone || double.IsInfinity(_openAt)) return;
            float t = Mathf.Clamp01((float)((Time.timeAsDouble - _openAt) / _slideSeconds));
            if (t <= 0f && Opened <= 0f) return;
            Opened = t;
            transform.position = _closed + Vector3.down * (_drop * t);
            if (t < 1f) return;
            // Sunk: out of the way for good (a restart reloads the scene).
            _gone = true;
            foreach (var c in _colliders) if (c != null) c.enabled = false;
            foreach (var r in _renderers) if (r != null) r.enabled = false;
        }
    }

    // The match's elevator start, scene-wide (ADR-055): where the elevators are, their doors, and when the doors
    // open. Added next to MatchDirector when the scene has elevators (the outdoor map); scenes without them (TestBox,
    // ZoneD) never get one, so nothing changes there. Like MatchState it needs no network object: the server sends
    // broadcasts and every peer keeps its own copy.
    public class ElevatorDoors : MonoBehaviour
    {
        // One elevator: where its occupant stands (feet), facing the way out, and its region.
        public struct Site
        {
            public Vector3 Spawn;
            public float YawDeg;
            public int Region;
        }

        public static ElevatorDoors Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        readonly List<Site> _sites = new List<Site>();
        readonly List<ElevatorDoor> _doors = new List<ElevatorDoor>();
        NetworkManager _network;
        bool _bound;

        // ---- Server ----
        double _serverOpenAt = double.PositiveInfinity;
        readonly HashSet<int> _serverEnded = new HashSet<int>();

        // ---- Client view (the host's own client included) ----
        bool _clientScheduled;
        double _clientOpenAt = double.PositiveInfinity;
        float _clientProtection;
        readonly HashSet<int> _clientEnded = new HashSet<int>();

        public IReadOnlyList<Site> Sites => _sites;
        public IReadOnlyList<ElevatorDoor> Doors => _doors;

        // Server: whether the opening is scheduled, and when (server clock; PositiveInfinity until scheduled).
        public bool ServerScheduled => !double.IsInfinity(_serverOpenAt);
        public double ServerOpenAt => _serverOpenAt;
        public bool ServerDoorsClosed => Time.timeAsDouble < _serverOpenAt;
        public double ServerProtectedUntil => _serverOpenAt + DeploymentRules.Current.ProtectionSeconds;

        // Client: the doors as this peer knows them.
        public bool ClientScheduled => _clientScheduled;
        public bool DoorsClosed => !_clientScheduled || Time.timeAsDouble < _clientOpenAt;
        // Seconds until the doors open; the whole delay while the match still waits for its first player.
        public float DoorSecondsLeft => !_clientScheduled
            ? DeploymentRules.Current.DoorDelaySeconds
            : Mathf.Max(0f, (float)(_clientOpenAt - Time.timeAsDouble));

        // This peer's own clock for the cars (ADR-065 (1)): the server's opening where this peer is the server (so
        // the server moves its bots with the doors it holds them by), otherwise the one the broadcast told it.
        public double LocalOpenAt => ServerScheduled ? _serverOpenAt : _clientScheduled ? _clientOpenAt : double.PositiveInfinity;
        // PositiveInfinity while the opening is not scheduled on this peer.
        public double LocalSecondsToOpen => double.IsInfinity(LocalOpenAt) ? double.PositiveInfinity : LocalOpenAt - Time.timeAsDouble;
        // Metres a car is below its station floor right now on this peer.
        public float CarOffsetNow => ElevatorRise.Offset(LocalSecondsToOpen, DeploymentRules.Current);

        // The car under station `station`, or null (a scene built before the cars, or a plain code-made door).
        public ElevatorCar CarOf(int station)
        {
            foreach (var car in ElevatorCar.All)
                if (car != null && car.Station == station) return car;
            return null;
        }

        // Client: protection left for a combatant (every combatant came up in an elevator when this exists).
        public float ProtectionSecondsLeftOf(int objectId)
        {
            if (_clientEnded.Contains(objectId)) return 0f;
            var rules = DeploymentRules.Current;
            if (!_clientScheduled) return rules.DoorDelaySeconds + rules.ProtectionSeconds;
            return Mathf.Max(0f, (float)(_clientOpenAt + _clientProtection - Time.timeAsDouble));
        }

        // Adds the component when the scene has elevators; null (and nothing added) when it has none.
        public static ElevatorDoors TryCreate(GameObject host)
        {
            var sites = ReadSites();
            if (sites.Count == 0) return null;
            var doors = host.GetComponent<ElevatorDoors>();
            if (doors == null) doors = host.AddComponent<ElevatorDoors>();
            doors.Setup(sites);
            ElevatorRides.Attach(doors);
            return doors;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (!_bound || _network == null) return;
            _network.ClientManager.UnregisterBroadcast<ElevatorDoorStatus>(OnDoorStatus);
            _network.ClientManager.UnregisterBroadcast<SpawnProtectionEnded>(OnProtectionEnded);
            _network.SceneManager.OnClientLoadedStartScenes -= OnClientLoaded;
        }

        void Setup(List<Site> sites)
        {
            _sites.Clear();
            _sites.AddRange(sites);
            _doors.Clear();
            // Doors the scene builder placed, by station; any station without one gets a plain box.
            var placed = new Dictionary<int, ElevatorDoor>();
            foreach (var door in FindObjectsByType<ElevatorDoor>(FindObjectsSortMode.None))
                if (door.Station >= 0 && door.Station < _sites.Count && !placed.ContainsKey(door.Station)) placed[door.Station] = door;
            Transform root = null;
            for (int i = 0; i < _sites.Count; i++)
            {
                if (!placed.TryGetValue(i, out var door))
                {
                    if (root == null) root = new GameObject("ElevatorDoors").transform;
                    door = Create(root, i, _sites[i]);
                }
                _doors.Add(door);
            }
        }

        // Called by MatchDirector before it starts the connections.
        public void Bind(NetworkManager network)
        {
            if (_bound || network == null) return;
            _bound = true;
            _network = network;
            _network.ClientManager.RegisterBroadcast<ElevatorDoorStatus>(OnDoorStatus);
            _network.ClientManager.RegisterBroadcast<SpawnProtectionEnded>(OnProtectionEnded);
            _network.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
            GetComponent<ElevatorRides>()?.Bind(network);
        }

        // ---- Door geometry ----

        // The plain door: a box across the station's front opening (Buildings.ElevatorStation: a 3 x 3 m bay whose
        // front edge is ElevatorBay / 2 ahead of the spawn point), from the floor to the porch roof, a hand's width
        // into each side wall, standing just inside the front edge.
        public static ElevatorDoor Create(Transform parent, int station, Site site)
        {
            var rules = DeploymentRules.Current;
            var rotation = Quaternion.Euler(0f, site.YawDeg, 0f);
            float width = Buildings.ElevatorBay + 2f * rules.DoorWallOverlap;
            float height = Buildings.StoreyClear;
            float thickness = rules.DoorThickness;
            var centre = site.Spawn + rotation * new Vector3(0f, height / 2f, Buildings.ElevatorBay / 2f - thickness / 2f);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ElevatorDoor" + station.ToString("00");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, rotation);
            go.transform.localScale = new Vector3(width, height, thickness);
            var material = DoorMaterial();
            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
            var door = go.AddComponent<ElevatorDoor>();
            door.Configure(station, height + 0.1f);
            return door;
        }

        static readonly Color DoorColor = new Color(0.42f, 0.44f, 0.46f);
        static Material _doorMaterial;

        // Brushed-steel grey, made in code (same approach as DeathCrate's marker).
        static Material DoorMaterial()
        {
            // Unity-null after a scene change threw the unused material away.
            if (_doorMaterial != null) return _doorMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            _doorMaterial = new Material(shader) { name = "ElevatorDoor" };
            _doorMaterial.SetColor("_BaseColor", DoorColor);
            _doorMaterial.SetFloat("_Metallic", 0.6f);
            _doorMaterial.SetFloat("_Smoothness", 0.45f);
            return _doorMaterial;
        }

        // The elevators of the scene, read from the map's WorldInfo (Game.Gameplay sits above this assembly, so it
        // is found by name, the way MatchDirector reaches the bots). Empty when the scene has none.
        static List<Site> ReadSites()
        {
            var sites = new List<Site>();
            var type = Type.GetType("Game.Gameplay.WorldInfo, Game.Gameplay");
            if (type == null) return sites;
            object info = type.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (info == null || (info is UnityEngine.Object o && o == null)) info = FindFirstObjectByType(type);
            if (info == null || (info is UnityEngine.Object u && u == null)) return sites;
            if (!(type.GetProperty("Elevators", BindingFlags.Public | BindingFlags.Instance)?.GetValue(info) is IEnumerable elevators)) return sites;
            foreach (var e in elevators)
            {
                var t = e.GetType();
                var spawn = t.GetField("Spawn")?.GetValue(e);
                var yaw = t.GetField("SpawnYawDeg")?.GetValue(e);
                var region = t.GetField("Region")?.GetValue(e);
                if (!(spawn is Vector3 at) || !(yaw is float deg) || !(region is int r)) continue;
                sites.Add(new Site { Spawn = at, YawDeg = deg, Region = r });
            }
            return sites;
        }

        // ---- Server ----

        bool ServerActive => _network != null && _network.IsServerStarted;

        // Starts the countdown (once): the doors open `delay` seconds from now, on every peer.
        public void ServerScheduleOpen(float delay)
        {
            if (ServerScheduled) return;
            _serverOpenAt = Time.timeAsDouble + Mathf.Max(0f, delay);
            // The server's own doors move on the server clock (a dedicated server has no client to tell it).
            foreach (var door in _doors) door.SetOpenAt(_serverOpenAt, DeploymentRules.Current.DoorSlideSeconds);
            ServerSendStatus(null);
        }

        // A combatant fired or threw while protected: tell everyone (the HUD drops the countdown).
        public void ServerProtectionEnded(Combatant combatant)
        {
            if (combatant == null || !_serverEnded.Add(combatant.ObjectId) || !ServerActive) return;
            _network.ServerManager.Broadcast(new SpawnProtectionEnded { ObjectId = combatant.ObjectId });
        }

        ElevatorDoorStatus ServerStatus() => new ElevatorDoorStatus
        {
            Scheduled = ServerScheduled,
            OpensIn = ServerScheduled ? (float)(_serverOpenAt - Time.timeAsDouble) : 0f,
            ProtectionSeconds = DeploymentRules.Current.ProtectionSeconds,
        };

        void ServerSendStatus(NetworkConnection only)
        {
            if (!ServerActive) return;
            if (only == null)
            {
                _network.ServerManager.Broadcast(ServerStatus());
                return;
            }
            if (!only.IsAuthenticated) return;
            _network.ServerManager.Broadcast(only, ServerStatus());
            foreach (int id in _serverEnded) _network.ServerManager.Broadcast(only, new SpawnProtectionEnded { ObjectId = id });
        }

        void OnClientLoaded(NetworkConnection connection, bool asServer)
        {
            if (asServer) ServerSendStatus(connection);
        }

        // ---- Clients ----

        void OnDoorStatus(ElevatorDoorStatus status, Channel channel)
        {
            _clientProtection = status.ProtectionSeconds;
            if (!status.Scheduled) return;
            double openAt = Time.timeAsDouble + status.OpensIn;
            // A late copy never moves an opening this peer already has.
            if (_clientScheduled && Math.Abs(openAt - _clientOpenAt) < 0.25) return;
            _clientScheduled = true;
            _clientOpenAt = openAt;
            // On a host the server already set the same doors (same clock); setting them again is harmless.
            if (!ServerActive)
                foreach (var door in _doors) door.SetOpenAt(_clientOpenAt, DeploymentRules.Current.DoorSlideSeconds);
        }

        void OnProtectionEnded(SpawnProtectionEnded message, Channel channel) => _clientEnded.Add(message.ObjectId);
    }
}
