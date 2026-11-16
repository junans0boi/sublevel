using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-240: the server's stagger registration and the victim-side mirror (guns_STAGGER_WIRING table).
    public class StaggerNetTests
    {
        static readonly StoppingRules R = new StoppingRules();
        static WeaponDef Gun(int stopping) => new WeaponDef { Id = 99, Name = "t", Stopping = stopping, Category = WeaponCategory.Primary };

        [Test]
        public void Stopping10_BodyHit_SlowsToSixtyFivePercent_ShakesFour_ThenEndsAfterHalfASecond()
        {
            var s = new StaggerState();
            Assert.IsTrue(StaggerHits.Register(s, Gun(10), HitZone.Body, 10.0, R));
            Assert.AreEqual(0.65f, s.SpeedFactor(10.1), 1e-5f);
            Assert.AreEqual(4f, s.ShakeDeg(10.1), 1e-5f);
            Assert.AreEqual(1f, s.SpeedFactor(10.6), 1e-6f);
            Assert.AreEqual(0f, s.ShakeDeg(10.6), 1e-6f);
        }

        [Test]
        public void Stopping1_AndMelee_RegisterNothing()
        {
            var s = new StaggerState();
            Assert.IsFalse(StaggerHits.Register(s, Gun(1), HitZone.Body, 0, R));
            Assert.IsFalse(StaggerHits.Register(s, new WeaponDef { Category = WeaponCategory.Melee, Stopping = 10 }, HitZone.Body, 0, R));
            Assert.IsFalse(s.Active(0.01));
        }

        [Test]
        public void ShotgunNinePelletsSameTick_IsOneEffect_NotNine()
        {
            var s = new StaggerState();
            for (int i = 0; i < 9; i++) Assert.IsTrue(StaggerHits.Register(s, Gun(7), HitZone.Body, 5.0, R));
            Assert.AreEqual(1f - 0.35f * WeaponStopping.Strength(7), s.SpeedFactor(5.1), 1e-5f);
        }

        [Test]
        public void WeakThenStrong_Replaces_StrongThenWeak_IsIgnored()
        {
            var s = new StaggerState();
            Assert.IsTrue(StaggerHits.Register(s, Gun(4), HitZone.Body, 0, R));
            Assert.IsTrue(StaggerHits.Register(s, Gun(9), HitZone.Body, 0.1, R));
            float strong = s.ShakeDeg(0.2);
            Assert.AreEqual(4f * WeaponStopping.Strength(9), strong, 1e-5f);
            Assert.IsFalse(StaggerHits.Register(s, Gun(4), HitZone.Body, 0.2, R), "weaker is ignored: nothing to send");
            Assert.AreEqual(strong, s.ShakeDeg(0.3), 1e-6f);
        }

        [Test]
        public void LimbHit_IsHalfOfBody()
        {
            var body = new StaggerState();
            var limb = new StaggerState();
            StaggerHits.Register(body, Gun(8), HitZone.Body, 0, R);
            StaggerHits.Register(limb, Gun(8), HitZone.Limb, 0, R);
            Assert.AreEqual(body.ShakeDeg(0.1) * 0.5f, limb.ShakeDeg(0.1), 1e-5f);
        }

        [Test]
        public void Clear_EndsItAtOnce()
        {
            var s = new StaggerState();
            StaggerHits.Register(s, Gun(10), HitZone.Body, 0, R);
            s.Clear();
            Assert.AreEqual(1f, s.SpeedFactor(0.1), 1e-6f);
        }

        [Test]
        public void Replica_FollowsTheServerValueWithin0_01_AndEndsOnItsOwnClock()
        {
            var server = new StaggerState();
            StaggerHits.Register(server, Gun(10), HitZone.Body, 100.0, R);
            var view = new StaggerReplica();
            view.Set(server.ShakeDeg(100.0), 1f - server.SpeedFactor(100.0), R.Seconds, 200.0); // client clock differs
            Assert.AreEqual(server.SpeedFactor(100.2), view.SpeedFactor(200.2), 0.01f);
            Assert.AreEqual(1f, view.SpeedFactor(200.5), 1e-6f);
            view.Set(4f, 0.35f, R.Seconds, 300.0);
            view.Clear();
            Assert.AreEqual(1f, view.SpeedFactor(300.1), 1e-6f);
            Assert.AreEqual(0f, view.ShakeDeg(300.1), 1e-6f);
        }
    }
}
