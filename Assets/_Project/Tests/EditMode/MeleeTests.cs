using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-059 (1): bare-handed punching — reach, cone, where the punch lands, damage and rate.
    public class MeleeTests
    {
        static readonly MeleeRules Rules = new MeleeRules();

        [Test]
        public void Defaults_MatchTheDecision()
        {
            Assert.AreEqual(12f, Rules.PunchDamage, 1e-5f);
            Assert.AreEqual(1.3f, Rules.PunchRange, 1e-5f);
            Assert.AreEqual(60f, Rules.PunchConeDeg, 1e-5f);
            Assert.AreEqual(0.55f, Rules.PunchInterval, 1e-5f);
            Assert.AreEqual(1.5f, Rules.HeadMultiplier, 1e-5f);
        }

        // Axis straight ahead (+Z) for the reach cases.
        static bool Reach(float dx, float dy, float dz) => Melee.InReach(dx, dy, dz, 0f, 0f, 1f, 1.3f, 60f);

        [Test]
        public void InReach_RangeAlongTheAxis()
        {
            Assert.IsTrue(Reach(0f, 0f, 1.0f));
            Assert.IsTrue(Reach(0f, 0f, 1.3f), "the range itself counts");
            Assert.IsFalse(Reach(0f, 0f, 1.31f));
            Assert.IsFalse(Reach(0f, 0f, 2.0f));
            Assert.IsTrue(Reach(0f, 0f, 0f), "a point at the eyes");
        }

        [Test]
        public void InReach_ConeIsSixtyDegreesWide_ThirtyEitherSide()
        {
            float r = 1f;
            float Rad(float deg) => deg * (float)Math.PI / 180f;
            Assert.IsTrue(Reach(r * (float)Math.Sin(Rad(29f)), 0f, r * (float)Math.Cos(Rad(29f))));
            Assert.IsFalse(Reach(r * (float)Math.Sin(Rad(31f)), 0f, r * (float)Math.Cos(Rad(31f))));
            Assert.IsTrue(Reach(0f, -r * (float)Math.Sin(Rad(28f)), r * (float)Math.Cos(Rad(28f))), "below the axis too");
            Assert.IsFalse(Reach(0f, -r * (float)Math.Sin(Rad(40f)), r * (float)Math.Cos(Rad(40f))));
            Assert.IsFalse(Reach(1f, 0f, 0f), "beside");
            Assert.IsFalse(Reach(0f, 0f, -1f), "behind");
        }

        [Test]
        public void InReach_TheAxisNeedNotBeUnitLength_AndAZeroAxisReachesNothing()
        {
            Assert.IsTrue(Melee.InReach(0f, 0f, 1f, 0f, 0f, 5f, 1.3f, 60f));
            Assert.IsFalse(Melee.InReach(0f, 0f, 1f, 0f, 0f, 0f, 1.3f, 60f));
        }

        [Test]
        public void InReach_StandingPlayer_ChestOfAStandingTargetOneMetreAhead()
        {
            // Eyes at 1.6 m, chest at 0.61 x 1.8 = 1.1 m, 1 m ahead, aim level: 26.6 degrees below the axis, 1.12 m away.
            float chest = 1.8f * Rules.ChestFraction;
            Assert.IsTrue(Melee.InReach(0f, chest - 1.6f, 1f, 0f, 0f, 1f, Rules.PunchRange, Rules.PunchConeDeg));
            // Two metres away it is out of range.
            Assert.IsFalse(Melee.InReach(0f, chest - 1.6f, 2f, 0f, 0f, 1f, Rules.PunchRange, Rules.PunchConeDeg));
        }

        [Test]
        public void HitHeight_FollowsTheAimLineToTheTarget()
        {
            // Level aim from eyes 1.6 m over the target's feet: lands at 1.6 m.
            Assert.AreEqual(1.6f, Melee.HitHeight(1.6f, 1f, 0f, 1f, 1.8f), 1e-5f);
            // Aimed 0.5 m down per metre, target 1 m away: 1.1 m (the chest).
            Assert.AreEqual(1.1f, Melee.HitHeight(1.6f, 1f, -0.5f, 1f, 1.8f), 1e-5f);
            // Clamped to the body.
            Assert.AreEqual(1.8f, Melee.HitHeight(1.6f, 1f, 1f, 1f, 1.8f), 1e-5f);
            Assert.AreEqual(0f, Melee.HitHeight(1.6f, 1f, -3f, 1f, 1.8f), 1e-5f);
            // Straight down or up.
            Assert.AreEqual(0f, Melee.HitHeight(1.6f, 0.2f, -1f, 0f, 1.8f), 1e-5f);
            Assert.AreEqual(1.8f, Melee.HitHeight(1.6f, 0.2f, 1f, 0f, 1.8f), 1e-5f);
        }

        [Test]
        public void HitHeight_DecidesTheZoneThroughHitZones()
        {
            var zones = new HitZoneRules();
            Assert.AreEqual(HitZone.Head, HitZones.At(Melee.HitHeight(1.6f, 1f, 0f, 1f, zones.Height(0f)), 0f, zones), "level aim at a standing target's face");
            Assert.AreEqual(HitZone.Body, HitZones.At(Melee.HitHeight(1.6f, 1f, -0.5f, 1f, zones.Height(0f)), 0f, zones), "aimed at the chest");
        }

        [Test]
        public void Damage_HeadTakesTheMultiplier_EverythingElseThePlainDamage()
        {
            Assert.AreEqual(12f, Melee.Damage(HitZone.Body, Rules), 1e-5f);
            Assert.AreEqual(12f, Melee.Damage(HitZone.Limb, Rules), 1e-5f);
            Assert.AreEqual(18f, Melee.Damage(HitZone.Head, Rules), 1e-5f);
            var custom = new MeleeRules { PunchDamage = 10f, HeadMultiplier = 2f };
            Assert.AreEqual(20f, Melee.Damage(HitZone.Head, custom), 1e-5f);
        }

        [Test]
        public void Rate_OnePunchPerInterval_WithTheServersSmallTolerance()
        {
            // The server uses the same cadence as for weapons: early by at most min(0.05, 0.4 x interval).
            var fire = new FireRules();
            double early = fire.ServerEarly(Rules.PunchInterval);
            Assert.AreEqual(0.05, early, 1e-9);
            var cadence = new FireCadence();
            Assert.IsTrue(cadence.TryFire(0.0, Rules.PunchInterval, early));
            Assert.IsFalse(cadence.TryFire(0.3, Rules.PunchInterval, early), "a second punch 0.3 s later is ignored");
            Assert.IsTrue(cadence.TryFire(0.52, Rules.PunchInterval, early), "a hair early is fine");
            Assert.IsFalse(cadence.TryFire(1.0, Rules.PunchInterval, early), "the schedule did not move: next due at 1.10");
            Assert.IsTrue(cadence.TryFire(1.1, Rules.PunchInterval, early));
        }

        [Test]
        public void DamageCause_HasMelee()
        {
            Assert.AreEqual(4, (int)DamageCause.Melee);
        }
    }
}
