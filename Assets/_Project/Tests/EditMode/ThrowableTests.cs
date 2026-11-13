using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // PUBG_GAP 4-3 batch I: the flight of a thrown grenade, its bounces and what the blast is worth at a distance.
    public class ThrowableTests
    {
        [Test]
        public void Defaults_AreTheAgreedNumbers()
        {
            var rules = new ThrowableRules();
            Assert.AreEqual(3, rules.MaxCarried);
            Assert.AreEqual(5f, rules.FuseSeconds, 1e-4f);
            Assert.AreEqual(18f, rules.ThrowSpeed, 1e-4f);
            Assert.AreEqual(100f, rules.BlastDamage, 1e-4f);
            Assert.AreEqual(3.5f, rules.BlastInnerRadius, 1e-4f);
            Assert.AreEqual(8.5f, rules.BlastMidRadius, 1e-4f);
            Assert.AreEqual(10f, rules.BlastOuterRadius, 1e-4f);
            Assert.AreEqual(10, (int)ThrowableRules.GrenadeLootKind, "the value the loot enum gets for grenades");
        }

        [Test]
        public void Flight_IsAParabola()
        {
            // Thrown from 1.5 m up at 10 m/s forward and 5 m/s up, gravity 10.
            Throwables.PositionAt(1f, 1.5f, 2f, 0f, 5f, 10f, 10f, 0f, out float x, out float y, out float z);
            Assert.AreEqual(1f, x, 1e-4f);
            Assert.AreEqual(1.5f, y, 1e-4f);
            Assert.AreEqual(2f, z, 1e-4f);

            Throwables.PositionAt(1f, 1.5f, 2f, 0f, 5f, 10f, 10f, 0.5f, out x, out y, out z);
            Assert.AreEqual(1f, x, 1e-4f, "no sideways speed");
            Assert.AreEqual(2.75f, y, 1e-4f, "the top: 1.5 + 2.5 - 1.25");
            Assert.AreEqual(7f, z, 1e-4f);

            Throwables.PositionAt(1f, 1.5f, 2f, 0f, 5f, 10f, 10f, 1f, out x, out y, out z);
            Assert.AreEqual(1.5f, y, 1e-4f, "back at the height of the hand");
            Assert.AreEqual(12f, z, 1e-4f);

            Throwables.PositionAt(1f, 1.5f, 2f, 0f, 5f, 10f, 10f, 2f, out x, out y, out z);
            Assert.AreEqual(-8.5f, y, 1e-4f, "1.5 + 10 - 20");
            Assert.AreEqual(22f, z, 1e-4f);

            Assert.AreEqual(1f, Throwables.FlightTime(5f, 10f), 1e-4f);
            Assert.AreEqual(0f, Throwables.FlightTime(-3f, 10f), 1e-4f, "thrown downwards");
            Assert.AreEqual(0f, Throwables.FlightTime(0f, 10f), 1e-4f);
        }

        [Test]
        public void PreviewPoints_CoverThePreviewTime()
        {
            var rules = new ThrowableRules { PreviewStepSeconds = 0.5f, PreviewSeconds = 2f };
            Assert.AreEqual(5, Throwables.PreviewPoints(rules), "0, 0.5, 1, 1.5, 2 seconds");
            rules.PreviewStepSeconds = 0f;
            Assert.AreEqual(2, Throwables.PreviewPoints(rules), "never less than a line");
            Assert.AreEqual(45, Throwables.PreviewPoints(new ThrowableRules()), "2.6 s in 0.06 s steps, plus the hand");
        }

        [Test]
        public void Bounce_TurnsTheSpeedAround_AndDampsIt()
        {
            var rules = new ThrowableRules { Restitution = 0.5f, SurfaceFriction = 0.8f };

            // Onto a floor: 10 m/s forward, 4 m/s down.
            float vx = 0f, vy = -4f, vz = 10f;
            Throwables.Bounce(ref vx, ref vy, ref vz, 0f, 1f, 0f, rules);
            Assert.AreEqual(0f, vx, 1e-4f);
            Assert.AreEqual(2f, vy, 1e-4f, "half of it back up");
            Assert.AreEqual(8f, vz, 1e-4f, "slowed along the floor");

            // Into a wall facing -Z.
            vx = 1f; vy = 2f; vz = 6f;
            Throwables.Bounce(ref vx, ref vy, ref vz, 0f, 0f, -1f, rules);
            Assert.AreEqual(0.8f, vx, 1e-4f);
            Assert.AreEqual(1.6f, vy, 1e-4f);
            Assert.AreEqual(-3f, vz, 1e-4f, "back off the wall");

            // Moving away from the surface already: untouched.
            vx = 0f; vy = 3f; vz = 1f;
            Throwables.Bounce(ref vx, ref vy, ref vz, 0f, 1f, 0f, rules);
            Assert.AreEqual(3f, vy, 1e-4f);
            Assert.AreEqual(1f, vz, 1e-4f);
        }

        [Test]
        public void ComesToRest_OnlySlowAndOnAFloor()
        {
            var rules = new ThrowableRules(); // rest below 0.8 m/s, floors from a normal of 0.6 up
            Assert.IsTrue(Throwables.ComesToRest(0.3f, 0.4f, 0f, 1f, rules), "0.5 m/s on flat ground");
            Assert.IsFalse(Throwables.ComesToRest(0.6f, 0.8f, 0f, 1f, rules), "1 m/s: still rolling");
            Assert.IsFalse(Throwables.ComesToRest(0.1f, 0.1f, 0f, 0f, rules), "a wall holds nothing");
            Assert.IsFalse(Throwables.ComesToRest(0.1f, 0.1f, 0f, 0.5f, rules), "too steep");
            Assert.IsTrue(Throwables.ComesToRest(0.1f, 0.1f, 0f, 0.7f, rules), "a gentle slope does");
        }

        [Test]
        public void BlastDamage_IsLethalMediumWeak_ByRing()
        {
            var rules = new ThrowableRules(); // 100 to 3.5 m, 40 at 8.5 m, 0 at 10 m
            Assert.AreEqual(100f, Throwables.BlastDamage(0f, rules), 1e-4f);
            Assert.AreEqual(100f, Throwables.BlastDamage(3.5f, rules), 1e-4f, "lethal ring");
            Assert.AreEqual(70f, Throwables.BlastDamage(6f, rules), 1e-3f, "half way between 100 and 40");
            Assert.AreEqual(40f, Throwables.BlastDamage(8.5f, rules), 1e-4f, "medium ring edge");
            Assert.AreEqual(20f, Throwables.BlastDamage(9.25f, rules), 1e-3f, "weak ring: half of 40");
            Assert.AreEqual(0f, Throwables.BlastDamage(10f, rules), 1e-4f);
            Assert.AreEqual(0f, Throwables.BlastDamage(30f, rules), 1e-4f);

            // A blast without a falloff band: all or nothing.
            var flat = new ThrowableRules { BlastDamage = 40f, BlastInnerRadius = 5f, BlastMidRadius = 5f, BlastOuterRadius = 5f };
            Assert.AreEqual(40f, Throwables.BlastDamage(5f, flat), 1e-4f);
            Assert.AreEqual(0f, Throwables.BlastDamage(5.1f, flat), 1e-4f);
        }

        [Test]
        public void ProneAndVest_CutTheBlast()
        {
            var rules = new ThrowableRules();
            Assert.AreEqual(100f, Throwables.AdjustBlast(100f, false, 0f, rules), 1e-4f);
            Assert.AreEqual(80f, Throwables.AdjustBlast(100f, true, 0f, rules), 1e-4f, "lying down: -20%");
            Assert.AreEqual(60f, Throwables.AdjustBlast(100f, false, 0.4f, rules), 1e-4f, "a level 2 vest");
            Assert.AreEqual(48f, Throwables.AdjustBlast(100f, true, 0.4f, rules), 1e-4f, "both");
            Assert.AreEqual(0f, Throwables.AdjustBlast(100f, false, 3f, rules), 1e-4f, "never more than all of it");
            Assert.AreEqual(100f, Throwables.AdjustBlast(100f, false, -1f, rules), 1e-4f);
        }

        [Test]
        public void Armor_ReductionOf_FollowsTheTierAndAKeptBrokenVest()
        {
            var rules = ArmorRules.Current;
            Assert.AreEqual(0f, Armor.ReductionOf(ArmorSlot.Vest, 0, 0f, rules));
            Assert.AreEqual(0.30f, Armor.ReductionOf(ArmorSlot.Vest, 1, 150f, rules), 1e-4f);
            Assert.AreEqual(0.55f, Armor.ReductionOf(ArmorSlot.Vest, 3, 10f, rules), 1e-4f);
            Assert.AreEqual(rules.BrokenVestReduction, Armor.ReductionOf(ArmorSlot.Vest, 3, 0f, rules), 1e-4f, "a broken vest keeps its 20%");
        }

        [Test]
        public void FuseLeft_AndRoll()
        {
            Assert.AreEqual(2f, Throwables.FuseLeft(5f, 3f), 1e-4f);
            Assert.AreEqual(0f, Throwables.FuseLeft(5f, 5f), 1e-4f, "out of fuse: it goes off in the hand");
            Assert.AreEqual(0f, Throwables.FuseLeft(5f, 9f), 1e-4f);
            Assert.AreEqual(5f, Throwables.FuseLeft(5f, -1f), 1e-4f);
            var rules = new ThrowableRules();
            Throwables.RollVelocity(3f, 2f, 4f, rules, out float vx, out float vz);
            Assert.AreEqual(rules.RollSpeed, (float)System.Math.Sqrt(vx * vx + vz * vz), 1e-3f, "the pitch is ignored");
            Assert.AreEqual(rules.RollSpeed * 0.6f, vx, 1e-3f);
            Throwables.RollVelocity(0f, 1f, 0f, rules, out vx, out vz);
            Assert.AreEqual(0f, vx + vz, "straight up: nowhere to roll");
        }

        [Test]
        public void Room_StopsAtTheCarryLimit()
        {
            var rules = new ThrowableRules(); // 3 at most
            Assert.AreEqual(1, Throwables.Room(0, 1, rules));
            Assert.AreEqual(3, Throwables.Room(0, 5, rules));
            Assert.AreEqual(1, Throwables.Room(2, 2, rules));
            Assert.AreEqual(0, Throwables.Room(3, 1, rules));
            Assert.AreEqual(0, Throwables.Room(4, 1, rules), "over the limit already");
            Assert.AreEqual(0, Throwables.Room(1, 0, rules));
        }

        [Test]
        public void TheThrower_IsOnlyInTheWayAfterTheArmDistance()
        {
            var rules = new ThrowableRules(); // 1 m
            Assert.IsFalse(Throwables.TouchesThrower(0f, rules));
            Assert.IsFalse(Throwables.TouchesThrower(0.9f, rules));
            Assert.IsTrue(Throwables.TouchesThrower(1f, rules));
            Assert.IsTrue(Throwables.TouchesThrower(12f, rules));
        }
    }
}
