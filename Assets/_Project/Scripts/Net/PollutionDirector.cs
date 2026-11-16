using System;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // The pollution of a match as the server tells it (ADR-235): the order, the seed and a hash of the front. The
    // client rebuilds the front from the shared map grid and checks the hash; times follow the server clock.
    public struct PollutionSync : IBroadcast
    {
        public int Seed;
        public int[] Order;
        public uint GridHash;
        public float Elapsed;   // seconds of match time when this was sent
    }

    // Runs the pollution of a match (T5/T6, SPEC 3). Next to MatchDirector, like ZoneDirector (which it replaces
    // once the lockdown is removed, T10). Server: builds the match from the map grid and the match seed, applies
    // damage in steps, sends PollutionSync. Client: rebuilds from it. No network object of its own.
    public partial class PollutionDirector : MonoBehaviour
    {
        public static PollutionDirector Instance { get; private set; }
        // The map's grid, its region count and (optionally) loot weights. Set by Game.Gameplay from the world data.
        public static Func<PollutionGrid> GridSource;
        public static int MapRegionCount;
        // Raised on a client (the host too) when the pollution is known / known to differ from the server's.
        public static event Action<PollutionDirector> ClientSynced;
        public static event Action<PollutionDirector> ClientHashMismatch;
        public static PollutionRules RulesOverride;
        // The pollution runs the match instead of the lockdown (T10 flips this on and removes ZoneDirector).
        public static bool AutoStart = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; GridSource = null; MapRegionCount = 0; ClientSynced = null; ClientHashMismatch = null; RulesOverride = null; AutoStart = true; }

        NetworkManager _network;
        bool _bound;
        PollutionMatch _match;
        double _clockOffset;    // match seconds = Time.timeAsDouble + offset
        bool _serverRunning;
        PollutionSync _lastSync;
        readonly Dictionary<int, PollutionExposure> _exposure = new Dictionary<int, PollutionExposure>();
        readonly List<Combatant> _scratch = new List<Combatant>(32);

        // Revive chips (WP 8, ADR-239): where each dropped chip lies and who may use them.
        public readonly ChipBoard Chips = new ChipBoard();
        readonly Dictionary<int, Vector3> _chipAt = new Dictionary<int, Vector3>();
        public bool ChipPosition(int owner, out Vector3 at) => _chipAt.TryGetValue(owner, out at);
        public PollutionMatch Match => _match;
        public bool Ready => _match != null;
        public PollutionRules Rules => RulesOverride ?? PollutionRules.Current;
        public double Now => Time.timeAsDouble + _clockOffset;
        public PollutionState[] States => _match?.States(Now);
        bool ServerActive => _network != null && _network.IsServerStarted;

        void Awake() { Instance = this; }

        void OnEnable()
        {
            Combatant.ServerDied += OnDied;
            Combatant.ServerKnocked += OnKnocked;
        }

        void OnDisable()
        {
            Combatant.ServerDied -= OnDied;
            Combatant.ServerKnocked -= OnKnocked;
        }

        static int TeamKey(Combatant c) => c.HasTeam ? c.TeamId.Value : -(c.ObjectId + 1);

        // A death in a squad drops the owner's chip where they fell; a carrier that dies or goes down drops theirs.
        void OnDied(Combatant c, DamageInfo info)
        {
            if (!_serverRunning || c == null) return;
            Chips.OnCarrierDown(c.ObjectId);
            int others = 0;
            foreach (var o in Combatant.All) if (o != null && o != c && !o.Dead.Value && o.IsTeammateOf(c)) others++;
            var mode = c.HasTeam ? TeamMatch.Mode : TeamMode.Solo;
            if (Chips.OnDeath(c.ObjectId, TeamKey(c), mode, others, Now) != null) _chipAt[c.ObjectId] = c.transform.position;
        }

        void OnKnocked(Combatant c, DamageInfo info)
        {
            if (!_serverRunning || c == null) return;
            Chips.OnCarrierDown(c.ObjectId);
        }

        // Picking a chip up, putting the carried ones into a platform's box. The caller checks the reach.
        public ChipResult ServerPickUpChip(Combatant carrier, int owner) =>
            Chips.PickUp(owner, Now, carrier.ObjectId, TeamKey(carrier), Rules);

        public ChipResult ServerDepositChips(Combatant carrier, int boxRegion) =>
            Chips.DepositAll(carrier.ObjectId, Now, boxRegion, States, Rules);

        // Keys of the match (ADR-170): 6-8, biased toward late regions, the first ones in supply crates (the crates
        // are the supply director's: until it takes them over they lie as loot like the rest).
        void ServerSpawnKeys(PollutionGrid grid, int regionCount, int seed)
        {
            var director = MatchDirector.Instance;
            if (director == null) return;
            var plan = KeyPlan.Plan(seed, _match.Timeline.Order, PollutionGridBuilder.LandAreaWeights(grid, regionCount), Rules);
            var rng = new System.Random(unchecked(seed * 17 + 5));
            var cells = new List<int>();
            foreach (var k in plan)
            {
                cells.Clear();
                for (int i = 0; i < grid.Count; i++) if (grid.IsLand(i) && grid.Region[i] == k.Region) cells.Add(i);
                for (int tries = 0; tries < 12 && cells.Count > 0; tries++)
                {
                    int cell = cells[rng.Next(cells.Count)];
                    float x = grid.OriginX + ((cell % grid.Width) + 0.5f) * grid.CellMeters, z = grid.OriginZ + ((cell / grid.Width) + 0.5f) * grid.CellMeters;
                    if (!Physics.Raycast(new Vector3(x, 400f, z), Vector3.down, out var hit, 800f)) continue;
                    director.ServerSpawnLoot(new LootEntry { Kind = KeyHolder.KeyLootKind, Amount = 1 }, hit.point + Vector3.up * 0.4f);
                    break;
                }
            }
        }

        void Start() { TryBind(); Started?.Invoke(this); }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (!_bound || _network == null) return;
            _network.ClientManager.UnregisterBroadcast<PollutionSync>(OnSync);
            _network.SceneManager.OnClientLoadedStartScenes -= OnClientLoaded;
        }

        public void Bind(NetworkManager network)
        {
            if (_bound || network == null) return;
            _bound = true;
            _network = network;
            _network.ClientManager.RegisterBroadcast<PollutionSync>(OnSync);
            _network.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
        }

        void TryBind()
        {
            if (_bound) return;
            var all = NetworkManager.Instances;
            if (all.Count > 0) Bind(all[0]);
        }

        // ---- Server ----

        // Begin the pollution clock now (the match clock). The seed decides the order; the grid is the map's.
        public bool ServerStart(int seed, PollutionGrid grid = null, int regionCount = 0)
        {
            grid = grid ?? GridSource?.Invoke();
            if (grid == null) return false;
            int n = regionCount > 0 ? regionCount : MapRegionCount;
            if (n <= 0) return false;
            int forced = -1;
            if (!string.IsNullOrEmpty(FinalRegionOverride) && EnsureGeometry())
            {
                forced = _geo.RegionIndexOf(FinalRegionOverride);
                if (forced < 0 && int.TryParse(FinalRegionOverride, out int idx) && idx >= 0 && idx < n) forced = idx;
            }
            _match = new PollutionMatch(grid, PollutionOrder.Plan(n, PollutionGridBuilder.Adjacency(grid, n), seed, forced), seed, Rules);
            _clockOffset = -Time.timeAsDouble;
            _exposure.Clear();
            _chipAt.Clear();
            _serverRunning = true;
            if (GetComponent<KeyElevatorNet>() == null) gameObject.AddComponent<KeyElevatorNet>();
            ServerSpawnKeys(grid, n, seed);
            _lastSync = new PollutionSync { Seed = seed, Order = _match.Snapshot.Order, GridHash = _match.GridHash };
            if (ServerActive) _network.ServerManager.Broadcast(Stamp(_lastSync));
            return true;
        }

        PollutionSync Stamp(PollutionSync s) { s.Elapsed = (float)Now; return s; }

        void OnClientLoaded(NetworkConnection connection, bool asServer)
        {
            if (!asServer || !_serverRunning || !ServerActive) return;
            _network.ServerManager.Broadcast(connection, Stamp(_lastSync));
        }

        // Gas protection (T6): the inventory calls this when the item is used.
        public void ServerUseFilter(int combatantId) { if (_match != null) Exposure(combatantId).UseFilter(Now, Rules); }
        public void ServerUseAntidote(int combatantId) { if (_match != null) Exposure(combatantId).UseAntidote(Now, Rules); }
        public PollutionExposure ExposureOf(int combatantId) => _exposure.TryGetValue(combatantId, out var e) ? e : null;

        PollutionExposure Exposure(int id)
        {
            if (!_exposure.TryGetValue(id, out var e)) _exposure[id] = e = new PollutionExposure();
            return e;
        }

        void Update()
        {
            if (!_bound) TryBind();
            CompatTick();
            if (!_serverRunning && AutoStart && ServerActive && MatchDirector.Instance != null)
                ServerStart(MatchDirector.Instance.LockdownSeed);
            if (!_serverRunning || !ServerActive) return;
            double now = Now;
            Chips.Tick(now, _match.States(now), Rules);
            _scratch.Clear();
            _scratch.AddRange(Combatant.All);
            foreach (var c in _scratch)
            {
                if (c == null || !c.IsServerInitialized) continue;
                int id = c.ObjectId;
                if (c.Dead.Value) { _exposure.Remove(id); continue; }
                var p = c.transform.position;
                float dmg = Exposure(id).Step(_match, p.x, p.z, now);
                if (dmg > 0f)
                    c.ServerTakeDamage(new DamageInfo { Amount = dmg, Attacker = null, Zone = HitZone.Body, WeaponId = 0, Cause = DamageCause.Zone });
            }
            _scratch.Clear();
        }

        // ---- Client ----

        void OnSync(PollutionSync s, FishNet.Transporting.Channel channel)
        {
            if (ServerActive && _serverRunning) { _clockOffset = s.Elapsed - Time.timeAsDouble; return; } // the host already has it
            var grid = GridSource?.Invoke();
            if (grid == null || s.Order == null) return;
            _match = PollutionMatch.FromSnapshot(grid, new PollutionSnapshot { Seed = s.Seed, Order = s.Order, GridHash = s.GridHash }, Rules, out bool ok);
            _clockOffset = s.Elapsed - Time.timeAsDouble;
            ClientSynced?.Invoke(this);
            if (!ok) { Debug.LogWarning("[pollution] front hash differs from the server's"); ClientHashMismatch?.Invoke(this); }
        }
    }
}
