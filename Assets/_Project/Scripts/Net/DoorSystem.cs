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
        // Doors do not move: a hash over them lets a bot look at the few near it instead of all (ADR-290).
        readonly PointGrid _grid = new PointGrid(4f);
        readonly List<int> _near = new List<int>();
        int _gridCount = -1;
        // Doors a person walks in by, with the lot they belong to (the building loot index lists them per building).
        public readonly List<(int Lot, Vector3 Position)> ExteriorDoors = new List<(int, Vector3)>();
        // The world plan the doors were made from (null without the generated map).
        public WorldPlan Plan { get; private set; }

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
            _network.ClientManager.UnregisterBroadcast<DoorDamaged>(OnDamaged);
            _network.ServerManager.UnregisterBroadcast<DoorRamRequest>(OnRam);
            _network.ServerManager.UnregisterBroadcast<DoorToggleRequest>(OnRequest);
            _network.SceneManager.OnClientLoadedStartScenes -= OnClientLoaded;
        }

        public void Bind(NetworkManager network)
        {
            if (_bound || network == null) return;
            _bound = true;
            _network = network;
            _network.ClientManager.RegisterBroadcast<DoorChanged>(OnChanged);
            _network.ClientManager.RegisterBroadcast<DoorDamaged>(OnDamaged);
            _network.ServerManager.RegisterBroadcast<DoorRamRequest>(OnRam);
            _network.ServerManager.RegisterBroadcast<DoorToggleRequest>(OnRequest);
            _network.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
            BuildFromMap();
            Debug.Log($"[Doors] {_doors.Count} doors built");
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
            var plan = Plan = WorldGenerator.Generate(spec);
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
                    if (opening.Exterior) ExteriorDoors.Add((i, centre));
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

        Vector3 At(int index) => new Vector3(_xs[index], _ys[index], _zs[index]);

        void EnsureGrid()
        {
            if (_gridCount == _doors.Count) return;
            _grid.Rebuild(_xs, _zs, _doors.Count);
            _gridCount = _doors.Count;
        }

        // Every peer builds its own door list; another platform's float rounding (the ARM64 server against a Mac client)
        // can make one door more or less and shift the numbers after it. So a message names the door by number and place,
        // and the receiver goes by the place when the two disagree. -1: no door of ours stands there.
        public int Resolve(int index, Vector3 at)
        {
            if (index >= 0 && index < _doors.Count && (At(index) - at).sqrMagnitude < 0.25f) return index;
            EnsureGrid();
            _grid.Query(at.x, at.z, 1f, _near);
            int best = -1;
            float bestSqr = 1f;
            foreach (int i in _near)
            {
                float sqr = (At(i) - at).sqrMagnitude;
                if (sqr < bestSqr) { best = i; bestSqr = sqr; }
            }
            return best;
        }

        // ---- Using a door ----

        // The index of the door F reaches from these eyes, or -1.
        public int Pick(Vector3 eye, Vector3 look) =>
            Doors.Pick(_xs, _ys, _zs, _doors.Count, eye.x, eye.y, eye.z, look.x, look.z, DoorRules.Current);

        // How far off the middle of the view (flat degrees) the door F would reach lies; float.MaxValue when there is none.
        // F weighs this against a pickup's bearing (Interact.PickupBeatsDoor, ADR-276).
        public static float Bearing(Vector3 eye, Vector3 look)
        {
            var self = Instance;
            if (self == null) return float.MaxValue;
            int index = self.Pick(eye, look);
            return index < 0 ? float.MaxValue : Interact.BearingDegrees(eye.x, eye.z, look.x, look.z, self._xs[index], self._zs[index]);
        }

        // F pressed by the local player: the door in reach opens or shuts. False when there is none.
        public static bool TryInteract(Combatant me, Vector3 eye, Vector3 look)
        {
            var self = Instance;
            if (self == null || me == null) return false;
            int index = self.Pick(eye, look);
            if (index < 0) return false;
            if (me.IsServerInitialized) self.ServerToggle(index, me.transform.position);
            else
            {
                // Through the connection that owns this body (a test process runs several).
                var network = me.NetworkManager != null ? me.NetworkManager : self._network;
                if (network != null) network.ClientManager.Broadcast(new DoorToggleRequest { Index = index, At = self.At(index) });
            }
            return true;
        }

        // Server: flips door `index` for someone standing at `from` (it opens away from them).
        public void ServerToggle(int index, Vector3 from)
        {
            if (index < 0 || index >= _doors.Count || _doors[index].State == DoorState.Broken) return;
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
            if (ServerActive) _network.ServerManager.Broadcast(new DoorChanged { Index = index, At = At(index), State = (byte)state });
        }

        // Server: the door takes `amount` damage (ADR-147); true when that broke it. Every client is told.
        public static bool ServerDamage(Door door, float amount, int sourceId = -1)
        {
            var self = Instance;
            if (self == null || door == null || door.State == DoorState.Broken || amount <= 0f) return false;
            int index = self._doors.IndexOf(door);
            if (index < 0) return false;
            float hp = door.Hp - amount;
            if (hp <= 0f)
            {
                self.ServerSet(index, DoorState.Broken);
                // Bots hear it go (a wooden crash, glass-loud).
                var at = door.transform.position;
                BotNoise.Emit(at.x, at.y, at.z, BotRules.Current.GlassHearingRadius, sourceId, NoiseKind.Other);
                return true;
            }
            door.SetHp(hp);
            if (self.ServerActive) self._network.ServerManager.Broadcast(new DoorDamaged { Index = index, At = self.At(index), Hp = (ushort)Mathf.CeilToInt(hp) });
            return false;
        }

        // Client: my car hit door `door` at this closing speed; the server decides. (The server's own car calls ServerDamage.)
        public static void ClientRam(Door door, float speed)
        {
            var self = Instance;
            if (self == null || self._network == null || door == null) return;
            int index = self._doors.IndexOf(door);
            if (index >= 0) self._network.ClientManager.Broadcast(new DoorRamRequest { Index = index, At = self.At(index), Speed = speed });
        }

        // Server: a bot at `position` walking along `heading` opens the closed door right ahead of it.
        public static void ServerBotOpen(Vector3 position, Vector3 heading)
        {
            var self = Instance;
            if (self == null) return;
            heading.y = 0f;
            var rules = DoorRules.Current;
            float reach = rules.BotReachMeters * rules.BotReachMeters;
            self.EnsureGrid();
            self._grid.Query(position.x, position.z, rules.BotReachMeters, self._near);
            // Walking the hits in door order, as the scan of the whole list did.
            for (int n = 0; n < self._near.Count; n++)
            {
                int i = self._near[n];
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
            int index = Resolve(request.Index, request.At);
            if (body == null || body.Dead.Value || index < 0) return;
            // Close enough to have pressed F on it (a little slack for network delay).
            var d = _doors[index].transform.position - body.transform.position;
            d.y = 0f;
            float reach = DoorRules.Current.ReachMeters + 1.5f;
            if (d.sqrMagnitude > reach * reach) return;
            ServerToggle(index, body.transform.position);
        }

        void OnRam(NetworkConnection connection, DoorRamRequest request, Channel channel)
        {
            var body = connection != null && connection.FirstObject != null ? connection.FirstObject.GetComponent<Combatant>() : null;
            int index = Resolve(request.Index, request.At);
            if (body == null || body.Dead.Value || index < 0 || float.IsNaN(request.Speed)) return;
            // The sender's car is at the door: within a car's length of the sender's body.
            var d = _doors[index].transform.position - body.transform.position;
            if (d.sqrMagnitude > 8f * 8f) return;
            ServerDamage(_doors[index], Doors.RamDamage(Mathf.Min(request.Speed, 40f), DoorRules.Current));
        }

        void OnClientLoaded(NetworkConnection connection, bool asServer)
        {
            if (!asServer || !ServerActive || !connection.IsAuthenticated) return;
            for (int i = 0; i < _doors.Count; i++)
            {
                if (_doors[i].State != DoorState.Closed)
                    _network.ServerManager.Broadcast(connection, new DoorChanged { Index = i, At = At(i), State = (byte)_doors[i].State, Quiet = true });
                else if (_doors[i].Hp < DoorRules.Current.MaxHp)
                    _network.ServerManager.Broadcast(connection, new DoorDamaged { Index = i, At = At(i), Hp = (ushort)Mathf.CeilToInt(_doors[i].Hp), Quiet = true });
            }
        }

        void OnDamaged(DoorDamaged message, Channel channel)
        {
            if (ServerActive) return; // the host's own door was set already
            int index = Resolve(message.Index, message.At);
            if (index >= 0) _doors[index].SetHp(message.Hp, message.Quiet);
        }

        void OnChanged(DoorChanged message, Channel channel)
        {
            int index = Resolve(message.Index, message.At);
            if (index >= 0) _doors[index].Set((DoorState)message.State, message.Quiet);
        }
    }
}
