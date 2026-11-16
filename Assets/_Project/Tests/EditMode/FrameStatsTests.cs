using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class FrameStatsTests
    {
        [Test]
        public void Percentiles_AreNearestRank()
        {
            var s = new FrameStats();
            for (int i = 1; i <= 100; i++) s.Add(i);
            Assert.AreEqual(50f, s.Percentile(50));
            Assert.AreEqual(95f, s.Percentile(95));
            Assert.AreEqual(99f, s.Percentile(99));
            Assert.AreEqual(100f, s.Max());
            Assert.AreEqual(50.5f, s.Mean(), 1e-4f);
        }

        [Test]
        public void Empty_IsZero_AndClearResets()
        {
            var s = new FrameStats();
            Assert.AreEqual(0f, s.Percentile(99));
            s.Add(5f); s.Clear();
            Assert.AreEqual(0, s.Count);
        }

        [Test]
        public void PerfArg_IsRead()
        {
            Assert.AreEqual("p0", ServerArgs.Parse(new[] { "--dedicated", "--perf", "p0" }).Perf);
            Assert.IsNull(ServerArgs.Parse(new[] { "--dedicated" }).Perf);
        }
    }
}
