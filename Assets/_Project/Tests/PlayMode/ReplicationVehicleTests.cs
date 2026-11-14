using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Managing;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #165: vehicles as seen by REMOTE clients (docs/MP_AUDIT_VEHICLES.md). The host plays the server; `Remote` (A) and a
    // second client (B) are NetworkManagers in the same process, each with its own copy of every vehicle.
    // The drivers are real: VehicleInput reads scripted keys for whichever player the test makes "local".
    // The host, remote client A and the test box's parked vehicles, with the keys of the local driver scripted (#165).
    public abstract class ReplicationVehicleBase : ReplicationTestBase
    {
        protected override string LogDir => "Logs/net-replication/vehicles";

        protected sealed class Keys : IVehicleInput
        {
            public VehicleInputFrame Frame = new VehicleInputFrame { Seat = -1 };

            public VehicleInputFrame Read()
            {
                var f = Frame;
                Frame.Interact = Frame.EngineToggle = Frame.ToggleView = Frame.Jerrycan = false;
                Frame.Seat = -1;
                return f;
            }
        }

        protected Keys KeyFrames;
        BotRules _bots;

        [UnitySetUp]
        public IEnumerator VehicleSetUp()
        {
            ReplicaLayers.SeparateVehicles = true;
            // The test box's bots and training dummies shoot at players: the riders must live through the test.
            _bots = BotRules.Current;
            BotRules.Current = new BotRules { TargetsPlayers = false };
            foreach (string name in new[] { "Game.AI.BotController, Game.AI", "Game.AI.TrainingDummy, Game.AI" })
            {
                var type = Type.GetType(name);
                if (type != null)
                    foreach (var shooter in UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None)) ((Behaviour)shooter).enabled = false;
            }
            KeyFrames = new Keys();
            VehicleInput.InputOverride = KeyFrames;
            yield return Until(() => !SpawnProtection.InputBlocked, "input free", 30f);
            yield return Seconds(1f); // the parked vehicles settle on their suspension
        }

        [UnityTearDown]
        public IEnumerator VehicleTearDown()
        {
            VehicleInput.InputOverride = null;
            if (_bots != null) BotRules.Current = _bots;
            yield return null;
        }

        // ---- Finding things on each side ----

        protected Vehicle ServerVehicle(Func<Vehicle, bool> pick)
        {
            foreach (var nob in Net.Host.ServerManager.Objects.Spawned.Values)
            {
                var v = nob != null ? nob.GetComponent<Vehicle>() : null;
                if (v != null && pick(v)) return v;
            }
            return null;
        }

        // The parked test-box vehicles (VehicleBuilder.AddToTestBox): the four-seat sedan alone at the west wall, the dirt bike
        // beside it, the two-seat sports car among the others on the east side.
        protected Vehicle FourSeatCar => ServerVehicle(v => v.name.StartsWith("FairheavenLT80"));
        protected Vehicle TwoSeatCar => ServerVehicle(v => v.name.StartsWith("GoblinV12"));
        protected Vehicle Bike => ServerVehicle(v => v.name.StartsWith("DirtBike"));

        protected static Vehicle Replica(NetworkManager manager, Vehicle server) =>
            manager.ClientManager.Objects.Spawned.TryGetValue(server.ObjectId, out var nob) ? nob.GetComponent<Vehicle>() : null;

        // Makes `who` the player VehicleInput (and the camera, HUD, ...) treat as this process's own.
        protected void SetLocal(RemoteClient who)
        {
            typeof(PlayerController).GetProperty("Local").GetSetMethod(true).Invoke(null, new object[] { who.Controller });
            typeof(Combatant).GetProperty("LocalPlayer").GetSetMethod(true).Invoke(null, new object[] { who.Player });
        }

        // The wheels of a replica that drives must not stand on the server's own copy of the vehicle (the replicas ignore it
        // for collisions, MultiHarness.ReplicaLayers): the copy goes where wheel casts and the default ray mask skip it.
        // Test-only (a real client has one copy).
        protected void SeparateTwins(Vehicle server)
        {
            foreach (var col in server.Colliders) col.gameObject.layer = 2;
        }

        // Every side's view of one combatant in a seat: attached to that side's copy of the vehicle, fully seated, eyes at the marker.
        protected bool SeatedOnSide(NetworkManager manager, Vehicle server, Combatant who, int seat, out string why)
        {
            var vehicle = Replica(manager, server);
            var body = MultiHarness.ReplicaOf(manager, who);
            why = "";
            if (vehicle == null || body == null) { why = "not spawned on this side"; return false; }
            if (vehicle.OccupantId(seat) != who.ObjectId) { why = "seat variable " + vehicle.OccupantId(seat); return false; }
            if (!VehicleSeat.TryGet(body, out var attached, out int at) || attached != vehicle || at != seat) { why = "not attached to seat " + seat; return false; }
            if (VehicleSeat.SeatedAmount(body) < 0.999f) { why = "still getting in"; return false; }
            // The body is put on the seat by this side's own vehicle copy: over the marker (the height depends on the pose).
            var marker = vehicle.Seat(seat).transform.position;
            float flat = Vector2.Distance(new Vector2(body.transform.position.x, body.transform.position.z), new Vector2(marker.x, marker.z));
            if (flat > 0.35f) { why = "body " + flat.ToString("F2") + " m beside the seat marker"; return false; }
            return true;
        }

        protected IEnumerator SeatedEverywhere(Vehicle server, Combatant who, int seat, params NetworkManager[] sides)
        {
            foreach (var side in sides)
            {
                string why = "";
                float end = Time.time + 10f;
                while (!SeatedOnSide(side, server, who, seat, out why) && Time.time < end) yield return null;
                Assert.IsTrue(SeatedOnSide(side, server, who, seat, out why), "seat " + seat + " on " + side.name + ": " + why);
            }
        }

        protected IEnumerator GetIn(RemoteClient who, Vehicle server, int doorSeat, int expectSeat)
        {
            SetLocal(who);
            var door = server.DoorPosition(doorSeat);
            who.Controller.Teleport(door + Vector3.up * 0.05f);
            for (int i = 0; i < 4; i++)
            {
                Log($"GetIn {who.Manager.name} t={i * 0.1f:F1}: at {who.Player.transform.position} door {door} (server copy {MultiHarness.ReplicaOf(Net.Host, who.Player).transform.position}) corrections {MoveGuard.Corrections}");
                yield return Seconds(0.1f);
            }
            // F near the vehicle (loot lying close keeps the F key for itself: then the same request the key makes).
            KeyFrames.Frame.Interact = true;
            float until = Time.time + 1f, nextLog = 0f;
            while (server.OccupantId(expectSeat) != who.Player.ObjectId && Time.time < until)
            {
                if (Time.time >= nextLog)
                {
                    nextLog = Time.time + 0.1f;
                    Log($"  after F: at {who.Player.transform.position} attached {VehicleSeat.IsAttached(who.Player)} seats {server.OccupantId(0)},{server.OccupantId(1)} ownerOfVehicle {server.Owner.ClientId} nearbyLoot {who.Controller.NearbyLoot != null} ext {who.Controller.ExternalControl}");
                }
                yield return null;
            }
            if (server.OccupantId(expectSeat) != who.Player.ObjectId)
            {
                Log($"GetIn {who.Manager.name}: F did nothing (loot nearby {who.Controller.NearbyLoot != null}, door distance {Vector3.Distance(who.Player.transform.position, server.DoorPosition(doorSeat)):F2}); asking the vehicle directly");
                var serverCopy = MultiHarness.ReplicaOf(Net.Host, who.Player);
                Log($"  server copy attached {VehicleSeat.IsAttached(serverCopy)} to {VehicleSeat.VehicleOf(serverCopy)?.name}, dead {serverCopy.Dead.Value}, own copy attached {VehicleSeat.IsAttached(who.Player)} to {VehicleSeat.VehicleOf(who.Player)?.name}");
                Log($"  server vehicle {server.name}: free seat {server.HasFreeSeat}, range {server.Rules.EnterRange}, door distances {Vector3.Distance(serverCopy.transform.position, server.DoorPosition(0)):F2}/{Vector3.Distance(serverCopy.transform.position, server.DoorPosition(1)):F2}, server copy spawned {serverCopy.IsSpawned}, replica {Replica(who.Manager, server)?.name} spawned {Replica(who.Manager, server)?.IsSpawned}");
                Replica(who.Manager, server).RequestEnter(who.Player);
                for (int i = 0; i < 12; i++)
                {
                    Log($"  after direct request +{i * 0.1f:F1}s: seats {server.OccupantId(0)},{server.OccupantId(1)} owner {server.Owner.ClientId} engine {server.EngineOn.Value} crashed {(server.IsMotorcycle ? "?" : "-")} pos {server.transform.position} up {server.transform.up}");
                    yield return Seconds(0.1f);
                }
            }
            yield return Until(() => server.OccupantId(expectSeat) == who.Player.ObjectId, who.Manager.name + " in seat " + expectSeat + " on the server");
        }
    }

    public class ReplicationVehicleTests : ReplicationVehicleBase
    {
        // ---- Boarding ----

        [UnityTest]
        public IEnumerator Boarding_RemotePlayersTakeEverySeatOfAFourSeatCar_AndEverySideShowsThem()
        {
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            var car = FourSeatCar;
            Assert.IsNotNull(car, "the test box's four-seat car");
            Assert.AreEqual(4, car.SeatCount);
            var host = Net.Host;
            var sides = new[] { host, Remote.Manager, b.Manager };

            // A takes the driver's seat with F at the door, and every side puts it there.
            yield return GetIn(Remote, car, 0, 0);
            yield return SeatedEverywhere(car, Remote.Player, 0, sides);
            Assert.IsTrue(Replica(Remote.Manager, car).EngineOn.Value, "taking the wheel starts the engine, on its own screen too");
            Assert.IsTrue(Replica(b.Manager, car).EngineOn.Value, "and on the other client's");

            // Ctrl+2..4 on A's own keys: the other seats, one after another.
            foreach (int seat in new[] { 1, 2, 3, 0 })
            {
                KeyFrames.Frame.Seat = seat;
                yield return Until(() => car.OccupantId(seat) == Remote.Player.ObjectId, "A moved to seat " + seat);
                yield return Seconds(car.Rules.SwitchSeconds + 0.3f);
                yield return SeatedEverywhere(car, Remote.Player, seat, sides);
                for (int s = 0; s < 4; s++)
                    if (s != seat) Assert.AreEqual(-1, Replica(b.Manager, car).OccupantId(s), "the seat A left is free for B too (seat " + s + ")");
            }

            // B and the host get in as well: three people in one car, each in their own seat on every side.
            yield return GetIn(b, car, 1, 1);
            HostPlayer.GetComponent<PlayerController>().Teleport(car.DoorPosition(2) + Vector3.up * 0.05f);
            yield return Seconds(0.3f);
            Assert.IsTrue(car.ServerEnter(HostPlayer) >= 2, "the host takes a back seat");
            int hostSeat = car.OccupantId(2) == HostPlayer.ObjectId ? 2 : 3;
            yield return SeatedEverywhere(car, Remote.Player, 0, sides);
            yield return SeatedEverywhere(car, b.Player, 1, sides);
            yield return SeatedEverywhere(car, HostPlayer, hostSeat, sides);

            // The car is full for a fourth person: the fourth seat is taken by a bot-like server seat, the next ask is refused.
            // B gets out with F: the seat frees on every side and B stands beside the car on its own screen.
            SetLocal(b);
            KeyFrames.Frame.Interact = true;
            yield return Until(() => car.OccupantId(1) == -1, "B's seat is free on the server");
            foreach (var side in sides)
                yield return Until(() => Replica(side, car).OccupantId(1) == -1 && !VehicleSeat.IsAttached(MultiHarness.ReplicaOf(side, b.Player)), "B out on " + side.name);
            Assert.Less(Vector3.Distance(b.Player.transform.position, car.DoorPosition(1)), 3.5f, "B stands beside the car");
            Assert.Greater(Vector3.Distance(b.Player.transform.position, car.transform.position + car.transform.up * 0.5f), 0.9f, "not inside its body");
            Assert.IsFalse(b.Controller.ExternalControl, "B's own walking is back");
        }

        [UnityTest]
        public IEnumerator Boarding_TwoSeatCarAndBike_TakeTwoAndRefuseTheThird()
        {
            Combatant.ServerDamaged += LogDamage;
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            foreach (var pick in new Func<Vehicle>[] { () => TwoSeatCar, () => Bike })
            {
                var v = pick();
                Assert.IsNotNull(v);
                Assert.AreEqual(2, v.SeatCount);
                var sides = new[] { Net.Host, Remote.Manager, b.Manager };
                yield return GetIn(Remote, v, 0, 0);
                yield return GetIn(b, v, 1, 1);
                yield return SeatedEverywhere(v, Remote.Player, 0, sides);
                yield return SeatedEverywhere(v, b.Player, 1, sides);
                Assert.AreEqual(Vehicles.NoSeat, v.ServerEnter(HostPlayer), "no third seat in " + v.name);
                Assert.AreEqual(v.IsMotorcycle, VehicleSeat.Astride(MultiHarness.ReplicaOf(b.Manager, Remote.Player)), "astride on a bike, seated in a car, as the other client sees it");
                // Both out again, ready for the next vehicle.
                foreach (var who in new[] { Remote, b })
                {
                    SetLocal(who);
                    KeyFrames.Frame.Interact = true;
                    yield return Until(() => !VehicleSeat.IsAttached(who.Player), who.Manager.name + " out");
                    yield return Seconds(0.2f);
                }
                yield return Until(() => v.OccupantId(0) == -1 && v.OccupantId(1) == -1, "seats free");
                Log($"  out of {v.name}: A hp {Remote.Player.Hp.Value:F0} dead {Remote.Player.Dead.Value}, B hp {b.Player.Hp.Value:F0} dead {b.Player.Dead.Value}, vehicle speed {v.SpeedKmh:F1} km/h");
            }
        }

        // ---- Driving ----

        sealed class Track
        {
            public readonly List<double> T = new List<double>();
            public readonly List<Vector3> P = new List<Vector3>();
            public void Add(Transform t) { T.Add(Time.timeAsDouble); P.Add(t.position); }

            // Speeds over sliding 0.1 s windows between `from` and `to`, and the share of each along `forward`.
            public List<float> Speeds(double from, double to, out int backwards)
            {
                var list = new List<float>();
                backwards = 0;
                int i = 0;
                for (double w = from; w + 0.1 <= to; w += 0.05)
                {
                    while (i < T.Count - 1 && T[i] < w) i++;
                    int j = i;
                    while (j < T.Count - 1 && T[j] < w + 0.1) j++;
                    if (j <= i) continue;
                    var d = P[j] - P[i];
                    d.y = 0f;
                    list.Add(d.magnitude / (float)(T[j] - T[i]));
                }
                return list;
            }
        }

        [UnityTest]
        public IEnumerator RemoteDriver_IsSeenSmoothlyByTheServerAndByAnotherClient_At100msAndTwoPercentLoss()
        {
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            var car = FourSeatCar;
            yield return GetIn(Remote, car, 0, 0);
            yield return GetIn(b, car, 1, 1); // a passenger rides along on every screen
            yield return SeatedEverywhere(car, Remote.Player, 0, new[] { Net.Host, Remote.Manager, b.Manager });
            SeparateTwins(car);
            Net.Simulate(Remote);
            Net.Simulate(b);
            var onA = Replica(Remote.Manager, car);
            var onB = Replica(b.Manager, car);
            Assert.IsTrue(onA.IsOwner && onA.Simulating, "the driver's client simulates");
            Assert.IsFalse(car.Simulating, "the server's copy only follows");
            Assert.IsFalse(onB.Simulating);
            Assert.AreEqual(Remote.Player.Owner.ClientId, car.Owner.ClientId, "ownership follows the driver's seat");
            SetLocal(Remote);

            var truth = new Track(); var server = new Track(); var seen = new Track(); var pass = new Track();
            var start = onA.transform.position;
            KeyFrames.Frame.Throttle = 1f;
            double from = Time.timeAsDouble + 1.2, to = 0;
            float end = Time.time + 3.2f;
            while (Time.time < end)
            {
                truth.Add(onA.transform); server.Add(car.transform); seen.Add(onB.transform);
                pass.Add(MultiHarness.ReplicaOf(b.Manager, b.Player).transform);
                yield return null;
            }
            to = Time.timeAsDouble;
            float moved = Vector3.Distance(onA.transform.position, start);
            Log($"driver moved {moved:F1} m in 3.2 s");
            Assert.Greater(moved, 10f, "the driver's car moved");
            KeyFrames.Frame.Throttle = -1f; // brake to a stop, then compare where the screens have it
            yield return Seconds(2.5f);
            KeyFrames.Frame.Throttle = 0f;
            yield return Seconds(0.8f);
            // The same car on the other screens, a moment behind the driver's.
            Assert.Less(Vector3.Distance(onB.transform.position, onA.transform.position), 2.5f, "client B sees it about where the driver sees it");
            Assert.Less(Vector3.Distance(car.transform.position, onA.transform.position), 2.5f, "and so does the server");

            // Window by window against what the driver's own screen showed (the same standards as a sprinting player, #161).
            var truthSpeeds = truth.Speeds(from, to, out _);
            foreach (var (name, trace) in new[] { ("server", server), ("client B", seen) })
            {
                var speeds = trace.Speeds(from, to, out _);
                Assert.AreEqual(truthSpeeds.Count, speeds.Count, "same windows");
                float meanRatio = 0f, maxRatio = 0f; int slow = 0, stall = 0, longest = 0, counted = 0;
                for (int i = 0; i < speeds.Count; i++)
                {
                    if (truthSpeeds[i] < 3f) continue;
                    float ratio = speeds[i] / truthSpeeds[i];
                    counted++;
                    if (ratio > 1.3f) Log($"    {name} window {i} ({from + i * 0.05 - truth.T[0]:F2} s): {speeds[i]:F1} m/s against the driver's {truthSpeeds[i]:F1}");
                    meanRatio += ratio; maxRatio = Mathf.Max(maxRatio, ratio);
                    if (ratio < MovementNetTests.MinSpeedShare) slow++;
                    stall = ratio < 0.25f ? stall + 1 : 0;
                    longest = Mathf.Max(longest, stall);
                }
                meanRatio /= Mathf.Max(1, counted);
                float stallSeconds = longest > 0 ? (longest + 1) * 0.05f : 0f;
                Log($"{name}: {counted} windows of {truthSpeeds.Count}, speed against the driver's: mean {meanRatio:F2}, fastest {maxRatio:F2}, under {MovementNetTests.MinSpeedShare:P0}: {slow}, longest stall {stallSeconds:F2} s");
                Assert.Greater(counted, 30, "the car was moving");
                Assert.AreEqual(1f, meanRatio, 0.15f, name + " follows the driver's speed");
                Assert.LessOrEqual(stallSeconds, MovementNetTests.MaxFreezeSeconds, name + " saw the vehicle stall");
                Assert.LessOrEqual(slow, Mathf.CeilToInt(MovementNetTests.MaxSlowWindowShare * counted), name + " slow windows");
                Assert.Less(maxRatio, MovementNetTests.MaxSpeedShare, name + " saw a jump");
            }

            // The passenger's body rides in the same place on the other screens as the seat.
            var carOnB = Replica(b.Manager, car);
            var passenger = MultiHarness.ReplicaOf(Remote.Manager, b.Player);
            Assert.Less(Vector3.Distance(passenger.EyePosition, Replica(Remote.Manager, car).Seat(1).transform.position), 0.5f, "B sits in the car on A's screen");
            Assert.Less(Vector3.Distance(MultiHarness.ReplicaOf(Net.Host, b.Player).EyePosition, car.Seat(1).transform.position), 0.5f, "and on the server");
            Net.ClearSimulation(Remote);
            Net.ClearSimulation(b);
        }

        [UnityTest]
        public IEnumerator DriverLeavingOrDying_TheServerTakesTheWheel_AndTheCarRollsOnWithItsPassenger()
        {
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            var car = FourSeatCar;
            yield return GetIn(Remote, car, 0, 0);
            yield return GetIn(b, car, 1, 1);
            SeparateTwins(car);
            SetLocal(Remote);
            KeyFrames.Frame.Throttle = 1f;
            yield return Seconds(2.5f);
            KeyFrames.Frame.Throttle = 0f;
            var onA0 = Replica(Remote.Manager, car);
            Log($"driving: A replica owner {onA0.IsOwner} simulating {onA0.Simulating} engine {onA0.EngineOn.Value} speed {onA0.SpeedKmh:F1} km/h at {onA0.transform.position}; server speed {car.SpeedKmh:F1} at {car.transform.position}; seats {car.OccupantId(0)},{car.OccupantId(1)}; A driver {VehicleSeat.IsDriver(Remote.Player)} attached to {VehicleSeat.VehicleOf(Remote.Player)?.name}");
            float speedBefore = car.SpeedKmh;
            Assert.Greater(speedBefore, 8f, "moving");

            // The driver is shot dead at the wheel: out of the seat, the server owns and simulates the car.
            RemoteOnHost.ServerTakeDamage(new DamageInfo { Amount = 1000f, Attacker = HostPlayer, Zone = HitZone.Head, Cause = DamageCause.Shot, WeaponId = Rifle });
            yield return Until(() => car.OccupantId(0) == -1, "the dead driver is out of the seat");
            yield return Until(() => !car.Owner.IsValid && car.Simulating, "the server simulates the driverless car");
            yield return Until(() => !Replica(Remote.Manager, car).Simulating, "the old driver's client stops simulating it");
            Assert.IsFalse(car.EngineOn.Value, "the engine is off");
            Assert.AreEqual(1, car.OccupantId(1) == b.Player.ObjectId ? 1 : 0, "the passenger stays in");
            var onB = Replica(b.Manager, car);
            var at = onB.transform.position;
            yield return Seconds(1.5f);
            Assert.Greater(Vector3.Distance(onB.transform.position, at), 0.5f, "the car rolled on, for the other client too");
            Assert.Less(Vector3.Distance(onB.transform.position, car.transform.position), 3f, "same place as the server's");
            yield return SeatedEverywhere(car, b.Player, 1, new[] { Net.Host, Remote.Manager, b.Manager });
        }

        [UnityTest]
        public IEnumerator DriverDisconnecting_TheCarStaysAndRollsOn_ForItsPassenger()
        {
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            var car = FourSeatCar;
            yield return GetIn(Remote, car, 0, 0);
            yield return GetIn(b, car, 1, 1);
            SeparateTwins(car);
            SetLocal(Remote);
            KeyFrames.Frame.Throttle = 1f;
            yield return Seconds(2f);
            KeyFrames.Frame.Throttle = 0f;
            int id = car.ObjectId;
            yield return Net.DropClient(Remote);
            yield return Seconds(1.5f);
            Assert.IsTrue(car != null && car.IsSpawned, "the car did not go away with its driver's connection");
            Assert.IsTrue(Net.Host.ServerManager.Objects.Spawned.ContainsKey(id), "still spawned on the server");
            Assert.IsTrue(b.Manager.ClientManager.Objects.Spawned.ContainsKey(id), "and on the other client");
            Assert.IsFalse(car.Owner.IsValid, "without an owner");
            Assert.IsTrue(car.Simulating, "the server drives the empty-handed car");
            yield return SeatedEverywhere(car, b.Player, 1, new[] { Net.Host, b.Manager });
        }

        // ---- Roadkill ----

        List<DamageInfo> _hurt;
        Combatant _hurtVictim;

        void LogDamage(Combatant victim, DamageInfo info, float applied) =>
            Log($"  damage: victim {victim.ObjectId} {info.Cause} {applied:F1} from {(info.Attacker != null ? info.Attacker.ObjectId + " " + info.Attacker.name + " at " + info.Attacker.transform.position + " [" + string.Join(",", System.Array.ConvertAll(info.Attacker.GetComponents<MonoBehaviour>(), c => c.GetType().Name + (c.enabled ? "" : "(off)"))) + "]" : "-1")}");

        [UnityTearDown]
        public IEnumerator RoadkillTearDown()
        {
            Combatant.ServerDamaged -= OnDamaged;
            Combatant.ServerDamaged -= LogDamage;
            yield return null;
        }

        void OnDamaged(Combatant victim, DamageInfo info, float applied)
        {
            if (victim == _hurtVictim) _hurt.Add(info);
        }

        [UnityTest]
        public IEnumerator Roadkill_ARemoteDriverRunsOverARemotePedestrian_DamageCreditAndShove()
        {
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            var car = FourSeatCar;
            yield return GetIn(Remote, car, 0, 0);
            SeparateTwins(car);
            SetLocal(Remote);
            // B stands ahead in the car's way (east of its start, same row).
            var way = car.transform.forward;
            var spot = car.transform.position + way * 15f;
            b.Controller.Teleport(new Vector3(spot.x, 0.1f, spot.z));
            yield return Seconds(0.5f);
            var standing = b.Player.transform.position;
            float hp0 = b.Player.Hp.Value;
            _hurt = new List<DamageInfo>();
            _hurtVictim = MultiHarness.ReplicaOf(Net.Host, b.Player);
            Combatant.ServerDamaged += OnDamaged;
            KeyFrames.Frame.Throttle = 1f;
            yield return Until(() => _hurt.Count > 0 || Vector3.Dot(spot - car.transform.position, way) < -3f, "the car reaches the pedestrian", 8f);
            KeyFrames.Frame.Throttle = -1f;
            Assert.Greater(_hurt.Count, 0, "the server counted a hit");
            Assert.AreEqual(DamageCause.Vehicle, _hurt[0].Cause);
            Assert.AreEqual(Remote.Player.ObjectId, _hurt[0].Attacker != null ? _hurt[0].Attacker.ObjectId : -1, "the driver gets the credit");
            Assert.Greater(_hurt[0].Amount, 5f, "a real hit");
            yield return Until(() => b.Player.Hp.Value < hp0 || b.Player.Dead.Value, "B's own health dropped");
            yield return Seconds(0.6f);
            Log($"B hp {hp0} -> {b.Player.Hp.Value}, pushed {Vector3.Distance(standing, b.Player.transform.position):F2} m, hit at {_hurt[0].Amount:F1}");
            Assert.Greater(Vector3.Distance(standing, b.Player.transform.position), 1f, "the shove moved B's body on B's own client");
            KeyFrames.Frame.Throttle = 0f;
        }

        // ---- Fuel, horn, engine ----

        [UnityTest]
        public IEnumerator FuelHornAndEngine_ReachTheOtherClient_AndARemoteDriverPoursItsOwnJerrycan()
        {
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            var car = FourSeatCar;
            yield return GetIn(Remote, car, 0, 0);
            yield return Seconds(1f);
            SeparateTwins(car);
            SetLocal(Remote);
            var tank = car.GetComponent<VehicleFuel>();
            var onB = Replica(b.Manager, car);
            var tankB = onB.GetComponent<VehicleFuel>();
            var hornB = onB.GetComponent<VehicleHorn>();
            var audioB = onB.GetComponent<VehicleAudio>();
            tank.ServerSetFuel(0.3f);
            yield return Until(() => Mathf.Abs(tankB.Fuel01 - 0.3f) < 0.01f, "B reads the tank");
            Assert.IsTrue(onB.EngineOn.Value);
            yield return Until(() => audioB.EnginePlaying, "B hears the engine");

            // The horn: A holds the button (no pistol out), B hears it; the server has it as a noise for the bots once.
            KeyFrames.Frame.Horn = true;
            yield return Until(() => hornB.Sounding && audioB.HornPlaying, "B hears the horn");
            Assert.AreEqual(1, car.GetComponent<VehicleHorn>().ServerPresses);
            KeyFrames.Frame.Horn = false;
            yield return Until(() => !hornB.Sounding && !audioB.HornPlaying, "the horn stops on B too");

            // Driving burns fuel: the number on B follows it down and the note rises with speed.
            float idle = audioB.EnginePitch;
            float fuel0 = tankB.Fuel01;
            KeyFrames.Frame.Throttle = 1f;
            yield return Seconds(2.5f);
            Assert.Less(tankB.Fuel01, fuel0, "B sees the tank go down");
            Assert.Greater(audioB.EnginePitch, idle, "and the engine note rise with the driver's speed");
            KeyFrames.Frame.Throttle = -1f;
            yield return Seconds(1.5f);
            KeyFrames.Frame.Throttle = 0f;

            // A's own jerrycan: J pours for five seconds, everybody sees who is pouring where; the tank takes 60 %.
            var cans = RemoteOnHost.GetComponent<CombatantJerrycan>();
            Assert.IsTrue(RemoteOnHost.ServerGive(new LootEntry { Kind = LootKind.Jerrycan, Amount = 1 }), "a can for A");
            tank.ServerSetFuel(0.1f);
            yield return Until(() => Remote.Player.GetComponent<CombatantJerrycan>().Count.Value == 1, "A has the can");
            yield return Until(() => car.SpeedKmh < 1f, "standing still");
            KeyFrames.Frame.Jerrycan = true;
            var canOnB = MultiHarness.ReplicaOf(b.Manager, Remote.Player).GetComponent<CombatantJerrycan>();
            yield return Until(() => canOnB.Using && canOnB.TargetId.Value == car.ObjectId, "B sees A pouring into the car");
            yield return Until(() => !canOnB.Using, "the pour ends", FuelRules.Current.JerrycanSeconds + 4f);
            Assert.AreEqual(1, cans.ServerCompleted);
            Assert.AreEqual(0, canOnB.Count.Value, "the can is used up on B's view too");
            yield return Until(() => Mathf.Abs(tankB.Fuel01 - (0.1f + FuelRules.Current.JerrycanAmount)) < 0.02f, "B reads the fuller tank");
        }
    }
}
