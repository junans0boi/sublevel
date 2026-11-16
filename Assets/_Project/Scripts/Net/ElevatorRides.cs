using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Net
{
    // A player standing in a station asks to ride to another region (ADR-076, #76).
    public struct ElevatorRideRequest : IBroadcast
    {
        public int Station;
        public int Region;
    }

    // The server's answer, to the rider's connection only.
    public struct ElevatorRideStatus : IBroadcast
    {
        public const byte KindStarted = 0, KindArrived = 1, KindRejected = 2;

        public byte Kind;
        public int Station;   // Started: where the ride began; Arrived: the car it changes into at the bottom
        public int Region;    // Started: the chosen destination; Arrived: the region it comes out in
        public float Seconds; // Started: the whole ride; Arrived: cooldown after the doors have opened
    }

    // A station's doors and car run a ride cycle (ADR-079), to everyone: every peer moves them from this.
    public struct ElevatorCycleStatus : IBroadcast
    {
        public int Station;
        public byte Kind;     // ElevatorCycleKind
        public float Ago;     // seconds since it began on the server when sent
    }

    // Region-to-region elevator rides during the match (ADR-076, ADR-079). Lives next to ElevatorDoors (added by
    // it), so only scenes with elevators have rides. Rules are ElevatorTravel's; the region states come from
    // PollutionDirector (all safe without a lockdown). The server decides; each ride runs a cycle at two stations
    // (ElevatorCycle): the departure doors close and the car goes down, the rider changes cars at the bottom, the
    // arrival car comes up and its doors open. ElevatorDoor and ElevatorCar read the cycles (StationCycle).
    // Movement is owner-authoritative for players (ADR-029): a player's own client keeps its body on the car floor
    // (TryLocalRide); the server does the same for bots, whose brain and navmesh agent rest meanwhile.
    public class ElevatorRides : MonoBehaviour
    {
        // How far above or below the station floor a combatant may be and still be in the station.
        const float StationHeight = 2f;
        // Above this much under the station floor nobody is taken for being in the shaft.
        const float ShaftCatch = 0.5f;
        // A bot's brain component lives in Game.AI, above this assembly: paused by name during its ride.
        const string BotBrainComponent = "BotController";
        // A bot this close to a station with nothing solid between it and the car boards it, the server walking it
        // in while the doors close: the navmesh reaches into the car, but another bot standing in it can hold the
        // walker off at the entrance (agent avoidance). The clear-line check keeps the walk-in from crossing a wall.
        const float BotStationReach = 8f;
        const float BotEyeHeight = 1.2f;

        public static ElevatorRides Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        struct Trip
        {
            public Combatant Rider;
            public int From, ToRegion, To;
            public double StartAt, SwitchAt; // server clock; SwitchAt is NaN until the rider changed cars
            public Behaviour Brain;
            public Vector3 BoardFrom;        // bots: where they stood when the ride started
            public int Group;                // key elevator car (WP 7), 0 = a normal ride
        }

        struct Cycle
        {
            public ElevatorCycleKind Kind;
            public double Start; // this peer's clock
        }

        ElevatorDoors _doors;
        NetworkManager _network;
        bool _bound;
        System.Random _rng = new System.Random(76);
        readonly Dictionary<int, Cycle> _cycles = new Dictionary<int, Cycle>();

        // ---- Server ----
        readonly Dictionary<int, Trip> _trips = new Dictionary<int, Trip>();
        readonly Dictionary<int, double> _readyAt = new Dictionary<int, double>(); // ObjectId -> cooldown over
        readonly List<int> _due = new List<int>();
        readonly List<ZoneState> _states = new List<ZoneState>();
        readonly List<int> _regionOfStation = new List<int>();

        // ---- Local rider (this peer's own player) ----
        bool _pending, _riding, _arrived, _switchPending;
        int _rideFrom = -1, _rideTo = -1, _rideRegion = -1;
        double _rideStart, _rideEnd, _arriveStart, _localReadyAt = double.NegativeInfinity;

        public bool LocalPending => _pending;
        // In the car: no moving or firing; TryLocalRide puts the feet on the car floor until the doors open.
        public bool LocalRiding => _riding && !(_arrived && Time.timeAsDouble >= _arriveStart + ElevatorCycle.Length(DeploymentRules.Current));
        public int LocalDestination => _rideRegion;
        public float LocalSecondsLeft => _riding ? Mathf.Max(0f, (float)(_rideEnd - Time.timeAsDouble)) : 0f;
        public float LocalCooldownLeft => Mathf.Max(0f, (float)(_localReadyAt - Time.timeAsDouble));

        public static ElevatorRides Attach(ElevatorDoors doors)
        {
            var rides = doors.GetComponent<ElevatorRides>();
            if (rides == null) rides = doors.gameObject.AddComponent<ElevatorRides>();
            rides._doors = doors;
            return rides;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (!_bound || _network == null) return;
            _network.ClientManager.UnregisterBroadcast<ElevatorRideStatus>(OnStatus);
            _network.ClientManager.UnregisterBroadcast<ElevatorCycleStatus>(OnCycle);
            _network.ServerManager.UnregisterBroadcast<ElevatorRideRequest>(OnRequest);
        }

        public void Bind(NetworkManager network)
        {
            if (_bound || network == null) return;
            _bound = true;
            _network = network;
            _network.ClientManager.RegisterBroadcast<ElevatorRideStatus>(OnStatus);
            _network.ClientManager.RegisterBroadcast<ElevatorCycleStatus>(OnCycle);
            _network.ServerManager.RegisterBroadcast<ElevatorRideRequest>(OnRequest);
        }

        // ---- Shared reads ----

        public int RegionCount
        {
            get
            {
                var zones = PollutionDirector.Instance;
                if (zones != null && zones.RegionCount > 0) return zones.RegionCount;
                int n = 0;
                if (_doors != null) foreach (var s in _doors.Sites) n = Mathf.Max(n, s.Region + 1);
                return n;
            }
        }

        // The region states as this peer knows them (safe everywhere without a lockdown).
        public void States(List<ZoneState> result)
        {
            result.Clear();
            var zones = PollutionDirector.Instance;
            for (int r = 0, n = RegionCount; r < n; r++) result.Add(zones != null ? zones.StateOf(r) : ZoneState.Safe);
        }

        // The station a combatant at `at` stands in (on its floor), or -1.
        public int StationAt(Vector3 at)
        {
            if (_doors == null) return -1;
            float radius = DeploymentRules.Current.StationRadius;
            var sites = _doors.Sites;
            for (int i = 0; i < sites.Count; i++)
            {
                var p = sites[i].Spawn;
                float dx = at.x - p.x, dz = at.z - p.z;
                if (dx * dx + dz * dz <= radius * radius && Mathf.Abs(at.y - p.y) <= StationHeight) return i;
            }
            return -1;
        }

        public Vector3 StationSpawn(int station) => _doors != null && station >= 0 && station < _doors.Sites.Count ? _doors.Sites[station].Spawn : Vector3.zero;
        public int StationCount => _doors != null ? _doors.Sites.Count : 0;
        public int RegionOfStation(int station) => _doors != null && station >= 0 && station < _doors.Sites.Count ? _doors.Sites[station].Region : -1;

        // Rides only after the deployment doors opened on this peer (ElevatorDoors.DoorsClosed covers the wait).
        public bool Running => _doors != null && !_doors.DoorsClosed;

        // A station whose doors and car are in a ride cycle right now: which kind and seconds since it began.
        public bool StationCycle(int station, out ElevatorCycleKind kind, out double t)
        {
            kind = default;
            t = 0.0;
            if (!_cycles.TryGetValue(station, out var c)) return false;
            t = Time.timeAsDouble - c.Start;
            kind = c.Kind;
            if (ElevatorCycle.Active(t, DeploymentRules.Current)) return true;
            return false;
        }

        public bool StationBusy(int station) => StationCycle(station, out _, out _);

        // Where the feet of someone at `at` go this frame because a station car under them is in a cycle (they
        // stood in the station and ride down and back up with it). False when no moving car concerns them.
        public bool TryCarFloor(Vector3 at, out float floorY)
        {
            floorY = 0f;
            if (_doors == null || _cycles.Count == 0) return false;
            var rules = DeploymentRules.Current;
            float radius = rules.StationRadius;
            foreach (var kv in _cycles)
            {
                if (!StationCycle(kv.Key, out var kind, out double t)) continue;
                var top = _doors.Sites[kv.Key].Spawn;
                float dx = at.x - top.x, dz = at.z - top.z;
                if (dx * dx + dz * dz > radius * radius) continue;
                if (at.y > top.y + StationHeight || at.y < top.y - rules.ShaftDepth - 2f) continue;
                // Standing on the floor while the car does not move: walk freely.
                if (!ElevatorCycle.Moving(t, rules) && at.y > top.y - ShaftCatch) continue;
                floorY = top.y - ElevatorCycle.CarOffset(kind, t, rules) + ElevatorCar.RideLift;
                return true;
            }
            return false;
        }

        void SetCycle(int station, ElevatorCycleKind kind, double start) => _cycles[station] = new Cycle { Kind = kind, Start = start };

        void OnCycle(ElevatorCycleStatus status, Channel channel)
        {
            // On a host the server already set the same cycle on the same clock.
            if (ServerActive) return;
            SetCycle(status.Station, (ElevatorCycleKind)status.Kind, Time.timeAsDouble - status.Ago);
        }

        // ---- Client: the local player ----

        // Asks to ride from the station the local player stands in. False when nothing was sent.
        public bool RequestRide(int station, int region)
        {
            if (_pending || _riding || _network == null || !_network.IsClientStarted) return false;
            _pending = true;
            _network.ClientManager.Broadcast(new ElevatorRideRequest { Station = station, Region = region });
            return true;
        }

        // Where the local player's feet go this frame while riding: the departure car floor, then (moved across
        // once, `switched`, facing out of the arrival station) the arrival car floor. False when not riding.
        public bool TryLocalRide(Vector3 at, out Vector3 feet, out bool switched, out float yawDeg)
        {
            feet = at;
            switched = false;
            yawDeg = 0f;
            if (!_riding || _doors == null) return false;
            var rules = DeploymentRules.Current;
            double now = Time.timeAsDouble;
            if (_arrived && now >= _arriveStart + ElevatorCycle.Length(rules))
            {
                _riding = false;
                return false;
            }
            if (!_arrived)
            {
                if (_rideFrom < 0 || _rideFrom >= _doors.Sites.Count) return false;
                var top = _doors.Sites[_rideFrom].Spawn;
                feet = new Vector3(at.x, top.y - ElevatorCycle.RiderOffset(false, now - _rideStart, rules) + ElevatorCar.RideLift, at.z);
                return true;
            }
            var site = _doors.Sites[_rideTo];
            float y = site.Spawn.y - ElevatorCycle.RiderOffset(true, now - _arriveStart, rules) + ElevatorCar.RideLift;
            if (_switchPending)
            {
                _switchPending = false;
                switched = true;
                yawDeg = site.YawDeg;
                feet = new Vector3(site.Spawn.x, y, site.Spawn.z);
                return true;
            }
            feet = new Vector3(at.x, y, at.z);
            return true;
        }

        void OnStatus(ElevatorRideStatus status, Channel channel)
        {
            _pending = false;
            var rules = DeploymentRules.Current;
            switch (status.Kind)
            {
                case ElevatorRideStatus.KindStarted:
                    _riding = true;
                    _arrived = false;
                    _rideFrom = status.Station;
                    _rideRegion = status.Region;
                    _rideStart = Time.timeAsDouble;
                    _rideEnd = _rideStart + status.Seconds;
                    break;
                case ElevatorRideStatus.KindArrived:
                    if (_doors == null || status.Station < 0 || status.Station >= _doors.Sites.Count) break;
                    _arrived = true;
                    _switchPending = true;
                    _rideTo = status.Station;
                    _rideRegion = status.Region;
                    _arriveStart = Time.timeAsDouble;
                    _rideEnd = _arriveStart + ElevatorCycle.Length(rules);
                    _localReadyAt = _rideEnd + status.Seconds;
                    break;
            }
        }

        // ---- Server ----

        bool ServerActive => _network != null && _network.IsServerStarted;

        // Server: a combatant is between stations.
        public bool ServerRiding(Combatant c) => c != null && _trips.ContainsKey(c.ObjectId);

        public float ServerCooldownLeft(Combatant c) =>
            c != null && _readyAt.TryGetValue(c.ObjectId, out var t) ? Mathf.Max(0f, (float)(t - Time.timeAsDouble)) : 0f;

        // Server: starts a ride for `rider` if the rules allow it. Players come through OnRequest, bots through
        // ServerBotRide; tests call it directly.
        public bool ServerTryStart(Combatant rider, int station, int region)
        {
            if (rider == null || rider.Dead.Value || rider.Knocked.Value || _doors == null || !ServerActive) return false;
            if (!_doors.ServerScheduled || _doors.ServerDoorsClosed) return false;
            if (MatchDirector.Instance != null && MatchDirector.Instance.State != null && MatchDirector.Instance.State.IsOver) return false;
            if (_trips.ContainsKey(rider.ObjectId) || !InStation(rider, station)) return false;
            if (StationBusy(station) || ServerCooldownLeft(rider) > 0f) return false;
            States(_states);
            if (!ElevatorTravel.CanRide(RegionOfStation(station), region, _states)) return false;

            var rules = DeploymentRules.Current;
            double now = Time.timeAsDouble;
            var trip = new Trip { Rider = rider, From = station, ToRegion = region, To = -1, StartAt = now, SwitchAt = double.NaN, BoardFrom = rider.transform.position };
            _trips[rider.ObjectId] = trip;
            ServerSetCycle(station, ElevatorCycleKind.Depart, now);
            Send(rider, new ElevatorRideStatus { Kind = ElevatorRideStatus.KindStarted, Station = station, Region = region, Seconds = ElevatorCycle.RideSeconds(rules) });
            return true;
        }

        readonly Dictionary<int, int> _groupStation = new Dictionary<int, int>();

        // Server: a key elevator ride the key system already approved (WP 7): no zone or cooldown check, and every
        // rider of the car (same group) comes out at the same station.
        public bool ServerKeyRide(Combatant rider, int station, int region, int group)
        {
            if (rider == null || rider.Dead.Value || _doors == null || !ServerActive || _trips.ContainsKey(rider.ObjectId)) return false;
            double now = Time.timeAsDouble;
            _trips[rider.ObjectId] = new Trip { Rider = rider, From = station, ToRegion = region, To = -1, StartAt = now, SwitchAt = double.NaN, BoardFrom = rider.transform.position, Group = group };
            ServerSetCycle(station, ElevatorCycleKind.Depart, now);
            Send(rider, new ElevatorRideStatus { Kind = ElevatorRideStatus.KindStarted, Station = station, Region = region, Seconds = ElevatorCycle.RideSeconds(DeploymentRules.Current) });
            return true;
        }

        // Server: a client that just loaded is told the cycles that are running (it joined mid-ride, ADR-131).
        public void ServerSendCycles(NetworkConnection connection)
        {
            if (!ServerActive || connection == null || !connection.IsAuthenticated) return;
            double now = Time.timeAsDouble;
            foreach (var kv in _cycles)
            {
                if (!ElevatorCycle.Active(now - kv.Value.Start, DeploymentRules.Current)) continue;
                _network.ServerManager.Broadcast(connection, new ElevatorCycleStatus { Station = kv.Key, Kind = (byte)kv.Value.Kind, Ago = (float)(now - kv.Value.Start) });
            }
        }

        void ServerSetCycle(int station, ElevatorCycleKind kind, double start)
        {
            SetCycle(station, kind, start);
            if (ServerActive) _network.ServerManager.Broadcast(new ElevatorCycleStatus { Station = station, Kind = (byte)kind, Ago = (float)(Time.timeAsDouble - start) });
        }

        void OnRequest(NetworkConnection connection, ElevatorRideRequest request, Channel channel)
        {
            if (!ServerActive) return;
            Combatant rider = null;
            foreach (var c in Combatant.All)
                if (c != null && c.IsPlayer && c.Owner == connection) { rider = c; break; }
            if (ServerTryStart(rider, request.Station, request.Region)) return;
            if (connection != null && connection.IsAuthenticated)
                _network.ServerManager.Broadcast(connection, new ElevatorRideStatus { Kind = ElevatorRideStatus.KindRejected, Station = -1, Region = -1 });
        }

        // ---- Bots (ADR-079) ----

        // Server, from BotController while its region is announced: whether to head for a station instead of the
        // pass `exitMeters` away (negative: none), and which one. Standing in it, the ride starts here.
        public bool ServerBotRide(Combatant bot, float exitMeters, out Vector3 station)
        {
            station = default;
            if (bot == null || _doors == null || !ServerActive || _trips.ContainsKey(bot.ObjectId)) return false;
            if (!_doors.ServerScheduled || _doors.ServerDoorsClosed) return false;
            var at = bot.transform.position;
            States(_states);
            var zones = PollutionDirector.Instance;
            int here = zones != null ? zones.RegionIndexAt(at) : -1;
            int best = -1;
            float bestMeters = float.MaxValue;
            var sites = _doors.Sites;
            for (int i = 0; i < sites.Count; i++)
            {
                if (sites[i].Region != here || StationBusy(i)) continue;
                float m = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(sites[i].Spawn.x, sites[i].Spawn.z));
                if (m < bestMeters) { bestMeters = m; best = i; }
            }
            if (!ElevatorTravel.BotShouldRide(here, _states, best >= 0 ? bestMeters : -1f, exitMeters, ServerCooldownLeft(bot))) return false;
            station = sites[best].Spawn;
            if (InStation(bot, best))
                ServerTryStart(bot, best, ElevatorTravel.BotDestination(here, _states, zones != null ? zones.FinalRegion : -1));
            return true;
        }

        bool InStation(Combatant c, int station)
        {
            if (_doors == null || station < 0 || station >= _doors.Sites.Count) return false;
            if (c.IsPlayer) return StationAt(c.transform.position) == station;
            var p = _doors.Sites[station].Spawn;
            var at = c.transform.position;
            if (new Vector2(at.x - p.x, at.z - p.z).magnitude > BotStationReach || Mathf.Abs(at.y - p.y) > StationHeight) return false;
            return ClearLine(at + Vector3.up * BotEyeHeight, p + Vector3.up * BotEyeHeight);
        }

        static readonly RaycastHit[] LineHits = new RaycastHit[16];

        // Nothing but combatants between two points (walls, posts and shut doors count).
        static bool ClearLine(Vector3 a, Vector3 b)
        {
            var d = b - a;
            int n = Physics.RaycastNonAlloc(a, d.normalized, LineHits, d.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (LineHits[i].collider.GetComponentInParent<Combatant>() == null) return false;
            return true;
        }

        // Rests (or wakes) a riding bot: its brain stops steering and its navmesh agent lets go of the body.
        static Behaviour ServerPauseBot(Combatant bot, bool pause, Behaviour brain = null)
        {
            if (brain == null) brain = bot.GetComponent(BotBrainComponent) as Behaviour;
            var agent = bot.GetComponent<NavMeshAgent>();
            if (pause && agent != null && agent.enabled) agent.enabled = false;
            // Woken, the brain enables its agent again (BotController.EnsureAgent), which snaps to the navmesh.
            if (brain != null) brain.enabled = !pause;
            return brain;
        }

        // ---- Bystanders (ADR-080): bots standing in a station whose car runs without them go down and up with it ----

        readonly Dictionary<int, (Combatant bot, Behaviour brain)> _carried = new Dictionary<int, (Combatant, Behaviour)>();
        readonly List<int> _released = new List<int>();

        void CarryBystanders()
        {
            if (_cycles.Count > 0)
                foreach (var c in Combatant.All)
                {
                    if (c == null || c.IsPlayer || c.Dead.Value || _trips.ContainsKey(c.ObjectId)) continue;
                    var p = c.transform.position;
                    if (!TryCarFloor(p, out float y)) continue;
                    if (!_carried.ContainsKey(c.ObjectId)) _carried[c.ObjectId] = (c, ServerPauseBot(c, true));
                    c.transform.position = new Vector3(p.x, y, p.z);
                }
            if (_carried.Count == 0) return;
            _released.Clear();
            foreach (var kv in _carried)
            {
                var (bot, brain) = kv.Value;
                if (bot != null && !bot.Dead.Value && TryCarFloor(bot.transform.position, out _)) continue;
                if (bot != null) ServerPauseBot(bot, false, brain);
                _released.Add(kv.Key);
            }
            foreach (int id in _released) _carried.Remove(id);
        }

        void Update()
        {
            if (!ServerActive) return;
            CarryBystanders();
            if (_trips.Count == 0) return;
            var rules = DeploymentRules.Current;
            double now = Time.timeAsDouble;
            _due.Clear();
            foreach (var kv in _trips) _due.Add(kv.Key);
            foreach (int id in _due)
            {
                var trip = _trips[id];
                if (trip.Rider == null || trip.Rider.Dead.Value)
                {
                    if (trip.Rider != null && trip.Brain != null) ServerPauseBot(trip.Rider, false, trip.Brain);
                    _trips.Remove(id);
                    continue;
                }
                if (double.IsNaN(trip.SwitchAt) && now >= trip.StartAt + ElevatorCycle.SwitchAt(rules))
                {
                    ServerSwitch(ref trip, now);
                    _trips[id] = trip;
                }
                bool arrived = !double.IsNaN(trip.SwitchAt);
                if (arrived && now >= trip.SwitchAt + ElevatorCycle.Length(rules))
                {
                    _trips.Remove(id);
                    _readyAt[id] = now + rules.TravelCooldownSeconds;
                    if (trip.Brain != null) ServerPauseBot(trip.Rider, false, trip.Brain);
                    continue;
                }
                // Bots: the server carries the body (players are carried by their own client). The brain is paused
                // here, not where the ride started: that may be in the middle of the brain's own Update.
                if (!trip.Rider.IsPlayer)
                {
                    if (trip.Brain == null)
                    {
                        trip.Brain = ServerPauseBot(trip.Rider, true);
                        _trips[id] = trip;
                    }
                    int station = arrived ? trip.To : trip.From;
                    var top = _doors.Sites[station].Spawn;
                    var p = trip.Rider.transform.position;
                    float y = top.y - ElevatorCycle.RiderOffset(arrived, now - (arrived ? trip.SwitchAt : trip.StartAt), rules) + ElevatorCar.RideLift;
                    if (!arrived)
                    {
                        // Boarding: into the middle of the car before the doors are shut.
                        float k = Mathf.Clamp01((float)((now - trip.StartAt) / ElevatorCycle.DoorSeconds(rules)));
                        p = Vector3.Lerp(trip.BoardFrom, top, k);
                    }
                    trip.Rider.transform.position = new Vector3(p.x, y, p.z);
                }
            }
        }

        // At the bottom: where the car comes out is decided now (the destination may have been announced on the
        // way), the arrival station's cycle starts and the rider changes cars.
        void ServerSwitch(ref Trip trip, double now)
        {
            States(_states);
            int from = RegionOfStation(trip.From);
            var pollution = PollutionDirector.Instance;
            int region = trip.Group > 0 && pollution != null && pollution.Ready
                ? KeyElevatorTravel.ArrivalRegion(from, trip.ToRegion, pollution.States)
                : ElevatorTravel.ArrivalRegion(from, trip.ToRegion, _states);
            int station;
            if (trip.Group > 0 && _groupStation.TryGetValue(trip.Group, out station)) { }
            else
            {
                _regionOfStation.Clear();
                for (int i = 0; i < _doors.Sites.Count; i++) _regionOfStation.Add(StationBusy(i) ? -1 : _doors.Sites[i].Region);
                station = ElevatorTravel.ArrivalStation(region, _regionOfStation, _rng);
                // shortcut: every station of the region busy -> back up the same shaft; a queue if this ever matters.
                if (station < 0) { station = trip.From; region = from; }
                if (trip.Group > 0) _groupStation[trip.Group] = station;
            }
            trip.To = station;
            trip.SwitchAt = now;
            ServerSetCycle(station, ElevatorCycleKind.Arrive, now);
            var site = _doors.Sites[station];
            if (!trip.Rider.IsPlayer)
            {
                trip.Rider.transform.SetPositionAndRotation(site.Spawn + Vector3.down * DeploymentRules.Current.CarStartDepth,
                    Quaternion.Euler(0f, site.YawDeg, 0f));
            }
            Send(trip.Rider, new ElevatorRideStatus
            {
                Kind = ElevatorRideStatus.KindArrived, Station = station, Region = region,
                Seconds = DeploymentRules.Current.TravelCooldownSeconds,
            });
        }

        void Send(Combatant rider, ElevatorRideStatus status)
        {
            if (!rider.IsPlayer) return;
            var owner = rider.Owner;
            if (owner == null || !owner.IsActive || !owner.IsAuthenticated) return;
            _network.ServerManager.Broadcast(owner, status);
        }
    }
}
