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
        public float RaceTime;    // seconds since GO (0 before)
        // The race order (ADR-515/518), first to last: the driver's ObjectId, name, laps done, whether finished, the finish time (or 0),
        // the best clean lap (0 = none yet) and how far into the lap in progress the clock is (-1 = not on a clean lap).
        public int[] Keys; public string[] Names; public byte[] LapsDone; public bool[] Done; public float[] Times; public float[] Best; public float[] Running;
        public byte[] LapsToGo;   // the laps this driver must do: the race's, one more after a false start (ADR-516)
        public float[] Gaps;      // seconds behind the leader (#510, RaceSplits.Gaps): 0 for the leader, -1 while not known
    }

    // The event race of a Race_<course> scene (ADR-512..519, #445, #446). Every side: all vehicles of the scene are race cars
    // (Vehicle.EventRace) and the client HUD is attached. Server: each person who arrives gets a car on the next grid slot and is seated at
    // its wheel; the race flow (RaceFlow) waits for everyone, counts down 3 s (input locked) and starts the race. From GO the server judges
    // every car from its own copy's place (RaceLap): laps, clean lap times, the finish after the course's laps (one more for a car that
    // left its grid slot before GO); the first finisher gives the rest AfterWinnerSeconds, then the results, then the menu. The phase and the
    // race order go out as RaceStandings.
    public sealed class RaceDirector : MonoBehaviour
    {
        public string CourseId = "snake";

        public static RaceDirector Current { get; private set; }
        public RaceEvent.CourseInfo Info { get; private set; }
        public int Laps { get; private set; }   // of this race: the start config's or the course's (#508)
        public RaceFlow Flow => _flow;
        public int CarCount => _cars.Count;
        public Vehicle CarOf(Combatant c) => c != null && _carOf.TryGetValue(c, out var v) ? v : null;

        NetworkManager _nm;
        RaceFlow _flow;
        RaceStart _start;
        readonly List<(float x, float z, float yaw)> _grid = new List<(float, float, float)>();
        readonly List<Vehicle> _cars = new List<Vehicle>();
        readonly Dictionary<Combatant, Vehicle> _carOf = new Dictionary<Combatant, Vehicle>();
        const float FalseStartMeters = 3f;

        sealed class Racer
        {
            public Vehicle Car; public Combatant Driver; public string Name; public RaceLap Lap; public Vector3 Grid;
            public bool FalseStart, Finished; public float FinishTime, Best;
            public bool Bot, Cut; public float Skill, StuckSince = -1f;     // a bot racer (ADR-517): how good, which way at the fork
            public float GoodS = float.NaN;                                  // where a bot was last on a hill road (RaceCourse.Follow)
            public readonly RaceSplits Splits = new RaceSplits();            // race times at the progress marks: the gaps (#510)
        }
        readonly List<Racer> _racers = new List<Racer>();
        readonly List<RaceEvent.Standing> _order = new List<RaceEvent.Standing>();
        float _nextSend;
        RacePhase _sentPhase = (RacePhase)255;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = null; Vehicle.EventRace = false; RaceEvent.Starting = null; }

        void Awake()
        {
            // The race started from the menu or the lobby when its course is one of this scene's (the pass's two directions share a scene).
            var start = RaceEvent.Starting;
            var chosen = RaceEvent.ById(start?.CourseId);
            bool ours = chosen != null && chosen.Scene == gameObject.scene.name;
            Info = ours ? chosen : RaceEvent.ById(CourseId) ?? RaceEvent.Courses[0];
            _start = ours ? start : null;
            Laps = RaceEvent.LapsOf(_start, Info);
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
            if (_flow.Phase == RacePhase.Countdown && !_botsIn) FillWithBots();
            DriveBots();
            bool everyoneDone = Judge(now);
            var before = _flow.Phase;
            _flow.Tick(now, everyoneIn, everyoneDone);
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
                    // A queued race seats by session token, and the player may spawn before its token arrives: wait for it while the race waits (#513).
                    if (!TokenKnown(c) && _flow.Phase == RacePhase.Waiting) continue;
                    car = SpawnCar(_cars.Count, EntryOf(c));
                    if (car == null) continue;
                    _carOf[c] = car;
                    var name = MatchState.Instance != null ? MatchState.Instance.ServerNameOf(c) : "";
                    _racers.Add(new Racer { Car = car, Driver = c, Name = string.IsNullOrEmpty(name) ? "플레이어 " + _cars.Count : name, Lap = new RaceLap(Info.Course), Grid = car.transform.position });
                }
                if (VehicleSeat.VehicleOf(c) == car) { seated++; continue; }
                if (VehicleSeat.IsAttached(c)) continue;
                Put(c, car.DoorPosition(Vehicles.DriverSeat));
                car.ServerSeatDriver(c);
            }
            return people > 0 && seated >= people && people >= Mathf.Max(ConnectedPeople(), _start != null ? _start.People : 0);   // the server's queued people too (#513)
        }

        int ConnectedPeople() => _nm.ServerManager.Clients.Count;

        // This person's car and paint from the start config: the host's own entry, or by session token.
        RaceStart.Entry? EntryOf(Combatant c) => _start == null ? null : RaceEvent.EntryFor(_start, TokenOf(c), c.Owner != null && c.Owner.IsLocalClient);

        string TokenOf(Combatant c)
        {
            var keeper = MatchDirector.Instance != null ? MatchDirector.Instance.Sessions : null;
            return keeper != null && c.Owner != null && c.Owner.IsValid ? keeper.TokenOf(c.Owner.ClientId) : null;
        }

        // Nothing to look up (no per-person entries, or the host's own player), or the token is in.
        bool TokenKnown(Combatant c) => _start == null || _start.Racers.Count == 0 || (c.Owner != null && c.Owner.IsLocalClient) || TokenOf(c) != null;

        // When the countdown starts, bots take the free grid slots up to the field (people + the config's bots, or RaceEvent.Racers; ADR-514/517,
        // #508): driverless cars on autopilot, each with
        // a name, a skill between 0.80 and 0.95 of the grip limit and a side of the fork.
        bool _botsIn;
        public int BotResets { get; private set; }   // tests: how often a bot had to be put back on the course
        void FillWithBots()
        {
            _botsIn = true;
            var rng = new System.Random(_cars.Count * 7919 + Info.Id.GetHashCode());
            int field = RaceEvent.FieldSize(_start, _carOf.Count, _grid.Count);
            for (int i = 0; _cars.Count < field; i++)
            {
                var car = SpawnCar(_cars.Count, null);
                if (car == null) break;
                car.Autopilot = true;
                car.ServerSetEngine(true);
                _racers.Add(new Racer
                {
                    Car = car, Name = BotNames.For(Info.Id.GetHashCode(), i), Lap = new RaceLap(Info.Course), Grid = car.transform.position,
                    Bot = true, Skill = RaceBotDriver.Skill(i, Mathf.Max(1, field - 1), Info.Id.GetHashCode()), Cut = rng.NextDouble() < 0.5,
                });
            }
            Debug.Log($"[RaceDirector] {_racers.Count} racers ({_racers.Count - _carOf.Count} bots)");
        }

        // Bot input every frame: held still before GO, then RaceBotDriver; a bot stuck (upside down, off the course or not moving) for
        // StuckResetSeconds is put back on the course.
        void DriveBots()
        {
            bool go = _flow.Phase == RacePhase.Racing || _flow.Phase == RacePhase.Finishing;
            var course = Info.Course;
            foreach (var r in _racers)
            {
                if (!r.Bot || r.Car == null) continue;
                var t = r.Car.transform;
                if (!go || r.Finished) { r.Car.SetDriverInput(new DriverInput { Handbrake = true }); continue; }   // held (a negative throttle would reverse once stopped)
                var (throttle, steer) = RaceBotDriver.Drive(course, t.position.x, t.position.z, t.eulerAngles.y, r.Car.Speed, r.Skill, r.Cut);
                r.Car.SetDriverInput(new DriverInput { Throttle = throttle, Steer = steer });
                var pos = t.position;
                r.GoodS = course.Follow(r.GoodS, pos.x, pos.y, pos.z);
                bool fell = course.Fell(r.GoodS, pos.y);   // off the edge of a hill road: put back at once (#451)
                bool stuck = fell || t.up.y < 0.3f || Mathf.Abs(course.Project(pos.x, pos.z).offset) > course.Width * 0.5f + course.Runoff + 2f || Mathf.Abs(r.Car.Speed) < 1f;
                if (!stuck) { r.StuckSince = -1f; continue; }
                if (r.StuckSince < 0f) { r.StuckSince = Time.time; continue; }
                if (Time.time - r.StuckSince < (fell ? RaceEvent.FallResetSeconds : RaceEvent.StuckResetSeconds)) continue;
                var (x, y, z, yaw) = course.ResetTo(pos.x, pos.z, r.GoodS);
                // Never onto another car (two bots stuck together were put back on top of each other, again and again): further back.
                float back = course.Project(x, z).s;
                for (int k = 1; k <= 4 && CarNear(new Vector3(x, y, z), r.Car); k++)
                {
                    (x, z, yaw) = course.PointAt(back - 8f * k); y = course.HeightAt(back - 8f * k);
                }
                r.Car.ServerPlace(new Vector3(x, y + 0.4f, z), Quaternion.Euler(0f, yaw, 0f));
                Debug.Log($"[RaceDirector] {r.Name} put back ({(fell ? "fell" : t.up.y < 0.3f ? "flipped" : Mathf.Abs(r.Car.Speed) < 1f ? "stopped" : "off the course")}) at s {course.Project(pos.x, pos.z).s:0} -> {r.GoodS:0}");
                r.StuckSince = -1f;
                BotResets++;
            }
        }

        bool CarNear(Vector3 at, Vehicle self)
        {
            foreach (var c in _cars) if (c != null && c != self && (c.transform.position - at).sqrMagnitude < 36f) return true;
            return false;
        }

        // The race as the server sees it (ADR-515): before GO a car that leaves its slot is a false start; from GO each car's place feeds
        // its lap judge. True when every racer has finished (none = not yet).
        bool Judge(double now)
        {
            var phase = _flow.Phase;
            bool before = phase == RacePhase.Waiting || phase == RacePhase.Countdown, racing = phase == RacePhase.Racing || phase == RacePhase.Finishing;
            int done = 0;
            foreach (var r in _racers)
            {
                if (r.Car == null) { done++; continue; }
                var p = r.Car.transform.position;
                if (before)
                {
                    if (!r.FalseStart && Vector3.Distance(p, r.Grid) > FalseStartMeters) { r.FalseStart = true; Debug.Log($"[RaceDirector] false start: {r.Name} (one more lap to go)"); }
                    continue;
                }
                if (r.Finished) { done++; continue; }
                if (!racing) continue;
                float s = Info.Course.Project(p.x, p.z).s;
                float lap = r.Lap.Step(s, now);
                r.Splits.Record(r.Lap.Progress(s), (float)(now - _flow.GoAt));
                if (lap > 0f && (r.Best <= 0f || lap < r.Best)) r.Best = lap;
                if (r.Lap.Laps >= LapsFor(r))
                {
                    // An open course has no lap to take away for a false start: time added instead (ADR-559).
                    r.Finished = true; r.FinishTime = (float)(now - _flow.GoAt) + (Info.Course.Open && r.FalseStart ? RaceEvent.FalseStartPenaltySeconds : 0f); done++;
                    _flow.FirstFinish(now);
                    Debug.Log($"[RaceDirector] {r.Name} finished in {r.FinishTime:0.00} s (best lap {r.Best:0.00})");
                }
            }
            return _racers.Count > 0 && done >= _racers.Count;
        }

        int LapsFor(Racer r) => Laps + (r.FalseStart && !Info.Course.Open ? 1 : 0);

        // The car on grid slot `slot`: the entry's model and paint, else (or for a model the scene lacks) the slot's model and paint.
        Vehicle SpawnCar(int slot, RaceStart.Entry? entry)
        {
            var prefabs = VehicleSpawner.Instance != null ? VehicleSpawner.Instance.CarPrefabs() : null;
            if (prefabs == null || prefabs.Count == 0) { Debug.LogWarning("[RaceDirector] no car models in the scene"); return null; }
            var (x, z, yaw) = _grid[slot];
            int model = slot % prefabs.Count;
            if (entry != null)
            {
                int found = prefabs.FindIndex(p => p.GetComponent<Vehicle>().ModelName == entry.Value.Car);
                if (found >= 0) model = found;
                else if (!string.IsNullOrEmpty(entry.Value.Car)) Debug.LogWarning($"[RaceDirector] no car model {entry.Value.Car} in the scene: slot car");
            }
            var nob = Instantiate(prefabs[model], new Vector3(x, Info.Course.RoadHeight(x, z) + 0.3f, z), Quaternion.Euler(0f, yaw, 0f));
            var v = nob.GetComponent<Vehicle>();
            v.ServerSetPaint(entry != null && VehicleColors.Valid(entry.Value.Paint) ? entry.Value.Paint : VehicleColors.Pick(slot, model));
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
            bool final = _flow.Phase == RacePhase.Results || _flow.Phase == RacePhase.Over;
            if (!final || !_orderFinal) BuildOrder();
            _orderFinal = final;   // the order is frozen once the results are up (cars still rolling must not reshuffle them)
            SendStandings(now);
        }

        void BuildOrder()
        {
            _order.Clear();
            for (int i = 0; i < _racers.Count; i++)
            {
                var r = _racers[i];
                float s = r.Car != null ? Info.Course.Project(r.Car.transform.position.x, r.Car.transform.position.z).s : 0f;
                _order.Add(new RaceEvent.Standing { Key = i, Finished = r.Finished, FinishTime = r.FinishTime, Progress = r.Lap.Progress(s) });
            }
            RaceEvent.Order(_order);
        }

        bool _orderFinal;

        void SendStandings(double now)
        {
            int n = _order.Count;
            var splits = new RaceSplits[n];
            var m = new RaceStandings
            {
                Phase = (byte)_flow.Phase, Countdown = _flow.CountdownLeft(now), SecondsLeft = _flow.SecondsLeft(now), Laps = (byte)Laps,
                RaceTime = _flow.GoAt >= 0 ? (float)(now - _flow.GoAt) : 0f,
                Keys = new int[n], Names = new string[n], LapsDone = new byte[n], Done = new bool[n], Times = new float[n], Best = new float[n], Running = new float[n], LapsToGo = new byte[n],
            };
            for (int k = 0; k < n; k++)
            {
                var r = _racers[_order[k].Key];
                m.Keys[k] = r.Driver != null ? r.Driver.ObjectId : -1 - _order[k].Key; m.Names[k] = r.Name; m.LapsDone[k] = (byte)Mathf.Min(255, r.Lap.Laps);
                m.Done[k] = r.Finished; m.Times[k] = r.FinishTime; m.Best[k] = r.Best;
                m.Running[k] = r.Lap.Timing && !r.Finished ? (float)(now - r.Lap.Start) : -1f;
                m.LapsToGo[k] = (byte)LapsFor(r);
                splits[k] = r.Splits;
            }
            m.Gaps = RaceSplits.Gaps(splits, m.Done, m.Times);
            _nm.ServerManager.Broadcast(m, true, Channel.Unreliable);
        }
    }
}
