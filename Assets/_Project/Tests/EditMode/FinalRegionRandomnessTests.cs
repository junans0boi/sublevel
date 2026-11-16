using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // M4 gate (#77, SPEC 3/3-1) on the pollution (T10): on the real island (6 regions) the final region, the last
    // one of the confirmation order, is spread over every region. The order draws it uniformly, so each region is
    // final about 1/6 of the time; the bounds below leave more than four standard deviations on either side.
    public class FinalRegionRandomnessTests
    {
        const int Seeds = 300;
        const float MinShare = 0.08f;
        const float MaxShare = 0.27f;

        static WorldPlan _world;
        static int[][] _adj;
        static WorldPlan World => _world ??= WorldGenerator.Generate(WorldSpec.CreateIsland());
        static int[][] Adj => _adj ??= PollutionGridBuilder.Adjacency(PollutionGridBuilder.Build(new WorldPlanTerrain(World), 24f), World.Regions.Count);

        [Test]
        public void EveryRegion_IsFinalOftenEnough_AndTheOrderHasEachRegionOnce()
        {
            int regions = World.Regions.Count;
            Assert.AreEqual(6, regions, "the island has six regions");
            var finals = new int[regions];
            for (int seed = 0; seed < Seeds; seed++)
            {
                var order = PollutionOrder.Plan(regions, Adj, seed);
                finals[order[regions - 1]]++;
                CollectionAssert.AreEquivalent(Enumerable.Range(0, regions), order, "seed " + seed + ": every region confirmed exactly once");
            }
            AssertSpread(finals);
        }

        // The seeds a running match uses are random per match (MatchDirector.LockdownSeed): large seeds, negatives too.
        [Test]
        public void LargeSeeds_SpreadTheFinalRegionToo()
        {
            var finals = new int[World.Regions.Count];
            for (int i = 0; i < Seeds; i++)
            {
                int seed = unchecked(i * 0x6F4A7C15 + 0x2545F491);
                finals[PollutionOrder.Plan(finals.Length, Adj, seed)[finals.Length - 1]]++;
            }
            AssertSpread(finals);
        }

        // A dedicated server's --region forces the final region without changing the other seeds.
        [Test]
        public void ForcedFinal_IsLast_AndKeepsTheRestConnected()
        {
            int n = World.Regions.Count;
            for (int r = 0; r < n; r++)
            {
                var order = PollutionOrder.Plan(n, Adj, 11, r);
                Assert.AreEqual(r, order[n - 1]);
                var open = Enumerable.Repeat(true, n).ToArray();
                for (int k = 0; k < n - 1; k++) { open[order[k]] = false; Assert.IsTrue(PollutionOrder.IsConnected(n, Adj, open)); }
            }
            CollectionAssert.AreEqual(PollutionOrder.Plan(n, Adj, 5), PollutionOrder.Plan(n, Adj, 5, -1));
        }

        void AssertSpread(int[] finals)
        {
            for (int r = 0; r < finals.Length; r++)
            {
                float share = finals[r] / (float)Seeds;
                string all = string.Join(", ", finals.Select((n, i) => World.Regions[i].Id + " " + n));
                Assert.GreaterOrEqual(share, MinShare, $"region {World.Regions[r].Id} is final too rarely ({all})");
                Assert.LessOrEqual(share, MaxShare, $"region {World.Regions[r].Id} is final too often ({all})");
            }
        }
    }
}
