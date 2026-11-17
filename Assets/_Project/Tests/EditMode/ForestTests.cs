using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-270: the forest belt on the mountain and hill slopes. Dense enough that nobody sees 20-30 m through it, open where people and vehicles move, and deterministic.
    public class ForestTests
    {
        const float Eye = 1.7f, Reach = 150f;
        static WorldPlan _plan;
        static WorldPlan Plan => _plan ?? (_plan = WorldGenerator.Generate(WorldSpec.CreateIsland()));

        static float TrunkRadius(Plant p) => 0.3f * p.Scale + 0.05f;

        // Mean distance (m) one can see along 8 horizontal rays at eye height from the points, stopped by a trunk or by the ground rising above the eye line.
        static float MeanSight(WorldPlan plan, List<Plant> plants, List<(float x, float z)> points)
        {
            var cells = new Dictionary<long, List<Plant>>();
            const float cell = 8f;
            long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
            foreach (var p in plants.Where(p => p.HasTrunk))
            {
                long k = Key((int)(p.X / cell), (int)(p.Z / cell));
                if (!cells.TryGetValue(k, out var list)) cells[k] = list = new List<Plant>();
                list.Add(p);
            }
            double sum = 0; int n = 0;
            foreach (var (x0, z0) in points)
            {
                float eye = plan.Height(x0, z0) + Eye;
                for (int d = 0; d < 8; d++)
                {
                    float ang = d * (float)Math.PI / 4f + 0.2f, dx = (float)Math.Cos(ang), dz = (float)Math.Sin(ang);
                    float seen = Reach;
                    for (float t = 1f; t < Reach && seen == Reach; t += 0.3f)
                    {
                        float x = x0 + dx * t, z = z0 + dz * t;
                        if (x < 0 || z < 0 || x >= plan.Size || z >= plan.Size) { seen = t; break; }
                        if (plan.Height(x, z) > eye) { seen = t; break; }
                        int cx = (int)(x / cell), cz = (int)(z / cell);
                        for (int ix = cx - 1; ix <= cx + 1 && seen == Reach; ix++)
                            for (int iz = cz - 1; iz <= cz + 1; iz++)
                                if (cells.TryGetValue(Key(ix, iz), out var list))
                                    foreach (var p in list)
                                    {
                                        float r = TrunkRadius(p) + 0.15f;   // the step is 0.3 m: a trunk is not skipped
                                        if ((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < r * r) { seen = t; break; }
                                    }
                    }
                    sum += seen; n++;
                }
            }
            return (float)(sum / n);
        }

        static List<(float, float)> SlopePoints(WorldPlan plan)
        {
            var pts = new List<(float, float)>();
            for (float z = 20f; z < plan.Size; z += 40f)
                for (float x = 20f; x < plan.Size; x += 40f)
                    if (Vegetation.ForestDensity(plan, x, z) >= 0.5f) pts.Add((x, z));
            return pts;
        }

        [Test]
        public void The_forest_belt_blocks_sight_on_the_slopes_and_the_numbers_are_logged()
        {
            var plan = Plan;
            var before = Vegetation.Plan(plan, false);
            var after = Vegetation.Plan(plan);
            var pts = SlopePoints(plan);
            Assert.Greater(pts.Count, 100, "forest mask points");
            float sightBefore = MeanSight(plan, before, pts), sightAfter = MeanSight(plan, after, pts);
            int treesBefore = before.Count(p => p.HasTrunk), treesAfter = after.Count(p => p.HasTrunk);
            string line = $"[Forest] points {pts.Count}, mean sight {sightBefore:F1} m -> {sightAfter:F1} m, trees {treesBefore} -> {treesAfter} " +
                $"(conifer {after.Count(p => p.Kind == PlantKind.Conifer)}), bushes {after.Count(p => !p.HasTrunk)}";
            UnityEngine.Debug.Log(line);
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "forest_numbers.txt"), line + "\n");
            Assert.LessOrEqual(sightAfter, 30f, line);
            Assert.Less(sightAfter, sightBefore);
        }

        [Test]
        public void Forest_trees_keep_off_roads_buildings_pads_passes_and_water()
        {
            var plan = Plan;
            var baseline = new HashSet<(int, int)>(Vegetation.Plan(plan, false).Where(p => p.HasTrunk).Select(p => ((int)(p.X * 10), (int)(p.Z * 10))));
            int forest = 0;
            foreach (var p in Vegetation.Plan(plan).Where(p => p.HasTrunk && !baseline.Contains(((int)(p.X * 10), (int)(p.Z * 10)))))
            {
                forest++;
                string at = $"forest tree at ({p.X:F0},{p.Z:F0})";
                Assert.GreaterOrEqual(plan.RoadDistance(p.X, p.Z), 5f, at + " is on a road verge");
                foreach (var lot in plan.Lots) Assert.GreaterOrEqual(lot.Distance(p.X, p.Z), 12f, at + " is near " + lot.Name);
                foreach (var e in plan.Elevators) Assert.GreaterOrEqual(e.Pad().Distance(p.X, p.Z), 14f, at + " is at " + e.Name);
                foreach (var pass in plan.Passes)
                    Assert.Greater((pass.X - p.X) * (pass.X - p.X) + (pass.Z - p.Z) * (pass.Z - p.Z), 30f * 30f, at + " is in " + pass.Name);
                foreach (var c in plan.ConvergencePoints)
                    Assert.Greater((c.X - p.X) * (c.X - p.X) + (c.Z - p.Z) * (c.Z - p.Z), c.ClearRadius * c.ClearRadius, at + " is in " + c.Name);
                Assert.GreaterOrEqual(p.Y, plan.SeaLevel + 0.3f, at + " is in the water");
            }
            Assert.Greater(forest, 1000, "there is a forest");
            Assert.LessOrEqual(forest, Vegetation.MaxForest);
        }

        [Test]
        public void The_forest_is_deterministic()
        {
            var a = Vegetation.Plan(Plan);
            var b = Vegetation.Plan(WorldGenerator.Generate(WorldSpec.CreateIsland()));
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i], "plant " + i);
        }
    }
}
