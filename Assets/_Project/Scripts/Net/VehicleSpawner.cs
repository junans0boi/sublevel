using FishNet.Managing;
using FishNet.Object;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Puts the match's vehicles out when the server starts (ADR-065 (5)): one per parking spot, of the model given for
    // that spot (an index into the prefab list: the car and motorcycle models, #89). Also holds what vehicles need to
    // know about the map: the sea level (they stop in water deeper than DrownDepth) and the road map (open terrain off
    // the roads is slower).
    // Lives on the match object next to the NetworkManager; the scene builders fill it (VehicleBuilder).
    public class VehicleSpawner : MonoBehaviour
    {
        // Every vehicle model of the map; each spot names one by index.
        [SerializeField] NetworkObject[] _prefabs = new NetworkObject[0];
        [SerializeField] Transform[] _spots = new Transform[0];
        [UnityEngine.Serialization.FormerlySerializedAs("_kinds")]
        [SerializeField] int[] _models = new int[0];
        [SerializeField] bool _hasWater;
        [SerializeField] float _waterLevel;
        // One bit per cell, square from (0, 0); no cells = no map (every surface counts as road).
        [SerializeField] float _roadCellSize = 4f;
        [SerializeField] int _roadCells;
        [SerializeField] byte[] _roadBits = new byte[0];

        public static VehicleSpawner Instance { get; private set; }

        NetworkManager _networkManager;
        bool _spawned;

        // Height of the sea surface; negative infinity on a map without one.
        public static float WaterLevel => Instance != null && Instance._hasWater ? Instance._waterLevel : float.NegativeInfinity;

        // Is (x, z) on a road? True everywhere when the map has no road map (the test box: all of it is built ground).
        public static bool OnRoad(float x, float z)
        {
            var s = Instance;
            if (s == null || s._roadCells <= 0 || s._roadBits == null || s._roadBits.Length == 0) return true;
            return RoadMask.Test(s._roadBits, s._roadCells, s._roadCellSize, x, z);
        }

        public int SpotCount => _spots != null ? _spots.Length : 0;
        public int ModelAt(int spot) => _models != null && spot >= 0 && spot < _models.Length ? _models[spot] : 0;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            _networkManager = GetComponent<NetworkManager>();
            if (_networkManager == null) _networkManager = FindAnyObjectByType<NetworkManager>();
        }

        // Polled rather than tied to the connection event, so it works whichever of the match's Start methods runs first.
        void Update()
        {
            if (_spawned || _networkManager == null || !_networkManager.IsServerStarted) return;
            _spawned = true;
            ServerSpawn();
        }

        void ServerSpawn()
        {
            if (_spots == null) return;
            for (int i = 0; i < _spots.Length; i++)
            {
                var spot = _spots[i];
                int model = ModelAt(i);
                if (spot == null || _prefabs == null || model < 0 || model >= _prefabs.Length || _prefabs[model] == null) continue;
                var vehicle = Instantiate(_prefabs[model], spot.position, spot.rotation);
                if (vehicle.TryGetComponent<Vehicle>(out var v)) v.ServerSetPaint(VehicleColors.Pick(i, model));
                _networkManager.ServerManager.Spawn(vehicle);
            }
        }

        // Editor builder only. `models[i]` is the index into `prefabs` of spot i's vehicle.
        public void Configure(NetworkObject[] prefabs, Transform[] spots, int[] models, bool hasWater, float waterLevel, float roadCellSize, int roadCells, byte[] roadBits)
        {
            _prefabs = prefabs ?? new NetworkObject[0];
            _spots = spots ?? new Transform[0];
            _models = models ?? new int[0];
            _hasWater = hasWater;
            _waterLevel = waterLevel;
            _roadCellSize = roadCellSize;
            _roadCells = roadCells;
            _roadBits = roadBits ?? new byte[0];
        }
    }
}
