using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;

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
        public int Station;   // Started: where the ride began; Arrived: where it comes out
        public int Region;    // Started: the chosen destination; Arrived: the region it came out in
        public float Seconds; // Started: ride time; Arrived: cooldown before the next ride
    }

    // Region-to-region elevator rides during the match (ADR-076). Lives next to ElevatorDoors (added by it), so only
    // scenes with elevators have rides. Rules are ElevatorTravel's; the region states come from ZoneDirector (all
    // safe without a lockdown). Movement is owner-authoritative for players (ADR-029), so the server decides and
    // the rider's own client moves its body: it waits in the departure shaft's pit (under the car floor, out of
    // sight and reach) for the ride, then stands on the arrival station's floor.
    // shortcut: players only; bots never ride (BotController would need a reason to), add when bots should.
    public class ElevatorRides : MonoBehaviour
    {
        // Feet above the shaft's pit floor while waiting out the ride.
        const float PitLift = 0.2f;
        // How far above or below the station floor a combatant may be and still be in the station.
        const float StationHeight = 2f;

        public static ElevatorRides Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        struct Trip
        {
            public Combatant Rider;
            public int From, ToRegion;
            public double ArriveAt;
        }

        ElevatorDoors _doors;
        NetworkManager _network;
        bool _bound;
        System.Random _rng = new System.Random(76);

        // ---- Server ----
        readonly Dictionary<int, Trip> _trips = new Dictionary<int, Trip>();
        readonly Dictionary<int, double> _arrivedAt = new Dictionary<int, double>();
        readonly List<int> _due = new List<int>();
        readonly List<ZoneState> _states = new List<ZoneState>();
        readonly List<int> _regionOfStation = new List<int>();

        // ---- Local rider (this peer's own player) ----
        bool _pending;
        bool _riding;
        int _rideFrom = -1, _rideRegion = -1;
        double _rideArriveAt, _readyAt = double.NegativeInfinity;
        bool _arrivalWaiting;
        Vector3 _arrival;
        float _arrivalYaw;

        public bool LocalPending => _pending;
        // In transit: no moving or firing; the body waits in the pit until LocalTakeArrival.
        public bool LocalRiding => _riding;
        public int LocalDestination => _rideRegion;
        public float LocalSecondsLeft => _riding ? Mathf.Max(0f, (float)(_rideArriveAt - Time.timeAsDouble)) : 0f;
        public float LocalCooldownLeft => Mathf.Max(0f, (float)(_readyAt - Time.timeAsDouble));

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
            _network.ServerManager.UnregisterBroadcast<ElevatorRideRequest>(OnRequest);
        }

        public void Bind(NetworkManager network)
        {
            if (_bound || network == null) return;
            _bound = true;
            _network = network;
            _network.ClientManager.RegisterBroadcast<ElevatorRideStatus>(OnStatus);
            _network.ServerManager.RegisterBroadcast<ElevatorRideRequest>(OnRequest);
        }

        // ---- Shared reads ----

        public int RegionCount
        {
            get
            {
                var zones = ZoneDirector.Instance;
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
            var zones = ZoneDirector.Instance;
            for (int r = 0, n = RegionCount; r < n; r++) result.Add(zones != null ? zones.StateOf(r) : ZoneState.Safe);
        }

        // The station a combatant at `at` stands in, or -1.
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

        public int RegionOfStation(int station) => _doors != null && station >= 0 && station < _doors.Sites.Count ? _doors.Sites[station].Region : -1;

        // Rides only after the deployment doors opened on this peer (ElevatorDoors.DoorsClosed covers the wait).
        public bool Running => _doors != null && !_doors.DoorsClosed;

        // ---- Client: the local player ----

        // Asks to ride from the station the local player stands in. False when nothing was sent.
        public bool RequestRide(int station, int region)
        {
            if (_pending || _riding || _network == null || !_network.IsClientStarted) return false;
            _pending = true;
            _network.ClientManager.Broadcast(new ElevatorRideRequest { Station = station, Region = region });
            return true;
        }

        // Where the local player's feet go this frame while riding (the departure pit). False when not riding.
        public bool TryLocalHold(out Vector3 feet)
        {
            feet = default;
            if (!_riding || _doors == null || _rideFrom < 0 || _rideFrom >= _doors.Sites.Count) return false;
            feet = _doors.Sites[_rideFrom].Spawn + Vector3.down * (DeploymentRules.Current.ShaftDepth - PitLift);
            return true;
        }

        // Once: where the local player comes out (the arrival station's floor). PlayerController moves the body.
        public bool LocalTakeArrival(out Vector3 feet, out float yawDeg)
        {
            feet = _arrival;
            yawDeg = _arrivalYaw;
            if (!_arrivalWaiting) return false;
            _arrivalWaiting = false;
            return true;
        }

        void OnStatus(ElevatorRideStatus status, Channel channel)
        {
            _pending = false;
            switch (status.Kind)
            {
                case ElevatorRideStatus.KindStarted:
                    _riding = true;
                    _rideFrom = status.Station;
                    _rideRegion = status.Region;
                    _rideArriveAt = Time.timeAsDouble + status.Seconds;
                    break;
                case ElevatorRideStatus.KindArrived:
                    _riding = false;
                    _rideRegion = status.Region;
                    _readyAt = Time.timeAsDouble + status.Seconds;
                    if (_doors != null && status.Station >= 0 && status.Station < _doors.Sites.Count)
                    {
                        _arrival = _doors.Sites[status.Station].Spawn + Vector3.up * ElevatorCar.RideLift;
                        _arrivalYaw = _doors.Sites[status.Station].YawDeg;
                        _arrivalWaiting = true;
                    }
                    break;
            }
        }

        // ---- Server ----

        bool ServerActive => _network != null && _network.IsServerStarted;

        // Server: a combatant is between stations (tests, HUD on the host).
        public bool ServerRiding(Combatant c) => c != null && _trips.ContainsKey(c.ObjectId);

        // Server: starts a ride for `rider` if the rules allow it. Public for tests; players come through OnRequest.
        public bool ServerTryStart(Combatant rider, int station, int region)
        {
            if (rider == null || rider.Dead.Value || rider.Knocked.Value || _doors == null || !ServerActive) return false;
            if (!_doors.ServerScheduled || _doors.ServerDoorsClosed) return false;
            if (MatchDirector.Instance != null && MatchDirector.Instance.State != null && MatchDirector.Instance.State.IsOver) return false;
            if (_trips.ContainsKey(rider.ObjectId)) return false;
            if (StationAt(rider.transform.position) != station || station < 0) return false;
            var rules = DeploymentRules.Current;
            double now = Time.timeAsDouble;
            double arrived = _arrivedAt.TryGetValue(rider.ObjectId, out var t) ? t : double.NegativeInfinity;
            if (ElevatorTravel.CooldownLeft(now, arrived, rules) > 0f) return false;
            States(_states);
            if (!ElevatorTravel.CanRide(RegionOfStation(station), region, _states)) return false;
            _trips[rider.ObjectId] = new Trip { Rider = rider, From = station, ToRegion = region, ArriveAt = now + rules.TravelSeconds };
            Send(rider, new ElevatorRideStatus { Kind = ElevatorRideStatus.KindStarted, Station = station, Region = region, Seconds = rules.TravelSeconds });
            return true;
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

        void Update()
        {
            if (_trips.Count == 0 || !ServerActive) return;
            double now = Time.timeAsDouble;
            _due.Clear();
            foreach (var kv in _trips) if (kv.Value.ArriveAt <= now || kv.Value.Rider == null) _due.Add(kv.Key);
            if (_due.Count == 0) return;
            States(_states);
            _regionOfStation.Clear();
            foreach (var s in _doors.Sites) _regionOfStation.Add(s.Region);
            foreach (int id in _due)
            {
                var trip = _trips[id];
                _trips.Remove(id);
                if (trip.Rider == null || trip.Rider.Dead.Value) continue;
                int from = RegionOfStation(trip.From);
                int region = ElevatorTravel.ArrivalRegion(from, trip.ToRegion, _states);
                int station = ElevatorTravel.ArrivalStation(region, _regionOfStation, _rng);
                if (station < 0) { station = trip.From; region = from; }
                _arrivedAt[id] = now;
                Send(trip.Rider, new ElevatorRideStatus
                {
                    Kind = ElevatorRideStatus.KindArrived, Station = station, Region = region,
                    Seconds = DeploymentRules.Current.TravelCooldownSeconds,
                });
            }
        }

        void Send(Combatant rider, ElevatorRideStatus status)
        {
            var owner = rider.Owner;
            if (owner == null || !owner.IsActive || !owner.IsAuthenticated) return;
            _network.ServerManager.Broadcast(owner, status);
        }
    }
}
