using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-245: bot names, difficulty mix, and the reaction to a pollution warning.
    public class BotBehaviourTests
    {
        [Test]
        public void Names_AreUniqueInAMatch_AndStablePerSeed()
        {
            Assert.GreaterOrEqual(BotNames.Pool.Length, 60);
            var seen = new HashSet<string>();
            for (int i = 0; i < 49; i++) Assert.IsTrue(seen.Add(BotNames.For(1234, i)), "name " + i);
            Assert.AreEqual(BotNames.For(1234, 7), BotNames.For(1234, 7));
            Assert.AreNotEqual(BotNames.For(1, 0) + BotNames.For(1, 1), BotNames.For(2, 0) + BotNames.For(2, 1), "another seed shuffles");
            Assert.AreNotEqual(BotNames.For(9, 0), BotNames.For(9, BotNames.Pool.Length), "past the pool a number is added");
        }

        [TestCase(BotDifficulty.Easy, 50, 40, 10)]
        [TestCase(BotDifficulty.Normal, 20, 60, 20)]
        [TestCase(BotDifficulty.Hard, 10, 40, 50)]
        public void Mix_FollowsTheSettingsShares_OverManyBots(BotDifficulty setting, int e, int n, int h)
        {
            int[] count = new int[3];
            const int bots = 20000;
            for (int i = 0; i < bots; i++) count[(int)BotMix.For(setting, 77, i)]++;
            Assert.AreEqual(e / 100f, count[0] / (float)bots, 0.02f);
            Assert.AreEqual(n / 100f, count[1] / (float)bots, 0.02f);
            Assert.AreEqual(h / 100f, count[2] / (float)bots, 0.02f);
            Assert.AreEqual(BotMix.For(setting, 77, 5), BotMix.For(setting, 77, 5), "fixed per bot");
        }

        [Test]
        public void Warning_HardLeavesEarlierThanEasy_AndEveryoneLeavesInTheLastStretch()
        {
            // Warning lead 105 s (PollutionRules.WarningLeadSeconds). Share of 2000 bots already on the move 60 s into the warning
            // (45 s before the confirmation) and the share that never waited past the last 40 s.
            float Moving(BotDifficulty d, float since)
            {
                int go = 0;
                for (int s = 0; s < 2000; s++) if (BotPollutionPlan.ShouldLeave(PollutionState.Warning, since, 105f - since, false, d, s)) go++;
                return go / 2000f;
            }
            Assert.AreEqual(1f, Moving(BotDifficulty.Hard, 30f), 0.01f, "hard: delay at most 10 s");
            Assert.Greater(Moving(BotDifficulty.Easy, 30f), 0.5f);
            Assert.Less(Moving(BotDifficulty.Easy, 30f), 0.8f, "easy: part of them still wait at 30 s (30/45 = 0.67)");
            Assert.Less(Moving(BotDifficulty.Easy, 10f), Moving(BotDifficulty.Normal, 10f));
            Assert.Less(Moving(BotDifficulty.Normal, 10f), Moving(BotDifficulty.Hard, 10f));
            Assert.AreEqual(1f, Moving(BotDifficulty.Easy, 66f), 0.001f, "40 s before the confirmation nobody stays");
        }

        [Test]
        public void WalkOut_NeverUnderTheMinimum_GrowsWithTheWayOut()
        {
            Assert.AreEqual(BotPollutionPlan.MinimumWalkSeconds, BotPollutionPlan.WalkOutSeconds(0f, 4.7f), 1e-4f);
            Assert.AreEqual(400f * 1.4f / 4.7f + 15f, BotPollutionPlan.WalkOutSeconds(400f, 4.7f), 1e-3f);
            Assert.Greater(BotPollutionPlan.WalkOutSeconds(400f, 2f), BotPollutionPlan.WalkOutSeconds(400f, 4.7f), "slower walkers leave sooner");
        }

        [Test]
        public void MetersToBorder_FindsTheFirstStepOutside_OrTheWholeWay()
        {
            Assert.AreEqual(100f, BotPollutionPlan.MetersToBorder((x, z) => x < 95f, 0f, 0f, 300f, 0f), 1e-3f);
            Assert.AreEqual(300f, BotPollutionPlan.MetersToBorder((x, z) => true, 0f, 0f, 0f, 300f), 1e-3f);
            Assert.AreEqual(0f, BotPollutionPlan.MetersToBorder((x, z) => false, 5f, 5f, 5f, 5f), 1e-3f);
        }

        // #333 (ADR-533): gas rise and bombardment both start at the confirmation (ADR-484), so a bot is safe once over the
        // border by then. Every bot of every level, at any distance the warning lead allows, starts early enough to walk
        // the way out (straight line × PathFactor) before the confirmation, whatever its reaction delay.
        [Test]
        public void Warning_EveryBotStartsInTimeToBeOutBeforeTheConfirmation([Values(0f, 100f, 300f, 450f)] float meters)
        {
            const float lead = 105f, speed = 4.7f;
            float walk = BotPollutionPlan.WalkOutSeconds(meters, speed);
            foreach (BotDifficulty d in Enum.GetValues(typeof(BotDifficulty)))
                for (int seed = 0; seed < 500; seed++)
                {
                    float start = -1f;
                    for (float since = 0f; since <= lead; since += 0.5f)
                        if (BotPollutionPlan.ShouldLeave(PollutionState.Warning, since, lead - since, false, d, seed, walk)) { start = since; break; }
                    Assert.GreaterOrEqual(start, 0f, $"{d} bot {seed} never left");
                    float arrive = start + meters * BotPollutionPlan.PathFactor / speed;
                    // Beyond the warning lead nobody can make it; then it must at least go at once.
                    if (walk <= lead) Assert.LessOrEqual(arrive, lead, $"{d} bot {seed} at {meters} m is still in when the region ends");
                    else Assert.AreEqual(0f, start, $"{d} bot {seed}: too far to make it, must go at the warning");
                }
        }

        [Test]
        public void Safe_StaysPut_ContaminatedOrDoomed_Leaves()
        {
            Assert.IsFalse(BotPollutionPlan.ShouldLeave(PollutionState.Safe, 0f, 999f, false, BotDifficulty.Hard, 1));
            Assert.IsTrue(BotPollutionPlan.ShouldLeave(PollutionState.Contaminated, 0f, 0f, false, BotDifficulty.Easy, 1));
            Assert.IsTrue(BotPollutionPlan.ShouldLeave(PollutionState.Safe, 0f, 999f, true, BotDifficulty.Easy, 1));
        }

        [Test]
        public void KnownFinal_NeedsTwoThirdsOfTheRegionsWarnedOrLost()
        {
            Assert.AreEqual(4, BotPollutionPlan.KnownFinalThreshold(6));
            Assert.IsFalse(BotPollutionPlan.KnowsFinal(3, 6));
            Assert.IsTrue(BotPollutionPlan.KnowsFinal(4, 6));
            Assert.AreEqual(2, BotPollutionPlan.KnownFinalThreshold(3), "slice with fewer regions");
        }

        [TestCase(BotDifficulty.Easy, 0.5f)]
        [TestCase(BotDifficulty.Normal, 0.8f)]
        [TestCase(BotDifficulty.Hard, 1f)]
        public void KeyUse_ShareFollowsTheDifficultyChance(BotDifficulty level, float chance)
        {
            int use = 0;
            const int bots = 20000;
            for (int s = 0; s < bots; s++) if (BotPollutionPlan.WantsKey(level, s)) use++;
            Assert.AreEqual(chance, use / (float)bots, 0.02f);
            Assert.AreEqual(BotPollutionPlan.WantsKey(level, 5), BotPollutionPlan.WantsKey(level, 5), "fixed per bot");
        }
    }
}
