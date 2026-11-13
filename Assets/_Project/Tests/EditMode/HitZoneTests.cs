using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-053 (3): where a hit lands decides the zone, the weapon's data decides what the zone is worth,
    // and the shooter's own movement widens the spread.
    public class HitZoneTests
    {
        static WeaponDef Weapon() => new WeaponDef
        {
            Id = 1, Name = "P", Damage = 20, SpreadDeg = 2, Pellets = 1, Range = 100,
            FalloffStart = 10, FalloffEnd = 30, MinDamageFactor = 0.5f,
        };

        [Test]
        public void Standing_HeightDecidesTheZone()
        {
            var rules = new HitZoneRules();
            Assert.AreEqual(1.8f, rules.StandingHeight, 1e-4f);
            Assert.AreEqual(1.5f, rules.HeadFrom(0f), 1e-4f);
            Assert.AreEqual(0.81f, rules.LimbBelow(0f), 1e-4f);

            Assert.AreEqual(HitZone.Head, HitZones.At(1.75f, 0f, rules));
            Assert.AreEqual(HitZone.Head, HitZones.At(1.55f, 0f, rules));
            Assert.AreEqual(HitZone.Body, HitZones.At(1.45f, 0f, rules));
            Assert.AreEqual(HitZone.Body, HitZones.At(1.1f, 0f, rules));
            Assert.AreEqual(HitZone.Body, HitZones.At(0.85f, 0f, rules));
            Assert.AreEqual(HitZone.Limb, HitZones.At(0.75f, 0f, rules));
            Assert.AreEqual(HitZone.Limb, HitZones.At(0.1f, 0f, rules));
            // A point just outside the collider still belongs to the nearest band.
            Assert.AreEqual(HitZone.Head, HitZones.At(1.9f, 0f, rules));
            Assert.AreEqual(HitZone.Limb, HitZones.At(-0.05f, 0f, rules));
        }

        [Test]
        public void Crouching_LowersTheBands()
        {
            var rules = new HitZoneRules();
            const float crouch = -0.5f; // eyes half a metre lower
            Assert.AreEqual(1.3f, rules.Height(crouch), 1e-4f);
            Assert.AreEqual(1.0f, rules.HeadFrom(crouch), 1e-4f);
            Assert.AreEqual(0.585f, rules.LimbBelow(crouch), 1e-4f);

            // Chest height of a standing target is the head of a crouching one.
            Assert.AreEqual(HitZone.Body, HitZones.At(1.1f, 0f, rules));
            Assert.AreEqual(HitZone.Head, HitZones.At(1.1f, crouch, rules));
            Assert.AreEqual(HitZone.Body, HitZones.At(0.9f, crouch, rules));
            Assert.AreEqual(HitZone.Body, HitZones.At(0.65f, crouch, rules));
            Assert.AreEqual(HitZone.Limb, HitZones.At(0.5f, crouch, rules));
            // What is a leg shot on a standing target is a body shot on a crouching one.
            Assert.AreEqual(HitZone.Limb, HitZones.At(0.7f, 0f, rules));
            Assert.AreEqual(HitZone.Body, HitZones.At(0.7f, crouch, rules));
        }

        [Test]
        public void StanceNeverShrinksTheBodyBelowTheMinimum()
        {
            var rules = new HitZoneRules();
            Assert.AreEqual(0.6f, rules.Height(-5f), 1e-4f);
            Assert.AreEqual(HitZone.Head, HitZones.At(0.4f, -5f, rules));
            Assert.AreEqual(HitZone.Limb, HitZones.At(0.2f, -5f, rules));
        }

        [Test]
        public void Multipliers_DefaultToTheAdr053Values_AndComeFromTheWeapon()
        {
            var w = Weapon();
            Assert.AreEqual(2f, HitZones.Multiplier(w, HitZone.Head), 1e-4f);
            Assert.AreEqual(1f, HitZones.Multiplier(w, HitZone.Body), 1e-4f);
            Assert.AreEqual(0.75f, HitZones.Multiplier(w, HitZone.Limb), 1e-4f);

            w.HeadFactor = 1.5f;
            w.LimbFactor = 0.8f;
            Assert.AreEqual(1.5f, HitZones.Multiplier(w, HitZone.Head), 1e-4f);
            Assert.AreEqual(0.8f, HitZones.Multiplier(w, HitZone.Limb), 1e-4f);
        }

        [Test]
        public void ZoneDamage_IsDistanceDamageTimesTheMultiplier()
        {
            var w = Weapon();
            Assert.AreEqual(40f, DamageRules.ForZone(w, 5f, HitZone.Head), 1e-4f);
            Assert.AreEqual(20f, DamageRules.ForZone(w, 5f, HitZone.Body), 1e-4f);
            Assert.AreEqual(15f, DamageRules.ForZone(w, 5f, HitZone.Limb), 1e-4f);
            // Half way down the falloff the projectile is worth 15.
            Assert.AreEqual(30f, DamageRules.ForZone(w, 20f, HitZone.Head), 1e-4f);
            Assert.AreEqual(11.25f, DamageRules.ForZone(w, 20f, HitZone.Limb), 1e-4f);
            Assert.AreEqual(0f, DamageRules.ForZone(w, 101f, HitZone.Head), 1e-4f);
        }

        [Test]
        public void MovementSpread_GrowsWithSpeed_AndMostInTheAir()
        {
            var w = Weapon();
            var rules = new FireRules();
            Assert.AreEqual(1.5f, w.MoveSpreadFactor, 1e-4f);
            Assert.AreEqual(2.5f, w.AirSpreadFactor, 1e-4f);
            Assert.AreEqual(0.5f, rules.MoveSpreadMinSpeed, 1e-4f);
            Assert.AreEqual(4.5f, rules.MoveSpreadFullSpeed, 1e-4f);

            Assert.AreEqual(1f, DamageRules.MovementSpread(w, 0f, false, rules), 1e-4f);
            Assert.AreEqual(1f, DamageRules.MovementSpread(w, 0.5f, false, rules), 1e-4f);
            Assert.AreEqual(1.25f, DamageRules.MovementSpread(w, 2.5f, false, rules), 1e-4f);
            Assert.AreEqual(1.5f, DamageRules.MovementSpread(w, 4.5f, false, rules), 1e-4f);
            Assert.AreEqual(1.5f, DamageRules.MovementSpread(w, 7f, false, rules), 1e-4f);
            Assert.AreEqual(2.5f, DamageRules.MovementSpread(w, 0f, true, rules), 1e-4f);
            Assert.AreEqual(1f, DamageRules.MovementSpread(w, float.NaN, false, rules), 1e-4f);

            // Data can never make moving more accurate than standing.
            w.MoveSpreadFactor = 0.5f;
            w.AirSpreadFactor = 0.5f;
            Assert.AreEqual(1f, DamageRules.MovementSpread(w, 7f, false, rules), 1e-4f);
            Assert.AreEqual(1f, DamageRules.MovementSpread(w, 0f, true, rules), 1e-4f);
        }

        [Test]
        public void Health_RefusesNaNDamage()
        {
            var h = new Health(100);
            Assert.AreEqual(0f, h.ApplyDamage(float.NaN));
            Assert.AreEqual(100f, h.Current);
            Assert.IsFalse(h.IsDead);
        }
    }
}
