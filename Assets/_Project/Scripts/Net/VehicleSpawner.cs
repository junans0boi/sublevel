using FishNet.Managing;
using FishNet.Object;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Puts the match's vehicles out when the server starts (ADR-065 (5)): one per parking spot, of the type given for
    // that spot (VehicleKind index into the prefab list). Also holds what vehicles need to know about the map: the sea
    // level (they stop in water deeper than DrownDepth) and the road map (open terrain off the roads is slower).
    // Lives on the match object next to the NetworkManager; the scene builders fill it (VehicleBuilder).
    public class VehicleSpawner : MonoBehaviour
    {
        // Indexed by VehicleKind: car, motorcycle.
        [SerializeField] NetworkObject[] _prefabs = new NetworkObject[0];
        [SerializeField] Transform[] _spots = new Transform[0];
        [SerializeField] int[] _kinds = new int[0];
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
        public VehicleKind KindAt(int spot) => _kinds != null && spot >= 0 && spot < _kinds.Length ? (VehicleKind)_kinds[spot] : VehicleKind.Car;

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
                int kind = (int)KindAt(i);
                if (spot == null || _prefabs == null || kind >= _prefabs.Length || _prefabs[kind] == null) continue;
                var vehicle = Instantiate(_prefabs[kind], spot.position, spot.rotation);
                _networkManager.ServerManager.Spawn(vehicle);
            }
        }

        // Editor builder only. `prefabs` by VehicleKind; `kinds[i]` is the VehicleKind of spot i.
        public void Configure(NetworkObject[] prefabs, Transform[] spots, int[] kinds, bool hasWater, float waterLevel, float roadCellSize, int roadCells, byte[] roadBits)
        {
            _prefabs = prefabs ?? new NetworkObject[0];
            _spots = spots ?? new Transform[0];
            _kinds = kinds ?? new int[0];
            _hasWater = hasWater;
            _waterLevel = waterLevel;
            _roadCellSize = roadCellSize;
            _roadCells = roadCells;
            _roadBits = roadBits ?? new byte[0];
        }
    }
}
