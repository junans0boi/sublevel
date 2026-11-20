using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #244 T3 / WP1: the numbers the world rules and the world builder used to hard-code are data in world.json.
    public class WorldRuleSpecTests
    {
        [Test]
        public void The_defaults_are_the_values_that_were_hard_coded()
        {
            var r = new WorldRuleSpec();
            Assert.AreEqual(3, r.MinElevators);
            Assert.AreEqual(4, r.MaxElevators);
            Assert.AreEqual(80f, r.MinElevatorSpacing);
            Assert.AreEqual(2, r.MinLotsNearElevator);
            Assert.AreEqual(60f, r.LotRange);
            Assert.AreEqual(35f, r.DoorstepRange);
            Assert.AreEqual(20f, r.MaxLotGroundSlopeDeg);
            Assert.AreEqual(40f, r.WalkSlopeDeg);
            Assert.AreEqual(0.5f, r.MinLandKm2);
            Assert.AreEqual(0.8f, r.MaxLandKm2);
            var b = new WorldBuildSpec();
            Assert.AreEqual(250, b.MaxLootSpawns);
            Assert.AreEqual(900, b.MaxRocks);
            Assert.AreEqual(80, b.MaxSingleLodRocks);
            Assert.AreEqual(700f, b.FarClip);
            Assert.AreEqual(140f, b.FogStart);
            Assert.AreEqual(640f, b.FogEnd);
        }

        [Test]
        public void The_plan_carries_the_rules_of_its_spec()
        {
            var spec = WorldSpec.CreateDefault();
            spec.Rules.MaxElevators = 6;
            spec.Build.MaxRocks = 1234;
            var plan = WorldGenerator.Generate(spec);
            Assert.AreEqual(6, plan.Rules.MaxElevators);
            Assert.AreEqual(1234, plan.Build.MaxRocks);
        }

        [Test]
        public void The_default_world_still_passes_every_rule()
        {
            Assert.IsEmpty(WorldRules.Validate(WorldGenerator.Generate(WorldSpec.CreateDefault())));
        }

        [Test]
        public void Validation_reads_the_limits_from_the_plan()
        {
            var plan = WorldGenerator.Generate(WorldSpec.CreateDefault());
            plan.Rules.MaxElevators = 2;
            Assert.IsTrue(WorldRules.Validate(plan).Any(v => v.Rule == WorldRule.ElevatorCount));

            plan = WorldGenerator.Generate(WorldSpec.CreateDefault());
            plan.Rules.MinLandKm2 = 3f; plan.Rules.MaxLandKm2 = 4f;
            Assert.IsTrue(WorldRules.Validate(plan).Any(v => v.Rule == WorldRule.LandArea));

            plan = WorldGenerator.Generate(WorldSpec.CreateDefault());
            plan.Rules.MinElevatorSpacing = 5000f;
            Assert.IsTrue(WorldRules.Validate(plan).Any(v => v.Rule == WorldRule.ElevatorSpacing));
        }

        [Test]
        public void A_plan_without_rules_is_checked_with_the_defaults()
        {
            var plan = WorldGenerator.Generate(WorldSpec.CreateDefault());
            plan.Rules = null;
            Assert.IsEmpty(WorldRules.Validate(plan));
        }

        [TestCase(1024f, 1f, 1024)]
        [TestCase(2400f, 1f, 2048)]
        [TestCase(2000f, 1f, 2048)]
        [TestCase(1024f, 2f, 512)]
        [TestCase(10f, 1f, 16)]
        [TestCase(100000f, 1f, 4096)]
        public void The_alphamap_is_the_power_of_two_nearest_to_size_over_metres_per_pixel(float size, float mpp, int expected)
        {
            var b = new WorldBuildSpec { AlphamapMetersPerPixel = mpp };
            Assert.AreEqual(expected, b.AlphamapResolution(size));
        }

        [TestCase(1024f, 1f, 1025)]
        [TestCase(2400f, 1f, 2049)]
        [TestCase(2400f, 2f, 1025)]
        [TestCase(10f, 1f, 33)]
        [TestCase(100000f, 1f, 4097)]
        public void The_heightmap_is_a_power_of_two_plus_one(float size, float mps, int expected)
        {
            var b = new WorldBuildSpec { HeightmapMetersPerSample = mps };
            Assert.AreEqual(expected, b.HeightmapResolution(size));
        }

        [Test]
        public void The_island_can_have_six_regions()
        {
            var spec = WorldSpec.CreateDefault();
            var extra = new[]
            {
                new RegionSpec { Id = "E", Name = "고원", Kind = "Mountain", CentreX = 820f, CentreZ = 800f, Radius = 250f, BaseHeight = 18f, Relief = 60f, Elevators = 3, Lots = 8, CompoundLots = 3, Hamlets = 0, HamletLots = 0, ConvergencePoints = 3, CompoundRadius = 40f, RocksPerHectare = 40f, TreesPerHectare = 30f },
                new RegionSpec { Id = "F", Name = "해안", Kind = "Town", CentreX = 200f, CentreZ = 800f, Radius = 220f, BaseHeight = 8f, Relief = 4f, Elevators = 3, Lots = 10, CompoundLots = 4, Hamlets = 1, HamletLots = 2, ConvergencePoints = 3, CompoundRadius = 45f, RocksPerHectare = 8f, TreesPerHectare = 20f },
            };
            spec.Regions = spec.Regions.Concat(extra).ToArray();
            var plan = WorldGenerator.Generate(spec);
            Assert.AreEqual(6, plan.Regions.Count);
            Assert.AreEqual(6, plan.Regions.Count(r => plan.Elevators.Any(e => e.Region == plan.Regions.IndexOf(r))));
        }
    }
}
