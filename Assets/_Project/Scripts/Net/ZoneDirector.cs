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
    // A change in the lockdown, sent to every client (and to each one that finishes loading, for what already happened).
    public struct ZoneUpdate : IBroadcast
    {
        public const byte KindRegion = 0, KindFinalRevealed = 1, KindSubZone = 2, KindPassOpen = 3, KindReset = 4;

        public byte Kind;
        public int Region;      // KindRegion / KindSubZone: the region; KindFinalRevealed: the final region
        public int Index;       // KindSubZone: local sub-zone index; KindPassOpen: pass index
        public byte State;      // ZoneState
        public float Remaining; // seconds until the state changes next (or the pass closes); negative: not at all
    }

    // A missile on its way (Impact false: shows a marker for Delay seconds) or landed (Impact true).
    public struct ZoneBlast : IBroadcast
    {
        public Vector3 Position;
        public float Delay;
        public float Radius;
        public bool Impact;
    }

    // A warning marker a client can draw on the ground until ImpactAt (Time.time).
    public struct BlastMarker
    {
        public Vector3 Position;
        public float Radius;
        public float ImpactAt;
    }

    // Runs the region lockdown of a match (M3): the closing order and timeline come from Lockdown.Plan; this applies
    // them with the match clock. The server decides, applies radiation and missiles and tells clients through
    // broadcasts (like MatchState: no network object of its own). The host reads the same state it writes.
    // Place one next to MatchDirector. The map comes from GeometrySource (Game.Gameplay hooks it up from WorldInfo).
    public class ZoneDirector : MonoBehaviour
    {
        const float RegionSampleHalfSize = 330f;     // blast points are drawn from a box this wide around a region's centre
        const float GroundProbeHeight = 400f;
        const float GroundProbeLength = 800f;
        const float WaterMargin = 1.5f;              // ground lower than this under the sea level is open water
        const float ExitReach = 10f;                 // metres past the pass an escaping bot aims for
        const float MarkerLinger = 0.3f;

        [SerializeField] int _seed;                  // 0: derived from the match seed
        [SerializeField] int _keysPerRegion = -1;    // key pickups placed per region at the start; -1: LockdownRules.KeysPerRegion

        public static ZoneDirector Instance { get; private set; }
        // Reads the map's geometry (set by Game.Gameplay from WorldInfo before any scene loads). Kept across sessions.
        public static Func<ZoneGeometry> GeometrySource;
        // Raised once per scene when the director exists (screens install here). Keeps its subscribers.
        public static event Action<ZoneDirector> Started;

        // Server: a region entered a new state / the final region was named.
        public static event Action<int, ZoneState> ServerRegionStateChanged;
        public static event Action<int> ServerFinalRevealed;
        // Client (the host too): the same, as clients see it. Static: subscribers unsubscribe in OnDestroy.
        public static event Action<int, ZoneState> ClientRegionStateChanged;
        public static event Action<int> ClientFinalRevealed;
        public static event Action<int, int, ZoneState> ClientSubZoneStateChanged; // region, local sub-zone, state
        public static event Action<Vector3, float, float> ClientBlastIncoming;      // position, seconds to impact, radius
        public static event Action<Vector3, float> ClientBlastImpact;                // position, radius
        public static event Action<int, float> ClientPassOpened;                     // pass, seconds it stays open

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            ServerRegionStateChanged = null;
            ServerFinalRevealed = null;
            ClientRegionStateChanged = null;
            ClientFinalRevealed = null;
            ClientSubZoneStateChanged = null;
            ClientBlastIncoming = null;
            ClientBlastImpact = null;
            ClientPassOpened = null;
            RulesOverride = null;
        }

        struct PendingBlast
        {
            public Vector3 Position;
            public double At;
            public float Radius;
        }

        LockdownRules _rules;
        NetworkManager _network;
        bool _bound;
        ZoneGeometry _geo;
        LockdownPlan _plan;
        int _planSeed;
        bool _serverReady;
        double _startTime;
        int _nextEvent;
        float _radiationTimer;
        System.Random _rng = new System.Random(1);

        // What this process knows, written by the server when it decides and by the client handlers when told.
        ZoneState[] _state = new ZoneState[0], _sub = new ZoneState[0];
        float[] _stateEnd = new float[0], _subEnd = new float[0], _passOpenEnd = new float[0]; // Time.time, negative = none
        float[] _nextBlast = new float[0], _nextSubBlast = new float[0];                      // server clock
        bool _revealed;
        int _final = -1;

        readonly List<PendingBlast> _pending = new List<PendingBlast>(16);
        readonly List<BlastMarker> _markers = new List<BlastMarker>(16);
        readonly List<Combatant> _scratch = new List<Combatant>(32);
        readonly Dictionary<int, float> _exposure = new Dictionary<int, float>();

        // ---- Read-only state for everyone ----

        public LockdownRules Rules => _rules ?? LockdownRules.Current;
        public ZoneGeometry Geometry { get { EnsureGeometry(); return _geo; } }
        public int RegionCount => EnsureGeometry() ? _geo.RegionCount : 0;
        // The server's secret plan (null before it started and on clients).
        public LockdownPlan ServerPlan => _plan;
        public bool ServerReady => _serverReady;
        // Seconds since the lockdown clock started (server).
        public float ServerElapsed => _serverReady ? (float)(Time.timeAsDouble - _startTime) : 0f;
        public bool FinalRegionRevealed => _revealed;
        // The final region once it has been named, otherwise -1.
        public int FinalRegion => _revealed ? _final : -1;
        public IReadOnlyList<BlastMarker> Markers => _markers;

        bool ClientActive => _network != null && _network.IsClientStarted;
        bool ServerActive => _network != null && _network.IsServerStarted;

        public ZoneState StateOf(int region) => region >= 0 && region < _state.Length ? _state[region] : ZoneState.Safe;

        public ZoneState StateOf(string regionId)
        {
            EnsureGeometry();
            return _geo != null ? StateOf(_geo.RegionIndexOf(regionId)) : ZoneState.Safe;
        }

        public string RegionNameOf(int region) => EnsureGeometry() && region >= 0 && region < _geo.RegionCount ? _geo.RegionName[region] : "";
        public string RegionIdOf(int region) => EnsureGeometry() && region >= 0 && region < _geo.RegionCount ? _geo.RegionId[region] : "";

        public int RegionIndexAt(Vector3 position) => EnsureGeometry() ? _geo.RegionAt(position.x, position.z) : -1;

        // "A".."D", or "" off the island.
        public string RegionIdAt(Vector3 position)
        {
            int r = RegionIndexAt(position);
            return r < 0 ? "" : _geo.RegionId[r];
        }

        // The state of the region a point is in (Safe off the island).
        public ZoneState StateAt(Vector3 position) => StateOf(RegionIndexAt(position));

        // Seconds until the state of a region changes; -1 for a region that is still safe, closed or the final one.
        public float SecondsLeft(int region)
        {
            if (region < 0 || region >= _state.Length || _state[region] == ZoneState.Safe) return -1f;
            float end = _stateEnd[region];
            return end < 0f ? -1f : Mathf.Max(0f, end - Time.time);
        }

        // Seconds until the next change. On the server this is the real schedule, including what players have not
        // been told yet; on clients it counts only announced changes. -1 when nothing is coming.
        public float SecondsToNextEvent
        {
            get
            {
                if (_serverReady && ServerActive)
                {
                    float now = ServerElapsed;
                    return Lockdown.NextEventAfter(_plan, now, out var next) ? Mathf.Max(0f, next.Time - now) : -1f;
                }
                float soonest = float.MaxValue;
                for (int i = 0; i < _stateEnd.Length; i++)
                    if (_state[i] != ZoneState.Safe && _stateEnd[i] >= 0f) soonest = Mathf.Min(soonest, _stateEnd[i]);
                for (int i = 0; i < _subEnd.Length; i++)
                    if (_sub[i] != ZoneState.Safe && _subEnd[i] >= 0f) soonest = Mathf.Min(soonest, _subEnd[i]);
                return soonest == float.MaxValue ? -1f : Mathf.Max(0f, soonest - Time.time);
            }
        }

        public ZoneState SubZoneState(int globalSubZone) => globalSubZone >= 0 && globalSubZone < _sub.Length ? _sub[globalSubZone] : ZoneState.Safe;

        // ---- Passes ----

        // A pass is locked while either region it joins is in lockdown or beyond.
        public bool IsPassLocked(int pass)
        {
            if (!EnsureGeometry() || pass < 0 || pass >= _geo.PassCount) return false;
            return !Lockdown.IsPassable(StateOf(_geo.PassA[pass])) || !Lockdown.IsPassable(StateOf(_geo.PassB[pass]));
        }

        // A key was used on it and the time has not run out.
        public bool IsPassOpen(int pass) => pass >= 0 && pass < _passOpenEnd.Length && _passOpenEnd[pass] > Time.time;

        // What the barrier does right now.
        public bool IsPassBlocking(int pass) => IsPassLocked(pass) && !IsPassOpen(pass);

        public float BarrierHalfWidth(int pass) => EnsureGeometry() && pass >= 0 && pass < _geo.PassCount ? _geo.PassWidth[pass] * 0.5f + WorldPass.Shoulder : 0f;

        // Distance in the ground plane from a point to the barrier of a pass (0 on it).
        public float PassDistance(int pass, Vector3 p)
        {
            if (!EnsureGeometry() || pass < 0 || pass >= _geo.PassCount) return float.MaxValue;
            float yaw = _geo.PassYawDeg[pass] * Mathf.Deg2Rad;
            float fx = Mathf.Sin(yaw), fz = Mathf.Cos(yaw);
            float dx = p.x - _geo.PassX[pass], dz = p.z - _geo.PassZ[pass];
            float along = dx * fx + dz * fz;
            float lateral = dx * fz - dz * fx;
            float over = Mathf.Max(0f, Mathf.Abs(lateral) - BarrierHalfWidth(pass));
            return Mathf.Sqrt(over * over + along * along);
        }

        // The pass to head for from a point: of the passes of its region, one that leads to a region still passable
        // (nearest first), else simply the nearest.
        public bool TryNearestExit(Vector3 from, out int pass, out Vector3 position, out int toRegion)
        {
            pass = -1; position = default; toRegion = -1;
            int region = RegionIndexAt(from);
            if (region < 0) return false;
            bool bestOpen = false;
            float bestD = float.MaxValue;
            for (int p = 0; p < _geo.PassCount; p++)
            {
                int other = _geo.PassA[p] == region ? _geo.PassB[p] : _geo.PassB[p] == region ? _geo.PassA[p] : -1;
                if (other < 0) continue;
                bool open = Lockdown.IsPassable(StateOf(other));
                float dx = from.x - _geo.PassX[p], dz = from.z - _geo.PassZ[p];
                float d = dx * dx + dz * dz;
                if ((open && !bestOpen) || (open == bestOpen && d < bestD))
                {
                    pass = p; toRegion = other; bestOpen = open; bestD = d;
                }
            }
            if (pass < 0) return false;
            position = new Vector3(_geo.PassX[pass], _geo.PassY[pass], _geo.PassZ[pass]);
            return true;
        }

        // Where a combatant in a region that has been announced (or is closing) should walk to get out: just beyond
        // the pass into the neighbouring region that stays open longest. False when the region is safe, off the
        // island, or has no way out. Bots call this on the server; the barrier is theirs to wait at or to open.
        public bool TryEscapeTarget(Vector3 from, out Vector3 target) => TryEscapeTarget(from, -1, out target, out _, out _);

        // The same, naming the pass and the region beyond it. `skipPass` (>= 0) is left out unless it is the only
        // way (a bot that got stuck on the way to it tries another).
        public bool TryEscapeTarget(Vector3 from, int skipPass, out Vector3 target, out int pass, out int toRegion)
        {
            if (TryEscapeTargetExcept(from, skipPass, out target, out pass, out toRegion)) return true;
            return skipPass >= 0 && TryEscapeTargetExcept(from, -1, out target, out pass, out toRegion);
        }

        bool TryEscapeTargetExcept(Vector3 from, int skipPass, out Vector3 target, out int pass, out int toRegion)
        {
            target = default;
            pass = -1;
            toRegion = -1;
            int region = RegionIndexAt(from);
            if (region < 0 || StateOf(region) == ZoneState.Safe) return false;
            int best = -1, bestOther = -1;
            float bestUntil = -1f, bestD = float.MaxValue;
            for (int p = 0; p < _geo.PassCount; p++)
            {
                if (p == skipPass) continue;
                int other = _geo.PassA[p] == region ? _geo.PassB[p] : _geo.PassB[p] == region ? _geo.PassA[p] : -1;
                if (other < 0 || !Lockdown.IsPassable(StateOf(other))) continue;
                // With the plan (the server) the best neighbour is the one that stays open longest; without it, the nearest.
                float until = _plan != null ? Lockdown.OpenUntil(_plan, other) : 0f;
                float dx = from.x - _geo.PassX[p], dz = from.z - _geo.PassZ[p];
                float d = dx * dx + dz * dz;
                if (until > bestUntil || (until == bestUntil && d < bestD))
                {
                    best = p; bestOther = other; bestUntil = until; bestD = d;
                }
            }
            if (best < 0) return false;
            float yaw = _geo.PassYawDeg[best] * Mathf.Deg2Rad;
            float sign = _geo.PassB[best] == bestOther ? 1f : -1f; // the pass faces from A into B
            target = new Vector3(_geo.PassX[best] + Mathf.Sin(yaw) * sign * ExitReach, _geo.PassY[best], _geo.PassZ[best] + Mathf.Cos(yaw) * sign * ExitReach);
            pass = best;
            toRegion = bestOther;
            return true;
        }

        // ---- What bots need to know about the lockdown (#77) ----

        // The middle of a region on the ground (its hub).
        public Vector3 RegionCentre(int region) =>
            EnsureGeometry() && region >= 0 && region < _geo.RegionCount ? new Vector3(_geo.CentreX[region], _geo.CentreY[region], _geo.CentreZ[region]) : Vector3.zero;

        // Every region but the final one is closed: only the final region is left to fight in.
        public bool OnlyFinalRegionLeft
        {
            get
            {
                int final = FinalRegion;
                if (final < 0 || !EnsureGeometry()) return false;
                for (int r = 0; r < _geo.RegionCount; r++)
                    if (r != final && StateOf(r) != ZoneState.Closed) return false;
                return true;
            }
        }

        // Inside the final region once its sub-zones have started closing: where to go when the sub-zone the point is
        // in is no longer safe. The target is the centre of the nearest sub-zone of that region that is still safe;
        // `urgent` when the point's own sub-zone is already being bombarded or closed. False when the point's
        // sub-zone is safe, outside the final region, or before anything there has been announced.
        public bool TrySafeSubZoneTarget(Vector3 from, out Vector3 target, out bool urgent)
        {
            target = default;
            urgent = false;
            int final = FinalRegion;
            if (final < 0 || !EnsureGeometry() || RegionIndexAt(from) != final) return false;
            int at = _geo.SubZoneAt(from.x, from.z);
            if (at < 0) return false;
            var state = SubZoneState(at);
            if (state == ZoneState.Safe) return false;
            urgent = state == ZoneState.Bombardment || state == ZoneState.Closed;
            float bestD = float.MaxValue;
            for (int s = 0; s < _geo.SubZoneTotal; s++)
            {
                if (_geo.SubRegion[s] != final || SubZoneState(s) != ZoneState.Safe) continue;
                float dx = from.x - _geo.SubX[s], dz = from.z - _geo.SubZ[s];
                float d = dx * dx + dz * dz;
                if (d >= bestD) continue;
                bestD = d;
                target = new Vector3(_geo.SubX[s], _geo.SubY[s], _geo.SubZ[s]);
            }
            return bestD < float.MaxValue;
        }

        // Is the point somewhere the lockdown is going to take soon: a region that is no longer safe, or (in the
        // final region) a sub-zone that is no longer safe? Bots stop wandering to such places.
        public bool IsDoomed(Vector3 position)
        {
            int region = RegionIndexAt(position);
            if (region < 0) return false;
            if (StateOf(region) != ZoneState.Safe) return true;
            if (region != FinalRegion) return false;
            int sub = _geo.SubZoneAt(position.x, position.z);
            return sub >= 0 && SubZoneState(sub) != ZoneState.Safe;
        }

        // How late in the closing order a region is, 0..1 (the final region 1). Only the host knows the plan; elsewhere 0.
        public float StageOf(int region)
        {
            if (_plan == null || region < 0) return 0f;
            int index = _plan.ClosingIndexOf(region);
            return index < 0 ? 1f : _plan.Order.Length <= 1 ? 0f : index / (float)(_plan.Order.Length - 1);
        }

        // What lockdown radiation costs per second in a region, whatever its state now (the HUD warns with it).
        public float RadiationDpsOf(int region) => Rules.DamagePerSecond(ZoneState.Lockdown, StageOf(region));

        // Radiation at a point right now (health per second, before the grace period).
        public float DamagePerSecondAt(Vector3 position)
        {
            int region = RegionIndexAt(position);
            if (region < 0) return 0f;
            var rules = Rules;
            float dps = rules.DamagePerSecond(StateOf(region), StageOf(region));
            if ((_revealed && region == _final) || (_plan != null && region == _plan.FinalRegion))
            {
                int sub = _geo.SubZoneAt(position.x, position.z);
                if (sub >= 0) dps = Mathf.Max(dps, rules.DamagePerSecond(SubZoneState(sub), 1f));
            }
            return dps;
        }

        // ---- Setup ----

        void Awake()
        {
            Instance = this;
            // Review builds only (hidden, removed before release with -feelcapture/-perfcapture): `-fastzones` runs a
            // whole closing in about a minute so warning, lockdown, radiation and bombardment can be seen quickly.
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-fastzones") >= 0)
                _rules = new LockdownRules
                {
                    FirstWarningSeconds = 40f, WarningSeconds = 10f, LockdownSeconds = 30f, BombardmentSeconds = 15f,
                    MinSecondsBetweenClosings = 60f, RadiationGraceSeconds = 1f,
                };
            // `-autoplay <folder>` (#77, same release rule): the quick match the simulation test plays.
            if (Array.IndexOf(args, "-autoplay") >= 0) _rules = LockdownRules.QuickMatch();
            if (RulesOverride != null) _rules = RulesOverride;
        }

        // Tests: the rules every director of the following scene loads starts with (null = as configured).
        public static LockdownRules RulesOverride;

        void Start()
        {
            TryBind();
            Started?.Invoke(this);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // The manager may already be gone when the scene unloads.
            if (!_bound || _network == null) return;
            _network.ClientManager.UnregisterBroadcast<ZoneUpdate>(OnZoneUpdate);
            _network.ClientManager.UnregisterBroadcast<ZoneBlast>(OnZoneBlast);
            _network.SceneManager.OnClientLoadedStartScenes -= OnClientLoaded;
        }

        // Called on its own as soon as a NetworkManager exists; tests with their own managers call it directly.
        public void Bind(NetworkManager network)
        {
            if (_bound || network == null) return;
            _bound = true;
            _network = network;
            _network.ClientManager.RegisterBroadcast<ZoneUpdate>(OnZoneUpdate);
            _network.ClientManager.RegisterBroadcast<ZoneBlast>(OnZoneBlast);
            _network.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
        }

        void TryBind()
        {
            if (_bound) return;
            var all = NetworkManager.Instances;
            if (all.Count > 0) Bind(all[0]);
        }

        // Tests and the editor builder hand over a map directly.
        public void SetGeometry(ZoneGeometry geometry)
        {
            _geo = geometry;
            int n = geometry != null ? geometry.RegionCount : 0;
            int s = geometry != null ? geometry.SubZoneTotal : 0;
            int p = geometry != null ? geometry.PassCount : 0;
            _state = new ZoneState[n]; _stateEnd = new float[n]; _nextBlast = new float[n];
            _sub = new ZoneState[s]; _subEnd = new float[s]; _nextSubBlast = new float[s];
            _passOpenEnd = new float[p];
            ResetViewState();
        }

        bool EnsureGeometry()
        {
            if (_geo == null && GeometrySource != null)
            {
                var g = GeometrySource();
                if (g != null && g.RegionCount > 0) SetGeometry(g);
            }
            return _geo != null;
        }

        void ResetViewState()
        {
            for (int i = 0; i < _state.Length; i++) { _state[i] = ZoneState.Safe; _stateEnd[i] = -1f; _nextBlast[i] = 0f; }
            for (int i = 0; i < _sub.Length; i++) { _sub[i] = ZoneState.Safe; _subEnd[i] = -1f; _nextSubBlast[i] = 0f; }
            for (int i = 0; i < _passOpenEnd.Length; i++) _passOpenEnd[i] = 0f;
            _revealed = false;
            _final = -1;
            _pending.Clear();
            _markers.Clear();
            _exposure.Clear();
        }

        // Server: use these rules (and, if not 0, this seed) from now on. Before the match starts it only stores them;
        // afterwards it plans again and restarts the clock, so a test can run a whole lockdown in seconds.
        public void ServerConfigure(LockdownRules rules, int seed = 0)
        {
            _rules = rules;
            if (seed != 0) _seed = seed;
            if (!_serverReady) return;
            ServerStartClock();
            ServerSend(new ZoneUpdate { Kind = ZoneUpdate.KindReset });
        }

        // Server: begin the lockdown clock now (the match clock; MatchDirector or the deployment can restart it).
        public void ServerStartClock()
        {
            if (!EnsureGeometry()) return;
            // A new plan every match (the closing order and the final region are random per match, SPEC 3): the
            // director's per-match seed, not the scene's fixed loot seed (which made every match end in one region).
            int matchSeed = MatchDirector.Instance != null ? MatchDirector.Instance.LockdownSeed : 1;
            _planSeed = _seed != 0 ? _seed : unchecked(matchSeed ^ 0x5EED5);
            _plan = Lockdown.Plan(_geo.RegionCount, _geo.PassA, _geo.PassB, _planSeed, _geo.SubZoneCounts(), Rules);
            _rng = new System.Random(unchecked(_planSeed * 31 + 7));
            _startTime = Time.timeAsDouble;
            _nextEvent = 0;
            _radiationTimer = 0f;
            ResetViewState();
            _serverReady = true;
        }

        // ---- Server ----

        void Update()
        {
            if (!_bound) TryBind();
            if (ServerActive) ServerUpdate();
            for (int i = _markers.Count - 1; i >= 0; i--)
                if (_markers[i].ImpactAt + MarkerLinger < Time.time) _markers.RemoveAt(i);
        }

        void ServerUpdate()
        {
            if (!_serverReady)
            {
                if (!EnsureGeometry()) return;
                ServerStartClock();
                ServerSpawnKeys();
            }
            float now = ServerElapsed;
            var events = _plan.Events;
            while (_nextEvent < events.Length && events[_nextEvent].Time <= now) ServerApply(events[_nextEvent++], now);
            ServerBombard(now);
            ServerDetonateDue();

            _radiationTimer += Time.deltaTime;
            if (_radiationTimer >= Rules.RadiationTickSeconds)
            {
                float dt = _radiationTimer;
                _radiationTimer = 0f;
                ServerRadiation(dt);
            }
        }

        void ServerApply(in LockdownEvent e, float now)
        {
            switch (e.Kind)
            {
                case LockdownEventKind.RegionState:
                {
                    float end = Lockdown.StateEndsAt(_plan, e.Region, e.Time);
                    float remaining = end < 0f ? -1f : Mathf.Max(0f, end - now);
                    _state[e.Region] = e.State;
                    _stateEnd[e.Region] = remaining < 0f ? -1f : Time.time + remaining;
                    if (e.State == ZoneState.Bombardment) _nextBlast[e.Region] = now + 0.5f;
                    ServerSend(new ZoneUpdate { Kind = ZoneUpdate.KindRegion, Region = e.Region, State = (byte)e.State, Remaining = remaining });
                    ServerRegionStateChanged?.Invoke(e.Region, e.State);
                    if (ClientActive) ClientRegionStateChanged?.Invoke(e.Region, e.State);
                    break;
                }
                case LockdownEventKind.FinalRevealed:
                    _revealed = true;
                    _final = e.Region;
                    ServerSend(new ZoneUpdate { Kind = ZoneUpdate.KindFinalRevealed, Region = e.Region });
                    ServerFinalRevealed?.Invoke(e.Region);
                    if (ClientActive) ClientFinalRevealed?.Invoke(e.Region);
                    break;
                case LockdownEventKind.SubZoneState:
                    for (int local = 0; local < _plan.SubZoneStep.Length; local++)
                    {
                        if (_plan.SubZoneStep[local] != e.Index) continue;
                        int global = _geo.GlobalSubZone(e.Region, local);
                        if (global < 0) continue;
                        float end = Lockdown.StateEndsAt(_plan, local, e.Time, true);
                        float remaining = end < 0f ? -1f : Mathf.Max(0f, end - now);
                        _sub[global] = e.State;
                        _subEnd[global] = remaining < 0f ? -1f : Time.time + remaining;
                        if (e.State == ZoneState.Bombardment) _nextSubBlast[global] = now + 0.5f;
                        ServerSend(new ZoneUpdate { Kind = ZoneUpdate.KindSubZone, Region = e.Region, Index = local, State = (byte)e.State, Remaining = remaining });
                        if (ClientActive) ClientSubZoneStateChanged?.Invoke(e.Region, local, e.State);
                    }
                    break;
            }
        }

        void ServerSend(in ZoneUpdate message)
        {
            if (ServerActive) _network.ServerManager.Broadcast(message);
        }

        // Radiation: standing in a region in lockdown or bombardment (or a closed sub-zone) for longer than the grace
        // period costs health every tick. Closed regions kill in seconds. Never instant.
        void ServerRadiation(float dt)
        {
            var rules = Rules;
            _scratch.Clear();
            _scratch.AddRange(Combatant.All);
            for (int i = 0; i < _scratch.Count; i++)
            {
                var c = _scratch[i];
                if (c == null || !c.IsServerInitialized) continue;
                int id = c.ObjectId;
                if (c.Dead.Value)
                {
                    _exposure.Remove(id);
                    continue;
                }
                float dps = DamagePerSecondAt(c.transform.position);
                if (dps <= 0f)
                {
                    _exposure.Remove(id);
                    continue;
                }
                _exposure.TryGetValue(id, out float exposed);
                exposed += dt;
                _exposure[id] = exposed;
                if (exposed < rules.RadiationGraceSeconds) continue;
                c.ServerTakeDamage(new DamageInfo { Amount = dps * dt, Attacker = null, Zone = HitZone.Body, WeaponId = 0, Cause = DamageCause.Zone });
            }
            _scratch.Clear();
        }

        // Bombardment: every region (or final-region sub-zone) in that state gets a missile every BlastInterval or so.
        void ServerBombard(float now)
        {
            var rules = Rules;
            for (int r = 0; r < _state.Length; r++)
            {
                if (_state[r] != ZoneState.Bombardment || now < _nextBlast[r]) continue;
                _nextBlast[r] = now + rules.BlastIntervalSeconds * (0.6f + 0.8f * (float)_rng.NextDouble());
                ServerQueueBlast(r, -1);
            }
            for (int s = 0; s < _sub.Length; s++)
            {
                if (_sub[s] != ZoneState.Bombardment || now < _nextSubBlast[s]) continue;
                _nextSubBlast[s] = now + rules.BlastIntervalSeconds * (0.6f + 0.8f * (float)_rng.NextDouble());
                ServerQueueBlast(_geo.SubRegion[s], s);
            }
        }

        void ServerQueueBlast(int region, int subZone)
        {
            if (!PickBlastPoint(region, subZone, out var point)) return;
            var rules = Rules;
            _pending.Add(new PendingBlast { Position = point, At = Time.timeAsDouble + rules.BlastWarningSeconds, Radius = rules.BlastOuterRadius });
            if (ServerActive) _network.ServerManager.Broadcast(new ZoneBlast { Position = point, Delay = rules.BlastWarningSeconds, Radius = rules.BlastOuterRadius, Impact = false });
            if (ClientActive) ClientIncoming(point, rules.BlastWarningSeconds, rules.BlastOuterRadius);
        }

        // Half of the missiles (BlastTargetedShare) land near someone standing in the zone, the rest anywhere in it.
        bool PickBlastPoint(int region, int subZone, out Vector3 point)
        {
            var rules = Rules;
            point = default;
            if (_rng.NextDouble() < rules.BlastTargetedShare)
            {
                _scratch.Clear();
                for (int i = 0; i < Combatant.All.Count; i++)
                {
                    var c = Combatant.All[i];
                    if (c == null || !c.IsServerInitialized || c.Dead.Value) continue;
                    if (InZone(c.transform.position, region, subZone)) _scratch.Add(c);
                }
                if (_scratch.Count > 0)
                {
                    var target = _scratch[_rng.Next(_scratch.Count)].transform.position;
                    _scratch.Clear();
                    float angle = (float)_rng.NextDouble() * Mathf.PI * 2f;
                    float reach = Mathf.Sqrt((float)_rng.NextDouble()) * rules.BlastScatter;
                    if (Ground(target.x + Mathf.Cos(angle) * reach, target.z + Mathf.Sin(angle) * reach, out point)) return true;
                    point = target;
                    return true;
                }
                _scratch.Clear();
            }
            for (int tries = 0; tries < 16; tries++)
            {
                float x, z;
                if (subZone >= 0)
                {
                    float angle = (float)_rng.NextDouble() * Mathf.PI * 2f;
                    float reach = Mathf.Sqrt((float)_rng.NextDouble()) * Mathf.Max(20f, _geo.SubRadius[subZone] * 1.6f);
                    x = _geo.SubX[subZone] + Mathf.Cos(angle) * reach;
                    z = _geo.SubZ[subZone] + Mathf.Sin(angle) * reach;
                }
                else
                {
                    x = _geo.CentreX[region] + ((float)_rng.NextDouble() * 2f - 1f) * RegionSampleHalfSize;
                    z = _geo.CentreZ[region] + ((float)_rng.NextDouble() * 2f - 1f) * RegionSampleHalfSize;
                }
                if (_geo.RegionAt(x, z) != region) continue;
                if (Ground(x, z, out point)) return true;
            }
            // Nothing found on the ground: aim at the region's centre rather than skip the missile.
            point = new Vector3(_geo.CentreX[region], _geo.CentreY[region], _geo.CentreZ[region]);
            return true;
        }

        bool InZone(Vector3 p, int region, int subZone)
        {
            if (_geo.RegionAt(p.x, p.z) != region) return false;
            return subZone < 0 || _geo.SubZoneAt(p.x, p.z) == subZone;
        }

        // The surface under a map position, unless it is open water.
        bool Ground(float x, float z, out Vector3 point)
        {
            point = default;
            if (!Physics.Raycast(new Vector3(x, GroundProbeHeight, z), Vector3.down, out var hit, GroundProbeLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.point.y < _geo.SeaLevel - WaterMargin) return false;
            point = hit.point;
            return true;
        }

        void ServerDetonateDue()
        {
            if (_pending.Count == 0) return;
            double now = Time.timeAsDouble;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].At > now) continue;
                var blast = _pending[i];
                _pending.RemoveAt(i);
                ServerDetonate(blast.Position, blast.Radius);
            }
        }

        // A missile lands: everyone within the radius takes damage by distance (nothing shelters from above).
        void ServerDetonate(Vector3 centre, float radius)
        {
            var rules = Rules;
            _scratch.Clear();
            _scratch.AddRange(Combatant.All);
            for (int i = 0; i < _scratch.Count; i++)
            {
                var c = _scratch[i];
                if (c == null || !c.IsServerInitialized || c.Dead.Value) continue;
                float damage = Lockdown.BlastDamageAt(Vector3.Distance(centre, c.transform.position + Vector3.up), rules);
                if (damage <= 0f) continue;
                c.ServerTakeDamage(new DamageInfo { Amount = damage, Attacker = null, Zone = HitZone.Body, WeaponId = 0, Cause = DamageCause.Blast });
            }
            _scratch.Clear();
            if (ServerActive) _network.ServerManager.Broadcast(new ZoneBlast { Position = centre, Radius = radius, Impact = true });
            if (ClientActive) ClientBlastImpact?.Invoke(centre, radius);
        }

        // Server: keeps a pass open for `seconds` (a key was used). Tests and AI call this directly.
        public void ServerOpenPass(int pass, float seconds)
        {
            if (!EnsureGeometry() || pass < 0 || pass >= _passOpenEnd.Length || seconds <= 0f) return;
            _passOpenEnd[pass] = Time.time + seconds;
            ServerSend(new ZoneUpdate { Kind = ZoneUpdate.KindPassOpen, Index = pass, Remaining = seconds });
            if (ClientActive) ClientPassOpened?.Invoke(pass, seconds);
        }

        // Keys lie around every region from the start (at least KeysPerRegion in each, near its compounds).
        void ServerSpawnKeys()
        {
            int per = _keysPerRegion >= 0 ? _keysPerRegion : Rules.KeysPerRegion;
            var director = MatchDirector.Instance;
            if (per <= 0 || director == null || _geo == null) return;
            var rng = new System.Random(unchecked(_planSeed * 17 + 5));
            var counts = _geo.SubZoneCounts();
            for (int region = 0; region < _geo.RegionCount; region++)
            {
                int first = _geo.GlobalSubZone(region, 0);
                int count = counts[region];
                for (int k = 0; k < per; k++)
                {
                    float cx = _geo.CentreX[region], cz = _geo.CentreZ[region], spread = 40f;
                    if (count > 0 && first >= 0)
                    {
                        int s = _geo.GlobalSubZone(region, k % count);
                        cx = _geo.SubX[s]; cz = _geo.SubZ[s]; spread = Mathf.Max(6f, _geo.SubRadius[s] * 0.7f);
                    }
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float reach = Mathf.Sqrt((float)rng.NextDouble()) * spread;
                    if (!Ground(cx + Mathf.Cos(angle) * reach, cz + Mathf.Sin(angle) * reach, out var at)) continue;
                    director.ServerSpawnLoot(new LootEntry { Kind = KeyHolder.KeyLootKind, Amount = 1 }, at + Vector3.up * 0.4f);
                }
            }
        }

        // A client that just loaded hears what already happened.
        void OnClientLoaded(NetworkConnection connection, bool asServer)
        {
            if (!asServer || !_serverReady || !ServerActive || connection == null || !connection.IsAuthenticated) return;
            float now = ServerElapsed;
            var manager = _network.ServerManager;
            for (int r = 0; r < _state.Length; r++)
            {
                if (_state[r] == ZoneState.Safe) continue;
                float end = Lockdown.StateEndsAt(_plan, r, now);
                manager.Broadcast(connection, new ZoneUpdate { Kind = ZoneUpdate.KindRegion, Region = r, State = (byte)_state[r], Remaining = end < 0f ? -1f : end - now });
            }
            if (_revealed) manager.Broadcast(connection, new ZoneUpdate { Kind = ZoneUpdate.KindFinalRevealed, Region = _final });
            for (int s = 0; s < _sub.Length; s++)
            {
                if (_sub[s] == ZoneState.Safe) continue;
                int local = _geo.LocalSubZone(s);
                float end = Lockdown.StateEndsAt(_plan, local, now, true);
                manager.Broadcast(connection, new ZoneUpdate { Kind = ZoneUpdate.KindSubZone, Region = _geo.SubRegion[s], Index = local, State = (byte)_sub[s], Remaining = end < 0f ? -1f : end - now });
            }
            for (int p = 0; p < _passOpenEnd.Length; p++)
                if (_passOpenEnd[p] > Time.time)
                    manager.Broadcast(connection, new ZoneUpdate { Kind = ZoneUpdate.KindPassOpen, Index = p, Remaining = _passOpenEnd[p] - Time.time });
        }

        // ---- Clients ----

        // The host already wrote what it knows when it decided; only remote clients apply what they are told.
        void OnZoneUpdate(ZoneUpdate m, Channel channel)
        {
            if (ServerActive || !EnsureGeometry()) return;
            switch (m.Kind)
            {
                case ZoneUpdate.KindReset:
                    ResetViewState();
                    break;
                case ZoneUpdate.KindRegion:
                    if (m.Region < 0 || m.Region >= _state.Length) return;
                    _state[m.Region] = (ZoneState)m.State;
                    _stateEnd[m.Region] = m.Remaining < 0f ? -1f : Time.time + m.Remaining;
                    ClientRegionStateChanged?.Invoke(m.Region, (ZoneState)m.State);
                    break;
                case ZoneUpdate.KindFinalRevealed:
                    _revealed = true;
                    _final = m.Region;
                    ClientFinalRevealed?.Invoke(m.Region);
                    break;
                case ZoneUpdate.KindSubZone:
                {
                    int global = _geo.GlobalSubZone(m.Region, m.Index);
                    if (global < 0) return;
                    _sub[global] = (ZoneState)m.State;
                    _subEnd[global] = m.Remaining < 0f ? -1f : Time.time + m.Remaining;
                    ClientSubZoneStateChanged?.Invoke(m.Region, m.Index, (ZoneState)m.State);
                    break;
                }
                case ZoneUpdate.KindPassOpen:
                    if (m.Index < 0 || m.Index >= _passOpenEnd.Length) return;
                    _passOpenEnd[m.Index] = Time.time + m.Remaining;
                    ClientPassOpened?.Invoke(m.Index, m.Remaining);
                    break;
            }
        }

        void OnZoneBlast(ZoneBlast m, Channel channel)
        {
            if (ServerActive) return;
            if (m.Impact) ClientBlastImpact?.Invoke(m.Position, m.Radius);
            else ClientIncoming(m.Position, m.Delay, m.Radius);
        }

        void ClientIncoming(Vector3 position, float delay, float radius)
        {
            _markers.Add(new BlastMarker { Position = position, Radius = radius, ImpactAt = Time.time + delay });
            ClientBlastIncoming?.Invoke(position, delay, radius);
        }
    }
}
