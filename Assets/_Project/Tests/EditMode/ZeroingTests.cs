using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-103: zero lists per scope, the breath, the sway.
    public class ZeroingTests
    {
        static readonly BallisticsRules Rules = new BallisticsRules();
        static OpticDef Scope(float zoom) => new OpticDef { Id = 99, Kind = OpticKind.Scope, Zoom = zoom };

        [TestCase(1f, 1)]
        [TestCase(2f, 4)]
        [TestCase(4f, 5)]
        [TestCase(8f, 8)]
        [TestCase(15f, 8)]
        public void DistanceList_PerScope(float zoom, int count)
        {
            Assert.AreEqual(count, Zeroing.Count(Scope(zoom), Rules));
            Assert.AreEqual(100f, Zeroing.Distance(Scope(zoom), Rules, 0));
        }

        [Test]
        public void Step_MovesInHundreds_AndStopsAtBothEnds()
        {
            var x4 = Scope(4f);
            Assert.AreEqual(200f, Zeroing.Step(x4, Rules, 100f, 1));
            Assert.AreEqual(500f, Zeroing.Step(x4, Rules, 500f, 1), "end of the list");
            Assert.AreEqual(100f, Zeroing.Step(x4, Rules, 100f, -1));
            Assert.AreEqual(300f, Zeroing.Step(x4, Rules, 400f, -1));
        }

        [Test]
        public void ZeroedAt300_TheBulletHitsTheLineOfSightAt300m()
        {
            var weapon = new WeaponDef { MuzzleSpeed = 880f };
            float speed = Ballistics.MuzzleSpeed(weapon, Rules);
            float deg = Ballistics.ElevationDeg(speed, Zeroing.Step(Scope(4f), Rules, 100f, 2), Rules.Gravity);
            float t = 300f / (speed * (float)System.Math.Cos(deg * System.Math.PI / 180.0));
            Assert.AreEqual(0f, Ballistics.HeightAt(speed, deg, t, Rules.Gravity), 0.02f);
        }

        [Test]
        public void Breath_DrainsToEmpty_ThenWaitsUntilRecovered()
        {
            var b = new BreathHold();
            float t = 0f;
            while (b.Holding || t == 0f) { b.Tick(0.1f, true, Rules); t += 0.1f; if (t > 10f) break; }
            Assert.AreEqual(Rules.BreathHoldSeconds, t, 0.2f, "lasts the hold duration");
            Assert.IsTrue(b.Winded);
            b.Tick(0.1f, true, Rules);
            Assert.IsFalse(b.Holding, "winded: holding is refused");
            float r = 0f;
            while (b.Winded && r < 10f) { b.Tick(0.1f, true, Rules); r += 0.1f; }
            Assert.AreEqual(Rules.BreathRestartFraction * Rules.BreathRecoverSeconds, r, 0.2f);
            b.Tick(0.1f, true, Rules);
            Assert.IsTrue(b.Holding, "can hold again");
            for (int i = 0; i < 60; i++) b.Tick(0.1f, false, Rules);
            Assert.AreEqual(1f, b.Fraction, 1e-4f);
        }

        [Test]
        public void Sway_NoneBelow3x_ShrinksHoldingBreath_GrowsWithZoom()
        {
            Assert.AreEqual(0f, ScopeSway.Amplitude(Rules, 2f, 1f, false, false));
            float x8 = ScopeSway.Amplitude(Rules, 8f, 1f, false, false);
            Assert.Greater(ScopeSway.Amplitude(Rules, 15f, 1f, false, false), x8);
            Assert.Less(ScopeSway.Amplitude(Rules, 8f, 1f, true, false), x8 * 0.2f);
            Assert.Less(ScopeSway.Amplitude(Rules, 8f, 0.6f, false, false), x8, "prone steadier");
            Assert.Greater(ScopeSway.Amplitude(Rules, 8f, 1f, false, true), x8, "gasping");
            ScopeSway.Offset(x8, 3.3f, out float yaw, out float pitch);
            Assert.LessOrEqual(System.Math.Abs(yaw), x8 + 1e-4f);
            Assert.LessOrEqual(System.Math.Abs(pitch), x8 + 1e-4f);
        }
    }
}
