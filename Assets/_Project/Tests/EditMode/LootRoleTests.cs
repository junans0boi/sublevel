using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-256: every building worth entering holds a weapon, roofs hold loot, and the point names carry role and region.
    public class LootRoleTests
    {
        [TestCase(BuildingKind.House, 3, 1)] [TestCase(BuildingKind.Shop, 3, 1)] [TestCase(BuildingKind.Barracks, 7, 3)] [TestCase(BuildingKind.Apartment, 4, 2)]
        [TestCase(BuildingKind.Shed, 2, 0)] [TestCase(BuildingKind.Stall, 1, 0)]
        public void Weapons_come_first_by_kind(BuildingKind kind, int points, int weapons)
        {
            var roles = LootPlacement.Roles(kind, points, 7);
            Assert.AreEqual(points, roles.Length);
            Assert.AreEqual(weapons, roles.Count(r => r == 'W'));
            Assert.IsTrue(roles.Take(weapons).All(r => r == 'W'));
        }

        [Test]
        public void A_role_only_rolls_what_fits_it()
        {
            var table = new LootTable { Entries = new[] { new LootEntry { Kind = LootKind.Weapon, Weight = 5 }, new LootEntry { Kind = LootKind.Ammo, Weight = 9 }, new LootEntry { Kind = LootKind.Bandage, Weight = 4 }, new LootEntry { Kind = LootKind.Vest, Weight = 2 } } };
            var plan = table.PlanByRole("WAHGXWAH".ToCharArray(), 3);
            Assert.AreEqual(LootKind.Weapon, plan[0].Kind); Assert.AreEqual(LootKind.Ammo, plan[1].Kind); Assert.AreEqual(LootKind.Bandage, plan[2].Kind); Assert.AreEqual(LootKind.Vest, plan[3].Kind);
            Assert.AreEqual(LootKind.Weapon, plan[5].Kind);
        }

        [Test]
        public void Spawn_names_round_trip_role_and_region()
        {
            Assert.IsTrue(LootSpawnName.Parse(LootSpawnName.Make("LootSpawn", 'H', 4, 17), out char role, out int region));
            Assert.AreEqual('H', role); Assert.AreEqual(4, region);
            Assert.IsFalse(LootSpawnName.Parse("LootSpawn3", out _, out _), "untagged scenes keep the plain roll");
        }

        [Test]
        public void Shops_villas_and_apartments_have_loot_on_their_roof_deck()
        {
            foreach (var kind in new[] { BuildingKind.Shop, BuildingKind.Villa, BuildingKind.Apartment })
                for (int seed = 1; seed <= 20; seed++)
                {
                    Buildings.FootprintLimits(kind, out float minW, out float minD, out float maxW, out float maxD);
                    var plan = Buildings.Generate(kind, (minW + maxW) / 2f, (minD + maxD) / 2f, seed);
                    var roof = plan.LootPoints.Where(p => p.Room == "Roof").ToList();
                    Assert.GreaterOrEqual(roof.Count, 1, $"{kind} seed {seed}");
                    Assert.IsTrue(roof.All(p => p.Y > plan.Rooms.Max(r => r.FloorY) + 2f), "on the deck, not a lower floor");
                    Assert.IsEmpty(BuildingRules.Validate(plan).Where(v => v.Rule == BuildingRule.RoofUnreachable || v.Rule == BuildingRule.LootUnsupported || v.Rule == BuildingRule.LootInsidePiece || v.Rule == BuildingRule.LootOutside).ToList());
                }
        }

        [Test]
        public void The_island_holds_far_more_weapons_than_two_per_player()
        {
            var spec = WorldSpec.CreateIsland();
            var plan = WorldGenerator.Generate(spec);
            var counts = LootPlacement.Allocate(plan.Lots.Select(l => new LootBuilding { Kind = l.Kind, Available = Buildings.Generate(l.Kind, l.SizeX, l.SizeZ, 1).LootPoints.Count }).ToList(), spec.Build);
            int weapons = 0, withLoot = 0;
            for (int i = 0; i < counts.Length; i++) { weapons += System.Math.Min(counts[i], LootPlacement.MinWeapons(plan.Lots[i].Kind)); if (counts[i] > 0) withLoot++; }
            Assert.GreaterOrEqual(weapons, 80 * 2 * 2, "weapon points vs two per player of an 80 player match, twice over");
            Assert.LessOrEqual(counts.Sum(), spec.Build.MaxLootSpawns);
        }
    }
}
