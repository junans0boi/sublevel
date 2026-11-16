using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class FrameStatsTests
    {
        [Test]
        public void Compute_SkipsWarmupFrames()
        {
            var times = new[] { 100f, 100f, 10f, 20f };
            var s = FrameStats.Compute(times, warmup: 2);
            Assert.AreEqual(2, s.Count);
            Assert.AreEqual(15f, s.AvgMs, 1e-4f);
            Assert.AreEqual(20f, s.MaxMs, 1e-4f);
        }

        [Test]
        public void Compute_P99AndFps()
        {
            var times = new float[100];
            for (int i = 0; i < 100; i++) times[i] = 10f;
            times[98] = 50f;
            times[99] = 50f;
            var s = FrameStats.Compute(times, 0);
            Assert.AreEqual(50f, s.P99Ms, 1e-4f);
            Assert.AreEqual(1000f / 10.8f, s.Fps, 0.01f);
        }

        [Test]
        public void Compute_EmptyAfterWarmup_ReturnsZeros()
        {
            var s = FrameStats.Compute(new[] { 5f }, warmup: 3);
            Assert.AreEqual(0, s.Count);
            Assert.AreEqual(0f, s.AvgMs);
        }
    }
}
