using System;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #249 T8 / G2: shops (2-3 storeys), villas (3-4) and apartments (5) with a straight flight per storey, alternating sides, and a
    // roof deck with a stair house. Every plan must pass the same building rules as the older kinds, plus a walkable roof.
    public class BlockBuildingTests
    {
        static readonly BuildingKind[] Kinds = { BuildingKind.Shop, BuildingKind.Villa, BuildingKind.Apartment };
        const int Seeds = 30;

        static (float w, float d)[] Sizes(BuildingKind kind)
        {
            Buildings.FootprintLimits(kind, out float w0, out float d0, out float w1, out float d1);
            return new[] { (w0, d0), ((w0 + w1) / 2f, (d0 + d1) / 2f), (w1, d1) };
        }

        [TestCase(BuildingKind.Shop)]
        [TestCase(BuildingKind.Villa)]
        [TestCase(BuildingKind.Apartment)]
        public void EverySeedAndSize_SatisfiesEveryRule_IncludingAWalkableRoof(BuildingKind kind)
        {
            foreach (var (w, d) in Sizes(kind))
                for (int seed = 0; seed < Seeds; seed++)
                {
                    var plan = Buildings.Generate(kind, w, d, seed);
                    var violations = BuildingRules.Validate(plan);
                    Assert.IsEmpty(violations, $"{kind} {w}x{d} seed {seed}: {string.Join("; ", violations.Select(v => v.ToString()))}");
                }
        }

        [TestCase(BuildingKind.Shop)]
        [TestCase(BuildingKind.Villa)]
        [TestCase(BuildingKind.Apartment)]
        public void Storeys_follow_the_design_and_every_floor_has_two_rooms_and_a_flight_up(BuildingKind kind)
        {
            BuildingKinds.Storeys(kind, out int min, out int max);
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int seed = 0; seed < Seeds; seed++)
            {
                var plan = Buildings.Generate(kind, Sizes(kind)[1].w, Sizes(kind)[1].d, seed);
                Assert.That(plan.Storeys, Is.InRange(min, max));
                seen.Add(plan.Storeys);
                Assert.AreEqual(plan.Storeys * 2, plan.Rooms.Count, "two rooms on every floor");
                Assert.AreEqual(plan.Storeys, plan.Stairs.Count, "a flight up from every floor, the last one to the roof");
                Assert.AreEqual(kind, plan.Kind);
            }
            Assert.AreEqual(max - min + 1, seen.Count, "every storey count in the range shows up");
        }

        [Test]
        public void Flights_alternate_sides_so_no_two_stand_above_each_other()
        {
            var plan = Buildings.Generate(BuildingKind.Apartment, 18f, 13f, 3);
            for (int k = 1; k < plan.Stairs.Count; k++)
                Assert.Greater(Math.Abs(plan.Stairs[k].X - plan.Stairs[k - 1].X), 3f, $"flights {k - 1} and {k}");
        }

        [Test]
        public void An_apartment_is_not_a_tall_house_in_the_budgets()
        {
            var plan = Buildings.Generate(BuildingKind.Apartment, 18f, 13f, 5);
            Assert.Greater(plan.Pieces.Count, BuildingRules.MaxPieces(BuildingKind.House), "more than a house is allowed");
            Assert.LessOrEqual(plan.Pieces.Count, BuildingRules.MaxPieces(BuildingKind.Apartment));
            Assert.LessOrEqual(plan.Decor.Count, BuildingRules.MaxDecor(BuildingKind.Apartment));
            Assert.Greater(plan.Height, 15f, "five storeys");
        }

        [Test]
        public void SameInputs_GiveTheSamePlan()
        {
            foreach (var kind in Kinds)
            {
                var a = Buildings.Generate(kind, 14f, 11f, 9);
                var b = Buildings.Generate(kind, 14f, 11f, 9);
                Assert.AreEqual(a.Pieces.Count, b.Pieces.Count);
                for (int i = 0; i < a.Pieces.Count; i++) Assert.AreEqual(a.Pieces[i], b.Pieces[i], a.Pieces[i].Name);
                CollectionAssert.AreEqual(a.LootPoints, b.LootPoints);
            }
        }

        [Test]
        public void Loot_is_spread_over_every_floor()
        {
            var plan = Buildings.Generate(BuildingKind.Villa, 14f, 12f, 2);
            for (int s = 0; s < plan.Storeys; s++)
            {
                float y = Buildings.FloorTop + s * Buildings.StoreyHeight;
                Assert.IsTrue(plan.LootPoints.Any(p => p.Y > y - 0.1f && p.Y < y + 1.6f), $"storey {s} holds loot");
            }
        }
    }
}
