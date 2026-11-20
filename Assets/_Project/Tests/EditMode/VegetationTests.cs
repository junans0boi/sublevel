using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // Trees, bushes and grass of the outdoor map (#71, ADR-070): where they may stand, how many there are, how the
    // grass map is read and how the graphics level and the camera pick a level of detail.
    public class VegetationTests
    {
        static WorldPlan _plan;
        static List<Plant> _plants;

        static WorldPlan Plan => _plan ?? (_plan = WorldGenerator.Generate(WorldSpec.CreateDefault()));
        static List<Plant> Plants => _plants ?? (_plants = Vegetation.Plan(Plan));

        [Test]
        public void ShippedWorld_HasWoodsBushesAndBothSpecies()
        {
            var trees = Plants.Where(p => p.HasTrunk).ToList();
            var bushes = Plants.Where(p => !p.HasTrunk).ToList();
            Assert.GreaterOrEqual(trees.Count, Plan.Trees.Count * 0.8f, "the plan's trees are kept (less those closer than the trunk spacing, ADR-487)");
            Assert.LessOrEqual(trees.Count, Vegetation.MaxTrees + Vegetation.MaxForest);
            Assert.Greater(bushes.Count, 500);
            Assert.LessOrEqual(bushes.Count, Vegetation.MaxBushes + Vegetation.MaxUndergrowth);
            Assert.Greater(trees.Count(p => p.Kind == PlantKind.Conifer), 100);
            Assert.Greater(trees.Count(p => p.Kind == PlantKind.Broadleaf), 100);
            for (int v = 0; v < Vegetation.Variants; v++)
                Assert.IsTrue(Plants.Any(p => p.Variant == v), "variant " + v + " is used");
            Assert.IsTrue(Plants.All(p => p.Variant >= 0 && p.Variant < Vegetation.Variants));
        }

        [Test]
        public void Mountain_IsMostlyConifer_LowRegionsMostlyBroadleaf()
        {
            float Share(RegionKind kind)
            {
                var trees = Plants.Where(p => p.HasTrunk && p.Region >= 0 && Plan.Regions[p.Region].Kind == kind).ToList();
                return trees.Count == 0 ? 0f : trees.Count(p => p.Kind == PlantKind.Conifer) / (float)trees.Count;
            }
            Assert.Greater(Share(RegionKind.Mountain), 0.5f);
            Assert.Less(Share(RegionKind.Wetland), 0.3f);
            Assert.Less(Share(RegionKind.Town), 0.4f);
        }

        [Test]
        public void Town_GainsYardAndStreetTrees()
        {
            int town = Plan.Regions.First(r => r.Kind == RegionKind.Town).Index;
            int planned = Plan.Trees.Count(t => t.Region == town);
            int placed = Plants.Count(p => p.HasTrunk && p.Region == town);
            Assert.Greater(placed, planned + 45, "yard and street trees are added to the town");
        }

        [Test]
        public void AddedTrees_KeepClearOfRoadsBuildingsElevatorsAndPasses()
        {
            // The plan's own scattered trees come first in the list and follow the generator's rules.
            foreach (var p in Plants.Skip(Plan.Trees.Count).Where(p => p.HasTrunk))
            {
                string at = $"tree at ({p.X:F0},{p.Z:F0})";
                Assert.GreaterOrEqual(Plan.RoadDistance(p.X, p.Z), 4f, at + " is beside a road (parked cars stand there)");
                foreach (var lot in Plan.Lots) Assert.GreaterOrEqual(lot.Distance(p.X, p.Z), 3.5f, at + " is against " + lot.Name);
                foreach (var e in Plan.Elevators) Assert.GreaterOrEqual(e.Pad().Distance(p.X, p.Z), 3.5f, at + " is against " + e.Name);
                foreach (var pass in Plan.Passes)
                    Assert.Greater((pass.X - p.X) * (pass.X - p.X) + (pass.Z - p.Z) * (pass.Z - p.Z), 20f * 20f, at + " is in " + pass.Name);
                Assert.GreaterOrEqual(p.Y, Plan.SeaLevel + 0.3f, at + " is in the water");
            }
        }

        [Test]
        public void Bushes_StayOffRoadsAndBuildings()
        {
            foreach (var p in Plants.Where(p => !p.HasTrunk))
            {
                string at = $"bush at ({p.X:F0},{p.Z:F0})";
                Assert.GreaterOrEqual(Plan.RoadDistance(p.X, p.Z), 1.4f, at + " is on a road");
                foreach (var lot in Plan.Lots) Assert.GreaterOrEqual(lot.Distance(p.X, p.Z), 1.9f, at + " is against " + lot.Name);
            }
        }

        [Test]
        public void Trees_AreNotStackedOnEachOther()
        {
            var all = Plants.Where(p => p.HasTrunk).ToList();   // the forest belt keeps its trunks 6.5 m apart since ADR-487
            foreach (var t in all)
                foreach (var o in all)
                {
                    if (o.X == t.X && o.Z == t.Z) continue;
                    Assert.Greater((o.X - t.X) * (o.X - t.X) + (o.Z - t.Z) * (o.Z - t.Z), 4f * 4f, $"trees at ({t.X:F0},{t.Z:F0}) and ({o.X:F0},{o.Z:F0})");
                }
        }

        [Test]
        public void Plan_IsTheSameEveryTime()
        {
            var again = Vegetation.Plan(WorldGenerator.Generate(WorldSpec.CreateDefault()));
            Assert.AreEqual(Plants.Count, again.Count);
            for (int i = 0; i < again.Count; i += 37)
            {
                Assert.AreEqual(Plants[i].X, again[i].X);
                Assert.AreEqual(Plants[i].Kind, again[i].Kind);
                Assert.AreEqual(Plants[i].Variant, again[i].Variant);
            }
        }

        // ---- grass ----

        [Test]
        public void Grass_DoesNotGrowOnRoadsPadsOrTheSea()
        {
            foreach (var road in Plan.Roads)
                for (int i = 0; i < road.X.Length; i += 3)
                    Assert.AreEqual(0f, Vegetation.GrassAllowed(Plan, road.X[i], road.Z[i], Plan.Height(road.X[i], road.Z[i])), "grass on " + road.Name);
            foreach (var lot in Plan.Lots)
                Assert.AreEqual(0f, Vegetation.GrassAllowed(Plan, lot.X, lot.Z, Plan.Height(lot.X, lot.Z)), "grass inside " + lot.Name);
            foreach (var e in Plan.Elevators)
                Assert.AreEqual(0f, Vegetation.GrassAllowed(Plan, e.X, e.Z, Plan.Height(e.X, e.Z)), "grass on " + e.Name);
            Assert.AreEqual(0f, Vegetation.GrassAllowed(Plan, 8f, 8f, Plan.Height(8f, 8f)), "grass at sea");
        }

        [Test]
        public void Grass_GrowsOnMostOfTheOpenGround()
        {
            int open = 0, grassy = 0;
            for (float z = 4f; z < Plan.Size; z += 16f)
                for (float x = 4f; x < Plan.Size; x += 16f)
                {
                    float h = Plan.Height(x, z);
                    if (Plan.RegionAt(x, z) < 0 || h < Plan.SeaLevel + 0.5f || Plan.SlopeTan(x, z) > 0.3f) continue;
                    open++;
                    if (Vegetation.GrassAllowed(Plan, x, z, h) > 0.5f) grassy++;
                }
            Assert.Greater(grassy, open * 0.7f);
        }

        [Test]
        public void Reeds_GrowOnlyInTheWetland()
        {
            foreach (var region in Plan.Regions)
            {
                bool any = Vegetation.TallGrass(Plan, region.HubX, region.HubZ, Plan.SeaLevel + 0.2f);
                Assert.AreEqual(region.Kind == RegionKind.Wetland, any, "reeds in " + region.Id);
            }
        }

        [Test]
        public void GrassCode_RoundTrips()
        {
            Assert.AreEqual(0, Vegetation.EncodeGrass(0.05f, false));
            Assert.AreEqual(0f, Vegetation.DecodeGrass(0, out bool none));
            Assert.IsFalse(none);
            Assert.AreEqual(0.5f, Vegetation.DecodeGrass(Vegetation.EncodeGrass(0.5f, false), out bool shortGrass), 0.01f);
            Assert.IsFalse(shortGrass);
            Assert.AreEqual(1f, Vegetation.DecodeGrass(Vegetation.EncodeGrass(1f, true), out bool reeds), 0.01f);
            Assert.IsTrue(reeds);
        }

        [Test]
        public void ScatterGrass_FollowsTheMapAndRepeats()
        {
            const int res = 8;
            var map = new byte[res * res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    map[z * res + x] = x < res / 2 ? Vegetation.EncodeGrass(1f, false) : (byte)0;
            var a = new List<GrassClump>();
            var b = new List<GrassClump>();
            // Map cells of 2 m: the cell (0,0) covers map texels 0..3 (all grass), (1,0) texels 4..7 (none).
            Vegetation.ScatterGrass(map, res, 2f, 0, 0, 7, a);
            Assert.Greater(a.Count, Vegetation.GrassPerSquareMetre * 64f * 0.9f);
            Assert.IsTrue(a.All(c => c.X >= 0f && c.X < 8f && c.Z >= 0f && c.Z < 8f));
            Vegetation.ScatterGrass(map, res, 2f, 0, 0, 7, b);
            CollectionAssert.AreEqual(a.Select(c => c.X), b.Select(c => c.X));
            Vegetation.ScatterGrass(map, res, 2f, 1, 0, 7, b);
            Assert.AreEqual(0, b.Count);
        }

        // ---- levels of detail and graphics levels ----

        [Test]
        public void Level_FollowsThePreset_AndCustomFollowsViewDistance()
        {
            Assert.AreEqual(0, Vegetation.Level(0, 3));
            Assert.AreEqual(1, Vegetation.Level(1, 0));
            Assert.AreEqual(2, Vegetation.Level(2, 0));
            Assert.AreEqual(0, Vegetation.Level(3, 0));
            Assert.AreEqual(1, Vegetation.Level(3, 1));
            Assert.AreEqual(2, Vegetation.Level(3, 3));
            Assert.AreEqual(0f, Vegetation.GrassRadius[0], "low draws no grass");
            Assert.Less(Vegetation.GrassRadius[1], Vegetation.GrassRadius[2]);
        }

        [Test]
        public void Lod_StepsDownWithDistance_AndAScopeBringsDetailBack()
        {
            int previous = 0;
            for (float d = 1f; d < 600f; d += 5f)
            {
                int lod = Vegetation.Lod(PlantKind.Broadleaf, d, 1f, 1f, 1);
                Assert.GreaterOrEqual(lod, previous, "LOD never steps back up with distance");
                previous = lod;
            }
            Assert.AreEqual(2, previous, "far trees are billboards, not dropped");
            Assert.AreEqual(0, Vegetation.Lod(PlantKind.Broadleaf, 20f, 1f, 1f, 1));
            Assert.AreEqual(2, Vegetation.Lod(PlantKind.Broadleaf, 200f, 1f, 1f, 1));
            Assert.AreEqual(0, Vegetation.Lod(PlantKind.Broadleaf, 200f, 1f, Vegetation.Zoom(8f), 1), "through a scope a far tree is drawn in full");
            Assert.AreEqual(-1, Vegetation.Lod(PlantKind.Bush, 300f, 1f, 1f, 2));
            Assert.LessOrEqual(Vegetation.Lod(PlantKind.Broadleaf, 50f, 1f, 1f, 2), Vegetation.Lod(PlantKind.Broadleaf, 50f, 1f, 1f, 0), "higher level, more detail");
            Assert.AreEqual(1f, Vegetation.Zoom(60f), 1e-4f);
        }
    }
}
