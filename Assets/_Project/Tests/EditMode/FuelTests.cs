using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-075: fuel use, the jerrycan, an empty tank and the engine note.
    public class FuelTests
    {
        static readonly FuelRules R = new FuelRules();

        // ---- Consumption ----

        [Test]
        public void Burn_FullThrottle_EmptiesAFullTankInAbout250Seconds()
        {
            Assert.AreEqual(250f, Fuel.SecondsPerTank(1f, false, false, R), 0.5f);
            float tank = 1f;
            for (int i = 0; i < 2490; i++) tank = Fuel.Burn(tank, true, 1f, false, false, 0.1f, R);
            Assert.Greater(tank, 0f);
            for (int i = 0; i < 20; i++) tank = Fuel.Burn(tank, true, 1f, false, false, 0.1f, R);
            Assert.AreEqual(0f, tank);
        }

        [Test]
        public void Burn_Idle_LastsTwentyFiveMinutes()
        {
            Assert.AreEqual(1500f, Fuel.SecondsPerTank(0f, false, false, R), 0.5f);
            Assert.AreEqual(0.9f, Fuel.Burn(1f, true, 0f, false, false, 150f, R), 1e-4f);
        }

        [Test]
        public void Burn_BoostBurnsFaster_MotorcycleSlower()
        {
            Assert.AreEqual(136.4f, Fuel.SecondsPerTank(1f, true, false, R), 0.5f);
            Assert.AreEqual(357.1f, Fuel.SecondsPerTank(1f, false, true, R), 0.5f);
            // Boost while braking / reversing does not burn extra.
            Assert.AreEqual(Fuel.SecondsPerTank(-1f, false, false, R), Fuel.SecondsPerTank(-1f, true, false, R), 1e-3f);
        }

        [Test]
        public void Burn_EngineOff_BurnsNothing()
        {
            Assert.AreEqual(0.5f, Fuel.Burn(0.5f, false, 1f, true, false, 60f, R));
        }

        [Test]
        public void Burn_NeverGoesBelowEmpty()
        {
            Assert.AreEqual(0f, Fuel.Burn(0.001f, true, 1f, true, false, 60f, R));
            Assert.AreEqual(0f, Fuel.Burn(0f, true, 1f, true, false, 1f, R));
        }

        // ---- Empty tank ----

        [Test]
        public void EngineCanRun_OnlyWithFuelAndNotDisabled()
        {
            Assert.IsTrue(Fuel.EngineCanRun(0.01f, false));
            Assert.IsFalse(Fuel.EngineCanRun(0f, false));
            Assert.IsFalse(Fuel.EngineCanRun(0.5f, true));
        }

        // ---- Jerrycan ----

        [Test]
        public void Refill_AddsSixtyPercent_NeverOverFull()
        {
            Assert.AreEqual(0.8f, Fuel.Refill(0.2f, R), 1e-5f);
            Assert.AreEqual(0.6f, Fuel.Refill(0f, R), 1e-5f);
            Assert.AreEqual(1f, Fuel.Refill(0.7f, R));
        }

        [Test]
        public void CanRefuel_NeedsRoomNearnessAndAStillVehicle()
        {
            Assert.IsTrue(Fuel.CanRefuel(0.3f, false, 1f, 0f, R));
            Assert.IsFalse(Fuel.CanRefuel(0.3f, false, 2f, 0f, R), "too far on foot");
            Assert.IsTrue(Fuel.CanRefuel(0.3f, true, 50f, 0f, R), "seated counts as next to it");
            Assert.IsFalse(Fuel.CanRefuel(0.3f, true, 0f, 12f, R), "moving");
            Assert.IsFalse(Fuel.CanRefuel(0.996f, false, 0.5f, 0f, R), "full");
        }

        [Test]
        public void RefuelBroken_WalkingOffOrDrivingAway()
        {
            Assert.IsFalse(Fuel.RefuelBroken(false, 2f, 0f, R));
            Assert.IsTrue(Fuel.RefuelBroken(false, 3f, 0f, R));
            Assert.IsTrue(Fuel.RefuelBroken(true, 0f, 15f, R));
        }

        // ---- Spawn and gauge ----

        [Test]
        public void SpawnShare_SeededWithinRange_AndRepeatable()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                float a = Fuel.SpawnShare(new Random(seed), R);
                Assert.That(a, Is.InRange(0.35f, 0.9f));
                Assert.AreEqual(a, Fuel.SpawnShare(new Random(seed), R));
            }
        }

        [Test]
        public void Percent_RoundsButShowsAnyFuelAsAtLeastOne()
        {
            Assert.AreEqual(0, Fuel.Percent(0f));
            Assert.AreEqual(1, Fuel.Percent(0.002f));
            Assert.AreEqual(50, Fuel.Percent(0.5f));
            Assert.AreEqual(100, Fuel.Percent(1f));
        }

        // ---- Engine note ----

        [Test]
        public void EngineNote_IdleAtRest_RedlineAtTopSpeed()
        {
            Assert.AreEqual(EngineNote.IdleRpm, EngineNote.Rpm01(0f, 30f, 5, 0f), 1e-5f);
            Assert.AreEqual(1f, EngineNote.Rpm01(30f, 30f, 5, 1f), 1e-5f);
            Assert.Greater(EngineNote.Rpm01(0f, 30f, 5, 1f), EngineNote.IdleRpm, "revving at a standstill");
        }

        [Test]
        public void EngineNote_DropsAtAGearShift()
        {
            // Gear 1 of 5 ends at t = 0.2^0.8 ≈ 0.276 of top speed.
            float before = EngineNote.Rpm01(0.27f * 30f, 30f, 5, 1f);
            float after = EngineNote.Rpm01(0.29f * 30f, 30f, 5, 1f);
            Assert.Greater(before, 0.9f);
            Assert.Less(after, 0.6f);
        }

        [Test]
        public void EngineNote_PitchAndVolumeRiseWithRpm()
        {
            Assert.Less(EngineNote.Pitch(0.2f, false), EngineNote.Pitch(0.9f, false));
            Assert.Less(EngineNote.Volume(0.2f, 0f), EngineNote.Volume(0.9f, 1f));
            Assert.LessOrEqual(EngineNote.Volume(1f, 1f), 1f);
        }
    }
}
