using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-290 (spatial hash) and ADR-291 (loot spawns by building footprint), the bots' building-level plan.
    public class BuildingLootIndexTests
    {
        [Test]
        public void PointGrid_answers_the_same_points_as_a_plain_scan_in_the_same_order()
        {
            var rng = new System.Random(7);
            const int n = 1500;
            var xs = new float[n]; var zs = new float[n];
            for (int i = 0; i < n; i++) { xs[i] = (float)rng.NextDouble() * 2000f - 200f; zs[i] = (float)rng.NextDouble() * 1500f - 100f; }
            var grid = new PointGrid(25f);
            grid.Rebuild(xs, zs, n);
            var hits = new List<int>();
            for (int q = 0; q < 400; q++)
            {
                float x = (float)rng.NextDouble() * 2000f - 200f, z = (float)rng.NextDouble() * 1500f - 100f;
                float r = q % 3 == 0 ? 1.8f : 25f + q % 7;
                var expected = new List<int>();
                for (int i = 0; i < n; i++) if ((xs[i] - x) * (xs[i] - x) + (zs[i] - z) * (zs[i] - z) <= r * r) expected.Add(i);
                grid.Query(x, z, r, hits);
                CollectionAssert.AreEqual(expected, hits, $"query {q}");
            }
        }

        [Test]
        public void PointGrid_rebuild_forgets_the_old_points()
        {
            var grid = new PointGrid(10f);
            grid.Rebuild(new[] { 1f, 2f }, new[] { 1f, 2f }, 2);
            grid.Rebuild(new[] { 500f }, new[] { 500f }, 1);
            var hits = new List<int>();
            grid.Query(1f, 1f, 20f, hits);
            Assert.IsEmpty(hits);
            grid.Query(500f, 500f, 1f, hits);
            CollectionAssert.AreEqual(new[] { 0 }, hits);
        }

        static BuildingLot Lot(float x, float z, float sx, float sz, float yaw, BuildingKind kind = BuildingKind.House) =>
            new BuildingLot { Kind = kind, X = x, Z = z, SizeX = sx, SizeZ = sz, YawDeg = yaw };

        [Test]
        public void A_spawn_belongs_to_the_footprint_it_stands_in_even_when_another_centre_is_closer()
        {
            // A long hall and a small shed beside it: the spot at the far end of the hall is nearer the shed's centre.
            var lots = new[] { Lot(0, 0, 40f, 8f, 0f, BuildingKind.Warehouse), Lot(26f, 0, 6f, 6f, 0f, BuildingKind.Shed) };
            var index = new BuildingLootIndex(lots, new[] { 19f, 25f, 100f }, new[] { 0f, 1f, 100f });
            Assert.AreEqual(0, index.BuildingOf(0), "inside the hall, nearer the shed centre (7 m) than the hall's (19 m)");
            Assert.AreEqual(1, index.BuildingOf(1));
            Assert.AreEqual(-1, index.BuildingOf(2), "in the open");
            Assert.AreEqual(1, index.Unassigned);
            Assert.AreEqual(BuildingKind.Warehouse, index.KindOf(0));
            CollectionAssert.AreEqual(new[] { 0 }, index.SpawnsOf(0).ToArray());
        }

        [Test]
        public void A_turned_footprint_is_tested_in_its_own_frame()
        {
            var lots = new[] { Lot(0, 0, 20f, 4f, 90f) };    // 20 m along the world's x/z axis after the turn
            float x, z;
            lots[0].ToWorld(8f, 0f, out x, out z);           // 8 m along its length
            var index = new BuildingLootIndex(lots, new[] { x }, new[] { z });
            Assert.AreEqual(0, index.BuildingOf(0));
            lots[0].ToWorld(0f, 8f, out x, out z);          // 8 m to the side: outside a 4 m wide house
            Assert.AreEqual(-1, new BuildingLootIndex(lots, new[] { x }, new[] { z }).BuildingOf(0));
        }

        [Test]
        public void Doors_are_listed_per_building()
        {
            var index = new BuildingLootIndex(new[] { Lot(0, 0, 10f, 10f, 0f) }, new float[0], new float[0]);
            index.AddDoor(0, 5f, 0.1f, 1f);
            Assert.AreEqual(1, index.DoorCount(0));
            index.DoorAt(0, 0, out float x, out float y, out float z);
            Assert.AreEqual((5f, 0.1f, 1f), (x, y, z));
        }

        // Every loot point the building generator makes (what the scene's spawn points are chosen from) maps to its own lot on the real map.
        [Test]
        public void Every_loot_point_of_the_real_map_belongs_to_exactly_its_own_building()
        {
            var path = Path.Combine(Application.dataPath, "_Project/Data/Resources/GameData/world.json");
            var spec = JsonUtility.FromJson<WorldSpec>(File.ReadAllText(path));
            var plan = WorldGenerator.Generate(spec);
            var xs = new List<float>(); var zs = new List<float>(); var owner = new List<int>();
            for (int i = 0; i < plan.Lots.Count; i++)
            {
                var lot = plan.Lots[i];
                var building = Buildings.Generate(lot.Kind, lot.SizeX, lot.SizeZ, i + 1);
                foreach (var p in building.LootPoints)
                {
                    lot.ToWorld(p.X, p.Z, out float x, out float z);
                    xs.Add(x); zs.Add(z); owner.Add(i);
                }
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var index = new BuildingLootIndex(plan.Lots, xs, zs);
            double ms = watch.Elapsed.TotalMilliseconds;
            TestContext.Out.WriteLine($"index of {plan.Lots.Count} lots / {xs.Count} points built in {ms:F1} ms");
            Assert.AreEqual(0, index.Unassigned, "unassigned");
            Assert.AreEqual(0, index.Ambiguous, "inside two footprints");
            for (int s = 0; s < owner.Count; s++) Assert.AreEqual(owner[s], index.BuildingOf(s), $"point {s}");
            Assert.Less(ms, 50.0, "build cost (ADR-291: once at server start, 50 ms)");
        }
    }
}
