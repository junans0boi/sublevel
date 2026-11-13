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
    // Boots the match as a host and populates it on the server: players, loot (seeded), training dummies.
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

        public static MatchDirector Instance { get; private set; }
        public Transform[] LootSpawns => _lootSpawns;
        public Transform[] DummySpawns => _dummySpawns;
        public Transform[] BotSpawns => _botSpawns;
        public int Seed => _seed;
        public ushort Port { get; private set; }
        // Score keeping for this match: alive count, kills, placement, winner.
        public MatchState State { get; private set; }

        const float BotSnapMeters = 2f;

        int _nextPlayerSpawn;

        void Awake()
        {
            Instance = this;
            // Scenes built before the match state existed get one here, so no scene has to be rebuilt for it.
            State = GetComponent<MatchState>();
            if (State == null) State = gameObject.AddComponent<MatchState>();
        }

        void Start()
        {
            State.Bind(_networkManager);
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

        // One bot per spawn point, put on the navmesh when there is one within reach. Bots are ordinary combatants:
        // MatchState counts them as they spawn (it recounts whenever the combatant list changes).
        void ServerSpawnBots()
        {
            if (_botPrefab == null || _botSpawns == null || _botSpawns.Length == 0) return;
            ServerGiveBotsPlacesToGo();
            int number = 0;
            foreach (var point in _botSpawns)
            {
                if (point == null) continue;
                var position = point.position;
                if (NavMesh.SamplePosition(position, out var hit, BotSnapMeters, NavMesh.AllAreas)) position = hit.position;
                var bot = Instantiate(_botPrefab, position, point.rotation);
                _networkManager.ServerManager.Spawn(bot);
                number++;
                var combatant = bot.GetComponent<Combatant>();
                if (combatant != null && State != null) State.ServerSetName(combatant, "봇 " + number);
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

        public void ServerSpawnLoot(LootEntry entry, Vector3 position)
        {
            var item = Instantiate(_lootPrefab, position, Quaternion.identity);
            item.GetComponent<LootItem>().Init(entry);
            _networkManager.ServerManager.Spawn(item);
        }

        void OnClientLoaded(NetworkConnection connection, bool asServer)
        {
            if (!asServer) return;
            var point = _playerSpawns[_nextPlayerSpawn++ % _playerSpawns.Length];
            var player = Instantiate(_playerPrefab, point.position, point.rotation);
            _networkManager.ServerManager.Spawn(player, connection);
        }

        // Server-side. Solo has no revival: restarting reloads the match scene.
        public void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
