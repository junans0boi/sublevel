using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #249 T8 / WP4a: the lots of one town (docs/bible/data/towns.json): an avenue, rows of lots on both sides, back alleys behind them.
    public class TownLayoutTests
    {
        static readonly (BuildingKind, int)[] Capital =
        {
            (BuildingKind.Shop, 36), (BuildingKind.House, 21), (BuildingKind.Stall, 8), (BuildingKind.Villa, 6), (BuildingKind.Apartment, 4),
            (BuildingKind.Barracks, 4), (BuildingKind.School, 3), (BuildingKind.WatchTower, 2), (BuildingKind.FactoryHall, 2),
            (BuildingKind.Warehouse, 2), (BuildingKind.Shed, 1), (BuildingKind.Market, 1),
        };

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(20261116)]
        public void The_capital_gets_all_90_buildings_inside_its_box_without_overlap(int seed)
        {
            var r = TownLayout.Place(440f, 300f, 12f, Capital, seed);
            Assert.AreEqual(90, r.Requested);
            Assert.AreEqual(90, r.Placed);
            foreach (var l in r.Lots)
            {
                Assert.LessOrEqual(Math.Abs(l.X) + l.Width / 2f, 220f + 0.01f, l.Kind.ToString());
                Assert.LessOrEqual(Math.Abs(l.Z) + l.Depth / 2f, 150f + 0.01f, l.Kind.ToString());
                Buildings.FootprintLimits(l.Kind, out float minW, out float minD, out float maxW, out float maxD);
                Assert.That(l.Width, Is.InRange(minW - 0.01f, maxW + 0.01f), l.Kind + " width");
                Assert.That(l.Depth, Is.InRange(minD - 0.01f, maxD + 0.01f), l.Kind + " depth");
            }
            for (int i = 0; i < r.Lots.Count; i++)
                for (int j = i + 1; j < r.Lots.Count; j++)
                {
                    var a = r.Lots[i]; var b = r.Lots[j];
                    bool overlap = Math.Abs(a.X - b.X) < (a.Width + b.Width) / 2f + 0.2f && Math.Abs(a.Z - b.Z) < (a.Depth + b.Depth) / 2f + 0.2f;
                    Assert.IsFalse(overlap, $"{a.Kind} and {b.Kind} overlap");
                }
        }

        [Test]
        public void Every_front_looks_at_the_avenue_or_a_back_alley()
        {
            var r = TownLayout.Place(440f, 300f, 12f, Capital, 7);
            foreach (var l in r.Lots)
            {
                // the middle of the front edge, a sidewalk out, lies on a street (avenue, parallel street or cross street)
                float fz = l.Z + (l.YawDeg == 180f ? -1f : 1f) * (l.Depth / 2f + TownLayout.Sidewalk + 0.5f);
                Assert.IsTrue(r.Streets.Any(s => l.X >= s.X0 - 0.01f && l.X <= s.X1 + 0.01f && fz >= s.Z0 - 0.01f && fz <= s.Z1 + 0.01f), $"{l.Kind} at {l.X:F0}, {l.Z:F0}");
            }
        }

        [Test]
        public void Both_sides_of_the_avenue_are_built_and_the_big_kinds_stand_behind()
        {
            var r = TownLayout.Place(440f, 300f, 12f, Capital, 3);
            int north = r.Lots.Count(l => l.Z > 0), south = r.Lots.Count(l => l.Z < 0);
            Assert.That(Math.Abs(north - south), Is.LessThanOrEqualTo(20), "no side is left empty");
            foreach (var l in r.Lots.Where(l => l.Kind == BuildingKind.Shop)) Assert.Less(l.Row, 2, "shops face the avenue");
            foreach (var l in r.Lots.Where(l => l.Kind == BuildingKind.Barracks || l.Kind == BuildingKind.FactoryHall)) Assert.GreaterOrEqual(l.Row, 2, "barracks and factories stand behind");
        }

        [Test]
        public void The_avenue_is_one_straight_street_across_the_town()
        {
            var r = TownLayout.Place(440f, 300f, 12f, Capital, 3);
            var avenue = r.Streets.Single(s => s.Avenue);
            Assert.AreEqual(-220f, avenue.X0); Assert.AreEqual(220f, avenue.X1);
            Assert.AreEqual(12f, avenue.Z1 - avenue.Z0, 0.001f);
            Assert.IsTrue(r.Lots.All(l => l.Z - l.Depth / 2f >= 6f - 0.01f || l.Z + l.Depth / 2f <= -6f + 0.01f), "nothing stands in the avenue");
            Assert.Greater(r.Streets.Count(s => !s.Avenue), 4, "parallel and cross streets make blocks");
        }

        [Test]
        public void SameInputs_GiveTheSameTown_AndAnotherSeedAnotherOne()
        {
            var a = TownLayout.Place(440f, 300f, 12f, Capital, 5);
            var b = TownLayout.Place(440f, 300f, 12f, Capital, 5);
            var c = TownLayout.Place(440f, 300f, 12f, Capital, 6);
            CollectionAssert.AreEqual(a.Lots, b.Lots);
            CollectionAssert.AreNotEqual(a.Lots, c.Lots);
        }

        [Test]
        public void Small_towns_and_hamlets_fit_their_boxes()
        {
            var m1 = TownLayout.Place(190f, 130f, 8f, new[] { (BuildingKind.Shop, 7), (BuildingKind.House, 6), (BuildingKind.School, 3) }, 5);
            Assert.AreEqual(m1.Requested, m1.Placed);
            var hamlet = TownLayout.Place(160f, 50f, 6f, new[] { (BuildingKind.House, 6) }, 5);
            Assert.AreEqual(hamlet.Requested, hamlet.Placed);
        }

        [Test]
        public void A_box_too_shallow_for_two_rows_places_nothing_instead_of_overlapping()
        {
            var r = TownLayout.Place(100f, 12f, 6f, new[] { (BuildingKind.House, 3) }, 1);
            Assert.AreEqual(0, r.Placed);
            Assert.AreEqual(3, r.Requested);
        }
    }
}
