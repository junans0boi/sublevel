using System;
using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // ADR-065 (5) in the real TestBox scene as a host: one car (z = -27) and one motorcycle (z = -21.5), both facing
    // east (+X) with open floor ahead. Getting on and off goes through the vehicle keys (VehicleInput.InputOverride);
    // weapon permissions are checked on the server path (Combatant.ServerFire).
    public class VehicleTests
    {
        sealed class NoPlayerInput : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        // Held values stay until changed; key presses (F, Ctrl+n, Z, V) last one frame.
        sealed class ScriptedVehicleInput : IVehicleInput
        {
            public VehicleInputFrame Frame = new VehicleInputFrame { Seat = -1 };

            public VehicleInputFrame Read()
            {
                var f = Frame;
                Frame.Interact = Frame.EngineToggle = Frame.ToggleView = false;
                Frame.Seat = -1;
                return f;
            }
        }

        const int Pistol = 1, Rifle = 3;
        const int PistolSlot = 2, RifleSlot = 0;

        ScriptedVehicleInput _keys;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        IEnumerator LoadTestBox()
        {
            _keys = new ScriptedVehicleInput();
            PlayerController.InputOverride = new NoPlayerInput();
            VehicleInput.InputOverride = _keys;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return WaitUntil(() => Find(VehicleKind.Car) != null && Find(VehicleKind.Motorcycle) != null, 10f, "the test box's car and motorcycle");
            // Let them settle on their suspension.
            yield return new WaitForSeconds(1f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            VehicleInput.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static Vehicle Find(VehicleKind kind)
        {
            foreach (var v in Vehicle.All)
                if (v != null && v.Kind == kind) return v;
            return null;
        }

        static int SeatOf(Combatant c) => VehicleSeat.TryGet(c, out _, out int seat) ? seat : -1;

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // Stands at the driver's door and presses F; waits until fully seated as the driver.
        IEnumerator GetOnAsDriver(Vehicle v)
        {
            var me = Combatant.LocalPlayer;
            PlayerController.Local.Teleport(v.DoorPosition(0) + Vector3.up * 0.05f);
            yield return null;
            yield return null;
            _keys.Frame.Interact = true;
            yield return WaitUntil(() => SeatOf(me) == 0, 3f, "in the driver's seat");
            yield return new WaitForSeconds(v.Rules.EnterSeconds + 0.3f);
            Assert.AreEqual(1f, VehicleSeat.SeatedAmount(me), 1e-4f, "fully seated");
            Assert.IsTrue(v.EngineOn.Value, "taking the wheel starts the engine");
        }

        IEnumerator SwitchTo(Vehicle v, int seat)
        {
            var me = Combatant.LocalPlayer;
            _keys.Frame.Seat = seat;
            yield return WaitUntil(() => SeatOf(me) == seat, 2f, "moved to seat " + seat);
            yield return new WaitForSeconds(v.Rules.SwitchSeconds + 0.2f);
        }

        static void GiveRifleAndPistol(Combatant me)
        {
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 }), "rifle");
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Pistol, Amount = 12 }), "pistol");
            Assert.AreEqual(Rifle, me.WeaponInSlot(RifleSlot));
            Assert.AreEqual(Pistol, me.WeaponInSlot(PistolSlot));
        }

        // ---- Car ----

        [UnityTest]
        public IEnumerator Car_EnterAsDriver_ThrottleThreeSeconds_MovesMoreThanTenMetres()
        {
            yield return LoadTestBox();
            var car = Find(VehicleKind.Car);
            yield return GetOnAsDriver(car);
            Assert.IsTrue(car.Simulating, "the host drives, so it simulates");
            var start = car.transform.position;
            _keys.Frame.Throttle = 1f;
            yield return new WaitForSeconds(3f);
            _keys.Frame.Throttle = -1f; // brake before the far wall
            float moved = Flat(car.transform.position, start);
            Assert.Greater(moved, 10f, "moved " + moved + " m");
            Assert.Less(Flat(Combatant.LocalPlayer.transform.position, car.transform.position), 2f, "the driver went along");
        }

        [UnityTest]
        public IEnumerator Car_CtrlTwo_MovesTheDriverToTheFrontPassengerSeat()
        {
            yield return LoadTestBox();
            var car = Find(VehicleKind.Car);
            var me = Combatant.LocalPlayer;
            yield return GetOnAsDriver(car);
            yield return SwitchTo(car, 1);
            Assert.AreEqual(1, SeatOf(me));
            Assert.AreEqual(-1, car.OccupantId(0), "the driver's seat is free");
            Assert.AreEqual(me.ObjectId, car.OccupantId(1));
        }

        [UnityTest]
        public IEnumerator Car_Driver_CannotFireTheRifle_ButFiresThePistol()
        {
            yield return LoadTestBox();
            var car = Find(VehicleKind.Car);
            var me = Combatant.LocalPlayer;
            GiveRifleAndPistol(me);
            yield return GetOnAsDriver(car);
            Assert.IsTrue(me.ServerSelectSlot(RifleSlot));
            yield return null;
            Assert.IsFalse(me.ServerFire(car.transform.forward), "no rifle from the wheel");
            Assert.IsTrue(me.ServerSelectSlot(PistolSlot));
            yield return new WaitForSeconds(1f);
            Assert.IsTrue(me.ServerFire(car.transform.forward), "the pistol fires");
        }

        // ADR-111: a pistol in the driver's hand no longer locks the wheel (it did before #142).
        [UnityTest]
        public IEnumerator Car_Driver_SteersWithThePistolOut()
        {
            yield return LoadTestBox();
            var car = Find(VehicleKind.Car);
            var me = Combatant.LocalPlayer;
            GiveRifleAndPistol(me);
            car.Body.position = new Vector3(-26f, 0.5f, -21f);
            car.Body.rotation = Quaternion.Euler(0f, 90f, 0f);
            car.Body.linearVelocity = car.Body.angularVelocity = Vector3.zero;
            yield return new WaitForSeconds(0.3f);
            yield return GetOnAsDriver(car);
            Assert.IsTrue(me.ServerSelectSlot(PistolSlot));
            yield return null;
            Assert.AreNotEqual(0, me.ActiveWeaponId, "the pistol is in hand");
            _keys.Frame.Throttle = 1f;
            yield return new WaitForSeconds(1.3f);
            _keys.Frame.Throttle = 0.4f;
            float yaw = car.transform.eulerAngles.y;
            _keys.Frame.Steer = 1f;
            yield return new WaitForSeconds(0.8f);
            float turned = Mathf.DeltaAngle(yaw, car.transform.eulerAngles.y);
            _keys.Frame.Steer = 0f;
            _keys.Frame.Throttle = -1f;
            Assert.Greater(turned, 10f, "the driver with a pistol out still turns right (turned " + turned.ToString("F1") + ")");
            yield return new WaitForSeconds(1f);
            _keys.Frame.Throttle = 0f;
        }

        [UnityTest]
        public IEnumerator Car_Passenger_FiresTheRifle()
        {
            yield return LoadTestBox();
            var car = Find(VehicleKind.Car);
            var me = Combatant.LocalPlayer;
            GiveRifleAndPistol(me);
            yield return GetOnAsDriver(car);
            yield return SwitchTo(car, 1);
            Assert.IsTrue(me.ServerSelectSlot(RifleSlot));
            yield return new WaitForSeconds(1f);
            Assert.IsTrue(me.ServerFire(car.transform.forward + car.transform.right * 0.5f), "the rifle fires from the passenger seat");
        }

        [UnityTest]
        public IEnumerator Car_Exit_PutsThePlayerBesideTheDriversDoor_OnTheGround()
        {
            yield return LoadTestBox();
            var car = Find(VehicleKind.Car);
            var me = Combatant.LocalPlayer;
            yield return GetOnAsDriver(car);
            float hp = me.Hp.Value;
            _keys.Frame.Interact = true;
            yield return WaitUntil(() => !VehicleSeat.IsAttached(me), 3f, "out of the car");
            yield return new WaitForSeconds(0.5f);
            var offset = me.transform.position - car.transform.position;
            float flat = Flat(me.transform.position, car.transform.position);
            Assert.That(flat, Is.InRange(1f, 3.5f), "beside the car");
            Assert.Less(Vector3.Dot(offset, car.transform.right), -0.8f, "on the driver's (left) side");
            Assert.AreEqual(0f, me.transform.position.y, 0.25f, "on the floor");
            Assert.AreEqual(-1, car.OccupantId(0));
            Assert.IsFalse(car.EngineOn.Value, "the engine stops when the driver gets out");
            Assert.AreEqual(hp, me.Hp.Value, 1e-3f, "getting out of a standing car does not hurt");
        }

        [UnityTest]
        public IEnumerator Car_EngineOff_CoastsOnItsMomentum_AndWRestartsIt()
        {
            yield return LoadTestBox();
            var car = Find(VehicleKind.Car);
            yield return GetOnAsDriver(car);
            _keys.Frame.Throttle = 1f;
            yield return new WaitForSeconds(2f);
            _keys.Frame.Throttle = 0f;
            _keys.Frame.EngineToggle = true;
            yield return WaitUntil(() => !car.EngineOn.Value, 1f, "Z switched the engine off");
            float before = car.Speed;
            Assert.Greater(before, 5f, "moving when the engine went off");
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(car.EngineOn.Value);
            Assert.Greater(car.Speed, before - 2f, "rolls on with little loss");
            _keys.Frame.Throttle = 1f;
            yield return WaitUntil(() => car.EngineOn.Value, 2f, "W started it again");
            _keys.Frame.Throttle = -1f;
        }

        [UnityTest]
        public IEnumerator Car_DriverMovesSeats_TheCarKeepsRolling()
        {
            yield return LoadTestBox();
            var car = Find(VehicleKind.Car);
            yield return GetOnAsDriver(car);
            _keys.Frame.Throttle = 1f;
            yield return new WaitForSeconds(2f);
            _keys.Frame.Throttle = 0f;
            float before = car.Speed;
            yield return SwitchTo(car, 1);
            Assert.Greater(car.Speed, 3f, "still rolling with nobody at the wheel");
            Assert.Greater(car.Speed, before - 2f, "only slight drag, no brakes");
        }

        [UnityTest]
        public IEnumerator Car_ExitAtSpeed_HurtsLikeAFall()
        {
            yield return LoadTestBox();
            var car = Find(VehicleKind.Car);
            var me = Combatant.LocalPlayer;
            yield return GetOnAsDriver(car);
            DamageInfo seen = default;
            me.ServerDamageFilter = info => { seen = info; return info.Amount; };
            float speed = Vehicles.Kmh(45f);
            car.Body.linearVelocity = car.transform.forward * speed;
            _keys.Frame.Interact = true;
            yield return WaitUntil(() => !VehicleSeat.IsSeated(me), 1f, "jumped out");
            me.ServerDamageFilter = null;
            Assert.AreEqual(DamageCause.Fall, seen.Cause);
            Assert.Greater(seen.Amount, 5f, "45 km/h hurts");
            Assert.Less(seen.Amount, 50f, "but is far from lethal");
            Assert.IsFalse(me.Dead.Value);
        }

        // ---- Running people over (#112) ----

        // A training dummy (server-owned) moved onto the vehicle's line `metres` ahead.
        static Combatant DummyAhead(Vehicle v, float metres)
        {
            var me = Combatant.LocalPlayer;
            Combatant dummy = null;
            foreach (var c in Combatant.All)
                if (c != null && c != me && !c.IsPlayer && !c.Dead.Value) { dummy = c; break; }
            Assert.IsNotNull(dummy, "a training dummy in the test box");
            var at = v.transform.position + v.transform.forward * metres;
            dummy.transform.position = new Vector3(at.x, 0f, at.z);
            return dummy;
        }

        [UnityTest]
        public IEnumerator Car_RunsOverAPerson_HurtsByTheSpeed_CreditsTheDriver_AndPushesThem()
        {
            yield return LoadTestBox();
            var car = Find(VehicleKind.Car);
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f; // the dummies shoot
            yield return GetOnAsDriver(car);
            var dummy = DummyAhead(car, 12f);
            var start = dummy.transform.position;
            DamageInfo seen = default;
            dummy.ServerDamageFilter = info => { seen = info; return info.Amount; };
            car.Body.linearVelocity = car.transform.forward * Vehicles.Kmh(32f); // coasting: hurt, not killed
            yield return WaitUntil(() => seen.Cause == DamageCause.Vehicle, 3f, "run over");
            Assert.AreSame(me, seen.Attacker, "the driver gets the credit");
            Assert.Greater(seen.Amount, 30f, "hurt by the speed");
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(car.SpeedKmh, 25f, "the car went through instead of stopping dead");
            _keys.Frame.Throttle = -1f;
            yield return new WaitForSeconds(0.4f);
            Assert.Greater(Flat(dummy.transform.position, start), 1.5f, "pushed away");
        }

        [UnityTest]
        public IEnumerator Car_RunsOverTheDriversTeammate_Unhurt_ButADriverlessCarHurtsAnyone()
        {
            yield return LoadTestBox();
            var car = Find(VehicleKind.Car);
            var carStart = car.transform.position;
            var carRotation = car.transform.rotation;
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;
            yield return GetOnAsDriver(car);
            var dummy = DummyAhead(car, 12f);
            me.TeamId.Value = 4;
            dummy.TeamId.Value = 4;
            float hp = dummy.Hp.Value;
            var start = dummy.transform.position;
            car.Body.linearVelocity = car.transform.forward * Vehicles.Kmh(32f); // coasting: hurt, not killed
            yield return new WaitForSeconds(1.5f);
            _keys.Frame.Throttle = -1f;
            Assert.AreEqual(hp, dummy.Hp.Value, 1e-4f, "the driver's teammate is not hurt");
            Assert.Greater(Flat(dummy.transform.position, start), 1.5f, "but pushed");
            yield return WaitUntil(() => car.SpeedKmh < 3f, 5f, "stopped");
            _keys.Frame.Throttle = 0f;
            _keys.Frame.Interact = true;
            yield return WaitUntil(() => SeatOf(me) < 0, 3f, "out");
            yield return new WaitForSeconds(1.2f);
            // Nobody at the wheel: rolling into the same (team) dummy from the start hurts it, with nobody credited.
            car.Body.position = carStart;
            car.Body.rotation = carRotation;
            car.Body.linearVelocity = car.Body.angularVelocity = Vector3.zero;
            yield return new WaitForSeconds(0.5f);
            DummyAhead(car, 10f);
            DamageInfo seen = default;
            dummy.ServerDamageFilter = info => { seen = info; return info.Amount; };
            car.Body.linearVelocity = car.transform.forward * Vehicles.Kmh(40f);
            yield return WaitUntil(() => seen.Cause == DamageCause.Vehicle, 3f, "run over by the empty car");
            Assert.IsNull(seen.Attacker, "nobody drove");
            Assert.Greater(seen.Amount, 0f);
        }

        // ---- Motorcycle ----

        [UnityTest]
        public IEnumerator Motorcycle_RideThreeSeconds_Moves()
        {
            yield return LoadTestBox();
            var bike = Find(VehicleKind.Motorcycle);
            yield return GetOnAsDriver(bike);
            var start = bike.transform.position;
            _keys.Frame.Throttle = 1f;
            yield return new WaitForSeconds(3f);
            _keys.Frame.Throttle = -1f;
            Assert.Greater(Flat(bike.transform.position, start), 10f, "rode away");
            Assert.Greater(bike.transform.up.y, 0.8f, "still upright");
        }

        // #182: the bike never leaves the ground by itself: parked, mounting, riding, braking, handbrake, dismounting.
        [UnityTest]
        public IEnumerator Motorcycle_NeverLaunchesItself([Values(0, 1)] int which)
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var bikes = new System.Collections.Generic.List<Vehicle>();
            foreach (var v in Vehicle.All) if (v != null && v.IsMotorcycle) bikes.Add(v);
            foreach (var bike in new[] { bikes[which] })
            {
                var home = bike.Body.position; var homeRotation = bike.Body.rotation;
                bike.Body.position = new Vector3(-26f, 0.3f, -21f);
                bike.Body.rotation = Quaternion.Euler(0f, 90f, 0f);
                bike.Body.linearVelocity = bike.Body.angularVelocity = Vector3.zero;
                yield return new WaitForSeconds(0.8f);
                float rest = bike.transform.position.y, peak = 0f, peakUp = 0f;
                string at = "", phase = "";
                System.Collections.IEnumerator Watch(float seconds)
                {
                    float end = Time.time + seconds;
                    while (Time.time < end)
                    {
                        float h = bike.transform.position.y - rest, up = bike.Body.linearVelocity.y;
                        if (h > peak) { if (h > 0.2f && peak <= 0.2f) at += $" lift@{Time.time:F1}s phase {phase} v={bike.SpeedKmh:F0}km/h tilt={Vector3.Angle(bike.transform.up, Vector3.up):F0}"; peak = h; }
                        if (up > peakUp) peakUp = up;
                        yield return null;
                    }
                }
                phase = "park"; yield return Watch(1f);
                phase = "mount"; yield return GetOnAsDriver(bike);
                me.ServerSetHolstered(true);
                phase = "accel"; _keys.Frame.Throttle = 1f; yield return Watch(1.5f);
                phase = "right"; _keys.Frame.Steer = 1f; yield return Watch(1.5f);
                phase = "left"; _keys.Frame.Steer = -1f; yield return Watch(1.5f);
                phase = "boost"; _keys.Frame.Steer = 0f; _keys.Frame.Boost = true; yield return Watch(1.5f);
                phase = "brake"; _keys.Frame.Boost = false; _keys.Frame.Throttle = -1f; yield return Watch(1.5f);
                phase = "reaccel"; _keys.Frame.Throttle = 1f; yield return Watch(1f);
                phase = "handbrake"; _keys.Frame.Throttle = 0f; _keys.Frame.Handbrake = true; yield return Watch(1.5f);
                _keys.Frame.Handbrake = false;
                phase = "dismount"; _keys.Frame.Interact = true;
                yield return WaitUntil(() => SeatOf(me) < 0, 3f, "off the bike");
                yield return Watch(1.5f);
                Debug.Log($"[#182] {bike.name}: peak height {peak:F2} m, peak up speed {peakUp:F2} m/s ({at})");
                Assert.Less(peak, 0.6f, $"{bike.name} stays on the ground (peak {peak:F2} m, up {peakUp:F2} m/s)");
                Assert.Less(peakUp, 4f, $"{bike.name} never shoots upward (peak {peakUp:F2} m/s)");
                bike.Body.position = home; bike.Body.rotation = homeRotation; bike.Body.linearVelocity = bike.Body.angularVelocity = Vector3.zero;
                yield return new WaitForSeconds(0.5f);
            }
        }

        // #109: D turns right and the bike and its rider lean right (into the turn); A the same to the left. Both bikes.
        [UnityTest]
        public IEnumerator Motorcycle_Steering_TurnsAndLeansIntoTheTurn_BothBikes()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var bikes = new System.Collections.Generic.List<Vehicle>();
            foreach (var v in Vehicle.All) if (v != null && v.IsMotorcycle) bikes.Add(v);
            Assert.GreaterOrEqual(bikes.Count, 2, "both bikes in the test box");
            foreach (var bike in bikes)
            {
                foreach (float steer in new[] { 1f, -1f })
                {
                    // Open floor south of the spawn block, facing +X, then a short run up before the turn.
                    bike.Body.position = new Vector3(-26f, 0.3f, -21f);
                    bike.Body.rotation = Quaternion.Euler(0f, 90f, 0f);
                    bike.Body.linearVelocity = bike.Body.angularVelocity = Vector3.zero;
                    yield return new WaitForSeconds(0.3f);
                    if (SeatOf(me) != 0) yield return GetOnAsDriver(bike);
                    me.ServerSetHolstered(true);
                    yield return null;
                    _keys.Frame.Throttle = 1f;
                    yield return new WaitForSeconds(1.3f);
                    _keys.Frame.Throttle = 0.4f;
                    float yaw = bike.transform.eulerAngles.y;
                    _keys.Frame.Steer = steer;
                    yield return new WaitForSeconds(0.8f);
                    var flatForward = Vector3.ProjectOnPlane(bike.transform.forward, Vector3.up).normalized;
                    var flatRight = Vector3.Cross(Vector3.up, flatForward);
                    float turned = Mathf.DeltaAngle(yaw, bike.transform.eulerAngles.y);
                    float modelLean = Mathf.Asin(Vector3.Dot(bike.Model.up, flatRight)) * Mathf.Rad2Deg;
                    float riderLean = Mathf.Asin(Vector3.Dot(me.transform.up, flatRight)) * Mathf.Rad2Deg;
                    float bodyRoll = Mathf.Asin(Vector3.Dot(bike.transform.up, flatRight)) * Mathf.Rad2Deg;
                    string what = $"{bike.name} steer {steer}: turned {turned:F1}, model lean {modelLean:F1}, rider {riderLean:F1}, body roll {bodyRoll:F1}, speed {bike.SpeedKmh:F0} km/h";
                    Debug.Log("[#109] " + what);
                    _keys.Frame.Steer = 0f;
                    _keys.Frame.Throttle = -1f;
                    Assert.Greater(turned * steer, 15f, "turned the pressed way: " + what);
                    Assert.Greater(modelLean * steer, 15f, "the bike leans into the turn: " + what);
                    Assert.Less(Mathf.Abs(bodyRoll), 8f, "the frame is not rolled out of the turn: " + what);
                    Assert.Greater(riderLean * steer, 15f, "the rider leans into the turn: " + what);
                    yield return new WaitForSeconds(1.5f);
                    _keys.Frame.Throttle = 0f;
                }
                _keys.Frame.Interact = true;
                yield return WaitUntil(() => SeatOf(me) < 0, 3f, "off the bike");
                yield return new WaitForSeconds(1f);
            }
        }

        [UnityTest]
        public IEnumerator Motorcycle_CtrlTwo_ToThePillion_WhoFiresTheRifle()
        {
            yield return LoadTestBox();
            var bike = Find(VehicleKind.Motorcycle);
            var me = Combatant.LocalPlayer;
            GiveRifleAndPistol(me);
            yield return GetOnAsDriver(bike);
            Assert.IsTrue(me.ServerSelectSlot(RifleSlot));
            yield return null;
            Assert.IsFalse(me.ServerFire(bike.transform.forward), "the rider fires a sidearm only");
            yield return SwitchTo(bike, 1);
            Assert.AreEqual(1, SeatOf(me), "on the pillion");
            Assert.IsTrue(VehicleSeat.Astride(me));
            Assert.AreEqual(Rifle, me.ActiveWeaponId, "the rifle taken out on the rider's seat is still in hand");
            yield return new WaitForSeconds(1f);
            Assert.IsTrue(me.ServerFire(bike.transform.forward), "the pillion fires the rifle");
        }

        [UnityTest]
        public IEnumerator Motorcycle_OffARamp_AirControlPitchesTheNoseDown_AndRolls()
        {
            yield return LoadTestBox();
            var bike = Find(VehicleKind.Motorcycle);
            // A ramp across the motorcycle's line, rising toward +X: low end on the floor, high end about 1.2 m up.
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.transform.SetPositionAndRotation(new Vector3(-10f, 0.55f, bike.transform.position.z), Quaternion.Euler(0f, 0f, 10f));
            ramp.transform.localScale = new Vector3(6f, 0.2f, 2.5f);
            yield return GetOnAsDriver(bike);
            _keys.Frame.Throttle = 1f;
            yield return WaitUntil(() => bike.Airborne, 6f, "off the ramp");
            _keys.Frame.Throttle = 0f;
            float before = bike.transform.eulerAngles.x;
            var right = bike.transform.right;
            float rollBefore = Vector3.Dot(bike.transform.up, right);
            _keys.Frame.Pitch = -1f; // Ctrl: nose down
            _keys.Frame.Roll = 1f;   // E: roll right (#88)
            yield return new WaitForSeconds(0.25f);
            float pitched = Mathf.DeltaAngle(before, bike.transform.eulerAngles.x);
            float rolled = Mathf.Asin(Mathf.Clamp(Vector3.Dot(bike.transform.up, right), -1f, 1f)) * Mathf.Rad2Deg
                - Mathf.Asin(Mathf.Clamp(rollBefore, -1f, 1f)) * Mathf.Rad2Deg;
            _keys.Frame.Pitch = 0f;
            _keys.Frame.Roll = 0f;
            Assert.Greater(pitched, 4f, "the nose went down by " + pitched + " degrees");
            Assert.Greater(rolled, 4f, "rolled right by " + rolled + " degrees");
            _keys.Frame.Throttle = -1f;
        }

        [UnityTest]
        public IEnumerator Motorcycle_HardCrash_ThrowsTheRiderOff_Hurt()
        {
            yield return LoadTestBox();
            var bike = Find(VehicleKind.Motorcycle);
            var me = Combatant.LocalPlayer;
            // A wall across the motorcycle's line 10 m ahead.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = bike.transform.position + bike.transform.forward * 10f + Vector3.up;
            wall.transform.rotation = bike.transform.rotation;
            wall.transform.localScale = new Vector3(4f, 2f, 1f);
            yield return GetOnAsDriver(bike);
            DamageInfo seen = default;
            me.ServerDamageFilter = info => { seen = info; return info.Amount; };
            bike.Body.linearVelocity = bike.transform.forward * Vehicles.Kmh(50f);
            yield return WaitUntil(() => !VehicleSeat.IsSeated(me), 3f, "thrown off");
            me.ServerDamageFilter = null;
            Assert.IsTrue(bike.Crashed, "the motorcycle is left to fall over");
            Assert.AreEqual(DamageCause.Fall, seen.Cause);
            Assert.Greater(seen.Amount, 0f, "hurt by the speed");
            Assert.Less(me.Hp.Value, 100f);
        }
    }
}
