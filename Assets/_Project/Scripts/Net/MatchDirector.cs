using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;
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
        [SerializeField] int _seed = 12345;
        [SerializeField] bool _startAsHost = true;

        public static MatchDirector Instance { get; private set; }
        public Transform[] LootSpawns => _lootSpawns;
        public Transform[] DummySpawns => _dummySpawns;
        public int Seed => _seed;

        int _nextPlayerSpawn;

        void Awake() => Instance = this;

        void Start()
        {
            _networkManager.ServerManager.OnServerConnectionState += OnServerState;
            _networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
            if (_startAsHost)
            {
                _networkManager.ServerManager.StartConnection();
                _networkManager.ClientManager.StartConnection();
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
