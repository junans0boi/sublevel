using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // What the driver asks of the vehicle this frame (owner side).
    public struct DriverInput
    {
        public float Throttle;    // W +1 / S -1
        public float Steer;       // D +1 / A -1
        public bool Handbrake;    // Space held (on the ground)
        public bool Boost;        // Left Shift held
        public bool Reloading;    // the driver is reloading the pistol: one hand off the wheel, steering is slower (ADR-111)
        public float Pitch;       // motorcycle in the air: Space +1 nose up, Ctrl -1 nose down
        public float Roll;        // motorcycle in the air: E +1 right, Q -1 left
    }

    // A drivable vehicle (ADR-065 (5)): the car and the motorcycle share this one component; VehicleKind picks the
    // rules block and turns on the two-wheel parts (Vehicle.TwoWheels.cs). One NetworkObject per vehicle.
    //
    // Physics: arcade raycast wheels on a Rigidbody instead of WheelColliders. Each wheel sphere-casts down from the
    // top of its suspension; a spring-damper holds the body up, and the tyre pushes along and across its own axes at
    // the wheel centre, never more than grip x load (a friction circle). Every number is data, the forces are plain
    // F = m * a terms whose stability does not depend on the fixed rate, and nothing needs friction-curve tuning that
    // can only be judged by driving (WheelColliders would, and they misbehave on a two-wheeler). Top speed depends on
    // the surface: roads and anything built count as road; open terrain off the road map (VehicleSpawner) is slower.
    //
    // Authority: whoever simulates is the only side with a dynamic Rigidbody — the driver's client (it is given
    // ownership on taking the driver's seat and loses it on leaving it), or the server while nobody drives. The
    // NetworkTransform (client authoritative) carries the result everywhere else, where the body is kinematic. When
    // the driver leaves the seat the server takes over with the velocity it last saw: the vehicle rolls on (slight drag
    // with passengers still in it, brakes on when it is empty).
    //
    // Seats: one SyncVar per seat holds the ObjectId of the occupant (-1 = free). Only the server writes them, after
    // checking distance and that the seat is free. Every side turns them into its own attachment table (VehicleSeat)
    // and places the occupants' bodies on the seats itself, so the bodies sit still in the vehicle on every screen.
    //
    // Extension points for fuel, damage, tyres and readouts: VehicleState on the same object (docs/handoff/VEHICLE_API.md).
    [DefaultExecutionOrder(-100)] // occupants are placed before the controllers and the animation look at them
    [RequireComponent(typeof(Rigidbody))]
    public partial class Vehicle : NetworkBehaviour
    {
        public const int MaxSeats = 4;
        // Car wheel order; a motorcycle has 0 = front, 1 = rear.
        public const int FrontLeft = 0, FrontRight = 1, RearLeft = 2, RearRight = 3;
        const float EnterDoorShare = 0.4f;   // of getting in spent reaching the door, the rest sliding onto the seat
        const float ExitGroundProbe = 6f;
        const float ExitCapsuleRadius = 0.33f, ExitCapsuleHeight = 1.8f, ExitCapsuleLift = 0.15f;
        const float RoofExitHeight = 1.8f;
        const float SpeedSmoothing = 10f;
        const float SteerShownSmoothing = 8f;
        const float MaxRightingSpeed = 2f;
        const float DriverStateSendSeconds = 0.25f;
        const float ImpactCooldownSeconds = 0.3f;
        const float MaxReportedSpeed = 60f;

        [SerializeField] VehicleKind _kind;
        // The visual root (everything drawn that is not a wheel). A model builder may replace its children; the
        // motorcycle leans and wheelies this transform.
        [SerializeField] Transform _model;
        [SerializeField] VehicleSeat[] _seats = new VehicleSeat[0];
        // Each wheel's pivot, built at the wheel centre with the vehicle resting on level ground. Steering turns the
        // front pivots; the spin part (a child) turns as the wheel rolls.
        [SerializeField] Transform[] _wheelPivots = new Transform[0];
        [SerializeField] Transform[] _wheelSpin = new Transform[0];
        [SerializeField] bool[] _frontWheel = new bool[0];
        [SerializeField] float _wheelbase = 2.6f;
        [SerializeField] float _halfLength = 2.2f; // nose (or tail) from the centre, for the exit points ahead and behind
        // This model's mass, wheel size and balance over its type's rules (#89); zeros keep the type's numbers.
        [SerializeField] VehicleFit _fit;
        [SerializeField] float _centreOfMassZ;

        public readonly SyncVar<int> Seat0 = new SyncVar<int>(-1);
        public readonly SyncVar<int> Seat1 = new SyncVar<int>(-1);
        public readonly SyncVar<int> Seat2 = new SyncVar<int>(-1);
        public readonly SyncVar<int> Seat3 = new SyncVar<int>(-1);
        // Z turns it off and on; W restarts it. On when a driver gets in (if VehicleState allows), off when they get out.
        public readonly SyncVar<bool> EngineOn = new SyncVar<bool>();

        public static readonly List<Vehicle> All = new List<Vehicle>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        static readonly RaycastHit[] WheelHits = new RaycastHit[8];
        static readonly RaycastHit[] GroundHits = new RaycastHit[8];
        static readonly Collider[] Overlaps = new Collider[16];
        readonly List<VehicleSeat.Occupancy> _occupants = new List<VehicleSeat.Occupancy>();
        readonly int[] _seatIds = new int[MaxSeats];
        // Seats the server has said were left (RpcLeft) while this side's copy of the seat still shows the occupant.
        readonly int[] _pendingClear = { -1, -1, -1, -1 };

        Rigidbody _body;
        VehicleState _state;
        Collider[] _colliders = new Collider[0];
        Vector3[] _anchorLocal = new Vector3[0]; // top of each wheel's suspension, in the wheel pivot's parent space (visuals)
        Vector3[] _anchorRoot = new Vector3[0];  // the same in the vehicle's own space as built (physics: never leans)
        float[] _compression = new float[0];
        bool[] _grounded = new bool[0];
        RaycastHit[] _hits = new RaycastHit[0];
        bool _simulating;
        float _defaultDrag;

        DriverInput _input;
        float _inputAt = float.NegativeInfinity;
        // Server: the driver's throttle and boost as last reported (for fuel use and engine sound, VehicleState users).
        float _serverThrottle;
        bool _serverBoost;
        float _serverInputAt = float.NegativeInfinity;
        float _sentThrottle = float.NaN;
        bool _sentBoost;
        float _sentAt = float.NegativeInfinity;

        float _steerDeg;            // simulating side: the front wheels' angle
        float _steerShown;          // every side: what the wheels show
        bool _offRoad;
        bool _drowned;
        bool _revving;
        float _launchUntil = float.NegativeInfinity;
        float _lastAccel;
        float _flippedFor;
        bool _righting;
        float _rightingLifted;
        float _nextImpactAt;
        Vector3 _estimatedVelocity; // non-simulating side: from the replicated motion, handed to physics on taking over
        Vector3 _lastFixedPosition;
        Vector3 _speedFrom;
        float _lastYaw;
        float _spin;

        // ---- Read-only state ----

        public VehicleKind Kind => _kind;
        public bool IsMotorcycle => _kind == VehicleKind.Motorcycle;
        // In the waiting island's race hall (ADR-508): no damage, no fuel use, no crash ejection, no roadkill. Every side judges it from the place.
        public bool IsRaceCar => EventRace || RaceTrack.InArea(transform.position.x, transform.position.z);
        // An event race scene is running (ADR-516): every vehicle is a race car, and nobody gets out. Set by the scene's RaceDirector on every side.
        public static bool EventRace;
        // Server: an event race's bot drives this car with no body at the wheel (ADR-517): its input counts as a driver's.
        public bool Autopilot { get; set; }
        public VehicleRules Rules
        {
            get
            {
                var rules = VehicleRules.For(_kind);
                if (!_fit.Any) return rules;
                // Race cars keep the type's engine and tyres whatever the model (ADR-557): a mixed grid stays fair.
                bool handling = !IsRaceCar;
                // Tuned again whenever the data loader replaces the type's rules.
                if (!ReferenceEquals(_tunedFrom, rules) || _tunedHandling != handling)
                {
                    _tuned = _fit.Apply(rules, handling);
                    _tunedFrom = rules;
                    _tunedHandling = handling;
                }
                return _tuned;
            }
        }
        VehicleRules _tuned, _tunedFrom;
        bool _tunedHandling;
        GearboxState _gearbox;

        // The automatic gearbox (ADR-558): the simulating side steps it with the driver's throttle, every other side with the
        // throttle read off the speed, so the HUD, the engine note and the camera all read one gear and one needle.
        public int Gear => _gearbox.Gear;
        public float EngineRpm => _gearbox.Rpm;
        public float RedlineRpm => Rules.Gearbox != null ? Rules.Gearbox.RedlineRpm : 7000f;
        public bool Shifting => _gearbox.Shifting;
        public int ShiftCount => _gearbox.ShiftCount;
        public int LastShift => _gearbox.LastShift;
        float _driftUntil = float.NegativeInfinity; // simulating side: Space let go at this time + DriftRecoverSeconds
        // The front wheels' angle as shown on this side (degrees, positive = right): the steering wheel follows it.
        public float SteerShown => _steerShown;
        public VehicleState State => _state;
        public Transform Model => _model;
        public string ModelName => _fit.Model;   // the prefab's name: its row in VehicleHandling (handling, cluster)
        public int SeatCount => Mathf.Min(_seats.Length, MaxSeats);
        public VehicleSeat Seat(int index) => index >= 0 && index < _seats.Length ? _seats[index] : null;
        public int WheelCount => _wheelPivots.Length;
        public Transform WheelPivot(int i) => i >= 0 && i < _wheelPivots.Length ? _wheelPivots[i] : null;
        public bool IsFrontWheel(int i) => i >= 0 && i < _frontWheel.Length && _frontWheel[i];
        // Every solid collider of the vehicle (body parts and any hitboxes a damage system adds as children).
        public IReadOnlyList<Collider> Colliders => _colliders;
        // Signed speed along the vehicle's facing, measured from its motion on every side (m/s).
        public float Speed { get; private set; }
        public float SpeedKmh => Vehicles.ToKmh(Mathf.Abs(Speed));
        public bool OffRoad => _offRoad;
        public bool Drowned => _drowned;
        public bool Righting => _righting;
        // All wheels off the ground (simulating side).
        public bool Airborne { get; private set; }
        // This side runs the physics (the driver's client, or the server with nobody driving).
        public bool Simulating => _simulating;
        public Rigidbody Body => _body;
        // Server: what the driver last reported (0 / false when nobody drives).
        public float ServerThrottle => Seat0.Value >= 0 && Time.time - _serverInputAt < 1f ? _serverThrottle : 0f;
        public bool ServerBoost => Seat0.Value >= 0 && Time.time - _serverInputAt < 1f && _serverBoost;

        // ObjectId of the combatant in a seat, -1 when free.
        public int OccupantId(int seat)
        {
            if (seat < 0 || seat >= SeatCount) return -1;
            switch (seat)
            {
                case 0: return Seat0.Value;
                case 1: return Seat1.Value;
                case 2: return Seat2.Value;
                case 3: return Seat3.Value;
                default: return -1;
            }
        }

        public bool IsFree(int seat) => seat >= 0 && seat < SeatCount && OccupantId(seat) < 0;

        public bool HasFreeSeat
        {
            get
            {
                for (int i = 0; i < SeatCount; i++) if (IsFree(i)) return true;
                return false;
            }
        }

        public bool AnyOccupant
        {
            get
            {
                for (int i = 0; i < SeatCount; i++) if (OccupantId(i) >= 0) return true;
                return false;
            }
        }

        // Where an occupant of `seat` steps in and out (on the ground beside the door / on that side of the saddle).
        public Vector3 DoorPosition(int seat) => Seat(seat) != null ? Seat(seat).Door.position : transform.position;

        // How far `point` is from getting in at `seat`: the distance to its door, but standing against the body (nose, tail,
        // the far side) counts as at the door (#190: the low sports car could not be boarded from its nose).
        const float BodyReach = 1.2f;
        public float EnterDistance(Vector3 point, int seat, float slack = 0f)
        {
            float d = Vector3.Distance(point, DoorPosition(seat));
            float range = Rules.EnterRange;
            if (d <= range + slack) return d;
            foreach (var c in _colliders)
                if (c != null && Vector3.Distance(c.ClosestPoint(point), point) <= BodyReach + slack) return range;
            return d;
        }

        // The nearest door of a free seat within `range` of `point`: the vehicle to get on with F. Null if none.
        public static Vehicle NearestEnterable(Vector3 point, float range, out float distance)
        {
            Vehicle best = null;
            distance = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                var v = All[i];
                if (v == null || !v.HasFreeSeat || v.Disabled) continue;
                for (int s = 0; s < v.SeatCount; s++)
                {
                    float d = v.EnterDistance(point, s);
                    if (d <= range && d < distance) { distance = d; best = v; }
                }
            }
            return best;
        }

        bool Disabled => _state != null && _state.Disabled;

        // ---- Life cycle ----

        void Awake()
        {
            _ = GameData.Rules; // loads the data (and VehicleRules.Current / Motorcycle) if nothing has yet
            var rules = Rules;
            _body = GetComponent<Rigidbody>();
            _state = GetComponent<VehicleState>();
            if (_state == null) _state = gameObject.AddComponent<VehicleState>();
            _body.mass = rules.MassKg;
            _body.isKinematic = true; // until this side is known to simulate
            _body.interpolation = RigidbodyInterpolation.None;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _body.angularDamping = 0.4f;
            _defaultDrag = _body.linearDamping;
            _body.centerOfMass = new Vector3(0f, rules.CentreOfMassHeight, _centreOfMassZ);
            if (_model == null) _model = transform.Find("Model");

            var colliders = new List<Collider>();
            foreach (var c in GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger) colliders.Add(c);
            _colliders = colliders.ToArray();
            if (!IsMotorcycle) SetCarInertia();

            int wheels = _wheelPivots.Length;
            float rest = Vehicles.RestLength(rules, wheels);
            _anchorLocal = new Vector3[wheels];
            _anchorRoot = new Vector3[wheels];
            _compression = new float[wheels];
            _grounded = new bool[wheels];
            _hits = new RaycastHit[wheels];
            for (int i = 0; i < wheels; i++)
            {
                var pivot = _wheelPivots[i];
                if (pivot == null) continue;
                _anchorLocal[i] = pivot.localPosition + Vector3.up * rest;
                // The model (a wheel's possible parent) is still in its built pose here.
                var parent = pivot.parent != null ? pivot.parent : transform;
                _anchorRoot[i] = transform.InverseTransformPoint(parent.TransformPoint(_anchorLocal[i]));
            }
            _speedFrom = transform.position;
            _lastFixedPosition = transform.position;
            _lastYaw = transform.eulerAngles.y;
        }

        public override void OnStartNetwork()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        public override void OnStopNetwork() => Shutdown();

        // Scene unloads destroy objects without the network callbacks.
        void OnDestroy() => Shutdown();

        void Shutdown()
        {
            All.Remove(this);
            Combatant.ServerDied -= OnServerDied;
            VehicleSeat.Collect(this, _occupants);
            foreach (var o in _occupants)
            {
                SetIgnored(o, false);
                VehicleSeat.Detach(o, false);
            }
            _occupants.Clear();
        }

        public override void OnStartServer()
        {
            Combatant.ServerDied -= OnServerDied;
            Combatant.ServerDied += OnServerDied;
            NetworkManager.ServerManager.Objects.OnPreDestroyClientObjects += OnOwnerDisconnecting;
        }

        public override void OnStopServer()
        {
            Combatant.ServerDied -= OnServerDied;
            if (NetworkManager != null) NetworkManager.ServerManager.Objects.OnPreDestroyClientObjects -= OnOwnerDisconnecting;
        }

        void Update()
        {
            UpdateAuthority();
            if (IsServerInitialized)
            {
                ServerCheckSeats();
                // Out of fuel (or burning): the engine stops.
                if (EngineOn.Value && !CanRunEngine) EngineOn.Value = false;
            }
            Reconcile();
            PlaceOccupants();
        }

        void FixedUpdate()
        {
            if (!IsSpawned) return;
            float dt = Time.fixedDeltaTime;
            if (_simulating) Simulate(dt);
            else
            {
                // What the replicated motion did since the last step: handed to physics if this side takes over.
                var v = (transform.position - _lastFixedPosition) / Mathf.Max(1e-4f, dt);
                _estimatedVelocity = Vector3.Lerp(_estimatedVelocity, v, 0.5f);
            }
            _lastFixedPosition = transform.position;
            TickRoadkill();
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                var moved = transform.position - _speedFrom;
                float measured = Vector3.Dot(moved, transform.forward) / dt;
                // A teleport is not speed.
                if (moved.sqrMagnitude > 100f) measured = Speed;
                float before = Speed;
                Speed = Mathf.Lerp(Speed, measured, 1f - Mathf.Exp(-SpeedSmoothing * dt));
                ShownAccel = Mathf.Lerp(ShownAccel, (Speed - before) / dt, 1f - Mathf.Exp(-4f * dt));
            }
            _speedFrom = transform.position;
            // Not simulating here (a passenger, another player's car): the gearbox follows the replicated speed.
            var gearRules = Rules.Gearbox;
            if (!_simulating && dt > 0f && gearRules != null)
            {
                bool running = EngineOn.Value && CanRunEngine;
                float readThrottle = ShownAccel > 0.4f ? 1f : ShownAccel < -0.4f && Speed < 0.5f ? -1f : 0f;
                _gearbox = Gearbox.Step(_gearbox, Speed, readThrottle, Vehicles.TopSpeed(_offRoad, true, Rules), running, dt, gearRules);
            }
            if (IsMotorcycle) PlaceModel(dt);
            PlaceOccupants();
            PlaceWheels(dt);
        }

        // Smoothed forward acceleration as seen on this side (m/s²), for the motorcycle's wheelie.
        public float ShownAccel { get; private set; }

        // ---- Authority ----

        bool ShouldSimulate => IsSpawned && (IsOwner || (IsServerInitialized && !Owner.IsValid));

        void UpdateAuthority()
        {
            bool simulate = ShouldSimulate;
            if (simulate == _simulating) return;
            _simulating = simulate;
            _body.isKinematic = !simulate;
            _body.interpolation = simulate ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            if (simulate)
            {
                // Carry on from the motion this side has been watching (the driver got out at speed).
                _body.linearVelocity = _estimatedVelocity;
                _body.angularVelocity = Vector3.zero;
                _steerDeg = _steerShown;
            }
        }

        // The driver's input (owner side), every frame. Used where the vehicle is simulated; the throttle and boost
        // also go to the server a few times a second (ServerThrottle / ServerBoost, for fuel use).
        public void SetDriverInput(in DriverInput input)
        {
            _input = input;
            _input.Throttle = Mathf.Clamp(input.Throttle, -1f, 1f);
            _input.Steer = Mathf.Clamp(input.Steer, -1f, 1f);
            _inputAt = Time.time;
            if (IsServerInitialized)
            {
                _serverThrottle = _input.Throttle;
                _serverBoost = input.Boost;
                _serverInputAt = Time.time;
                return;
            }
            bool changed = _input.Throttle != _sentThrottle || input.Boost != _sentBoost;
            if (!changed && Time.time - _sentAt < DriverStateSendSeconds * 2f) return;
            if (Time.time - _sentAt < DriverStateSendSeconds) return;
            _sentThrottle = _input.Throttle;
            _sentBoost = input.Boost;
            _sentAt = Time.time;
            CmdDriverState((sbyte)Mathf.RoundToInt(_input.Throttle * 100f), input.Boost);
        }

        // ---- Physics (simulating side only) ----

        bool CanRunEngine => _state == null || Fuel.EngineCanRun(_state.Fuel01, _state.Disabled);

        void Simulate(float dt)
        {
            var r = Rules;
            bool driver = (Seat0.Value >= 0 || Autopilot) && Time.time - _inputAt <= r.InputStaleSeconds && !Disabled;
            bool engine = EngineOn.Value && CanRunEngine;
            var input = driver ? _input : default;

            float water = VehicleSpawner.WaterLevel;
            _drowned = !float.IsNegativeInfinity(water) && Vehicles.IsDrowned(water, transform.position.y, r);
            _body.linearDamping = _drowned ? r.DrownedDrag : _defaultDrag;

            var up = transform.up;
            var forward = transform.forward;
            float forwardSpeed = Vector3.Dot(_body.linearVelocity, forward);

            float throttle = input.Throttle;
            // Engine off (or under water): no pull; S still brakes while rolling forward.
            if (!engine || _drowned) throttle = throttle < 0f && forwardSpeed > 0.5f ? throttle : 0f;
            bool boost = engine && input.Boost && throttle > 0f;
            // Handbrake on the ground only (in the air Space pitches a motorcycle).
            bool handbrake = input.Handbrake && !Airborne;
            if (handbrake) _driftUntil = Time.time + r.DriftRecoverSeconds;
            // 1 the moment Space is let go, 0 once the rear tyres have their grip back.
            float drift = handbrake ? 1f : Mathf.Clamp01((_driftUntil - Time.time) / Mathf.Max(0.01f, r.DriftRecoverSeconds));

            // Launch: W + Shift + Space held revs on the spot; letting go of Space launches at full pull.
            bool revving = engine && input.Throttle > 0f && input.Boost && input.Handbrake && Mathf.Abs(forwardSpeed) < 1f;
            if (revving) _revving = true;
            else if (_revving)
            {
                _revving = false;
                if (engine && input.Throttle > 0f && !input.Handbrake) _launchUntil = Time.time + r.LaunchSeconds;
            }
            bool launching = Time.time < _launchUntil && throttle > 0f;

            float topFactor = _state != null ? _state.TopSpeedFactor : 1f;
            float top = Vehicles.TopSpeed(_offRoad, boost, r) * topFactor;
            float reverseTop = Vehicles.ReverseTopSpeed(_offRoad, r) * topFactor;

            float handsOn = Vehicles.SteerHandsFactor(input.Reloading, r);
            float limit = Vehicles.SteerLimit(forwardSpeed, Vehicles.TopSpeed(false, r), r) * handsOn * (handbrake ? r.HandbrakeSteerBoost : 1f);
            float target = input.Steer * limit + (_state != null ? _state.SteerPullDeg : 0f);
            bool returning = Mathf.Abs(target) < Mathf.Abs(_steerDeg) || target * _steerDeg < 0f;
            _steerDeg = Mathf.MoveTowards(_steerDeg, target, (returning ? r.SteerReturnDegPerSecond : r.SteerDegPerSecond) * handsOn * dt);

            float accel;
            if (driver && !_drowned) accel = Vehicles.DriveAccel(forwardSpeed, throttle, top, reverseTop, dt, r, boost ? r.BoostAccelFactor : 1f);
            else accel = Vehicles.BrakeAccel(forwardSpeed, _drowned ? r.BrakeDecel : AnyOccupant ? r.NoDriverDecel : r.AbandonedBrakeDecel, dt);
            if (launching && forwardSpeed < top) accel = Mathf.Max(accel, r.EngineAccel * r.LaunchAccelFactor);
            if (revving) accel = 0f;
            if (r.Gearbox != null)
            {
                _gearbox = Gearbox.Step(_gearbox, forwardSpeed, revving ? 0f : throttle, top, engine && !_drowned, dt, r.Gearbox);
                // Mid-shift the engine lets go for a moment: the pull drops, braking does not.
                if (accel * forwardSpeed > 0f && Mathf.Abs(throttle) > 0.05f) accel *= Gearbox.Pull(_gearbox, r.Gearbox);
            }
            _lastAccel = accel;
            bool hold = (Mathf.Abs(throttle) < 0.05f || revving) && Mathf.Abs(forwardSpeed) < r.HoldSpeed;

            int wheels = _anchorLocal.Length;
            int grounded = 0, offRoadWheels = 0;
            for (int i = 0; i < wheels; i++)
            {
                _grounded[i] = CastWheel(i, out _hits[i]);
                if (!_grounded[i]) { _compression[i] = 0f; continue; }
                grounded++;
                if (_hits[i].collider is TerrainCollider && !VehicleSpawner.OnRoad(_hits[i].point.x, _hits[i].point.z)) offRoadWheels++;
            }
            _offRoad = grounded > 0 && offRoadWheels * 2 >= grounded;
            bool wasAirborne = Airborne;
            Airborne = wheels > 0 && grounded == 0;

            float massShare = _body.mass / Mathf.Max(1, wheels);
            float gripRate = Mathf.Min(r.SideGripPerSecond, 1f / dt);
            var gravity = Physics.gravity;
            var groundNormal = Vector3.zero;
            for (int i = 0; i < wheels; i++)
            {
                if (!_grounded[i]) continue;
                var hit = _hits[i];
                groundNormal += hit.normal;
                float drop = _state != null ? _state.DropOf(i) : 0f;
                float compression = r.SuspensionTravel - hit.distance - drop;
                float compressionSpeed = (compression - _compression[i]) / dt;
                _compression[i] = compression;
                float load = Vehicles.SuspensionForce(compression, compressionSpeed, r);
                var centre = AnchorWorld(i) - up * hit.distance;
                _body.AddForceAtPosition(up * load, centre);

                bool front = IsFrontWheel(i);
                bool rear = !front;
                // The tyre's frame comes from the body, not the hit normal: a sphere cast on bumpy terrain returns normals tilted
                // tens of degrees at the bumps' edges, and on a climb that turned the wheel's upward speed into "sideways sliding"
                // and the car spun round entering a slope (준환 play feedback, VehicleHandlingTests.SlopeEntry_KeepsHeading).
                var wheelForward = front ? Quaternion.AngleAxis(_steerDeg, up) * forward : forward;
                var side = Vector3.Cross(up, wheelForward);
                var v = _body.GetPointVelocity(centre);
                float vSide = Vector3.Dot(v, side);
                float vForward = Vector3.Dot(v, wheelForward);

                // Sideways the tyre takes out sliding and stands against the slope; along, it drives and brakes.
                float sideForce = (-vSide * gripRate - Vector3.Dot(gravity, side)) * massShare;
                float alongForce;
                if (hold) alongForce = (-vForward * gripRate - Vector3.Dot(gravity, wheelForward)) * massShare;
                else
                {
                    alongForce = accel * _body.mass / grounded;
                    if (handbrake && rear)
                        alongForce -= Mathf.Sign(vForward) * Mathf.Min(r.HandbrakeDecel * massShare, Mathf.Abs(vForward) * massShare / dt);
                }
                float rearGrip = IsMotorcycle ? (handbrake ? r.HandbrakeRearGrip : 1f) : Mathf.Lerp(r.RearGripBias, r.HandbrakeRearGrip, drift);
                float grip = r.TyreGrip * (rear ? rearGrip : 1f) * (_state != null ? _state.GripOf(i) : 1f);
                var tyre = new Vector2(sideForce, alongForce);
                float most = grip * load;
                if (tyre.sqrMagnitude > most * most) tyre = tyre.normalized * most;
                // A motorcycle's sideways grip pushes at the centre of mass height (#109): from the wheel centre it rolled
                // the frame out of the turn, further than the model's lean into it. The lean is the model's alone.
                var sideAt = IsMotorcycle ? centre + up * Vector3.Dot(_body.worldCenterOfMass - centre, up) : centre;
                _body.AddForceAtPosition(side * tyre.x, sideAt);
                // Same for drive and brake along the bike (#182): at the wheel centre a hard brake out of reverse (or a hard
                // pull) pitched the frame nose-up and the springs threw it into the air. The wheelie is the model's alone.
                _body.AddForceAtPosition(wheelForward * tyre.y, sideAt);
            }

            if (!IsMotorcycle && grounded >= 3) AssistYaw(forwardSpeed, drift, r);
            if (IsMotorcycle) SimulateTwoWheels(dt, input, wasAirborne, grounded > 0 ? groundNormal.normalized : Vector3.up);
            TickFlip(dt, r);
        }

        // A car's turning inertia as a solid box of its size, not as its colliders' volumes: the low box spans only the
        // axles (approach angle), and the colliders' own inertia left the car light to spin (ADR-557).
        void SetCarInertia()
        {
            Bounds b = default; bool any = false;
            foreach (var c in _colliders)
            {
                if (!(c is BoxCollider box)) continue;
                var cb = new Bounds(transform.InverseTransformPoint(box.transform.TransformPoint(box.center)), Vector3.Scale(box.size, box.transform.lossyScale));
                if (!any) { b = cb; any = true; } else b.Encapsulate(cb);
            }
            if (!any) return;
            float w = b.size.x, h = Mathf.Max(1.2f, b.size.y), l = b.size.z, m = _body.mass;
            _body.inertiaTensor = new Vector3(m / 12f * (h * h + l * l), m / 12f * (w * w + l * l), m / 12f * (w * w + h * h));
            _body.inertiaTensorRotation = Quaternion.identity;
        }

        // Yaw beyond what the front wheels ask for (speed x tan(steer) / wheelbase) plus a margin is damped: steering turns
        // the car, it does not spin it (준환 play feedback). A drift opens the margin so the tail can come round.
        void AssistYaw(float forwardSpeed, float drift, VehicleRules r)
        {
            var up = transform.up;
            float yaw = Vector3.Dot(_body.angularVelocity, up);
            float want = forwardSpeed * Mathf.Tan(_steerDeg * Mathf.Deg2Rad) / Mathf.Max(1f, _wheelbase);
            // ...but never more than the tyres can carry: lateral grip x g over the speed.
            float most = r.TyreGrip * 9.81f / Mathf.Max(3f, Mathf.Abs(forwardSpeed));
            want = Mathf.Clamp(want, -most, most);
            float margin = Mathf.Lerp(r.YawAssistMarginDegPerSecond, r.DriftYawMarginDegPerSecond, drift) * Mathf.Deg2Rad;
            float excess = yaw - want;
            if (Mathf.Abs(excess) <= margin) return;
            excess -= Mathf.Sign(excess) * margin;
            _body.AddTorque(-up * (excess * r.YawAssist), ForceMode.Acceleration);
        }

        Vector3 AnchorWorld(int i) => transform.TransformPoint(_anchorRoot[i]);

        // The nearest ground under a wheel: anything solid but this vehicle and the combatants (a vehicle does not
        // stand on people). False when the wheel hangs free.
        bool CastWheel(int i, out RaycastHit best)
        {
            var r = Rules;
            int count = Physics.SphereCastNonAlloc(AnchorWorld(i), r.WheelRadius, -transform.up, WheelHits, r.SuspensionTravel,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            best = default;
            float nearest = float.MaxValue;
            for (int h = 0; h < count; h++)
            {
                var hit = WheelHits[h];
                // Distance 0: the sphere started inside it (the vehicle's own body, or ground it sank into).
                if (hit.distance <= 0f || hit.distance >= nearest) continue;
                if (hit.collider.attachedRigidbody == _body || hit.collider is CharacterController) continue;
                if (hit.collider.GetComponentInParent<Combatant>() != null) continue;
                nearest = hit.distance;
                best = hit;
            }
            if (nearest < float.MaxValue) return true;
            // Sunk into the ground (a drop from a parking spot that bottomed the springs): the sphere starts inside it, so
            // every hit is at distance 0 and the wheel would never find it again. Ray from just above the anchor instead.
            const float Lift = 0.5f;
            var up = transform.up;
            int rays = Physics.RaycastNonAlloc(AnchorWorld(i) + up * Lift, -up, WheelHits, Lift + r.WheelRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float top = float.MaxValue;
            for (int h = 0; h < rays; h++)
            {
                var hit = WheelHits[h];
                if (hit.distance >= top || hit.collider.attachedRigidbody == _body || hit.collider is CharacterController || hit.collider.GetComponentInParent<Combatant>() != null) continue;
                top = hit.distance;
                best = hit;
            }
            if (top == float.MaxValue) return false;
            best.distance = Mathf.Max(0.001f, top - Lift - r.WheelRadius);
            return true;
        }

        // On its roof or side and (nearly) still for RightingDelaySeconds: it slowly turns back onto its wheels.
        void TickFlip(float dt, VehicleRules r)
        {
            if (_righting)
            {
                var flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(-transform.up, Vector3.up);
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
                var upright = Quaternion.LookRotation(flat.normalized, Vector3.up);
                float angle = Quaternion.Angle(_body.rotation, upright);
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                float lift = 0f;
                if (_rightingLifted < r.RightingLiftMeters && angle > 20f)
                {
                    lift = Mathf.Min(r.RightingLiftMeters - _rightingLifted, r.RightingLiftMeters * 2f * dt);
                    _rightingLifted += lift;
                }
                _body.MovePosition(_body.position + Vector3.up * lift);
                _body.MoveRotation(Quaternion.RotateTowards(_body.rotation, upright, 180f / Mathf.Max(0.1f, r.RightingSeconds) * dt));
                if (angle < 1f)
                {
                    _righting = false;
                    _flippedFor = 0f;
                }
                return;
            }
            if (Vehicles.IsFlipped(transform.up.y, r) && _body.linearVelocity.magnitude < MaxRightingSpeed)
            {
                _flippedFor += dt;
                if (_flippedFor >= r.RightingDelaySeconds)
                {
                    _righting = true;
                    _rightingLifted = 0f;
                }
            }
            else _flippedFor = 0f;
        }

        // Impacts (simulating side): VehicleState.Impact for a damage system; a motorcycle throws its riders off.
        void OnCollisionEnter(Collision collision)
        {
            if (!_simulating || collision.contactCount == 0) return;
            var other = collision.collider;
            if (other is CharacterController || other.GetComponentInParent<Combatant>() != null) return;
            // A car into a door (ADR-147): the server breaks it by the closing speed; a remote driver reports it.
            if (other.GetComponentInParent<Door>() is Door ram && Time.time >= _nextImpactAt)
            {
                float closing = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(0).normal));
                if (closing >= Rules.CollisionMinSpeed || closing >= DoorRules.Current.RamMinSpeed)
                {
                    _nextImpactAt = Time.time + ImpactCooldownSeconds;
                    if (IsServerInitialized) DoorSystem.ServerDamage(ram, Doors.RamDamage(closing, DoorRules.Current));
                    else if (IsOwner) DoorSystem.ClientRam(ram, closing);
                }
            }
            float impact = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(0).normal));
            var r = Rules;
            if (impact < r.CollisionMinSpeed || Time.time < _nextImpactAt) return;
            _nextImpactAt = Time.time + ImpactCooldownSeconds;
            _state?.RaiseImpact(impact, other);
            if (IsMotorcycle && Vehicles.Crashes(impact, r) && AnyOccupant) ReportCrash(impact);
        }

        // ---- Wheels (every side) ----

        float[] _wheelDrop = new float[0];
        Vector3 _wheelsCastAt;
        Quaternion _wheelsCastRot;
        int _wheelsRecastFrame;

        void PlaceWheels(float dt)
        {
            var r = Rules;
            float yaw = transform.eulerAngles.y;
            if (_simulating) _steerShown = _steerDeg;
            else if (dt > 0f)
            {
                // Not replicated: worked out from how fast the vehicle turns at its speed (bicycle model).
                float yawRate = Mathf.DeltaAngle(_lastYaw, yaw) / dt * Mathf.Deg2Rad;
                float speed = Speed;
                float wanted = Mathf.Abs(speed) < 1f ? _steerShown
                    : Mathf.Clamp(Mathf.Atan(yawRate * _wheelbase / speed) * Mathf.Rad2Deg, -r.SteerMaxDeg, r.SteerMaxDeg);
                _steerShown = Mathf.Lerp(_steerShown, wanted, 1f - Mathf.Exp(-SteerShownSmoothing * dt));
            }
            _lastYaw = yaw;
            _spin = Mathf.Repeat(_spin + Speed / Mathf.Max(0.05f, r.WheelRadius) * Mathf.Rad2Deg * dt, 360f);

            // Standing still (most of the ~40 parked vehicles): the ground under the wheels is where it was; look again
            // every 30 frames for something that moved under them (#484).
            bool still = transform.position == _wheelsCastAt && transform.rotation == _wheelsCastRot && Time.frameCount < _wheelsRecastFrame
                && _wheelDrop.Length == _wheelPivots.Length;
            if (!still)
            {
                _wheelsCastAt = transform.position;
                _wheelsCastRot = transform.rotation;
                _wheelsRecastFrame = Time.frameCount + 30;
                if (_wheelDrop.Length != _wheelPivots.Length) _wheelDrop = new float[_wheelPivots.Length];
            }
            for (int i = 0; i < _wheelPivots.Length && i < _anchorLocal.Length; i++)
            {
                var pivot = _wheelPivots[i];
                if (pivot == null) continue;
                if (!still) _wheelDrop[i] = CastWheel(i, out var hit) ? hit.distance : r.SuspensionTravel;
                float drop = _wheelDrop[i];
                if (_state != null) drop += _state.DropOf(i);
                pivot.localPosition = _anchorLocal[i] - Vector3.up * drop;
                pivot.localRotation = IsFrontWheel(i) ? Quaternion.Euler(0f, _steerShown, 0f) : Quaternion.identity;
                if (i < _wheelSpin.Length && _wheelSpin[i] != null) _wheelSpin[i].localRotation = Quaternion.Euler(_spin, 0f, 0f);
            }
        }

        // ---- Occupants (every side) ----

        // The combatant with this network id as the manager this vehicle lives in holds it (a process with several managers,
        // the two-client tests, has one copy of everyone in each; Combatant.All lists them all).
        internal Combatant FindCombatant(int objectId)
        {
            if (objectId < 0) return null;
            var nm = NetworkManager;
            if (nm != null)
            {
                var spawned = IsServerInitialized ? nm.ServerManager.Objects.Spawned : nm.ClientManager.Objects.Spawned;
                if (spawned.TryGetValue(objectId, out var nob) && nob != null)
                {
                    var found = nob.GetComponent<Combatant>();
                    if (found != null) return found;
                }
            }
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].ObjectId == objectId) return all[i];
            return null;
        }

        // Brings this side's attachment table in line with the replicated seats.
        void Reconcile()
        {
            var r = Rules;
            int seats = SeatCount;
            for (int s = 0; s < seats; s++)
            {
                int id = OccupantId(s);
                if (_pendingClear[s] >= 0 && _pendingClear[s] != id) _pendingClear[s] = -1;
                _seatIds[s] = id == _pendingClear[s] ? -1 : id;
            }
            for (int s = 0; s < seats; s++)
            {
                var c = FindCombatant(_seatIds[s]);
                if (c == null) continue; // not spawned on this side yet; tried again next frame
                var o = VehicleSeat.Find(c);
                if (o != null && o.Vehicle == this && o.Phase != VehicleSeat.Phase.Leaving)
                {
                    if (o.Seat != s)
                    {
                        o.Seat = s;
                        VehicleSeat.Begin(o, VehicleSeat.Phase.Switching, r.SwitchSeconds);
                    }
                    continue;
                }
                // Getting in (or straight from another vehicle, which this side never saw let go).
                if (o != null)
                {
                    if (o.Vehicle != null) o.Vehicle.SetIgnored(o, false);
                    VehicleSeat.Detach(o, false);
                }
                o = VehicleSeat.Attach(c, this, s, VehicleSeat.Phase.Entering, r.EnterSeconds);
                SetIgnored(o, true);
                _state?.RaiseEntered(c, s);
            }
            VehicleSeat.Collect(this, _occupants);
            foreach (var o in _occupants)
            {
                if (o.Phase == VehicleSeat.Phase.Leaving || InAnySeat(o.Combatant.ObjectId)) continue;
                // The seat was let go and the server's exit point has not arrived: head for this side's own guess.
                StartLeaving(o, ExitPoint(o.Seat, o.Combatant), false);
            }
        }

        bool InAnySeat(int objectId)
        {
            for (int s = 0; s < SeatCount; s++) if (_seatIds[s] == objectId) return true;
            return false;
        }

        void StartLeaving(VehicleSeat.Occupancy o, Vector3 exit, bool fromServer)
        {
            if (o.Phase == VehicleSeat.Phase.Leaving)
            {
                // Already on the way out: the server's point wins over the local guess, the movement keeps going.
                if (fromServer) { o.ExitWorld = exit; o.ExitFromServer = true; }
                return;
            }
            VehicleSeat.Begin(o, VehicleSeat.Phase.Leaving, Rules.EnterSeconds);
            o.ExitWorld = exit;
            o.ExitFromServer = fromServer;
            _state?.RaiseLeft(o.Combatant, o.Seat);
        }

        // Where the body's origin (the feet of its standing frame) goes for the eyes to be at the seat's marker.
        Vector3 SeatOrigin(int seat, Combatant c)
        {
            var marker = Seat(seat).transform;
            float eye = Vector3.Dot(c.EyePosition - c.transform.position, c.transform.up);
            if (eye < 0.5f) eye = 1.6f;
            return marker.position - marker.up * eye;
        }

        static float Ease(float t) => t * t * (3f - 2f * t);

        void PlaceOccupants()
        {
            VehicleSeat.Collect(this, _occupants);
            foreach (var o in _occupants)
            {
                var c = o.Combatant;
                if (o.Seat < 0 || o.Seat >= SeatCount || Seat(o.Seat) == null) continue;
                KeepIgnored(o);
                var seatRotation = Seat(o.Seat).transform.rotation;
                var seatOrigin = SeatOrigin(o.Seat, c);
                float t = Ease(o.Progress);
                Vector3 position;
                Quaternion rotation;
                switch (o.Phase)
                {
                    case VehicleSeat.Phase.Entering:
                    {
                        // From where they stood to the door, then in onto the seat; all relative to the vehicle so the
                        // path moves with it.
                        var from = transform.TransformPoint(o.FromLocal);
                        var door = DoorPosition(o.Seat);
                        position = t < EnterDoorShare
                            ? Vector3.Lerp(from, door, t / EnterDoorShare)
                            : Vector3.Lerp(door, seatOrigin, (t - EnterDoorShare) / (1f - EnterDoorShare));
                        rotation = Quaternion.Slerp(transform.rotation * o.FromRotation, seatRotation, t);
                        if (o.Progress >= 1f) o.Phase = VehicleSeat.Phase.Seated;
                        break;
                    }
                    case VehicleSeat.Phase.Switching:
                        position = Vector3.Lerp(transform.TransformPoint(o.FromLocal), seatOrigin, t);
                        rotation = Quaternion.Slerp(transform.rotation * o.FromRotation, seatRotation, t);
                        if (o.Progress >= 1f) o.Phase = VehicleSeat.Phase.Seated;
                        break;
                    case VehicleSeat.Phase.Leaving:
                    {
                        // Out through the door to the exit point, ending upright and facing the vehicle's way.
                        var door = DoorPosition(o.Seat);
                        position = t < 1f - EnterDoorShare
                            ? Vector3.Lerp(seatOrigin, door, t / (1f - EnterDoorShare))
                            : Vector3.Lerp(door, o.ExitWorld, (t - (1f - EnterDoorShare)) / EnterDoorShare);
                        var flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                        var upright = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : Quaternion.identity;
                        rotation = Quaternion.Slerp(seatRotation, upright, t);
                        break;
                    }
                    default:
                        position = seatOrigin;
                        rotation = seatRotation;
                        break;
                }
                c.transform.SetPositionAndRotation(position, rotation);
                if (o.Phase == VehicleSeat.Phase.Leaving && o.Progress >= 1f)
                {
                    var upright = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
                    c.transform.SetPositionAndRotation(o.ExitWorld, upright);
                    SetIgnored(o, false);
                    VehicleSeat.Detach(o, true);
                }
            }
        }

        // The occupant's own colliders do not touch the vehicle while attached (its capsule sits inside the body).
        void SetIgnored(VehicleSeat.Occupancy o, bool ignore)
        {
            if (o.Colliders == null) return;
            foreach (var mine in _colliders)
            {
                if (mine == null || !mine.enabled || !mine.gameObject.activeInHierarchy) continue;
                foreach (var theirs in o.Colliders)
                    if (theirs != null && theirs.enabled && theirs.gameObject.activeInHierarchy) Physics.IgnoreCollision(mine, theirs, ignore);
            }
            o.ControllerWasEnabled = o.Controller != null && o.Controller.enabled;
        }

        // Turning a collider off and on again forgets IgnoreCollision: set it again when the controller comes back.
        void KeepIgnored(VehicleSeat.Occupancy o)
        {
            bool enabled = o.Controller != null && o.Controller.enabled;
            if (enabled != o.ControllerWasEnabled) SetIgnored(o, true);
        }

        // ---- Exit point (server decides; clients use the same function as a first guess) ----

        // Feet position beside the seat's door (the side of the seat), on the ground and with room for a standing body.
        // Then the other side, behind, in front, and as a last resort on top.
        public Vector3 ExitPoint(int seat, Combatant leaving)
        {
            var door = Seat(seat) != null ? transform.InverseTransformPoint(DoorPosition(seat)) : new Vector3(-1.5f, 0f, 0f);
            var candidates = new[]
            {
                new Vector3(door.x, 0f, door.z),
                new Vector3(-door.x, 0f, door.z),
                new Vector3(0f, 0f, -_halfLength - 1f),
                new Vector3(0f, 0f, _halfLength + 1f),
            };
            foreach (var local in candidates)
            {
                var world = transform.TransformPoint(local);
                if (FindGround(world, out var feet) && HasRoom(feet, leaving)) return feet;
            }
            return transform.position + Vector3.up * RoofExitHeight;
        }

        bool FindGround(Vector3 around, out Vector3 feet)
        {
            var start = new Vector3(around.x, transform.position.y + 2.5f, around.z);
            int count = Physics.RaycastNonAlloc(start, Vector3.down, GroundHits, ExitGroundProbe, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            feet = around;
            for (int i = 0; i < count; i++)
            {
                var h = GroundHits[i];
                if (h.distance >= nearest || h.collider.attachedRigidbody == _body || h.collider.GetComponentInParent<Combatant>() != null) continue;
                nearest = h.distance;
                feet = h.point + Vector3.up * 0.05f;
            }
            return nearest < float.MaxValue;
        }

        bool HasRoom(Vector3 feet, Combatant leaving)
        {
            var bottom = feet + Vector3.up * (ExitCapsuleRadius + ExitCapsuleLift);
            var top = feet + Vector3.up * (ExitCapsuleHeight - ExitCapsuleRadius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, ExitCapsuleRadius, Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var col = Overlaps[i];
                if (leaving != null && col.transform.IsChildOf(leaving.transform)) continue;
                return false;
            }
            return true;
        }

        // ---- Server ----

        // The server's idea of the vehicle's speed (m/s): its own physics, or the motion it has been receiving.
        internal float ServerSpeed => _simulating ? _body.linearVelocity.magnitude : _estimatedVelocity.magnitude;

        void ServerSetSeat(int seat, int objectId)
        {
            int was = OccupantId(seat);
            switch (seat)
            {
                case 0: Seat0.Value = objectId; break;
                case 1: Seat1.Value = objectId; break;
                case 2: Seat2.Value = objectId; break;
                case 3: Seat3.Value = objectId; break;
                default: return;
            }
            if (seat != Vehicles.DriverSeat || was == objectId) return;
            ServerFollowDriver();
        }

        // The driver's client simulates the vehicle; with nobody at the wheel (or a driver nobody owns) the server does.
        void ServerFollowDriver()
        {
            var driver = FindCombatant(Seat0.Value);
            if (driver != null && driver.Owner.IsValid)
            {
                if (Owner != driver.Owner) GiveOwnership(driver.Owner);
            }
            else if (Owner.IsValid) RemoveOwnership();
        }

        // A connection is going: a vehicle it owns (its driver's) must not be despawned with it, passengers and all.
        void OnOwnerDisconnecting(NetworkConnection connection)
        {
            if (connection != null && Owner == connection) RemoveOwnership();
        }

        // The event race (ADR-514): a racer starts in the driver's seat of their grid car, wherever they stood.
        [Server]
        public bool ServerSeatDriver(Combatant c)
        {
            if (c == null || !c.IsSpawned || c.Dead.Value || VehicleSeat.IsAttached(c) || Disabled || !IsFree(Vehicles.DriverSeat)) return false;
            ServerSeat(c, Vehicles.DriverSeat);
            ServerSetEngine(true);
            return true;
        }

        // Gets `c` in: the free seat with the nearest door (a motorcycle: the driver's seat first). Returns the seat, or -1 when
        // refused (dead, already in a vehicle, no door within EnterRange, full, disabled).
        [Server]
        public int ServerEnter(Combatant c)
        {
            var r = Rules;
            if (c == null || !c.IsSpawned || c.Dead.Value || VehicleSeat.IsAttached(c) || Disabled) return Vehicles.NoSeat;
            int seats = SeatCount;
            var occupants = new int[seats];
            var distances = new float[seats];
            for (int s = 0; s < seats; s++)
            {
                occupants[s] = OccupantId(s);
                distances[s] = EnterDistance(c.transform.position, s, GameData.Rules.Guard.LagSlackMeters);
            }
            int seat = Vehicles.ChooseEnterSeat(occupants, distances, r.EnterRange + GameData.Rules.Guard.LagSlackMeters, IsMotorcycle);
            if (seat < 0)
            {
                if (c.IsPlayer && System.Array.TrueForAll(distances, d => d > r.EnterRange + GameData.Rules.Guard.LagSlackMeters))
                    ServerGuard.Refuse(GuardKind.Vehicle, c, "enter from too far");
                return seat;
            }
            ServerSeat(c, seat);
            // Taking the wheel starts the engine.
            if (seat == Vehicles.DriverSeat) ServerSetEngine(true);
            return seat;
        }

        void ServerSeat(Combatant c, int seat)
        {
            ServerSetSeat(seat, c.ObjectId);
            // Seated is upright: no crouch, prone or lean carried in.
            var stance = c.GetComponent<CombatantStance>();
            if (stance != null && stance.State.Value != 0) stance.State.Value = 0;
            c.ServerSetStance(AimMode.Hip, 0);
            // The driver's hands go to the wheel unless a pistol is already in them.
            if (seat == Vehicles.DriverSeat && c.ActiveWeaponId != 0 && !Vehicles.CanUseWeapon(seat, c.ActiveWeapon)) c.ServerSetHolstered(true);
            Reconcile();
        }

        // Ctrl+1..4: into another free seat (takes SwitchSeconds; no weapon use meanwhile). A driver moving away
        // leaves the vehicle rolling on its momentum.
        [Server]
        public bool ServerSwitch(Combatant c, int to)
        {
            int from = SeatIndexOf(c);
            if (from < 0 || c.Dead.Value) return false;
            int seats = SeatCount;
            if (!InputGuard.ValidSlot(to, seats)) return ServerGuard.Refuse(GuardKind.Vehicle, c, $"seat {to}");
            var occupants = new int[seats];
            for (int s = 0; s < seats; s++) occupants[s] = OccupantId(s);
            if (!Vehicles.CanSwitch(occupants, from, to)) return false;
            ServerSetSeat(from, -1);
            ServerSeat(c, to);
            return true;
        }

        // F in a seat: out beside that seat's door (or wherever there is room). Faster than ExitSafeKmh it hurts like a
        // fall; from ExitLethalKmh on it kills.
        [Server]
        public bool ServerExit(Combatant c) => ServerExit(c, ServerSpeed);

        // Everyone out at once, unhurt (the race cars of the waiting island at the departure, ADR-511).
        [Server]
        public void ServerEjectAll()
        {
            for (int s = 0; s < SeatCount; s++)
            {
                var c = FindCombatant(OccupantId(s));
                if (c != null) ServerExit(c, 0f);
            }
        }

        // The server puts an unmanned vehicle down somewhere, at rest (a race car back on its grid spot, ADR-506).
        [Server]
        public void ServerPlace(Vector3 position, Quaternion rotation) => Place(position, rotation);

        // The side that simulates the vehicle (its driver's client, or the server while nobody drives) puts it down somewhere, at rest: an event
        // racer's R reset (ADR-516). Ignored elsewhere (the next replicated move would undo it).
        public void Place(Vector3 position, Quaternion rotation)
        {
            if (!_simulating && !(IsServerInitialized && !Owner.IsValid)) return;   // the server places only what nobody else simulates
            transform.SetPositionAndRotation(position, rotation);
            _body.position = position; _body.rotation = rotation;
            if (!_body.isKinematic) { _body.linearVelocity = Vector3.zero; _body.angularVelocity = Vector3.zero; }
            // Not motion: no velocity is read off the jump when physics takes over, and the watchers snap there instead of sliding.
            _estimatedVelocity = Vector3.zero; _lastFixedPosition = position; _speedFrom = position;
            if (TryGetComponent<FishNet.Component.Transforming.NetworkTransform>(out var nt)) nt.Teleport();
        }

        bool ServerExit(Combatant c, float speed)
        {
            int seat = SeatIndexOf(c);
            if (seat < 0) return false;
            var exit = ExitPoint(seat, c);
            ServerSetSeat(seat, -1);
            if (seat == Vehicles.DriverSeat) EngineOn.Value = false;
            RpcLeft(c.ObjectId, seat, exit);
            float damage = c.Dead.Value ? 0f : Vehicles.ExitDamage(Vehicles.ToKmh(speed), Rules);
            if (damage > 0f) c.ServerTakeDamage(new DamageInfo { Amount = damage, Zone = HitZone.Body, Cause = DamageCause.Fall });
            return true;
        }

        // Z / W: the engine. Refused while VehicleState forbids it (no fuel, disabled).
        [Server]
        public bool ServerSetEngine(bool on)
        {
            if (on && !CanRunEngine) return false;
            EngineOn.Value = on;
            return true;
        }

        // A hard crash (motorcycle): everyone is thrown off, hurt by the speed, and the machine is left to fall over.
        [Server]
        public void ServerCrash(float speed)
        {
            if (IsRaceCar) return;
            speed = Mathf.Clamp(speed, 0f, MaxReportedSpeed);
            _crashedUntil = Time.time + Rules.CrashRecoverSeconds;
            for (int s = 0; s < SeatCount; s++)
            {
                var c = FindCombatant(OccupantId(s));
                if (c != null) ServerExit(c, speed);
            }
            _state?.RaiseCrashed(speed);
        }

        int SeatIndexOf(Combatant c)
        {
            if (c == null) return -1;
            for (int s = 0; s < SeatCount; s++) if (OccupantId(s) == c.ObjectId) return s;
            return -1;
        }

        // Seats held by someone who is gone (despawned, disconnected) are freed; the dead are put out.
        void ServerCheckSeats()
        {
            ServerFollowDriver(); // also after the driver's connection came back (#166)
            for (int s = 0; s < SeatCount; s++)
            {
                int id = OccupantId(s);
                if (id < 0) continue;
                var c = FindCombatant(id);
                if (c == null || !c.IsSpawned) ServerSetSeat(s, -1);
                else if (c.Dead.Value) ServerExit(c, 0f);
            }
        }

        void OnServerDied(Combatant victim, DamageInfo info)
        {
            if (SeatIndexOf(victim) >= 0) ServerExit(victim, 0f);
        }

        // Every side starts the way out toward the server's exit point (the host runs it at once).
        [ObserversRpc(RunLocally = true)]
        void RpcLeft(int objectId, int seat, Vector3 exit)
        {
            if (seat >= 0 && seat < _pendingClear.Length && OccupantId(seat) == objectId) _pendingClear[seat] = objectId;
            var c = FindCombatant(objectId);
            var o = c != null ? VehicleSeat.Find(c) : null;
            if (o == null || o.Vehicle != this) return;
            StartLeaving(o, exit, true);
        }

        // ---- Requests (owner side): the host is the server and acts at once ----

        public void RequestEnter(Combatant c)
        {
            if (c == null) return;
            if (IsServerInitialized) ServerEnter(c);
            else CmdEnter(c.NetworkObject);
        }

        public void RequestExit(Combatant c)
        {
            if (c == null || EventRace) return;   // no getting out in an event race (ADR-516)
            if (IsServerInitialized) ServerExit(c);
            else CmdExit(c.NetworkObject);
        }

        public void RequestSwitch(Combatant c, int seat)
        {
            if (c == null || EventRace) return;   // the racer stays at the wheel (ADR-516)
            if (IsServerInitialized) ServerSwitch(c, seat);
            else CmdSwitch(c.NetworkObject, seat);
        }

        // Only the driver switches the engine.
        public void RequestEngine(Combatant c, bool on)
        {
            if (c == null) return;
            if (IsServerInitialized) { if (SeatIndexOf(c) == Vehicles.DriverSeat) ServerSetEngine(on); }
            else CmdEngine(c.NetworkObject, on);
        }

        void ReportCrash(float speed)
        {
            _crashedUntil = Time.time + Rules.CrashRecoverSeconds;
            if (IsServerInitialized) ServerCrash(speed);
            else CmdCrash(speed);
        }

        // The asking client must own the combatant it speaks for.
        static Combatant Asker(NetworkObject who, NetworkConnection conn)
        {
            if (who == null || conn == null || who.Owner != conn) return null;
            return who.GetComponent<Combatant>();
        }

        [ServerRpc(RequireOwnership = false)]
        void CmdEnter(NetworkObject who, NetworkConnection conn = null)
        {
            var c = Asker(who, conn);
            if (c != null) ServerEnter(c);
        }

        [ServerRpc(RequireOwnership = false)]
        void CmdExit(NetworkObject who, NetworkConnection conn = null)
        {
            var c = Asker(who, conn);
            if (c != null && !EventRace) ServerExit(c);
        }

        [ServerRpc(RequireOwnership = false)]
        void CmdSwitch(NetworkObject who, int seat, NetworkConnection conn = null)
        {
            var c = Asker(who, conn);
            if (c != null && !EventRace) ServerSwitch(c, seat);
        }

        [ServerRpc(RequireOwnership = false)]
        void CmdEngine(NetworkObject who, bool on, NetworkConnection conn = null)
        {
            var c = Asker(who, conn);
            if (c != null && SeatIndexOf(c) == Vehicles.DriverSeat) ServerSetEngine(on);
        }

        // From the simulating client (the driver owns the vehicle): movement is the owner's (ADR-029), so its crash
        // report is taken like a fall report, capped.
        [ServerRpc(RequireOwnership = false)]
        void CmdCrash(float speed, NetworkConnection conn = null)
        {
            if (conn == null || conn != Owner) return;
            if (float.IsNaN(speed) || speed < 0f) { ServerGuard.Refuse(GuardKind.Arg, null, $"crash speed {speed}"); return; }
            if (!IsMotorcycle) { ServerGuard.Refuse(GuardKind.Vehicle, null, "crash report from a car"); return; }   // only a bike throws its riders
            if (Time.time < _crashedUntil) return;   // one crash per recovery; the riders are already off
            // Held to the speed the server saw (#502): a standing machine cannot throw its riders off at 60 m/s.
            if (TryGetComponent<VehicleDamage>(out var damage)) speed = Mathf.Min(speed, damage.ServerReportCap());
            ServerCrash(speed);
        }

        [ServerRpc(RequireOwnership = false)]
        void CmdDriverState(sbyte throttle, bool boost, NetworkConnection conn = null)
        {
            var driver = FindCombatant(Seat0.Value);
            if (conn == null || driver == null || driver.Owner != conn) return;
            _serverThrottle = Mathf.Clamp(throttle / 100f, -1f, 1f);
            _serverBoost = boost;
            _serverInputAt = Time.time;
        }

        // ---- Editor builder only ----

        public void Configure(VehicleKind kind, Transform model, VehicleSeat[] seats, Transform[] wheelPivots, Transform[] wheelSpin,
            bool[] frontWheel, float wheelbase, float halfLength)
        {
            _kind = kind;
            _model = model;
            _seats = seats;
            _wheelPivots = wheelPivots;
            _wheelSpin = wheelSpin;
            _frontWheel = frontWheel;
            _wheelbase = wheelbase;
            _halfLength = halfLength;
        }

        public void ConfigureFit(VehicleFit fit, float centreOfMassZ)
        {
            _fit = fit;
            _centreOfMassZ = centreOfMassZ;
        }
    }
}
