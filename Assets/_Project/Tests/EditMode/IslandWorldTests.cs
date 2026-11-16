using System;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // The C2 island 'Cheongnam-do' (ADR-168, ADR-204): what the map track promised in docs/tracks.md, as numbers.
    public class IslandWorldTests
    {
        static WorldSpec _spec;
        static WorldPlan _plan;
        static WorldSpec Spec => _spec ?? (_spec = WorldSpec.CreateIsland());
        static WorldPlan Plan => _plan ?? (_plan = WorldGenerator.Generate(Spec));

        [Test]
        public void Island_has_six_regions_seventeen_towns_and_411_buildings_of_the_listed_kinds()
        {
            Assert.AreEqual(6, Plan.Regions.Count);
            Assert.AreEqual(17, Plan.SubZones.Count);
            Assert.AreEqual(411, Plan.Lots.Count);
            // towns.json totals: house 178, shop 138, villa 22, stall 16, warehouse 14, barracks 8, apartment 8, shed 8, factory 6, school 6, tower 5, market 2
            var count = Plan.Lots.GroupBy(l => l.Kind).ToDictionary(g => g.Key, g => g.Count());
            Assert.AreEqual(178, count[BuildingKind.House]); Assert.AreEqual(138, count[BuildingKind.Shop]); Assert.AreEqual(22, count[BuildingKind.Villa]);
            Assert.AreEqual(16, count[BuildingKind.Stall]); Assert.AreEqual(14, count[BuildingKind.Warehouse]); Assert.AreEqual(8, count[BuildingKind.Barracks]);
            Assert.AreEqual(8, count[BuildingKind.Apartment]); Assert.AreEqual(8, count[BuildingKind.Shed]); Assert.AreEqual(6, count[BuildingKind.FactoryHall]);
            Assert.AreEqual(6, count[BuildingKind.School]); Assert.AreEqual(5, count[BuildingKind.WatchTower]); Assert.AreEqual(2, count[BuildingKind.Market]);
            foreach (var t in Spec.Island.Towns)
                Assert.AreEqual(IslandGenerator.Composition(t.Composition).Sum(c => c.count), Plan.Lots.Count(l => l.Name.StartsWith(t.Id + "-")), t.Name);
        }

        [Test]
        public void Twenty_stations_of_four_elevators_make_80_slots_and_the_bays_do_not_touch()
        {
            Assert.AreEqual(80, Plan.Elevators.Count, "deployment slots (one player an elevator)");
            Assert.AreEqual(20, Plan.Elevators.Select(e => e.Bank).Distinct().Count());
            CollectionAssert.AreEqual(new[] { 3, 4, 3, 3, 4, 3 }, Enumerable.Range(0, 6).Select(r => Plan.Elevators.Where(e => e.Region == r).Select(e => e.Bank).Distinct().Count()).ToArray());
            Assert.IsTrue(Plan.Elevators.GroupBy(e => e.Bank).All(g => g.Count() == 4));
            for (int i = 0; i < Plan.Elevators.Count; i++)
                for (int j = i + 1; j < Plan.Elevators.Count; j++)
                {
                    var a = Plan.Elevators[i]; var b = Plan.Elevators[j];
                    var fa = new BuildingLot { X = a.X, Z = a.Z, SizeX = WorldElevator.StationWidth, SizeZ = WorldElevator.StationDepth, YawDeg = a.FacingDeg };
                    var fb = new BuildingLot { X = b.X, Z = b.Z, SizeX = WorldElevator.StationWidth, SizeZ = WorldElevator.StationDepth, YawDeg = b.FacingDeg };
                    Assert.IsFalse(BuildingLot.Overlap(fa, fb, 0f), $"{a.Name} and {b.Name} overlap");
                }
            foreach (var e in Plan.Elevators)
                foreach (var l in Plan.Lots)
                    Assert.IsFalse(BuildingLot.Overlap(new BuildingLot { X = e.X, Z = e.Z, SizeX = WorldElevator.StationWidth, SizeZ = WorldElevator.StationDepth, YawDeg = e.FacingDeg }, l, 0f), $"{e.Name} stands on {l.Name}");
        }

        [Test]
        public void World_rules_hold_on_the_island()
        {
            var violations = WorldRules.Validate(Plan);
            Assert.IsEmpty(violations, string.Join("\n", violations.Take(20).Select(v => v.ToString())));
        }

        [Test]
        public void Regenerating_gives_the_same_island()
        {
            Assert.AreEqual(Hash(Plan), Hash(WorldGenerator.Generate(WorldSpec.CreateIsland())));
        }

        static ulong Hash(WorldPlan p)
        {
            ulong h = 1469598103934665603UL;
            void Mix(float v) { h = (h ^ (ulong)(long)Math.Round(v * 100.0)) * 1099511628211UL; }
            foreach (var l in p.Lots) { Mix(l.X); Mix(l.Z); Mix(l.Y); Mix(l.YawDeg); Mix(l.SizeX); Mix((float)l.Kind); }
            foreach (var r in p.Roads) for (int i = 0; i < r.X.Length; i++) { Mix(r.X[i]); Mix(r.Y[i]); Mix(r.Z[i]); }
            foreach (var e in p.Elevators) { Mix(e.X); Mix(e.Z); }
            for (int z = 0; z <= 2400; z += 37) for (int x = 0; x <= 2400; x += 41) Mix(p.Height(x, z));
            Mix(p.Rocks.Count); Mix(p.Trees.Count);
            return h;
        }

        [Test]
        public void No_road_is_steeper_than_twelve_degrees_over_any_30_m()
        {
            foreach (var r in Plan.Roads)
            {
                var d = new System.Collections.Generic.List<float>(); var y = new System.Collections.Generic.List<float>();
                float acc = 0f;
                for (int i = 0; i < r.X.Length; i++)
                {
                    if (i > 0) acc += (float)Math.Sqrt((r.X[i] - r.X[i - 1]) * (r.X[i] - r.X[i - 1]) + (r.Z[i] - r.Z[i - 1]) * (r.Z[i] - r.Z[i - 1]));
                    d.Add(acc); y.Add(r.Y[i]);
                }
                Assert.LessOrEqual(RoadGraph.MaxGradeDeg(d, y), 12f, r.Name);
            }
        }

        [Test]
        public void Five_bridges_over_water_and_one_ford()
        {
            Assert.AreEqual(5, Plan.Bridges.Count);
            foreach (var b in Plan.Bridges)
            {
                var road = Plan.Roads[b.Road];
                Assert.Greater(b.Length, 15f, b.Name);
                Assert.IsTrue(road.Bridge[b.FromPoint] && road.Bridge[b.ToPoint - 1], b.Name);
                for (int p = b.FromPoint; p <= b.ToPoint; p++)
                    foreach (var river in Plan.Rivers)
                    {
                        int near = 0; float best = float.MaxValue;
                        for (int i = 0; i < river.X.Length; i++)
                        {
                            float dx = river.X[i] - road.X[p], dz = river.Z[i] - road.Z[p], d = dx * dx + dz * dz;
                            if (d < best) { best = d; near = i; }
                        }
                        if (best < river.HalfWidth[near] * river.HalfWidth[near]) Assert.Greater(road.Y[p], river.Y[near] + 0.6f, $"{b.Name} deck clears the water at point {p}");
                    }
            }
            Assert.AreEqual(1, Plan.Roads.Count(r => r.Ford != null && r.Ford.Any(f => f)), "one ford");
            Assert.IsTrue(Plan.Roads.Where(r => r.Bridge != null).All(r => r.Kind != RoadKind.Path || !r.Bridge.Any(b => b)), "dirt tracks ford, they do not get decks");
        }

        [Test]
        public void Five_vehicle_passes_and_three_foot_passes()
        {
            Assert.AreEqual(8, Plan.Passes.Count);
            Assert.AreEqual(5, Plan.Passes.Count(p => p.Width >= 18f));
            CollectionAssert.AreEquivalent(new[] { "북령고개", "동령재", "서령재", "남령재", "샘골재" }, Plan.Passes.Where(p => p.Width >= 18f).Select(p => p.Name));
        }

        [Test]
        public void Walkable_land_share_and_summit_match_the_design_targets()
        {
            const int n = 1201;
            var h = new float[n * n];
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) h[j * n + i] = Plan.Height(i * 2f, j * 2f);
            int land = 0, walk = 0; float top = 0f;
            for (int j = 1; j < n - 1; j++)
                for (int i = 1; i < n - 1; i++)
                {
                    float v = h[j * n + i];
                    if (v <= Plan.SeaLevel + 0.3f) continue;
                    land++; top = Math.Max(top, v);
                    float sx = (h[j * n + i + 1] - h[j * n + i - 1]) / 4f, sz = (h[(j + 1) * n + i] - h[(j - 1) * n + i]) / 4f;
                    if (Math.Atan(Math.Sqrt(sx * sx + sz * sz)) * 180.0 / Math.PI <= 35.0) walk++;
                }
            float km2 = land * 4f / 1e6f;
            Assert.That(km2, Is.InRange(3.0f, 3.5f), "land km2 (design 3.23)");
            Assert.GreaterOrEqual(100f * walk / land, 88f, "share of land walkable at 35 degrees (03_mountains: 88%)");
            Assert.That(top - Plan.SeaLevel, Is.InRange(185f, 215f), "summit above the sea (design about 203)");
        }

        [Test]
        public void Every_building_stands_on_dry_flat_ground_and_not_in_a_river()
        {
            foreach (var l in Plan.Lots)
            {
                Assert.LessOrEqual(l.GroundSlopeDeg, 20f, l.Name);
                Assert.Greater(Plan.Height(l.X, l.Z), Plan.SeaLevel + 0.3f, l.Name);
            }
        }

        [Test]
        public void Road_graph_of_roads_json_connects_every_town_and_survives_losing_any_paved_road()
        {
            var g = new RoadGraph();
            foreach (var e in Spec.Island.Roads)
                g.Add(new RoadEdge { A = e.A, B = e.B, Class = e.Class == "H" ? RoadClass.Trunk : e.Class == "P" ? RoadClass.Paved : RoadClass.Dirt });
            Assert.IsTrue(g.IsConnected());
            Assert.AreEqual(22, g.Nodes.Count);
            Assert.AreEqual(29, g.Edges.Count);
            Assert.IsEmpty(g.Bridges(e => e.Class != RoadClass.Dirt), "no single paved road cuts the network");
        }
    }
}
