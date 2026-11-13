using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-112: the swim and breath rules.
    public class SwimTests
    {
        static readonly SwimRules Rules = new SwimRules();

        [Test]
        public void Swimming_StartsAt1_2m_StopsBelow1_0m()
        {
            Assert.IsFalse(Swim.Swimming(false, 1.19f, Rules));
            Assert.IsTrue(Swim.Swimming(false, 1.2f, Rules));
            Assert.IsTrue(Swim.Swimming(true, 1.05f, Rules), "no flicker at the edge");
            Assert.IsFalse(Swim.Swimming(true, 0.99f, Rules));
            Assert.AreEqual(2f, Swim.Depth(3f, 1f));
            Assert.AreEqual(0f, Swim.Depth(1f, 3f));
        }

        [Test]
        public void SwimSpeed_IsPubgsRatio_OfTheSprint()
        {
            Assert.AreEqual(2.9f, Swim.Speed(6.3f, Rules), 0.01f);
        }

        [Test]
        public void Floating_EyesJustAboveTheSurface_Diving_UnderIt_NeverInTheFloor()
        {
            // Lying eyes are 0.4 m over the feet.
            Assert.AreEqual(5f + 0.1f - 0.4f, Swim.TargetFeetY(5f, 0f, 0.4f, false, Rules), 1e-4f);
            Assert.AreEqual(5f - 0.9f - 0.4f, Swim.TargetFeetY(5f, 0f, 0.4f, true, Rules), 1e-4f);
            Assert.AreEqual(0.05f, Swim.TargetFeetY(1.0f, 0f, 0.4f, true, Rules), 1e-4f, "shallow pool: held off the floor");
            Assert.IsTrue(Swim.HeadUnder(4.0f, 5f, Rules));
            Assert.IsFalse(Swim.HeadUnder(5.1f, 5f, Rules));
        }

        [Test]
        public void Breath_Lasts20s_ThenDrownsPerSecond_AndRecoversIn9s()
        {
            var b = new SwimBreath();
            float t = 0f, damage = 0f;
            while (b.Fraction > 0f && t < 40f) { damage += b.Tick(0.1f, true, Rules); t += 0.1f; }
            Assert.AreEqual(20f, t, 0.2f);
            Assert.AreEqual(0f, damage, "no damage while the breath lasts");
            float drown = 0f;
            for (int i = 0; i < 20; i++) drown += b.Tick(0.1f, true, Rules);
            Assert.AreEqual(Rules.DrownDamagePerSecond * 2f, drown, 0.01f, "2 s of drowning");
            Assert.IsTrue(b.Drowning);
            float up = 0f;
            while (b.Fraction < 1f && up < 30f) { b.Tick(0.1f, false, Rules); up += 0.1f; }
            Assert.AreEqual(9f, up, 0.2f, "full again after 9 s with the head up");
            Assert.AreEqual(0f, b.Tick(0.1f, false, Rules));
        }

        [Test]
        public void ADrowningHead_BreathingAgainStopsTheDamage()
        {
            var b = new SwimBreath();
            for (int i = 0; i < 250; i++) b.Tick(0.1f, true, Rules);
            Assert.Greater(b.Tick(0.1f, true, Rules), 0f);
            Assert.AreEqual(0f, b.Tick(0.1f, false, Rules));
            Assert.Greater(b.Fraction, 0f);
        }
    }
}
