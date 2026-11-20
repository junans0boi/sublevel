using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Server -> every client, 4 times a second and at every change of phase (ADR-518): where the race is.
    public struct RaceStandings : IBroadcast
    {
        public byte Phase;        // RacePhase
        public float Countdown;   // seconds to GO while counting down
        public float SecondsLeft; // to the end while finishing, to the menu on the results
        public byte Laps;         // laps of this race
    }

    // The event race of a Race_<course> scene (ADR-512..519, #445). Every side: all vehicles of the scene are race cars (Vehicle.EventRace)
    // and the client HUD is attached. Server: each person who arrives gets a car on the next grid slot and is seated at its wheel; the
    // race flow (RaceFlow) waits for everyone, counts down 3 s (input locked) and starts the race; the phase goes out as RaceStandings.
    public sealed class RaceDirector : MonoBehaviour
    {
        public string CourseId = "snake";

        public static RaceDirector Current { get; private set; }
        public RaceEvent.CourseInfo Info { get; private set; }
        public RaceFlow Flow => _flow;
        public int CarCount => _cars.Count;
        public Vehicle CarOf(Combatant c) => c != null && _carOf.TryGetValue(c, out var v) ? v : null;

        NetworkManager _nm;
        RaceFlow _flow;
        readonly List<(float x, float z, float yaw)> _grid = new List<(float, float, float)>();
        readonly List<Vehicle> _cars = new List<Vehicle>();
        readonly Dictionary<Combatant, Vehicle> _carOf = new Dictionary<Combatant, Vehicle>();
        float _nextSend;
        RacePhase _sentPhase = (RacePhase)255;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = null; Vehicle.EventRace = false; }

        void Awake()
        {
            Info = RaceEvent.ById(CourseId) ?? RaceEvent.Courses[0];
            Current = this;
            Vehicle.EventRace = true;
            _grid.AddRange(Info.Course.GridSpots(RaceEvent.GridSlots));
        }

        void OnDestroy()
        {
            if (Current != this) return;   // a newer race scene's director already took over
            Current = null;
            Vehicle.EventRace = false;
        }

        void Start()
        {
            _nm = GetComponent<NetworkManager>();
            if (_nm == null) _nm = InstanceFinder.NetworkManager;
            RaceHud.Attach(_nm);
        }

        void Update()
        {
            if (_nm == null || !_nm.IsServerStarted) return;
            double now = Time.unscaledTimeAsDouble;
            if (_flow == null) _flow = new RaceFlow(now);
            bool everyoneIn = SeatArrivals();
            var before = _flow.Phase;
            _flow.Tick(now, everyoneIn, false);
            if (_flow.Phase != before) Debug.Log($"[RaceDirector] {Info.Name}: {before} -> {_flow.Phase} ({_cars.Count} cars)");
            if (_flow.Phase != _sentPhase || Time.unscaledTime >= _nextSend) Send(now);
        }

        // Cars for the people who have arrived (one per grid slot, in order) and seats at their wheels. True when every connected person is
        // seated (and there is someone).
        bool SeatArrivals()
        {
            int people = 0, seated = 0;
            foreach (var c in Combatant.All)
            {
                if (c == null || !c.IsPlayer || !c.IsServerInitialized || c.Dead.Value) continue;
                people++;
                if (!_carOf.TryGetValue(c, out var car) || car == null)
                {
                    if (_cars.Count >= _grid.Count) continue;   // the grid is full: they watch
                    car = SpawnCar(_cars.Count);
                    if (car == null) continue;
                    _carOf[c] = car;
                }
                if (VehicleSeat.VehicleOf(c) == car) { seated++; continue; }
                if (VehicleSeat.IsAttached(c)) continue;
                Put(c, car.DoorPosition(Vehicles.DriverSeat));
                car.ServerSeatDriver(c);
            }
            return people > 0 && seated >= people && people >= ConnectedPeople();
        }

        int ConnectedPeople() => _nm.ServerManager.Clients.Count;

        Vehicle SpawnCar(int slot)
        {
            var prefabs = VehicleSpawner.Instance != null ? VehicleSpawner.Instance.CarPrefabs() : null;
            if (prefabs == null || prefabs.Count == 0) { Debug.LogWarning("[RaceDirector] no car models in the scene"); return null; }
            var (x, z, yaw) = _grid[slot];
            var nob = Instantiate(prefabs[slot % prefabs.Count], new Vector3(x, 0.3f, z), Quaternion.Euler(0f, yaw, 0f));
            var v = nob.GetComponent<Vehicle>();
            v.ServerSetPaint(VehicleColors.Pick(slot, slot % prefabs.Count));
            _nm.ServerManager.Spawn(nob);
            _cars.Add(v);
            return v;
        }

        static void Put(Combatant c, Vector3 at)
        {
            c.transform.position = at;
            if (c.TryGetComponent<MoveGuard>(out var guard)) guard.ServerRelocate(at);
            if (c.TryGetComponent<CombatantStance>(out var stance)) stance.ServerCorrect(at);
        }

        void Send(double now)
        {
            _sentPhase = _flow.Phase;
            _nextSend = Time.unscaledTime + 0.25f;
            _nm.ServerManager.Broadcast(new RaceStandings
            {
                Phase = (byte)_flow.Phase, Countdown = _flow.CountdownLeft(now), SecondsLeft = _flow.SecondsLeft(now), Laps = (byte)Info.Laps,
            }, true, Channel.Unreliable);
        }
    }
}
