using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class ServerFrameStatsTests
    {
        [Test]
        public void Percentiles_AreNearestRank()
        {
            var s = new ServerFrameStats();
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
            var s = new ServerFrameStats();
            Assert.AreEqual(0f, s.Percentile(99));
            s.Add(5f); s.Clear();
            Assert.AreEqual(0, s.Count);
        }

        [Test]
        public void ForcedStart_NeedsSomeone_UnlessMeasuring()
        {
            Assert.IsFalse(LobbyRules.MayForceStart(0, false), "production: an empty room never starts");
            Assert.IsTrue(LobbyRules.MayForceStart(1, false));
            Assert.IsTrue(LobbyRules.MayForceStart(0, true));
        }

        [Test]
        public void PerfGatherArg_IsRead_AndBadValueIsAnError()
        {
            var o = ServerArgs.Parse(new[] { "--perf", "p3", "--perf-gather", "120.5,-80" });
            Assert.AreEqual(120.5f, o.GatherX);
            Assert.AreEqual(-80f, o.GatherZ);
            Assert.IsNotNull(ServerArgs.Parse(new[] { "--perf-gather", "12" }).Error);
        }

        [Test]
        public void PerfArg_IsRead()
        {
            Assert.AreEqual("p0", ServerArgs.Parse(new[] { "--dedicated", "--perf", "p0" }).Perf);
            Assert.IsNull(ServerArgs.Parse(new[] { "--dedicated" }).Perf);
        }
    }
}
