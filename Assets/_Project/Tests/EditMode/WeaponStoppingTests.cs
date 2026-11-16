using System.IO;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-182: stopping power 1-10 shakes the victim's aim and slows them for a moment; hits never add up.
    public class WeaponStoppingTests
    {
        static readonly StoppingRules R = new StoppingRules();
        static WeaponDef Gun(int stopping) => new WeaponDef { Id = 99, Name = "t", Stopping = stopping, Category = WeaponCategory.Primary };

        [Test]
        public void Strength_IsZeroAtOne_AndOneAtTen_Linear()
        {
            Assert.AreEqual(0f, WeaponStopping.Strength(0), 1e-6f);
            Assert.AreEqual(0f, WeaponStopping.Strength(1), 1e-6f);
            Assert.AreEqual(1f, WeaponStopping.Strength(10), 1e-6f);
            Assert.AreEqual(1f, WeaponStopping.Strength(15), 1e-6f, "clamped");
            Assert.AreEqual(4f / 9f, WeaponStopping.Strength(5), 1e-6f);
        }

        [Test]
        public void Stopping10_BodyHit_ShakesFourDegrees_SlowsThirtyFivePercent_ForHalfASecond()
        {
            var e = WeaponStopping.Of(Gun(10), HitZone.Body, R);
            Assert.AreEqual(4f, e.ShakeDeg, 1e-5f);
            Assert.AreEqual(0.35f, e.Slow, 1e-5f);
            Assert.AreEqual(0.5f, e.Seconds, 1e-5f);
        }

        [Test]
        public void Stopping1_DoesNothing_AndMeleeNeither()
        {
            Assert.IsTrue(WeaponStopping.Of(Gun(1), HitZone.Body, R).IsNone);
            Assert.IsTrue(WeaponStopping.Of(new WeaponDef { Category = WeaponCategory.Melee, Stopping = 10 }, HitZone.Body, R).IsNone);
            Assert.IsTrue(WeaponStopping.Of(null, HitZone.Body, R).IsNone);
        }

        [Test]
        public void HeadCountsLikeBody_LimbHalf()
        {
            var body = WeaponStopping.Of(Gun(8), HitZone.Body, R);
            var head = WeaponStopping.Of(Gun(8), HitZone.Head, R);
            var limb = WeaponStopping.Of(Gun(8), HitZone.Limb, R);
            Assert.AreEqual(body.ShakeDeg, head.ShakeDeg, 1e-6f);
            Assert.AreEqual(body.ShakeDeg * 0.5f, limb.ShakeDeg, 1e-5f);
            Assert.AreEqual(body.Slow * 0.5f, limb.Slow, 1e-5f);
        }

        [Test]
        public void StrongerHit_ReplacesTheRunningOne_WeakerIsIgnored_EqualRenews()
        {
            var s = new StaggerState();
            var weak = WeaponStopping.Of(Gun(4), HitZone.Body, R);
            var strong = WeaponStopping.Of(Gun(9), HitZone.Body, R);
            Assert.AreEqual(1f, s.SpeedFactor(0), 1e-6f, "calm at first");
            s.Apply(weak, 0);
            Assert.AreEqual(weak.ShakeDeg, s.ShakeDeg(0.1), 1e-6f);
            s.Apply(strong, 0.2);
            Assert.AreEqual(strong.ShakeDeg, s.ShakeDeg(0.3), 1e-6f, "stronger replaces");
            Assert.AreEqual(1f - strong.Slow, s.SpeedFactor(0.3), 1e-6f);
            s.Apply(weak, 0.4);
            Assert.AreEqual(strong.ShakeDeg, s.ShakeDeg(0.45), 1e-6f, "weaker is ignored while the strong one runs");
            Assert.IsTrue(s.Active(0.69), "strong one started at 0.2, lasts to 0.7");
            Assert.IsFalse(s.Active(0.71), "and the ignored hit did not extend it");
            s.Apply(strong, 0.6); s.Apply(strong, 0.9);
            Assert.IsTrue(s.Active(1.3), "equal hits renew the time");
            Assert.AreEqual(0f, s.ShakeDeg(1.5), 1e-6f);
            Assert.AreEqual(1f, s.SpeedFactor(1.5), 1e-6f);
        }

        [Test]
        public void Shotgun_NinePelletsOnTheBody_DoNotStack()
        {
            var s = new StaggerState();
            var e = WeaponStopping.Of(Gun(9), HitZone.Body, R);
            for (int i = 0; i < 9; i++) s.Apply(e, 1.0);
            Assert.AreEqual(e.ShakeDeg, s.ShakeDeg(1.0), 1e-6f);
            Assert.AreEqual(1f - e.Slow, s.SpeedFactor(1.0), 1e-6f);
            Assert.Greater(s.SpeedFactor(1.0), 0.5f, "never close to rooted");
        }

        [Test]
        public void ShippedRoster_StoppingScalesWithTheHardestHitters()
        {
            var t = JsonUtility.FromJson<WeaponTable>(File.ReadAllText("Assets/_Project/Data/Resources/GameData/weapons.json"));
            float obelisk = WeaponStopping.Of(t.Find(15), HitZone.Body, R).ShakeDeg;
            float ordo = WeaponStopping.Of(t.Find(2), HitZone.Body, R).ShakeDeg;
            Assert.AreEqual(4f, obelisk, 1e-5f);
            Assert.Less(ordo, obelisk * 0.2f);
        }
    }
}
