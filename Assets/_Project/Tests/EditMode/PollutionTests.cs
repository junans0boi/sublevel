using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // The pollution core (ADR-169, S6): confirmation order, front flood, damage stages, gas cover. Pure C#.
    public class PollutionTests
    {
        // The island's region graph (docs/bible/data/map.json "지역 인접 관계"): R2 is a dead end behind R1.
        static readonly int[][] Adj = { new[] { 1, 3, 4 }, new[] { 0, 4, 2 }, new[] { 1 }, new[] { 0, 4, 5 }, new[] { 0, 3, 1, 5 }, new[] { 3, 4 } };

        // Orders pollution_sim.html `closeOrder` gives for the same seeds (computed in node from the simulator's code).
        static readonly Dictionary<int, int[]> SimOrders = new Dictionary<int, int[]>
        {
            { 1, new[] { 0, 5, 2, 1, 4, 3 } }, { 2, new[] { 2, 1, 3, 5, 0, 4 } }, { 3, new[] { 0, 3, 2, 5, 1, 4 } },
            { 4, new[] { 2, 0, 1, 3, 4, 5 } }, { 5, new[] { 5, 0, 3, 2, 1, 4 } }, { 6, new[] { 0, 5, 2, 1, 4, 3 } },
            { 7, new[] { 2, 5, 4, 3, 1, 0 } }, { 8, new[] { 4, 2, 5, 3, 1, 0 } }, { 9, new[] { 5, 0, 3, 4, 2, 1 } },
            { 10, new[] { 5, 4, 2, 1, 0, 3 } }, { 11, new[] { 4, 5, 2, 1, 0, 3 } }, { 12, new[] { 0, 3, 5, 2, 4, 1 } },
        };

        [Test]
        public void Order_MatchesTheSimulatorForTheSameSeed()
        {
            foreach (var kv in SimOrders)
                CollectionAssert.AreEqual(kv.Value, PollutionOrder.Plan(6, Adj, kv.Key), "seed " + kv.Key);
        }

        [Test]
        public void Order_IsAPermutation_AndTheOpenRegionsStayConnectedAfterEveryStep()
        {
            for (int seed = -50; seed < 1000; seed++)
            {
                var order = PollutionOrder.Plan(6, Adj, seed);
                CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5 }, order, "seed " + seed);
                var open = new[] { true, true, true, true, true, true };
                for (int k = 0; k < 5; k++)
                {
                    open[order[k]] = false;
                    Assert.IsTrue(PollutionOrder.IsConnected(6, Adj, open), "seed " + seed + " step " + k);
                }
            }
        }

        [Test]
        public void Order_DeadEndRegionCloses_BeforeItsOnlyNeighbour_UnlessItIsFinal()
        {
            for (int seed = 0; seed < 500; seed++)
            {
                var order = PollutionOrder.Plan(6, Adj, seed);
                int posDeadEnd = Array.IndexOf(order, 2), posHub = Array.IndexOf(order, 1);
                if (order[5] == 2) Assert.AreEqual(4, posHub, "R2 final: R1 can only be the last to close, seed " + seed);
                else Assert.Less(posDeadEnd, posHub, "seed " + seed);
            }
        }

        [Test]
        public void Order_EveryRegionIsFinalOftenEnough()
        {
            var finals = new int[6];
            const int seeds = 600;
            for (int seed = 0; seed < seeds; seed++) finals[PollutionOrder.Plan(6, Adj, seed)[5]]++;
            for (int r = 0; r < 6; r++)
            {
                float share = finals[r] / (float)seeds;
                Assert.That(share, Is.InRange(0.11f, 0.23f), "region " + r);
            }
        }

        [Test]
        public void Order_DisconnectedMapStillGetsAFullOrder()
        {
            var two = new[] { new int[0], new int[0], new int[0] };
            var order = PollutionOrder.Plan(3, two, 7);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, order);
        }

        [Test]
        public void Rules_ConfirmTimes_AreTheAgreedTable_AndGapsAreKept()
        {
            var t = new PollutionRules().ConfirmSeconds(6);
            CollectionAssert.AreEqual(new[] { 240f, 450f, 630f, 780f, 900f, 990f }, t);
            var tight = new PollutionRules { ConfirmMinutes = new[] { 5f, 5f, 5.1f } };
            var s = tight.ConfirmSeconds(3);
            Assert.AreEqual(5.5f * 60f, s[1], 1e-3f);
            Assert.AreEqual(6f * 60f, s[2], 1e-3f);
        }

        [Test]
        public void Rules_DamageStages_AreTheAgreedTable()
        {
            var r = new PollutionRules();
            Assert.AreEqual(0f, r.DamagePerSecond(0));
            CollectionAssert.AreEqual(new[] { 2f, 3.2f, 5f, 8f, 12.6f, 20f }, new[] { r.DamagePerSecond(1), r.DamagePerSecond(2), r.DamagePerSecond(3), r.DamagePerSecond(4), r.DamagePerSecond(5), r.DamagePerSecond(6) });
            Assert.AreEqual(20f, r.DamagePerSecond(9));
        }

        // ---- Front on a small flat map: sea ring, land block, two regions (left R0 closes first, right R1 is final). ----

        static PollutionGrid Block(int w, int h, int splitX)
        {
            var g = new PollutionGrid(w, h, 12f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    bool land = x >= 1 && y >= 1 && x < w - 1 && y < h - 1;
                    g.Region[i] = land ? (byte)(x < splitX ? 0 : 1) : PollutionGrid.Sea;
                }
            return g;
        }

        static PollutionTimeline Flood(PollutionGrid g, PollutionRules r = null) => new PollutionTimeline(g, new[] { 0, 1 }, r ?? new PollutionRules { ConfirmMinutes = new[] { 4f, 8f } });

        [Test]
        public void Front_StartsOnTheCoastAtConfirmation_AndNeverBeforeIt()
        {
            var g = Block(20, 11, 10);
            var tl = Flood(g);
            for (int i = 0; i < g.Count; i++)
            {
                if (g.IsSea(i)) { Assert.Greater(tl.ArriveMinutes[i], 1e8f); continue; }
                float confirm = tl.ConfirmAt(g.Region[i]) / 60f;
                Assert.GreaterOrEqual(tl.ArriveMinutes[i], confirm - 1e-4f, "cell " + i);
            }
            Assert.AreEqual(4f, tl.ArriveMinutes[1 * 20 + 2], 1e-4f, "coast cell of R0 arrives at R0's confirmation");
        }

        [Test]
        public void Front_OneCellInland_TakesCellOverSpeed()
        {
            var g = Block(20, 11, 10);
            var tl = Flood(g);
            // (3, 2) is one cell inside the top coast row (y = 1) of R0, which is the first closing (factor 1 on flat ground, 60 m/min).
            float expected = 4f + 12f / 60f;
            Assert.AreEqual(expected, tl.ArriveMinutes[2 * 20 + 3], 2e-3f);
        }

        [Test]
        public void Front_FinalRegionRunsSlower_ByTheFinalFactor()
        {
            var g = Block(20, 11, 10);
            var tl = Flood(g);
            float stage2 = 12f / (60f * 1.05f * 0.7f); // second closing: x1.05, final x0.7
            Assert.AreEqual(8f + stage2, tl.ArriveMinutes[2 * 20 + 15], 2e-3f);
        }

        [Test]
        public void Front_PavedRoadIsFasterThanOpenGround_UnpavedInBetween()
        {
            // Tall block so the road row is far from the north and south coasts (their fronts would win otherwise).
            float Along(byte road)
            {
                var g = Block(60, 41, 45);
                for (int x = 2; x < 44; x++) g.Road[20 * 60 + x] = road;
                var tl = Flood(g);
                return tl.ArriveMinutes[20 * 60 + 20] - tl.ArriveMinutes[20 * 60 + 10];
            }
            Assert.AreEqual(10 * 12f / 100f, Along(2), 0.06f);   // paved: 100 m/min
            Assert.AreEqual(10 * 12f / 80f, Along(1), 0.06f);    // unpaved: mean of 100 and 60
        }

        [Test]
        public void Front_SteepGroundAndRivers_SlowIt()
        {
            // One cell inland of R0's coast (3, 2), the front comes straight down from (3, 1).
            float Step(Action<PollutionGrid> setup)
            {
                var g = Block(20, 11, 10);
                setup(g);
                return Flood(g).ArriveMinutes[2 * 20 + 3] - 4f;
            }
            Assert.AreEqual(12f / 60f, Step(g => { }), 2e-3f);
            Assert.AreEqual(12f / 30f, Step(g => { for (int i = 0; i < g.Count; i++) g.SlopeDeg[i] = 40f; }), 2e-3f);
            Assert.AreEqual(12f / 45f, Step(g => { for (int i = 0; i < g.Count; i++) g.SlopeDeg[i] = 25f; }), 2e-3f, "25 degrees: halfway between 60 and 30");
            Assert.AreEqual(12f * 0.5f * (1f / 60f + 1f / 36f), Step(g => g.River[2 * 20 + 3] = true), 2e-3f, "a river cell moves at 0.6 x 60");
        }

        [Test]
        public void Front_DoesNotSpreadIntoARegionBeforeItIsConfirmed()
        {
            var g = Block(20, 11, 10);
            var tl = Flood(g);
            for (int i = 0; i < g.Count; i++)
                if (!g.IsSea(i) && g.Region[i] == 1) Assert.GreaterOrEqual(tl.ArriveMinutes[i] * 60f, 8f * 60f - 1e-2f);
        }

        [Test]
        public void Timeline_StatesAndWarning()
        {
            var tl = Flood(Block(20, 11, 10));
            Assert.AreEqual(PollutionState.Safe, tl.StateOf(0, 240 - 106));
            Assert.AreEqual(PollutionState.Warning, tl.StateOf(0, 240 - 104));
            Assert.AreEqual(PollutionState.Contaminated, tl.StateOf(0, 240));
            Assert.AreEqual(0, tl.Confirmed(239.9));
            Assert.AreEqual(1, tl.Confirmed(240));
            Assert.AreEqual(2, tl.Confirmed(480));
            Assert.AreEqual(1, tl.NextConfirmIndex(300));
            Assert.AreEqual(-1, tl.NextConfirmIndex(500));
            Assert.AreEqual(1, tl.FinalRegion);
        }

        [Test]
        public void Timeline_DamageFollowsTheFront_AndTheStageTable()
        {
            var g = Block(20, 11, 10);
            var tl = Flood(g);
            int coast = 1 * 20 + 2, inland = 5 * 20 + 5;
            Assert.AreEqual(0f, tl.DamagePerSecond(coast, 239f));
            Assert.AreEqual(2f, tl.DamagePerSecond(coast, 241f));
            Assert.AreEqual(0f, tl.DamagePerSecond(inland, 241f), "the front has not got there yet");
            double later = tl.ArriveMinutes[inland] * 60.0 + 1;
            Assert.AreEqual(later >= 480 ? 3.2f : 2f, tl.DamagePerSecond(inland, later));
            Assert.AreEqual(3.2f, tl.DamagePerSecond(coast, 500f), "stage rises everywhere the front has been");
        }

        [Test]
        public void Gas_CutsDamage_ButNotInTheFinalRegion_AndTheBetterCoverWins()
        {
            var r = new PollutionRules();
            var g = Block(20, 11, 10);
            var tl = Flood(g, new PollutionRules { ConfirmMinutes = new[] { 4f, 8f } });
            double t = 500;
            int inR0 = 1 * 20 + 2, inFinal = 5 * 20 + 18;
            var filter = GasCover.Filter(t, r);
            Assert.AreEqual(3.2f * 0.5f, tl.DamagePerSecond(inR0, t + 10, filter), 1e-4f);
            Assert.AreEqual(3.2f, tl.DamagePerSecond(inR0, t + 61, filter), 1e-4f, "cover over");
            double late = tl.ArriveMinutes[inFinal] * 60.0 + 1;
            var cover = GasCover.Filter(late, r);
            Assert.AreEqual(tl.DamagePerSecond(inFinal, late), tl.DamagePerSecond(inFinal, late + 1, cover) * 1f, 1e-4f, "no effect in the final region");
            var both = GasCover.Filter(t, r).With(GasCover.Antidote(t, r), t);
            Assert.AreEqual(r.AntidoteFactor, both.Factor);
            var after = both.With(GasCover.Filter(t + 20, r), t + 20);
            Assert.AreEqual(r.AntidoteFactor, after.Factor, "the antidote still runs");
            var expired = both.With(GasCover.Filter(t + 40, r), t + 40);
            Assert.AreEqual(r.FilterFactor, expired.Factor, "antidote over, filter wins");
        }

        [Test]
        public void EndMinutes_IsWhenTheSafeLandFallsToTheEndArea()
        {
            var tl = Flood(Block(20, 11, 10));
            float end = tl.EndMinutes(0.0f);
            Assert.Greater(end, 8f);
            Assert.AreEqual(0f, tl.SafeAreaKm2(end * 60f + 1f), 1e-6f);
            Assert.Greater(tl.SafeAreaKm2(100f), 0f);
        }

        [Test]
        public void Timeline_IsDeterministic()
        {
            var a = Flood(Block(30, 20, 14)); var b = Flood(Block(30, 20, 14));
            CollectionAssert.AreEqual(a.ArriveMinutes, b.ArriveMinutes);
        }
    }
}
