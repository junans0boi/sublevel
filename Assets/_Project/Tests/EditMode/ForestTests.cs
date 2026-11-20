using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-270, ADR-487: the forest belt on the mountain and hill slopes. Its undergrowth hides a crouching player, its trunks stand far enough
    // apart that a car weaves through, it stays open where people and vehicles move, and it is deterministic.
    public class ForestTests
    {
        const float Eye = 1.7f, Reach = 150f;
        static WorldPlan _plan;
        static WorldPlan Plan => _plan ?? (_plan = WorldGenerator.Generate(WorldSpec.CreateIsland()));

        static float TrunkRadius(Plant p) => 0.3f * p.Scale + 0.05f;

        // Mean distance (m) one can see along 8 horizontal rays at eye height from the points, stopped by a trunk or by the ground rising above the eye line,
        // and with bushes also by a bush taller than the eye (about 1.1 m x scale high, 0.8 m x scale round).
        static float MeanSight(WorldPlan plan, List<Plant> plants, List<(float x, float z)> points, float eyeHeight = Eye, bool bushes = false)
        {
            var cells = new Dictionary<long, List<Plant>>();
            const float cell = 8f;
            long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
            foreach (var p in plants.Where(p => p.HasTrunk || bushes))
            {
                long k = Key((int)(p.X / cell), (int)(p.Z / cell));
                if (!cells.TryGetValue(k, out var list)) cells[k] = list = new List<Plant>();
                list.Add(p);
            }
            double sum = 0; int n = 0;
            foreach (var (x0, z0) in points)
            {
                float eye = plan.Height(x0, z0) + eyeHeight;
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
                                        if (!p.HasTrunk && p.Y + 1.1f * p.Scale < eye) continue;
                                        float r = p.HasTrunk ? TrunkRadius(p) + 0.15f : 0.8f * p.Scale;   // the step is 0.3 m: a trunk is not skipped
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
        public void The_forest_hides_a_crouching_player_without_walling_in_the_view_and_the_numbers_are_logged()
        {
            var plan = Plan;
            var before = Vegetation.Plan(plan, false);
            var after = Vegetation.Plan(plan);
            var pts = SlopePoints(plan);
            Assert.Greater(pts.Count, 100, "forest mask points");
            float sightBefore = MeanSight(plan, before, pts), sightAfter = MeanSight(plan, after, pts);
            float crouchBefore = MeanSight(plan, before, pts, 1f, true), crouchAfter = MeanSight(plan, after, pts, 1f, true);
            int treesBefore = before.Count(p => p.HasTrunk), treesAfter = after.Count(p => p.HasTrunk);
            string line = $"[Forest] points {pts.Count}, mean sight standing past trunks {sightBefore:F1} m -> {sightAfter:F1} m, crouched past trunks and bushes {crouchBefore:F1} m -> {crouchAfter:F1} m, trees {treesBefore} -> {treesAfter} " +
                $"(conifer {after.Count(p => p.Kind == PlantKind.Conifer)}), bushes {after.Count(p => !p.HasTrunk)}";
            UnityEngine.Debug.Log(line);
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "forest_numbers.txt"), line + "\n");
            Assert.LessOrEqual(crouchAfter, 40f, line);   // a crouching player is hidden from afar
            Assert.Less(sightAfter, sightBefore, line);
            Assert.GreaterOrEqual(sightAfter, 40f, line);   // ADR-487: standing, the trunks alone no longer wall the view in at 20-30 m
        }

        [Test]
        public void Forest_trees_keep_off_roads_buildings_pads_passes_and_water()
        {
            var plan = Plan;
            var baseline = new HashSet<(int, int)>(Vegetation.Plan(plan, false).Where(p => p.HasTrunk).Select(p => ((int)(p.X * 10), (int)(p.Z * 10))));
            int forest = 0;
            // Yard and street trees can take a spot here that a plan tree dropped by the trunk spacing (ADR-487) held before; they follow their own rules.
            foreach (var p in Vegetation.Plan(plan).Where(p => p.HasTrunk && !baseline.Contains(((int)(p.X * 10), (int)(p.Z * 10)))
                && (Vegetation.ForestDensity(plan, p.X, p.Z) > 0f || Vegetation.MeadowDensity(plan, p.X, p.Z) > 0f)))
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

        // ADR-487: trunks of the wood (the colliders) stand at least the forest spacing apart, centre to centre.
        [Test]
        public void Forest_trunks_keep_the_spacing_a_car_needs()
        {
            var plan = Plan;
            var trunks = Vegetation.Plan(plan).Where(p => p.HasTrunk).ToList();
            var cells = Cells(trunks);
            var nearest = new List<float>();
            foreach (var p in trunks)
            {
                if (Vegetation.ForestDensity(plan, p.X, p.Z) <= 0f) continue;
                float best = float.MaxValue;
                foreach (var q in Around(cells, p.X, p.Z))
                    if (q.X != p.X || q.Z != p.Z) best = Math.Min(best, (float)Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z)));
                nearest.Add(best);
            }
            nearest.Sort();
            string line = $"[Forest] {nearest.Count} trunks in the wood, nearest neighbour min {nearest[0]:F2} m, 1 % {nearest[nearest.Count / 100]:F2} m, median {nearest[nearest.Count / 2]:F2} m";
            UnityEngine.Debug.Log(line);
            Assert.Greater(nearest.Count, 5000, line);
            Assert.GreaterOrEqual(nearest[0], 5.5f, line);                       // town yard trees (4.5 m) may stand by the edge of the wood
            Assert.GreaterOrEqual(nearest[nearest.Count / 100], Vegetation.ForestSpacing - 0.01f, line);
        }

        // ADR-487: a car 2.4 m wide (with margin) driving 200 m straight up a forested slope gets through, steering round the trunks: from every
        // 40 m point of the wood it heads uphill, each metre takes the heading nearest uphill (within 90 degrees of it, turning at most 35 degrees a
        // metre) whose next 8 m are clear (1 m when nothing is), and must make 200 m of uphill progress in 400 m of driving.
        [Test]
        public void A_car_weaves_200_m_up_the_forested_slopes()
        {
            var plan = Plan;
            var cells = Cells(Vegetation.Plan(plan).Where(p => p.HasTrunk));
            const float Half = 1.2f;
            bool Clear(float x, float z)
            {
                foreach (var q in Around(cells, x, z))
                {
                    float r = 0.45f * q.Scale + 0.05f + Half;   // the largest broadleaf trunk is 0.44 m x scale round
                    if ((q.X - x) * (q.X - x) + (q.Z - z) * (q.Z - z) < r * r) return false;
                }
                return true;
            }
            int starts = 0, through = 0;
            for (float z0 = 20f; z0 < plan.Size; z0 += 40f)
                for (float x0 = 20f; x0 < plan.Size; x0 += 40f)
                {
                    if (Vegetation.ForestDensity(plan, x0, z0) < 0.5f || !Clear(x0, z0)) continue;
                    starts++;
                    float uphill = (float)Math.Atan2(plan.Height(x0, z0 + 1f) - plan.Height(x0, z0 - 1f), plan.Height(x0 + 1f, z0) - plan.Height(x0 - 1f, z0));
                    float x = x0, z = z0, heading = uphill;
                    for (int step = 0; step < 400; step++)
                    {
                        bool moved = false;
                        for (int look = 8; look >= 1 && !moved; look -= 7)
                            for (int k = 0; k <= 36 && !moved; k++)
                            {
                                float h = uphill + (k + 1) / 2 * (k % 2 == 0 ? 1 : -1) * 5f * (float)Math.PI / 180f;
                                if (Math.Abs(Math.IEEERemainder(h - heading, 2 * Math.PI)) > 35f * Math.PI / 180f) continue;
                                float cx = (float)Math.Cos(h), cz = (float)Math.Sin(h);
                                bool free = true;
                                for (int a = 1; a <= look && free; a++) free = Clear(x + a * cx, z + a * cz);
                                if (!free) continue;
                                x += cx; z += cz; heading = h; moved = true;
                            }
                        if (!moved) break;
                        if ((x - x0) * Math.Cos(uphill) + (z - z0) * Math.Sin(uphill) >= 200f) { through++; break; }
                    }
                }
            string line = $"[Forest] car drives from {starts} points, {through} get 200 m up the slope ({100f * through / starts:F1} %)";
            UnityEngine.Debug.Log(line);
            Assert.Greater(starts, 300, line);
            Assert.GreaterOrEqual(through, starts * 0.95f, line);   // before ADR-487: 8 %
        }

        static Dictionary<long, List<Plant>> Cells(IEnumerable<Plant> plants)
        {
            var cells = new Dictionary<long, List<Plant>>();
            foreach (var p in plants)
            {
                long k = CellKey((int)Math.Floor(p.X / 8f), (int)Math.Floor(p.Z / 8f));
                if (!cells.TryGetValue(k, out var list)) cells[k] = list = new List<Plant>();
                list.Add(p);
            }
            return cells;
        }

        static long CellKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        static IEnumerable<Plant> Around(Dictionary<long, List<Plant>> cells, float x, float z)
        {
            int cx = (int)Math.Floor(x / 8f), cz = (int)Math.Floor(z / 8f);
            for (int a = -1; a <= 1; a++)
                for (int b = -1; b <= 1; b++)
                    if (cells.TryGetValue(CellKey(cx + a, cz + b), out var list))
                        foreach (var q in list) yield return q;
        }

        [Test]
        public void The_forest_is_deterministic()
        {
            var a = Vegetation.Plan(Plan);
            var b = Vegetation.Plan(WorldGenerator.Generate(WorldSpec.CreateIsland()));
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i], "plant " + i);
        }
            // ADR-487: the third-person car camera does not draw bushes on its sight line to the car, nor a tree whose boughs hold the camera,
        // but keeps the bush the car drives through and a trunk beside the car (it has a collider).
        [Test]
        public void CarCamera_ClearsTheSightLine_ButKeepsTrunksBesideTheCar()
        {
            var eye = new UnityEngine.Vector3(0f, 3f, 0f);
            var car = new UnityEngine.Vector3(0f, 0f, 6.5f);
            bool Cleared(PlantKind k, float h, float x, float z) => Game.Gameplay.VegetationRenderer.Cleared(k, h, new UnityEngine.Vector3(x, 0f, z), eye, car);
            Assert.IsTrue(Cleared(PlantKind.Bush, 1.5f, 0.5f, 3f), "bush between camera and car");
            Assert.IsTrue(Cleared(PlantKind.Bush, 1.5f, 0.3f, -0.5f), "bush round the camera");
            Assert.IsFalse(Cleared(PlantKind.Bush, 1.5f, 4f, 3f), "bush off the sight line");
            Assert.IsFalse(Cleared(PlantKind.Bush, 1.5f, 0f, 6.2f), "bush the car drives through");
            Assert.IsTrue(Cleared(PlantKind.Conifer, 20f, 2f, -1f), "conifer whose boughs hold the camera");
            Assert.IsFalse(Cleared(PlantKind.Conifer, 20f, 2f, 6f), "trunk beside the car");
        }
    }
}
