using System;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #245 T4 / WP1: loot points per building = points available x kind weight, capped by what the map allows,
    // instead of "one per building in turn until 250 points are taken".
    public class LootPlacementTests
    {
        static LootBuilding B(BuildingKind kind, int available = 12) => new LootBuilding { Kind = kind, Available = available };

        [Test]
        public void Barracks_and_factories_are_richer_than_houses_and_stalls_are_poor()
        {
            float W(BuildingKind k) => LootPlacement.KindWeight(k);
            Assert.Greater(W(BuildingKind.Barracks), W(BuildingKind.FactoryHall));
            Assert.Greater(W(BuildingKind.FactoryHall), W(BuildingKind.Warehouse));
            Assert.Greater(W(BuildingKind.Warehouse), W(BuildingKind.House));
            Assert.Greater(W(BuildingKind.House), W(BuildingKind.Shed));
            Assert.Greater(W(BuildingKind.Shed), W(BuildingKind.Stall));
            foreach (BuildingKind kind in Enum.GetValues(typeof(BuildingKind)))
                Assert.Greater(W(kind), 0f, kind.ToString());
        }

        [Test]
        public void A_barracks_gets_more_points_than_a_house_of_the_same_size()
        {
            var counts = LootPlacement.Allocate(new[] { B(BuildingKind.House), B(BuildingKind.Barracks), B(BuildingKind.Stall) }, new WorldBuildSpec { MaxLootSpawns = 1000 });
            Assert.Greater(counts[1], counts[0]);
            Assert.Greater(counts[0], counts[2]);
            Assert.GreaterOrEqual(counts[2], 1);
        }

        [Test]
        public void A_building_never_gets_more_points_than_it_has()
        {
            var counts = LootPlacement.Allocate(new[] { B(BuildingKind.Barracks, 2), B(BuildingKind.House, 1) }, new WorldBuildSpec { MaxLootSpawns = 1000 });
            Assert.AreEqual(2, counts[0]);
            Assert.AreEqual(1, counts[1]);
        }

        [Test]
        public void A_building_without_points_gets_none()
        {
            var counts = LootPlacement.Allocate(new[] { B(BuildingKind.House, 0), B(BuildingKind.ElevatorStation, 5) }, new WorldBuildSpec { MaxLootSpawns = 1000 });
            Assert.AreEqual(0, counts[0]);
            Assert.AreEqual(0, counts[1], "stations hold no loot");
        }

        [TestCase(40)]
        [TestCase(100)]
        [TestCase(250)]
        [TestCase(1000)]
        public void The_total_is_the_cap_or_less_and_every_building_gets_some_when_the_cap_allows(int cap)
        {
            var kinds = new[] { BuildingKind.House, BuildingKind.Shop, BuildingKind.Villa, BuildingKind.Apartment, BuildingKind.School, BuildingKind.Barracks,
                BuildingKind.FactoryHall, BuildingKind.Warehouse, BuildingKind.Market, BuildingKind.Stall, BuildingKind.Shed, BuildingKind.WatchTower };
            var buildings = Enumerable.Range(0, 100).Select(i => B(kinds[i % kinds.Length], 6 + i % 9)).ToArray();
            var counts = LootPlacement.Allocate(buildings, new WorldBuildSpec { MaxLootSpawns = cap });
            Assert.LessOrEqual(counts.Sum(), cap);
            for (int i = 0; i < counts.Length; i++) Assert.LessOrEqual(counts[i], buildings[i].Available);
            if (cap >= buildings.Length) Assert.IsTrue(counts.All(c => c >= 1), "every building has some loot");
            else
            {
                Assert.AreEqual(cap, counts.Sum());
                // the poor kinds are the ones left empty: nothing without loot is richer than the poorest building with some
                float poorestFunded = counts.Zip(buildings, (c, b) => (c, b)).Where(x => x.c > 0).Min(x => LootPlacement.KindWeight(x.b.Kind));
                Assert.IsTrue(counts.Zip(buildings, (c, b) => (c, b)).Where(x => x.c == 0).All(x => LootPlacement.KindWeight(x.b.Kind) <= poorestFunded));
            }
        }

        [Test]
        public void The_total_follows_the_number_of_buildings_not_a_fixed_250()
        {
            var spec = new WorldBuildSpec { MaxLootSpawns = 100000 };
            int Total(int n) => LootPlacement.Allocate(Enumerable.Range(0, n).Select(_ => B(BuildingKind.House)).ToArray(), spec).Sum();
            Assert.Greater(Total(411), Total(73) * 4);
        }

        [Test]
        public void The_current_island_keeps_about_250_points()
        {
            var plan = WorldGenerator.Generate(WorldSpec.CreateDefault());
            var buildings = plan.Lots.Select(l => B(l.Kind, 10)).ToArray();
            int total = LootPlacement.Allocate(buildings, plan.Build).Sum();
            Assert.That(total, Is.InRange(150, plan.Build.MaxLootSpawns));
        }

        [Test]
        public void The_same_input_gives_the_same_counts()
        {
            var buildings = Enumerable.Range(0, 60).Select(i => B((BuildingKind)(i % 8), 4 + i % 7)).ToArray();
            var spec = new WorldBuildSpec { MaxLootSpawns = 90 };
            CollectionAssert.AreEqual(LootPlacement.Allocate(buildings, spec), LootPlacement.Allocate(buildings, spec));
        }
    }
}
