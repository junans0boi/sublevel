using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // M4 gate (#77, ROADMAP "final-region randomness test", SPEC 3/3-1): on the real map (the generated World plan,
    // 4 regions joined by passes), the final region is spread over every region and never in the closing order.
    // The generator draws it uniformly, so each region should be final about 25 % of the time; over 200 seeds the
    // bounds below leave more than three standard deviations (about 6 %) on either side.
    public class FinalRegionRandomnessTests
    {
        const int Seeds = 200;
        const float MinShare = 0.15f;
        const float MaxShare = 0.35f;

        static WorldPlan _world;

        static WorldPlan World => _world ??= WorldGenerator.Generate(WorldSpec.CreateDefault());

        [Test]
        public void EveryRegion_IsFinalOftenEnough_AndNeverInTheClosingOrder()
        {
            var world = World;
            int regions = world.Regions.Count;
            Assert.AreEqual(4, regions, "the M4 map has four regions");
            var finals = new int[regions];
            for (int seed = 0; seed < Seeds; seed++)
            {
                var plan = Lockdown.Plan(world, seed);
                Assert.IsTrue(plan.FinalRegion >= 0 && plan.FinalRegion < regions, "seed " + seed);
                finals[plan.FinalRegion]++;
                CollectionAssert.DoesNotContain(plan.Order, plan.FinalRegion, "seed " + seed + ": the final region is in the closing order");
                CollectionAssert.AreEquivalent(Enumerable.Range(0, regions).Where(r => r != plan.FinalRegion), plan.Order,
                    "seed " + seed + ": every other region closes exactly once");
                Assert.AreEqual(ZoneState.Safe, Lockdown.StateAt(plan, plan.FinalRegion, 1e6f), "seed " + seed + ": the final region never closes");
            }
            for (int r = 0; r < regions; r++)
            {
                float share = finals[r] / (float)Seeds;
                string all = string.Join(", ", finals.Select((n, i) => world.Regions[i].Id + " " + n));
                Assert.GreaterOrEqual(share, MinShare, $"region {world.Regions[r].Id} is final too rarely ({all})");
                Assert.LessOrEqual(share, MaxShare, $"region {world.Regions[r].Id} is final too often ({all})");
            }
        }

        // The seeds a running match uses are random per match (MatchDirector.LockdownSeed); the same check over a
        // spread of large seeds like those.
        [Test]
        public void LargeSeeds_SpreadTheFinalRegionToo()
        {
            var world = World;
            var finals = new int[world.Regions.Count];
            for (int i = 0; i < Seeds; i++)
            {
                // Spread over the whole int range, negatives included.
                int seed = unchecked(i * 0x6F4A7C15 + 0x2545F491);
                finals[Lockdown.Plan(world, seed).FinalRegion]++;
            }
            for (int r = 0; r < finals.Length; r++)
            {
                float share = finals[r] / (float)Seeds;
                Assert.GreaterOrEqual(share, MinShare, "region " + world.Regions[r].Id + ": " + string.Join(", ", finals));
                Assert.LessOrEqual(share, MaxShare, "region " + world.Regions[r].Id + ": " + string.Join(", ", finals));
            }
        }
    }
}
