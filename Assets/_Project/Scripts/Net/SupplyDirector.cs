using System;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Server -> clients: a crate came down (also sent to a client that finishes loading, Ago = how long ago, ADR-131).
    public struct SupplyDropped : IBroadcast
    {
        public int Id, Region, Seed;
        public Vector3 Position;
        public float Ago;
    }

    // Server -> clients: the crate was opened (its items are ordinary loot now).
    public struct SupplyOpened : IBroadcast { public int Id; }

    // Client -> server: the player held F next to crate `Id`.
    public struct SupplyOpenRequest : IBroadcast { public int Id; }

    // One supply crate that came down (#144).
    public class SupplyCrate
    {
        public int Id;
        public int Region;
        public Vector3 Position;   // where it lands, on the ground
        public float DroppedAt;    // Time.time
        public float LandsAt;      // Time.time: before this it is still coming down and cannot be opened
        public int Seed;           // decides the contents
        public float BotDwell;     // seconds a bot has been standing next to it
    }

    // Server: at the start of each lockdown stage the rules ask for (SupplyRules), an elevator in a still-safe region
    // sends a crate down (ADR-113). Opening it lays its loot out as ordinary pickups, like a death crate.
    // The crate is plain server state plus the static events below, not a network object; remote clients get the same
    // list through broadcasts (SupplyDropped / SupplyOpened, a late joiner is sent the crates still there, ADR-131) and
    // ask to open one with SupplyOpenRequest, so their views and map follow it the way the host's do.
    // Installs itself under the ZoneDirector, so nothing in the scenes refers to it.
    public class SupplyDirector : MonoBehaviour
    {
        const float GroundProbeUp = 3f, GroundProbeDown = 60f, ExitOffset = 3f;

        public static SupplyDirector Instance { get; private set; }
        // Server side, for the views (host) and the bots' places to go.
        public static event Action<SupplyCrate> Dropped;
        public static event Action<SupplyCrate> Opened;

        readonly List<SupplyCrate> _crates = new List<SupplyCrate>();
        int _nextId = 1;
        static readonly List<ZoneState> States = new List<ZoneState>();

        public IReadOnlyList<SupplyCrate> Crates => _crates;
        public int DropCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            ZoneDirector.Started -= Install;
            ZoneDirector.Started += Install;
        }

        static void Install(ZoneDirector director)
        {
            if (director != null && director.GetComponent<SupplyDirector>() == null) director.gameObject.AddComponent<SupplyDirector>();
        }

        NetworkManager _network;
        bool _bound;
        bool ServerActive => _network != null && _network.IsServerStarted;

        void Awake() => Instance = this;
        void OnEnable() => ZoneDirector.ServerRegionStateChanged += OnRegionState;
        void OnDisable() => ZoneDirector.ServerRegionStateChanged -= OnRegionState;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (!_bound || _network == null) return;
            _network.ClientManager.UnregisterBroadcast<SupplyDropped>(OnClientDropped);
            _network.ClientManager.UnregisterBroadcast<SupplyOpened>(OnClientOpened);
            _network.ServerManager.UnregisterBroadcast<SupplyOpenRequest>(OnOpenRequest);
            _network.SceneManager.OnClientLoadedStartScenes -= OnClientLoaded;
        }

        // Called on its own as soon as a NetworkManager exists; tests with their own managers call it directly.
        public void Bind(NetworkManager network)
        {
            if (_bound || network == null) return;
            _bound = true;
            _network = network;
            _network.ClientManager.RegisterBroadcast<SupplyDropped>(OnClientDropped);
            _network.ClientManager.RegisterBroadcast<SupplyOpened>(OnClientOpened);
            _network.ServerManager.RegisterBroadcast<SupplyOpenRequest>(OnOpenRequest);
            _network.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
        }

        void Start() => TryBind();

        void TryBind()
        {
            if (_bound) return;
            var all = NetworkManager.Instances;
            if (all.Count > 0) Bind(all[0]);
        }

        // F held next to a landed crate: the server opens it itself, a client asks the server.
        public void RequestOpen(SupplyCrate crate, Combatant by)
        {
            if (crate == null || by == null) return;
            if (by.IsServerInitialized) ServerTryOpen(crate, by);
            else if (_network != null && _network.IsClientStarted) _network.ClientManager.Broadcast(new SupplyOpenRequest { Id = crate.Id });
        }

        void OnOpenRequest(NetworkConnection connection, SupplyOpenRequest request, Channel channel)
        {
            if (!ServerActive || connection == null || connection.FirstObject == null) return;
            var by = connection.FirstObject.GetComponent<Combatant>();
            foreach (var crate in _crates)
                if (crate.Id == request.Id) { ServerTryOpen(crate, by); return; }
        }

        void OnClientLoaded(NetworkConnection connection, bool asServer)
        {
            if (!asServer || !ServerActive || connection == null || !connection.IsAuthenticated) return;
            foreach (var c in _crates)
                _network.ServerManager.Broadcast(connection, new SupplyDropped { Id = c.Id, Region = c.Region, Seed = c.Seed, Position = c.Position, Ago = Time.time - c.DroppedAt });
        }

        // A remote client: the host's own director already holds the crate.
        void OnClientDropped(SupplyDropped m, Channel channel)
        {
            if (ServerActive) return;
            foreach (var c in _crates) if (c.Id == m.Id) return;
            float droppedAt = Time.time - m.Ago;
            var crate = new SupplyCrate
            {
                Id = m.Id, Region = m.Region, Position = m.Position, Seed = m.Seed,
                DroppedAt = droppedAt, LandsAt = droppedAt + SupplyRules.Current.DescentSeconds,
            };
            _crates.Add(crate);
            DropCount++;
            Dropped?.Invoke(crate);
        }

        void OnClientOpened(SupplyOpened m, Channel channel)
        {
            if (ServerActive) return;
            for (int i = 0; i < _crates.Count; i++)
            {
                if (_crates[i].Id != m.Id) continue;
                var crate = _crates[i];
                _crates.RemoveAt(i);
                Opened?.Invoke(crate);
                return;
            }
        }

        void OnRegionState(int region, ZoneState state)
        {
            var zones = ZoneDirector.Instance;
            if (state != ZoneState.Lockdown || zones == null || zones.ServerPlan == null) return;
            int stage = zones.ServerPlan.ClosingIndexOf(region) + 1; // 1-based; 0 = not in the closing order
            if (stage < 1 || !SupplyPlan.DropAt(stage, SupplyRules.Current)) return;
            ServerDrop(stage);
        }

        // Server: sends a crate down in a region that is still safe. Null when there is none (or no spot).
        public SupplyCrate ServerDrop(int stage)
        {
            var zones = ZoneDirector.Instance;
            if (zones == null) return null;
            int matchSeed = MatchDirector.Instance != null ? MatchDirector.Instance.LockdownSeed : 1;
            int seed = unchecked(matchSeed * 131 + stage * 17 + 3);
            States.Clear();
            for (int r = 0; r < zones.RegionCount; r++) States.Add(zones.StateOf(r));
            int region = SupplyPlan.PickRegion(States, new System.Random(seed));
            if (region < 0) return null;
            var rules = SupplyRules.Current;
            var crate = new SupplyCrate
            {
                Id = _nextId++, Region = region, Position = LandingSpot(zones, region), DroppedAt = Time.time,
                LandsAt = Time.time + rules.DescentSeconds, Seed = seed,
            };
            _crates.Add(crate);
            DropCount++;
            Dropped?.Invoke(crate);
            if (ServerActive) _network.ServerManager.Broadcast(new SupplyDropped { Id = crate.Id, Region = region, Seed = seed, Position = crate.Position, Ago = 0f });
            return crate;
        }

        // In front of the region's elevator; the middle of the region when it has none (test scenes).
        static Vector3 LandingSpot(ZoneDirector zones, int region)
        {
            var from = zones.RegionCentre(region);
            var doors = ElevatorDoors.Instance;
            if (doors != null)
                foreach (var site in doors.Sites)
                {
                    if (site.Region != region) continue;
                    float yaw = site.YawDeg * Mathf.Deg2Rad;
                    from = site.Spawn + new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * ExitOffset;
                    break;
                }
            if (Physics.Raycast(from + Vector3.up * GroundProbeUp, Vector3.down, out var hit, GroundProbeUp + GroundProbeDown,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) from = hit.point;
            return from;
        }

        public bool InReach(SupplyCrate crate, Vector3 at)
        {
            float dx = at.x - crate.Position.x, dz = at.z - crate.Position.z;
            float reach = SupplyRules.Current.ReachMeters;
            return dx * dx + dz * dz <= reach * reach && Mathf.Abs(at.y - crate.Position.y) <= 3f;
        }

        public SupplyCrate NearestOpenable(Vector3 at)
        {
            foreach (var c in _crates) if (Time.time >= c.LandsAt && InReach(c, at)) return c;
            return null;
        }

        // Server: opens the crate for `by`: its items lie in a ring around it and it is gone. False when it
        // has not landed, `by` is out of reach or dead, or there is nowhere to put the items.
        public bool ServerTryOpen(SupplyCrate crate, Combatant by)
        {
            if (crate == null || !_crates.Contains(crate) || Time.time < crate.LandsAt) return false;
            if (by == null || by.Dead.Value || !InReach(crate, by.transform.position)) return false;
            var director = MatchDirector.Instance;
            if (director == null) return false;
            var rules = SupplyRules.Current;
            var items = SupplyPlan.Contents(GameData.SupplyLoot, rules.ItemCount, crate.Seed);
            for (int i = 0; i < items.Count; i++)
            {
                LootScatter.RingOffset(i, items.Count, rules.RingRadius, out float x, out float z);
                director.ServerSpawnLoot(items[i], crate.Position + new Vector3(x, 0.3f, z));
            }
            _crates.Remove(crate);
            Opened?.Invoke(crate);
            if (ServerActive) _network.ServerManager.Broadcast(new SupplyOpened { Id = crate.Id });
            return true;
        }

        // Bots: one standing next to a landed crate long enough opens it (they walk there: BotController.SupplyPoints).
        void Update()
        {
            if (_crates.Count == 0) return;
            float dt = Time.deltaTime;
            var rules = SupplyRules.Current;
            for (int i = _crates.Count - 1; i >= 0; i--)
            {
                var crate = _crates[i];
                if (Time.time < crate.LandsAt) continue;
                Combatant near = null;
                foreach (var c in Combatant.All)
                    if (c != null && !c.IsPlayer && c.IsServerInitialized && !c.Dead.Value && InReach(crate, c.transform.position)) { near = c; break; }
                crate.BotDwell = near != null ? crate.BotDwell + dt : 0f;
                if (near != null && crate.BotDwell >= rules.OpenSeconds) ServerTryOpen(crate, near);
            }
        }
    }
}
