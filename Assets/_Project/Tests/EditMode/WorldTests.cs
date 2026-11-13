using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // Open-world island (W1-1, ADR-052): the shipped spec must generate a plan that satisfies the world rules,
    // the same one every time, and the rules must catch breakage.
    public class WorldTests
    {
        static WorldPlan Fresh() => WorldGenerator.Generate(WorldSpec.CreateDefault());

        static bool Violates(WorldPlan plan, WorldRule rule) => WorldRules.Validate(plan).Any(v => v.Rule == rule);

        static float Spread(int i, int salt) => 100f + (i * salt % 83) * 10f; // 100..920, inside the map

        // ---- the shipped world ----

        [Test]
        public void ShippedWorld_SatisfiesEveryRule()
        {
            var violations = WorldRules.Validate(Fresh());
            Assert.IsEmpty(violations, string.Join("\n", violations.Select(v => v.ToString())));
        }

        [Test]
        public void ShippedSpec_MatchesWorldJson()
        {
            string path = Path.Combine(UnityEngine.Application.dataPath, "_Project/Data/Resources/GameData/world.json");
            var fromFile = UnityEngine.JsonUtility.FromJson<WorldSpec>(File.ReadAllText(path));
            Assert.AreEqual(UnityEngine.JsonUtility.ToJson(WorldSpec.CreateDefault(), true), UnityEngine.JsonUtility.ToJson(fromFile, true));
        }

        [Test]
        public void ShippedWorld_HasFourNamedRegionsJoinedByPasses()
        {
            var plan = Fresh();
            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D" }, plan.Regions.Select(r => r.Id));
            CollectionAssert.AreEqual(new[] { "산악", "습지", "마을", "공장" }, plan.Regions.Select(r => r.Name));
            CollectionAssert.AreEqual(new[] { RegionKind.Mountain, RegionKind.Wetland, RegionKind.Town, RegionKind.Factory }, plan.Regions.Select(r => r.Kind));
            // Mountain touches everything, town sits between wetland and factory; wetland and factory never meet.
            var borders = plan.Passes.Select(p => plan.Regions[p.RegionA].Id + plan.Regions[p.RegionB].Id).ToList();
            CollectionAssert.AreEquivalent(new[] { "AB", "AC", "AD", "BC", "CD" }, borders.Distinct());
            foreach (var border in borders.Distinct())
                Assert.AreEqual(2, borders.Count(b => b == border), "passes on border " + border);
            foreach (var region in plan.Regions)
                Assert.AreEqual(region.Index, plan.RegionAt(region.HubX, region.HubZ), "hub of " + region.Id);
            Assert.AreEqual(-1, plan.RegionAt(5f, 5f));
        }

        [Test]
        public void ShippedWorld_HasTheTerrainEachRegionPromises()
        {
            var plan = Fresh();
            float peak = 0f, deepestPool = 0f;
            for (float z = 0f; z <= plan.Size; z += 8f)
                for (float x = 0f; x <= plan.Size; x += 8f)
                {
                    float h = plan.Height(x, z);
                    Assert.That(h, Is.InRange(0f, plan.MaxHeight));
                    int region = plan.RegionAt(x, z);
                    if (region < 0) continue;
                    if (plan.Regions[region].Kind == RegionKind.Mountain) peak = Math.Max(peak, h);
                    if (plan.Regions[region].Kind == RegionKind.Wetland && plan.BorderDistance(x, z) > 30f)
                        deepestPool = Math.Max(deepestPool, plan.SeaLevel - h);
                }
            Assert.Greater(peak, plan.SeaLevel + 50f, "the mountain has real relief");
            Assert.Greater(deepestPool, 0.05f, "the wetland has standing water");
            Assert.LessOrEqual(deepestPool, WorldRules.WadeDepth, "wetland pools can be waded");
            Assert.Less(plan.Height(2f, 2f), plan.SeaLevel - WorldRules.WadeDepth, "the map edge is open sea");
        }

        [Test]
        public void ShippedWorld_ScattersCoverAndTrees()
        {
            var plan = Fresh();
            int mountain = plan.Regions.First(r => r.Kind == RegionKind.Mountain).Index;
            int town = plan.Regions.First(r => r.Kind == RegionKind.Town).Index;
            Assert.Greater(plan.Rocks.Count(r => r.Region == mountain), plan.Rocks.Count(r => r.Region == town), "rocks are densest in the mountain");
            Assert.Greater(plan.Trees.Count, 300);
            Assert.Less(plan.Rocks.Count + plan.Trees.Count, 8000, "scatter stays inside the draw budget");
            foreach (var tree in plan.Trees)
            {
                Assert.GreaterOrEqual(tree.Y, plan.SeaLevel, "no tree in the water");
                Assert.Greater(plan.RoadDistance(tree.X, tree.Z), 0f, "no tree on a road");
                Assert.IsFalse(plan.Lots.Any(l => l.Distance(tree.X, tree.Z) <= 0f), "no tree inside a building");
            }
            Assert.IsTrue(plan.LootPoints.All(p => p.Lot < 0), "indoor loot comes from the building plans, not the world plan");
            foreach (var region in plan.Regions)
                Assert.That(plan.SubZones.Count(s => s.Region == region.Index), Is.InRange(3, 4), "sub-zones of " + region.Id);
            foreach (var e in plan.Elevators)
                Assert.AreEqual(e.SubZone, plan.SubZoneAt(e.X, e.Z), e.Name);
        }

        [Test]
        public void Splat_WeightsSumToOne_AndRoadsAreDirt()
        {
            var plan = Fresh();
            for (int i = 0; i < 60; i++)
            {
                plan.Splat(Spread(i, 37), Spread(i, 59), out float grass, out float dirt, out float rock, out float sand);
                Assert.AreEqual(1f, grass + dirt + rock + sand, 1e-4f);
                Assert.That(Math.Min(Math.Min(grass, dirt), Math.Min(rock, sand)), Is.GreaterThanOrEqualTo(0f));
            }
            var road = plan.Roads.First(r => r.Kind == RoadKind.Trunk);
            int mid = road.X.Length / 4; // between a hub and its pass
            plan.Splat(road.X[mid], road.Z[mid], out _, out float onRoad, out _, out _);
            Assert.Greater(onRoad, 0.9f);
        }

        [Test]
        public void ShippedWorld_HasEnoughBuildingsOfTheRightKinds()
        {
            var plan = Fresh();
            int Count(RegionKind region, params BuildingKind[] kinds) =>
                plan.Lots.Count(l => plan.Regions[l.Region].Kind == region && (kinds.Length == 0 || kinds.Contains(l.Kind)));
            Assert.GreaterOrEqual(Count(RegionKind.Town), 30);
            Assert.GreaterOrEqual(Count(RegionKind.Factory), 14);
            Assert.GreaterOrEqual(Count(RegionKind.Mountain), 8);
            Assert.GreaterOrEqual(Count(RegionKind.Wetland), 8);
            Assert.Greater(Count(RegionKind.Town, BuildingKind.House), Count(RegionKind.Town) / 2, "the town is mostly houses");
            Assert.Greater(Count(RegionKind.Factory, BuildingKind.FactoryHall), 0);
            Assert.Greater(Count(RegionKind.Factory, BuildingKind.Warehouse), 0);
            Assert.AreEqual(0, Count(RegionKind.Mountain, BuildingKind.FactoryHall, BuildingKind.Warehouse));
            foreach (var lot in plan.Lots)
            {
                Buildings.FootprintLimits(lot.Kind, out float minX, out float minZ, out float maxX, out float maxZ);
                Assert.That(lot.SizeX, Is.InRange(minX, maxX), lot.Name);
                Assert.That(lot.SizeZ, Is.InRange(minZ, maxZ), lot.Name);
                // The front looks onto a road: walking straight out of the front door you reach one within 45 m.
                bool road = false;
                for (float step = 1f; step <= 45f && !road; step += 1f)
                {
                    lot.ToWorld(0f, lot.SizeZ * 0.5f + step, out float x, out float z);
                    road = plan.RoadDistance(x, z) <= 1f;
                }
                Assert.IsTrue(road, lot.Name + " does not face a road");
            }
            Buildings.FootprintLimits(BuildingKind.ElevatorStation, out float minW, out float minD, out float maxW, out float maxD);
            Assert.That(WorldElevator.StationWidth, Is.InRange(minW, maxW));
            Assert.That(WorldElevator.StationDepth, Is.InRange(minD, maxD));
        }

        [Test]
        public void RoadNetwork_IsSparse()
        {
            var plan = Fresh();
            float metres = 0f;
            foreach (var road in plan.Roads.Where(r => r.Kind != RoadKind.Path))
                for (int i = 1; i < road.X.Length; i++)
                    metres += (float)Math.Sqrt(Math.Pow(road.X[i] - road.X[i - 1], 2) + Math.Pow(road.Z[i] - road.Z[i - 1], 2));
            Assert.Less(metres, 4500f, "one road per pass, joined into a tree: the first version had 5.4 km");
            Assert.AreEqual(plan.Passes.Count, plan.Roads.Count(r => r.Pass >= 0), "one road through each pass");
        }

        // ---- determinism ----

        [Test]
        public void World_IsTheSameEveryTime()
        {
            var a = Fresh();
            var b = Fresh();
            for (int i = 0; i < 50; i++)
                Assert.AreEqual(a.Height(Spread(i, 37), Spread(i, 59)), b.Height(Spread(i, 37), Spread(i, 59)));
            Assert.AreEqual(a.Elevators.Count, b.Elevators.Count);
            for (int i = 0; i < a.Elevators.Count; i++)
            {
                Assert.AreEqual(a.Elevators[i].X, b.Elevators[i].X);
                Assert.AreEqual(a.Elevators[i].Z, b.Elevators[i].Z);
                Assert.AreEqual(a.Elevators[i].FacingDeg, b.Elevators[i].FacingDeg);
            }
            string Places(WorldPlan p) => string.Join("|",
                p.Lots.Select(l => $"{l.Name} {l.X} {l.Z} {l.SizeX} {l.SizeZ} {l.YawDeg}")
                    .Concat(p.Passes.Select(x => $"{x.Name} {x.X} {x.Y} {x.Z}"))
                    .Concat(p.Roads.Select(r => $"{r.Name} {r.X.Length} {r.Y.Sum()}"))
                    .Concat(new[] { $"{p.Rocks.Count} {p.Trees.Count}" }));
            Assert.AreEqual(Places(a), Places(b));
        }

        [Test]
        public void DifferentSeed_GivesADifferentWorld()
        {
            var a = Fresh();
            var spec = WorldSpec.CreateDefault();
            spec.Seed += 1;
            var b = WorldGenerator.Generate(spec);
            int changed = 0;
            for (int i = 0; i < 50; i++)
                if (a.Height(Spread(i, 37), Spread(i, 59)) != b.Height(Spread(i, 37), Spread(i, 59))) changed++;
            Assert.Greater(changed, 10);
            string Elevators(WorldPlan p) => string.Join("|", p.Elevators.Select(e => $"{e.X} {e.Z}"));
            Assert.AreNotEqual(Elevators(a), Elevators(b));
        }

        // ---- ground quality ----

        [Test]
        public void Height_IsContinuous()
        {
            var plan = Fresh();
            // Anywhere, cliffs included: no tears (ridge flanks and mountain faces stay well under 10 m per metre).
            for (float z = 3f; z < plan.Size; z += 7f)
                for (float x = 3f; x < plan.Size - 1f; x += 7f)
                    Assert.LessOrEqual(Math.Abs(plan.Height(x + 1f, z) - plan.Height(x, z)), 10f, $"step at ({x}, {z})");
            // On the plains around the town and factory hubs the ground is gentle.
            foreach (var region in plan.Regions.Where(r => r.Kind == RegionKind.Town || r.Kind == RegionKind.Factory))
                for (float dz = -60f; dz <= 60f; dz += 5f)
                    for (float dx = -60f; dx < 60f; dx += 1f)
                    {
                        float x = region.HubX + dx, z = region.HubZ + dz;
                        Assert.LessOrEqual(Math.Abs(plan.Height(x + 1f, z) - plan.Height(x, z)), 0.75f, $"step at ({x}, {z}) in {region.Id}");
                    }
        }

        [Test]
        public void FillHeights_MatchesHeight()
        {
            var plan = Fresh();
            var grid = new float[65, 65];
            plan.FillHeights(grid, 1f / plan.MaxHeight);
            Assert.AreEqual(plan.Height(16f * 10, 16f * 40) / plan.MaxHeight, grid[40, 10], 1e-6f); // [z, x]
            Assert.AreEqual(plan.Height(plan.Size, 0f) / plan.MaxHeight, grid[0, 64], 1e-6f);
        }

        [Test]
        public void GenerateAndValidate_FinishInTime()
        {
            Fresh(); // not the JIT's first pass
            var watch = Stopwatch.StartNew();
            WorldRules.Validate(Fresh());
            watch.Stop();
            Assert.Less(watch.ElapsedMilliseconds, 3000);
        }

        // ---- every rule catches its breakage ----

        [Test]
        public void TooFewElevators_IsAViolation()
        {
            var plan = Fresh();
            int keep = plan.Elevators.First(e => e.Region == 0).SubZone;
            plan.Elevators.RemoveAll(e => e.Region == 0 && e.SubZone != keep);
            Assert.IsTrue(Violates(plan, WorldRule.ElevatorCount));
        }

        [Test]
        public void MissingLots_AreAViolation()
        {
            var plan = Fresh();
            plan.Lots.RemoveAll(l => l.Region == 1);
            var rules = WorldRules.Validate(plan).Select(v => v.Rule).ToList();
            CollectionAssert.Contains(rules, WorldRule.LotCount);
            CollectionAssert.Contains(rules, WorldRule.ElevatorNearLots);
        }

        [Test]
        public void ElevatorWithNoBuildingAtTheDoor_IsAViolation()
        {
            var plan = Fresh();
            var e = plan.Elevators[0];
            plan.Lots.RemoveAll(l => l.Distance(e.X, e.Z) <= WorldRules.DoorstepRange);
            Assert.IsTrue(Violates(plan, WorldRule.ElevatorDoorstepLot));
        }

        [Test]
        public void LotTheBuildingGeneratorCannotFill_IsAViolation()
        {
            var plan = Fresh();
            plan.Lots[0].Kind = BuildingKind.FactoryHall;
            plan.Lots[0].SizeX = 5f;
            Assert.IsTrue(Violates(plan, WorldRule.LotFootprint));
        }

        [Test]
        public void MissingConvergencePoint_IsAViolation()
        {
            var plan = Fresh();
            plan.ConvergencePoints.RemoveAt(0);
            Assert.IsTrue(Violates(plan, WorldRule.ConvergenceCount));
        }

        [Test]
        public void ElevatorsTooClose_IsAViolation()
        {
            var plan = Fresh();
            plan.Elevators[1].X = plan.Elevators[0].X + 12f;
            plan.Elevators[1].Z = plan.Elevators[0].Z;
            Assert.IsTrue(Violates(plan, WorldRule.ElevatorSpacing));
        }

        [Test]
        public void ElevatorInTheSea_IsAViolation()
        {
            var plan = Fresh();
            plan.Elevators[0].X = 6f;
            plan.Elevators[0].Z = 6f;
            var rules = WorldRules.Validate(plan).Select(v => v.Rule).ToList();
            CollectionAssert.Contains(rules, WorldRule.AboveSea);
            CollectionAssert.Contains(rules, WorldRule.ElevatorUnreachable);
            CollectionAssert.Contains(rules, WorldRule.ElevatorOffRoad);
        }

        [Test]
        public void LotOnASlope_IsAViolation()
        {
            var plan = Fresh();
            // A ridge flank: dry ground that rises more than 2.5 m in 3 m.
            float steepX = -1f, steepZ = -1f;
            for (float z = 100f; z < plan.Size - 100f && steepX < 0f; z += 6f)
                for (float x = 100f; x < plan.Size - 100f && steepX < 0f; x += 6f)
                    if (plan.Height(x, z) > plan.SeaLevel + 3f && Math.Abs(plan.Height(x + 3f, z) - plan.Height(x, z)) > 2.5f)
                    {
                        steepX = x;
                        steepZ = z;
                    }
            Assert.GreaterOrEqual(steepX, 0f, "the island has a cliff somewhere");
            plan.Lots[0].X = steepX + 1.5f;
            plan.Lots[0].Z = steepZ;
            plan.Lots[0].YawDeg = 0f;
            Assert.IsTrue(Violates(plan, WorldRule.FlatPad));
        }

        [Test]
        public void LotCutIntoASteepSlope_IsAViolation()
        {
            var plan = Fresh();
            plan.Lots[0].GroundSlopeDeg = WorldRules.MaxLotGroundSlopeDeg + 5f;
            Assert.IsTrue(Violates(plan, WorldRule.LotGroundSlope));
        }

        [Test]
        public void OverlappingLots_AreAViolation()
        {
            var plan = Fresh();
            plan.Lots[1].X = plan.Lots[0].X + 1f;
            plan.Lots[1].Z = plan.Lots[0].Z;
            Assert.IsTrue(Violates(plan, WorldRule.LotOverlap));
        }

        [Test]
        public void LotOnARoad_IsAViolation()
        {
            var plan = Fresh();
            var road = plan.Roads.First(r => r.Kind == RoadKind.Trunk);
            plan.Lots[0].X = road.X[road.X.Length / 4];
            plan.Lots[0].Z = road.Z[road.X.Length / 4];
            Assert.IsTrue(Violates(plan, WorldRule.LotOnRoad));
        }

        [Test]
        public void CrampedConvergencePoint_IsAViolation()
        {
            var plan = Fresh();
            plan.ConvergencePoints[0].ClearRadius = 3f;
            Assert.IsTrue(Violates(plan, WorldRule.ConvergenceClear));
        }

        [Test]
        public void BuildingInsideAConvergencePoint_IsAViolation()
        {
            var plan = Fresh();
            plan.Lots[0].X = plan.ConvergencePoints[0].X + 2f;
            plan.Lots[0].Z = plan.ConvergencePoints[0].Z;
            Assert.IsTrue(Violates(plan, WorldRule.ConvergenceClear));
        }

        [Test]
        public void SteepRoad_IsAViolation()
        {
            var plan = Fresh();
            var road = plan.Roads.First(r => r.Kind == RoadKind.Trunk);
            road.Y[road.Y.Length / 2] += 10f;
            Assert.IsTrue(Violates(plan, WorldRule.RoadSlope));
        }

        [Test]
        public void RoadUnderWater_IsAViolation()
        {
            var plan = Fresh();
            var road = plan.Roads.First(r => r.Kind == RoadKind.Trunk);
            for (int i = 0; i < road.Y.Length; i++) road.Y[i] = plan.SeaLevel - 1f;
            Assert.IsTrue(Violates(plan, WorldRule.RoadUnderWater));
        }

        [Test]
        public void RegionCutOffFromTheRoadNetwork_IsAViolation()
        {
            var plan = Fresh();
            plan.Roads.RemoveAll(r => r.Kind == RoadKind.Trunk);
            Assert.IsTrue(Violates(plan, WorldRule.RoadGraph));
        }

        [Test]
        public void RegionWithoutAPass_IsAViolation()
        {
            var plan = Fresh();
            plan.Passes.RemoveAll(p => p.RegionA == 1 || p.RegionB == 1);
            Assert.IsTrue(Violates(plan, WorldRule.PassCount));
        }

        [Test]
        public void GapInARidgeThatIsNotAPass_IsAViolation()
        {
            // The ground still has every gap; forgetting the passes means the regions join somewhere unguarded.
            var plan = Fresh();
            plan.Passes.Clear();
            Assert.IsTrue(Violates(plan, WorldRule.RegionsLeak));
        }

        [Test]
        public void ShuttingThePasses_SeparatesEveryRegion()
        {
            // The converse of the test above, stated on its own: with the real passes there is no leak.
            Assert.IsFalse(Violates(Fresh(), WorldRule.RegionsLeak));
        }

        [Test]
        public void TooLittleLand_IsAViolation()
        {
            var plan = Fresh();
            plan.SeaLevel += 40f; // the sea rises over everything but the ridges and the mountain
            Assert.IsTrue(Violates(plan, WorldRule.LandArea));
        }

        [Test]
        public void PointOutsideTheMap_IsAViolation()
        {
            var plan = Fresh();
            plan.ConvergencePoints[0].X = plan.Size + 5f;
            Assert.IsTrue(Violates(plan, WorldRule.InsideMap));
        }

        [Test]
        public void BrokenPlan_IsReportedNotThrown()
        {
            Assert.IsTrue(Violates(null, WorldRule.Malformed));
            var plan = Fresh();
            plan.Roads[0].Y = new float[1];
            Assert.DoesNotThrow(() => WorldRules.Validate(plan));
            Assert.IsTrue(Violates(plan, WorldRule.Malformed));
            Assert.IsNotEmpty(WorldRules.Validate(WorldGenerator.Generate(new WorldSpec())), "an empty spec is a world with nothing in it");
        }
    }
}
