using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-313 (#377): the statistics row and the server arguments of a statistics run.
    public class BotStatsTests
    {
        [Test]
        public void A_row_is_a_header_and_a_value_line_in_the_order_added()
        {
            var row = new BotStatsCsv().Add("level", "hard").Add("seed", 3).Add("armed4min", 0.9125f).Add("gap", 0f);
            Assert.AreEqual("level,seed,armed4min,gap", row.Header);
            Assert.AreEqual("hard,3,0.913,0", row.Line);
        }

        [Test]
        public void Shares_and_percentiles_handle_empty_input()
        {
            Assert.AreEqual(0f, BotStatsCsv.Share(3, 0));
            Assert.AreEqual(0.5f, BotStatsCsv.Share(40, 80));
            Assert.AreEqual(0f, BotStatsCsv.Percentile(new List<float>(), 50f));
            var times = new List<float> { 10f, 30f, 20f, 300f };
            Assert.AreEqual(20f, BotStatsCsv.Percentile(times, 50f));
            Assert.AreEqual(300f, BotStatsCsv.Percentile(times, 90f));
            Assert.AreEqual(3, BotStatsCsv.CountAtMost(times, 240f));
        }

        [Test]
        public void The_statistics_arguments_are_parsed_and_checked()
        {
            var o = ServerArgs.Parse(new[] { "--dedicated", "--perf", "p0_hard_s2", "--perf-level", "hard", "--perf-seed", "2", "--perf-squad" });
            Assert.IsNull(o.Error);
            Assert.AreEqual(2, o.PerfLevel); Assert.AreEqual(2, o.PerfSeed); Assert.IsTrue(o.PerfSquad);
            var none = ServerArgs.Parse(new[] { "--dedicated" });
            Assert.IsFalse(none.HasPerfLevel); Assert.IsFalse(none.HasPerfSeed);
            Assert.IsNotNull(ServerArgs.Parse(new[] { "--perf-level", "brutal" }).Error);
            Assert.IsNotNull(ServerArgs.Parse(new[] { "--perf-seed", "x" }).Error);
        }

        [Test]
        public void A_pure_level_run_gives_every_bot_that_level()
        {
            try
            {
                BotMix.Pure = true;
                for (int i = 0; i < 40; i++) Assert.AreEqual(BotDifficulty.Hard, BotMix.For(BotDifficulty.Hard, 7, i));
            }
            finally { BotMix.Pure = false; }
            var seen = new HashSet<BotDifficulty>();
            for (int i = 0; i < 200; i++) seen.Add(BotMix.For(BotDifficulty.Normal, 7, i));
            Assert.AreEqual(3, seen.Count, "the usual mix has all three");
        }
    }
}
