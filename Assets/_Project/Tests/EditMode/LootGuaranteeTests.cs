using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-210: every region offers at least one jerrycan (vehicle fuel balance, ADR-200 / ADR-221).
    public class LootGuaranteeTests
    {
        static LootTable Table() => new LootTable
        {
            Entries = new[]
            {
                new LootEntry { Kind = LootKind.Weapon, Weight = 10 }, new LootEntry { Kind = LootKind.Ammo, Weight = 20 }, new LootEntry { Kind = LootKind.Bandage, Weight = 14 },
                new LootEntry { Kind = LootKind.Jerrycan, Amount = 1, Weight = 3 },
            },
        };

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(20261116)]
        public void Every_region_gets_at_least_one_jerrycan_whatever_the_roll(int seed)
        {
            var table = Table();
            var region = Enumerable.Range(0, 900).Select(i => i % 6).ToArray();      // 150 points in each of 6 regions
            var plan = table.Plan(region.Length, seed);
            LootGuarantee.Apply(plan, region, 6, table, LootKind.Jerrycan, 1, seed);
            for (int r = 0; r < 6; r++)
                Assert.GreaterOrEqual(Enumerable.Range(0, plan.Length).Count(i => region[i] == r && plan[i].Kind == LootKind.Jerrycan), 1, "region " + r);
        }

        [Test]
        public void A_region_that_already_has_enough_is_left_alone_and_the_weapon_point_stays()
        {
            var table = Table();
            var region = Enumerable.Range(0, 900).Select(i => i % 6).ToArray();
            var plan = table.Plan(region.Length, 5);
            var before = (LootEntry[])plan.Clone();
            int turned = LootGuarantee.Apply(plan, region, 6, table, LootKind.Jerrycan, 1, 5);
            Assert.AreEqual(LootKind.Weapon, plan[0].Kind, "the farming point keeps its weapon");
            Assert.AreEqual(turned, Enumerable.Range(0, plan.Length).Count(i => plan[i] != before[i]));
            Assert.LessOrEqual(turned, 6, "at most one per region is added when the roll gave none");
        }

        [Test]
        public void Same_seed_same_result_and_a_region_with_no_points_is_skipped()
        {
            var table = Table();
            var region = Enumerable.Range(0, 300).Select(i => i % 3).ToArray();        // regions 3..5 have no points
            var a = table.Plan(300, 9); var b = table.Plan(300, 9);
            Assert.AreEqual(LootGuarantee.Apply(a, region, 6, table, LootKind.Jerrycan, 2, 9), LootGuarantee.Apply(b, region, 6, table, LootKind.Jerrycan, 2, 9));
            CollectionAssert.AreEqual(a, b);
            for (int r = 0; r < 3; r++) Assert.GreaterOrEqual(Enumerable.Range(0, 300).Count(i => region[i] == r && a[i].Kind == LootKind.Jerrycan), 2);
        }
    }
}
