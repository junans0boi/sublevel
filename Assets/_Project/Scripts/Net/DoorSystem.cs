using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The doors of the map's buildings (ADR-104, G11): builds a Door in every swing-door opening of every lot, keeps the
    // server's state and carries changes to the clients. Like GlassPanes it needs no network object. Doors are numbered in
    // plan order, so every peer builds the same list from the same seed. Scenes without the generated map get none.
    public class DoorSystem : MonoBehaviour
    {
        public static DoorSystem Instance { get; private set; }

        NetworkManager _network;
        bool _bound;
        Transform _root;
        readonly List<Door> _doors = new List<Door>();
        float[] _xs = new float[0], _ys = new float[0], _zs = new float[0];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public int Count => _doors.Count;
        public Door this[int index] => _doors[index];

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (!_bound || _network == null) return;
            _network.ClientManager.UnregisterBroadcast<DoorChanged>(OnChanged);
            _network.ServerManager.UnregisterBroadcast<DoorToggleRequest>(OnRequest);
            _network.SceneManager.OnClientLoadedStartScenes -= OnClientLoaded;
        }

        public void Bind(NetworkManager network)
        {
            if (_bound || network == null) return;
            _bound = true;
            _network = network;
            _network.ClientManager.RegisterBroadcast<DoorChanged>(OnChanged);
            _network.ServerManager.RegisterBroadcast<DoorToggleRequest>(OnRequest);
            _network.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
            BuildFromMap();
        }

        bool ServerActive => _network != null && _network.IsServerStarted;

        // Same hash as the map builder's, so the plan of lot i is the one the scene was built from.
        static uint Hash(int seed, int index)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u + (uint)index * 40503u + 0x9E3779B9u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return h;
            }
        }

        // The lots stand in the scene as "Lot<nn>_<Kind>" under "Buildings"; their plans come from the world spec again.
        void BuildFromMap()
        {
            var buildings = GameObject.Find("Buildings");
            if (buildings == null) return;
            var spec = LoadSpec();
            if (spec == null) return;
            var plan = WorldGenerator.Generate(spec);
            _root = new GameObject("Doors").transform;
            _root.SetParent(transform, false);
            for (int i = 0; i < plan.Lots.Count; i++)
            {
                var lot = plan.Lots[i];
                var tr = buildings.transform.Find($"Lot{i:00}_{lot.Kind}");
                if (tr == null) continue;
                var building = Buildings.Generate(lot.Kind, lot.SizeX, lot.SizeZ, unchecked((int)Hash(plan.Seed, i)));
                foreach (var opening in building.Doors)
                {
                    // Roll doors stay open: vehicles and crowds use them.
                    if (opening.Width >= 3f) continue;
                    var centre = tr.TransformPoint(new Vector3(opening.X, opening.Y, opening.Z));
                    var facing = tr.rotation * Quaternion.Euler(0f, opening.FacingDeg, 0f);
                    Add(Door.Create(_root, _doors.Count, centre, facing, opening.Width, opening.Height));
                }
            }
        }

        static WorldSpec LoadSpec()
        {
            var text = Resources.Load<TextAsset>("GameData/world");
            if (text != null)
            {
                var spec = JsonUtility.FromJson<WorldSpec>(text.text);
                if (spec != null && spec.Regions != null && spec.Regions.Length > 0) return spec;
            }
            return WorldSpec.CreateDefault();
        }

        // Code-made doors (tests, other scenes) join the same list.
        public Door Add(Door door)
        {
            _doors.Add(door);
            _xs = Grow(_xs, _doors.Count);
            _ys = Grow(_ys, _doors.Count);
            _zs = Grow(_zs, _doors.Count);
            _ys[_doors.Count - 1] = door.transform.position.y;
            _xs[_doors.Count - 1] = door.transform.position.x;
            _zs[_doors.Count - 1] = door.transform.position.z;
            return door;
        }

        static float[] Grow(float[] a, int n)
        {
            if (a.Length >= n) return a;
            var b = new float[System.Math.Max(n, a.Length * 2 + 8)];
            a.CopyTo(b, 0);
            return b;
        }

        // ---- Using a door ----

        // The index of the door F reaches from these eyes, or -1.
        public int Pick(Vector3 eye, Vector3 look) =>
            Doors.Pick(_xs, _ys, _zs, _doors.Count, eye.x, eye.y, eye.z, look.x, look.z, DoorRules.Current);

        // F pressed by the local player: the door in reach opens or shuts. False when there is none.
        public static bool TryInteract(Combatant me, Vector3 eye, Vector3 look)
        {
            var self = Instance;
            if (self == null || me == null) return false;
            int index = self.Pick(eye, look);
            if (index < 0) return false;
            if (me.IsServerInitialized) self.ServerToggle(index, me.transform.position);
            else if (self._network != null) self._network.ClientManager.Broadcast(new DoorToggleRequest { Index = index });
            return true;
        }

        // Server: flips door `index` for someone standing at `from` (it opens away from them).
        public void ServerToggle(int index, Vector3 from)
        {
            if (index < 0 || index >= _doors.Count) return;
            var door = _doors[index];
            int side = Doors.SideOf(from.x, from.z, door.transform.position.x, door.transform.position.z, door.Forward.x, door.Forward.z);
            ServerSet(index, Doors.Toggle(door.State, side));
        }

        public void ServerSet(int index, DoorState state)
        {
            if (index < 0 || index >= _doors.Count) return;
            var door = _doors[index];
            if (door.State == state) return;
            door.Set(state);
            if (ServerActive) _network.ServerManager.Broadcast(new DoorChanged { Index = index, State = (byte)state });
        }

        // Server: a bot at `position` walking along `heading` opens the closed door right ahead of it.
        public static void ServerBotOpen(Vector3 position, Vector3 heading)
        {
            var self = Instance;
            if (self == null) return;
            heading.y = 0f;
            var rules = DoorRules.Current;
            float reach = rules.BotReachMeters * rules.BotReachMeters;
            for (int i = 0; i < self._doors.Count; i++)
            {
                var door = self._doors[i];
                if (door.State != DoorState.Closed) continue;
                var to = door.transform.position - position;
                if (Mathf.Abs(to.y) > 2.5f) continue;
                to.y = 0f;
                if (to.sqrMagnitude > reach) continue;
                // Facing it, or standing at it.
                if (to.sqrMagnitude > 0.25f && Vector3.Dot(heading.normalized, to.normalized) < 0.3f) continue;
                self.ServerToggle(i, position);
            }
        }

        // How many closed doors stand across the straight line a-b (for sounds and the bots' ears).
        public static int ClosedBetween(Vector3 a, Vector3 b)
        {
            var self = Instance;
            if (self == null) return 0;
            int n = 0;
            for (int i = 0; i < self._doors.Count; i++)
            {
                var door = self._doors[i];
                if (door.State != DoorState.Closed) continue;
                var c = door.transform.position;
                if (Mathf.Max(a.y, b.y) < c.y || Mathf.Min(a.y, b.y) > c.y + door.Height) continue;
                var r = door.Right;
                if (Doors.Crosses(a.x, a.z, b.x, b.z, c.x, c.z, r.x, r.z, door.Width * 0.5f)) n++;
            }
            return n;
        }

        // ---- Network ----

        void OnRequest(NetworkConnection connection, DoorToggleRequest request, Channel channel)
        {
            var body = connection != null && connection.FirstObject != null ? connection.FirstObject.GetComponent<Combatant>() : null;
            if (body == null || body.Dead.Value || request.Index < 0 || request.Index >= _doors.Count) return;
            // Close enough to have pressed F on it (a little slack for network delay).
            var d = _doors[request.Index].transform.position - body.transform.position;
            d.y = 0f;
            float reach = DoorRules.Current.ReachMeters + 1.5f;
            if (d.sqrMagnitude > reach * reach) return;
            ServerToggle(request.Index, body.transform.position);
        }

        void OnClientLoaded(NetworkConnection connection, bool asServer)
        {
            if (!asServer || !ServerActive || !connection.IsAuthenticated) return;
            for (int i = 0; i < _doors.Count; i++)
                if (_doors[i].State != DoorState.Closed)
                    _network.ServerManager.Broadcast(connection, new DoorChanged { Index = i, State = (byte)_doors[i].State, Quiet = true });
        }

        void OnChanged(DoorChanged message, Channel channel)
        {
            if (message.Index < 0 || message.Index >= _doors.Count) return;
            _doors[message.Index].Set((DoorState)message.State, message.Quiet);
        }
    }
}
