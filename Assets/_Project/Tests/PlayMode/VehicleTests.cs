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
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(me.ServerFire(car.transform.forward), "the pistol fires");
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
            yield return new WaitForSeconds(0.2f);
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
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(me.ServerFire(bike.transform.forward), "the pillion fires the rifle");
        }

        [UnityTest]
        public IEnumerator Motorcycle_OffARamp_AirControlPitchesTheNoseDown()
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
            _keys.Frame.Pitch = -1f; // Ctrl: nose down
            yield return new WaitForSeconds(0.25f);
            float pitched = Mathf.DeltaAngle(before, bike.transform.eulerAngles.x);
            _keys.Frame.Pitch = 0f;
            Assert.Greater(pitched, 4f, "the nose went down by " + pitched + " degrees");
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
