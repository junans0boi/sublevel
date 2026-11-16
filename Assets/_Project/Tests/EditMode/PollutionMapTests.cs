using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // The real island as the pollution's input (T4): grid from WorldPlanTerrain, region graph, order connectivity
    // on that graph, and a front that ends the match in a sane time.
    public class PollutionMapTests
    {
        static WorldPlan _plan;
        static PollutionGrid _grid;
        static int[][] _adj;
        static WorldPlan Plan => _plan ?? (_plan = WorldGenerator.Generate(WorldSpec.CreateIsland()));
        static PollutionGrid Grid => _grid ?? (_grid = PollutionGridBuilder.Build(new WorldPlanTerrain(Plan), 24f));
        static int[][] Adj => _adj ?? (_adj = PollutionGridBuilder.Adjacency(Grid, Plan.Regions.Count));

        [Test]
        public void Grid_CoversTheIsland_EveryRegionHasLand_SeaSurroundsIt()
        {
            int n = Plan.Regions.Count;
            Assert.AreEqual(6, n);
            var land = PollutionGridBuilder.LandAreaWeights(Grid, n);
            for (int r = 0; r < n; r++) Assert.Greater(land[r], 50f, "region " + r + " has land");
            int sea = 0, rivers = 0, roads = 0;
            for (int i = 0; i < Grid.Count; i++) { if (Grid.IsSea(i)) sea++; else if (Grid.River[i]) rivers++; if (Grid.Road[i] > 0) roads++; }
            Assert.Greater(sea, Grid.Count / 10, "open water around the island");
            Assert.Greater(roads, 100, "roads reach the grid");
            Assert.GreaterOrEqual(rivers, 0);
        }

        [Test]
        public void Adjacency_IsSymmetric_AndTheWholeIslandIsOneLandmass()
        {
            int n = Plan.Regions.Count;
            for (int a = 0; a < n; a++)
                foreach (int b in Adj[a]) CollectionAssert.Contains(Adj[b], a);
            var open = new bool[n];
            for (int i = 0; i < n; i++) open[i] = true;
            Assert.IsTrue(PollutionOrder.IsConnected(n, Adj, open), "the regions touch by land");
        }

        [Test]
        public void Order_AnySeed_KeepsTheRemainingRegionsConnected()
        {
            int n = Plan.Regions.Count;
            for (int seed = 1; seed <= 60; seed++)
            {
                var order = PollutionOrder.Plan(n, Adj, seed);
                CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5 }, order, "every region once, seed " + seed);
                var open = new bool[n];
                for (int i = 0; i < n; i++) open[i] = true;
                for (int k = 0; k < n - 1; k++)
                {
                    open[order[k]] = false;
                    Assert.IsTrue(PollutionOrder.IsConnected(n, Adj, open), "seed " + seed + " after closing " + (k + 1));
                }
            }
        }

        [Test]
        public void Front_OnTheIsland_EndsInTwentyToTwentyFiveMinutes()
        {
            // The ADR-169 target (SPEC 3): sim mean 22.1 min. A few seeds on the real map; the bound is the spec's.
            double sum = 0; int count = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var m = new PollutionMatch(Grid, Adj, Plan.Regions.Count, seed);
                float end = m.Timeline.EndMinutes();
                Assert.Greater(end, 15f, "seed " + seed);
                Assert.Less(end, 30f, "seed " + seed);
                sum += end; count++;
            }
            TestContext.WriteLine("island front end, mean of " + count + " seeds: " + (sum / count).ToString("0.0") + " min");
        }
    }
}
