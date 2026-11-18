using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #162, ADR-141: the position history and how far a shot is rewound.
    public class LagCompTests
    {
        static PositionHistory Line(int capacity, int samples, double dt = 0.1)
        {
            // Moves 1 m per sample along x, from t = 0.
            var h = new PositionHistory(capacity);
            for (int i = 0; i < samples; i++) h.Record(i * dt, i, 0f, 0f);
            return h;
        }

        [Test]
        public void Empty_HasNoPosition()
        {
            Assert.IsFalse(new PositionHistory(8).TryAt(1.0, out _, out _, out _));
        }

        [Test]
        public void BetweenTwoSamples_IsInterpolated()
        {
            var h = Line(16, 5);
            Assert.IsTrue(h.TryAt(0.25, out float x, out _, out _));
            Assert.AreEqual(2.5f, x, 1e-4f);
        }

        [Test]
        public void OutsideTheRecorded_ClampsToTheEnds()
        {
            var h = Line(16, 5);
            h.TryAt(-3.0, out float before, out _, out _);
            h.TryAt(99.0, out float after, out _, out _);
            Assert.AreEqual(0f, before);
            Assert.AreEqual(4f, after);
        }

        [Test]
        public void TheRingForgetsTheOldest()
        {
            var h = Line(4, 10);
            Assert.AreEqual(4, h.Count);
            Assert.AreEqual(0.6, h.Oldest, 1e-9);
            h.TryAt(0.0, out float x, out _, out _);
            Assert.AreEqual(6f, x, "older than the ring: the oldest kept");
            h.TryAt(0.75, out x, out _, out _);
            Assert.AreEqual(7.5f, x, 1e-4f);
        }

        [Test]
        public void ASampleNotNewer_IsIgnored()
        {
            var h = Line(4, 3);
            h.Record(0.1, 99f, 0f, 0f);
            Assert.AreEqual(3, h.Count);
        }

        [Test]
        public void RewindIsTheTicksBetween_ButNeverNegativeOrOverTheCap()
        {
            double dt = 1.0 / 30.0;
            Assert.AreEqual(5 * dt, LagComp.RewindSeconds(100, 95, dt), 1e-5);
            Assert.AreEqual(0f, LagComp.RewindSeconds(100, 100, dt), "no delay");
            Assert.AreEqual(0f, LagComp.RewindSeconds(100, 130, dt), "claim from the future");
            Assert.AreEqual(LagComp.MaxRewindSeconds, LagComp.RewindSeconds(1000, 10, dt), "a lie gets the cap");
            Assert.AreEqual(0.1f, LagComp.RewindSeconds(1000, 10, dt, 0.1f));
            Assert.AreEqual(0f, LagComp.RewindSeconds(100, 50, 0.0));
        }

        [Test]
        public void TheCap_IsThirtyHundredths_AndTheDataDefaultAgrees()
        {
            Assert.AreEqual(0.30f, LagComp.MaxRewindSeconds);
            Assert.AreEqual(LagComp.MaxRewindSeconds, new BallisticsRules().MaxRewindSeconds, "the rules default is the cap");
        }

        [Test]
        public void ViewTick_IsBehindByTheInterpolation_PlusTheCopysExtraTick_AndStaysAtZero()
        {
            int before = LagComp.ViewDelayTicks;
            try
            {
                LagComp.ViewDelayTicks = LagComp.InterpolationTicks + LagComp.ReplicaExtraTicks;
                Assert.AreEqual(97u, LagComp.ViewTick(100));
                Assert.AreEqual(0u, LagComp.ViewTick(1));
                Assert.AreEqual(0u, LagComp.ViewTick(3), "exactly the delay: tick 0");
                LagComp.ViewDelayTicks = LagComp.InterpolationTicks; // the old model, what the measurement compares against
                Assert.AreEqual(98u, LagComp.ViewTick(100));
            }
            finally { LagComp.ViewDelayTicks = before; }
        }
    }
}
