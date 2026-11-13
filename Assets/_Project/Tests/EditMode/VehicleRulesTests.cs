using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-065 (5): vehicle seat rules, weapon permissions and the pure driving / riding helpers.
    public class VehicleRulesTests
    {
        static readonly VehicleRules Car = new VehicleRules();
        static readonly VehicleRules Bike = VehicleRules.MotorcycleDefaults();
        static readonly WeaponDef Pistol = new WeaponDef { Id = 1, Category = WeaponCategory.Sidearm };
        static readonly WeaponDef Rifle = new WeaponDef { Id = 3, Category = WeaponCategory.Primary };
        const float Dt = 0.02f;

        // ---- Seats ----

        [Test]
        public void Enter_TakesTheDriversSeatWhenFree_EvenIfAnotherDoorIsNearer()
        {
            Assert.AreEqual(0, Vehicles.ChooseEnterSeat(new[] { -1, -1, -1, -1 }, new[] { 2.5f, 0.5f, 1f, 1f }, 3f));
        }

        [Test]
        public void Enter_DriverTaken_TakesTheFreeSeatWithTheNearestDoor()
        {
            Assert.AreEqual(3, Vehicles.ChooseEnterSeat(new[] { 7, -1, -1, -1 }, new[] { 0.2f, 2f, 1.5f, 0.6f }, 3f));
            Assert.AreEqual(2, Vehicles.ChooseEnterSeat(new[] { 7, 8, -1, 9 }, new[] { 0.2f, 0.3f, 2.9f, 0.1f }, 3f));
        }

        [Test]
        public void Enter_RefusedWhenFarOrFull()
        {
            Assert.AreEqual(Vehicles.NoSeat, Vehicles.ChooseEnterSeat(new[] { -1, -1, -1, -1 }, new[] { 3.1f, 3.2f, 4f, 5f }, 3f), "too far");
            Assert.AreEqual(Vehicles.NoSeat, Vehicles.ChooseEnterSeat(new[] { 1, 2, 3, 4 }, new[] { 0.5f, 0.5f, 0.5f, 0.5f }, 3f), "full");
            Assert.AreEqual(0, Vehicles.ChooseEnterSeat(new[] { -1, -1 }, new[] { 3f, 9f }, 3f), "the range itself counts (motorcycle, two seats)");
        }

        [Test]
        public void Switch_OnlyIntoAnotherFreeExistingSeat()
        {
            var occupants = new[] { 5, -1, 6, -1 };
            Assert.IsTrue(Vehicles.CanSwitch(occupants, 0, 1), "Ctrl+2 to the free front passenger seat");
            Assert.IsTrue(Vehicles.CanSwitch(occupants, 2, 3));
            Assert.IsFalse(Vehicles.CanSwitch(occupants, 0, 2), "taken");
            Assert.IsFalse(Vehicles.CanSwitch(occupants, 0, 0), "the same seat");
            Assert.IsFalse(Vehicles.CanSwitch(occupants, 0, 4), "no fifth seat");
            Assert.IsFalse(Vehicles.CanSwitch(new[] { 5, -1 }, 0, 2), "a motorcycle has two seats");
            Assert.IsFalse(Vehicles.CanSwitch(occupants, -1, 1), "not seated");
        }

        // ---- Weapon permission matrix ----

        [Test]
        public void Driver_FiresAPistolOnly()
        {
            Assert.IsTrue(Vehicles.CanUseWeapon(0, Pistol), "driver + pistol");
            Assert.IsFalse(Vehicles.CanUseWeapon(0, Rifle), "driver + rifle");
            Assert.IsFalse(Vehicles.CanUse(0, HandUse.Throwable), "driver throws nothing");
            Assert.IsFalse(Vehicles.CanUseWeapon(0, null), "no punching from the wheel");
        }

        [Test]
        public void Passengers_FireAndThrowAnything_ButDoNotPunch()
        {
            for (int seat = 1; seat < 4; seat++)
            {
                Assert.IsTrue(Vehicles.CanUseWeapon(seat, Rifle), "rifle from seat " + seat);
                Assert.IsTrue(Vehicles.CanUseWeapon(seat, Pistol), "pistol from seat " + seat);
                Assert.IsTrue(Vehicles.CanUse(seat, HandUse.Throwable), "throw from seat " + seat);
                Assert.IsFalse(Vehicles.CanUse(seat, HandUse.Punch), "punch from seat " + seat);
            }
        }

        [Test]
        public void OnFoot_EverythingIsAllowed()
        {
            Assert.IsTrue(Vehicles.CanUseWeapon(Vehicles.NoSeat, Rifle));
            Assert.IsTrue(Vehicles.CanUseWeapon(Vehicles.NoSeat, null));
            Assert.IsTrue(Vehicles.CanUse(Vehicles.NoSeat, HandUse.Throwable));
        }

        // ---- Driving ----

        [Test]
        public void Throttle_AcceleratesBelowTopSpeed_NotAboveIt()
        {
            float top = Vehicles.TopSpeed(false, Car);
            Assert.AreEqual(Car.EngineAccel, Vehicles.DriveAccel(0f, 1f, top, 7f, Dt, Car), 1e-4f, "full pull from a standstill");
            Assert.Greater(Vehicles.DriveAccel(top * 0.5f, 1f, top, 7f, Dt, Car), 0f);
            Assert.Less(Vehicles.DriveAccel(top + 2f, 1f, top, 7f, Dt, Car), 0f, "above the surface's top speed it slows");
            Assert.AreEqual(25f, top, 0.01f, "90 km/h");
        }

        [Test]
        public void Boost_RaisesTopSpeedAndPull()
        {
            Assert.AreEqual(Vehicles.Kmh(Car.BoostMaxKmh), Vehicles.TopSpeed(false, true, Car), 1e-4f);
            Assert.Greater(Vehicles.TopSpeed(false, true, Car), Vehicles.TopSpeed(false, false, Car));
            float top = Vehicles.TopSpeed(false, true, Car);
            Assert.AreEqual(Car.EngineAccel * Car.BoostAccelFactor, Vehicles.DriveAccel(0f, 1f, top, 7f, Dt, Car, Car.BoostAccelFactor), 1e-4f);
            Assert.AreEqual(120f, Bike.BoostMaxKmh, 1e-4f, "motorcycle boosted ~120 km/h");
            Assert.Greater(Bike.EngineAccel, Car.EngineAccel, "the motorcycle pulls harder");
        }

        [Test]
        public void OffRoad_IsAboutFifteenPercentSlower()
        {
            Assert.AreEqual(0.85f, Vehicles.TopSpeed(true, Car) / Vehicles.TopSpeed(false, Car), 1e-4f);
        }

        [Test]
        public void Braking_NeverReversesWithinAStep()
        {
            float a = Vehicles.DriveAccel(0.05f, -1f, 25f, 7f, Dt, Car);
            Assert.GreaterOrEqual(0.05f + a * Dt, -1e-5f, "S at a crawl stops first");
            float coast = Vehicles.DriveAccel(0.01f, 0f, 25f, 7f, Dt, Car);
            Assert.AreEqual(0f, 0.01f + coast * Dt, 1e-5f, "coasting comes to rest, not backwards");
            Assert.AreEqual(0f, 0.02f + Vehicles.BrakeAccel(0.02f, 4f, Dt) * Dt, 1e-5f);
        }

        [Test]
        public void Reverse_IsCapped()
        {
            float reverseTop = Vehicles.ReverseTopSpeed(false, Car);
            Assert.Less(Vehicles.DriveAccel(-1f, -1f, 25f, reverseTop, Dt, Car), 0f, "reverses below the cap");
            Assert.Greater(Vehicles.DriveAccel(-reverseTop - 1f, -1f, 25f, reverseTop, Dt, Car), 0f, "pulled back above it");
        }

        [Test]
        public void Steering_NarrowsWithSpeed()
        {
            Assert.AreEqual(Car.SteerMaxDeg, Vehicles.SteerLimit(0f, 25f, Car), 1e-4f);
            Assert.AreEqual(Car.SteerAtTopSpeedDeg, Vehicles.SteerLimit(25f, 25f, Car), 1e-4f);
            Assert.Less(Vehicles.SteerLimit(12f, 25f, Car), Vehicles.SteerLimit(4f, 25f, Car));
        }

        [Test]
        public void Suspension_RestsUnderTheWeight_AndNeverPulls()
        {
            float rest = Vehicles.RestLength(Car, 4);
            Assert.Greater(rest, 0.1f);
            float load = Vehicles.SuspensionForce(Car.SuspensionTravel - rest, 0f, Car);
            Assert.AreEqual(Car.MassKg * 9.81f / 4f, load, 1f, "each wheel carries a quarter of the car at rest");
            Assert.AreEqual(0f, Vehicles.SuspensionForce(-0.1f, -2f, Car));
            float bikeRest = Vehicles.RestLength(Bike, 2);
            Assert.AreEqual(Bike.MassKg * 9.81f / 2f, Vehicles.SuspensionForce(Bike.SuspensionTravel - bikeRest, 0f, Bike), 1f, "two wheels share the motorcycle");
        }

        [Test]
        public void RoadMask_RoundTrips()
        {
            var bits = RoadMask.Build(10, 4f, (x, z) => x > 20f && z < 12f);
            Assert.IsTrue(RoadMask.Test(bits, 10, 4f, 22f, 2f));
            Assert.IsFalse(RoadMask.Test(bits, 10, 4f, 18f, 2f));
            Assert.IsFalse(RoadMask.Test(bits, 10, 4f, 22f, 14f));
            Assert.IsFalse(RoadMask.Test(bits, 10, 4f, -1f, 2f), "outside the map");
            Assert.IsFalse(RoadMask.Test(bits, 10, 4f, 41f, 2f), "outside the map");
        }

        // ---- Getting out at speed ----

        [Test]
        public void ExitDamage_NoneUpTo30_RisingToLethalAt65()
        {
            Assert.AreEqual(0f, Vehicles.ExitDamage(0f, Car));
            Assert.AreEqual(0f, Vehicles.ExitDamage(30f, Car), "30 km/h is safe");
            float at40 = Vehicles.ExitDamage(40f, Car), at50 = Vehicles.ExitDamage(50f, Car), at60 = Vehicles.ExitDamage(60f, Car);
            Assert.Greater(at40, 0f);
            Assert.Greater(at50, at40);
            Assert.Greater(at60, at50);
            Assert.Less(at60, 100f, "still survivable just below 65");
            Assert.AreEqual(100f * (20f / 35f) * (20f / 35f), at50, 1e-3f);
            Assert.GreaterOrEqual(Vehicles.ExitDamage(65f, Car), 100f, "65 km/h kills");
            Assert.GreaterOrEqual(Vehicles.ExitDamage(120f, Car), 100f);
        }

        // ---- Two wheels ----

        [Test]
        public void AirControl_MapsKeysToTorque()
        {
            Vehicles.AirControl(1f, 0f, Bike, out float right, out float forward);
            Assert.AreEqual(-Bike.AirPitchAccelDeg, right, 1e-4f, "Space: nose up is a negative turn about the right axis");
            Assert.AreEqual(0f, forward, 1e-4f);
            Vehicles.AirControl(-1f, 0f, Bike, out right, out _);
            Assert.AreEqual(Bike.AirPitchAccelDeg, right, 1e-4f, "Ctrl: nose down");
            Vehicles.AirControl(0f, 1f, Bike, out _, out forward);
            Assert.AreEqual(-Bike.AirRollAccelDeg, forward, 1e-4f, "E: roll right is a negative turn about the forward axis");
            Vehicles.AirControl(0f, -1f, Bike, out _, out forward);
            Assert.AreEqual(Bike.AirRollAccelDeg, forward, 1e-4f, "Q: roll left");
            Vehicles.AirControl(5f, -5f, Bike, out right, out forward);
            Assert.AreEqual(-Bike.AirPitchAccelDeg, right, 1e-4f, "inputs are clamped");
            Assert.AreEqual(Bike.AirRollAccelDeg, forward, 1e-4f);
        }

        [Test]
        public void Crash_EjectsAtTheThreshold_AndOnBadLandings()
        {
            Assert.IsFalse(Vehicles.Crashes(Bike.CrashImpactSpeed - 0.01f, Bike));
            Assert.IsTrue(Vehicles.Crashes(Bike.CrashImpactSpeed, Bike));
            Assert.IsTrue(Vehicles.Crashes(20f, Bike));
            Assert.IsFalse(Vehicles.BadLanding(30f, Bike));
            Assert.IsTrue(Vehicles.BadLanding(Bike.CrashLandAngleDeg + 1f, Bike));
        }

        [Test]
        public void Lean_GrowsWithSpeedAndSteering()
        {
            Assert.AreEqual(0f, Vehicles.LeanDeg(0f, 1f, Bike), 1e-4f, "no lean standing still");
            Assert.AreEqual(Bike.MaxLeanDeg, Vehicles.LeanDeg(30f, 1f, Bike), 1e-4f, "full lock at speed");
            Assert.AreEqual(-Bike.MaxLeanDeg, Vehicles.LeanDeg(30f, -1f, Bike), 1e-4f, "the other way");
            Assert.Less(Vehicles.LeanDeg(5f, 1f, Bike), Vehicles.LeanDeg(10f, 1f, Bike));
            Assert.AreEqual(0f, Vehicles.WheelieDeg(-3f, Bike), 1e-4f, "no wheelie braking");
            Assert.AreEqual(Bike.WheelieMaxDeg, Vehicles.WheelieDeg(100f, Bike), 1e-4f);
        }

        [Test]
        public void Rules_ForEachKind()
        {
            Assert.AreSame(VehicleRules.Current, VehicleRules.For(VehicleKind.Car));
            Assert.AreSame(VehicleRules.Motorcycle, VehicleRules.For(VehicleKind.Motorcycle));
            Assert.AreEqual(4, Car.SeatCount);
            Assert.AreEqual(2, Bike.SeatCount);
            Assert.Less(Bike.MassKg, Car.MassKg);
        }
    }
}
