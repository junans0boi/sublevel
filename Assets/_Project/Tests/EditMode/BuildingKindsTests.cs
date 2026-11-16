using System;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #244 T3 / G1-G2: twelve building kinds (plus the elevator station). Until each gets its own generator (T8) a new kind is built
    // with the shape of its family, so nothing about the existing five changes.
    public class BuildingKindsTests
    {
        static readonly BuildingKind[] All = (BuildingKind[])Enum.GetValues(typeof(BuildingKind));

        [Test]
        public void There_are_twelve_kinds_and_the_station_and_the_old_values_keep_their_numbers()
        {
            Assert.AreEqual(13, All.Length);
            Assert.AreEqual(0, (int)BuildingKind.House);
            Assert.AreEqual(1, (int)BuildingKind.Warehouse);
            Assert.AreEqual(2, (int)BuildingKind.FactoryHall);
            Assert.AreEqual(3, (int)BuildingKind.Shed);
            Assert.AreEqual(4, (int)BuildingKind.ElevatorStation);
        }

        [TestCase(BuildingKind.House, BuildingKind.House)]
        [TestCase(BuildingKind.ElevatorStation, BuildingKind.ElevatorStation)]
        [TestCase(BuildingKind.Shop, BuildingKind.House)]
        [TestCase(BuildingKind.Villa, BuildingKind.House)]
        [TestCase(BuildingKind.Apartment, BuildingKind.House)]
        [TestCase(BuildingKind.School, BuildingKind.Warehouse)]
        [TestCase(BuildingKind.Barracks, BuildingKind.Warehouse)]
        [TestCase(BuildingKind.Market, BuildingKind.Warehouse)]
        [TestCase(BuildingKind.Stall, BuildingKind.Shed)]
        [TestCase(BuildingKind.WatchTower, BuildingKind.Shed)]
        public void A_kind_is_built_with_the_shape_of_its_family(BuildingKind kind, BuildingKind family) =>
            Assert.AreEqual(family, BuildingKinds.Family(kind));

        [Test]
        public void Every_family_is_one_of_the_five_generated_shapes()
        {
            var shapes = new[] { BuildingKind.House, BuildingKind.Warehouse, BuildingKind.FactoryHall, BuildingKind.Shed, BuildingKind.ElevatorStation };
            foreach (var kind in All) CollectionAssert.Contains(shapes, BuildingKinds.Family(kind), kind.ToString());
        }

        // G2: storeys per kind; the roof can be entered only on shops, villas and apartments.
        [TestCase(BuildingKind.House, 1, 2, false)]
        [TestCase(BuildingKind.Shop, 2, 3, true)]
        [TestCase(BuildingKind.Villa, 3, 4, true)]
        [TestCase(BuildingKind.Apartment, 5, 5, true)]
        [TestCase(BuildingKind.School, 2, 3, false)]
        [TestCase(BuildingKind.Barracks, 1, 2, false)]
        [TestCase(BuildingKind.FactoryHall, 1, 1, false)]
        [TestCase(BuildingKind.Warehouse, 1, 1, false)]
        [TestCase(BuildingKind.Shed, 1, 1, false)]
        [TestCase(BuildingKind.Stall, 1, 1, false)]
        [TestCase(BuildingKind.Market, 1, 1, false)]
        [TestCase(BuildingKind.WatchTower, 1, 2, false)]
        public void Storeys_and_roof_access_follow_the_design(BuildingKind kind, int min, int max, bool roof)
        {
            BuildingKinds.Storeys(kind, out int lo, out int hi);
            Assert.AreEqual(min, lo, kind + " min");
            Assert.AreEqual(max, hi, kind + " max");
            Assert.AreEqual(roof, BuildingKinds.RoofAccess(kind), kind + " roof");
        }

        [Test]
        public void Every_kind_has_footprint_limits_and_generates_a_plan_that_keeps_the_rules()
        {
            foreach (var kind in All.Where(k => k != BuildingKind.ElevatorStation))
            {
                Buildings.FootprintLimits(kind, out float minW, out float minD, out float maxW, out float maxD);
                Assert.Greater(maxW, 0f, kind.ToString());
                for (int seed = 0; seed < 4; seed++)
                {
                    var plan = Buildings.Generate(kind, minW, minD, seed);
                    Assert.AreEqual(kind, plan.Kind, kind.ToString());
                    var violations = BuildingRules.Validate(plan);
                    Assert.IsEmpty(violations, $"{kind} seed {seed}: {string.Join("; ", violations)}");
                }
            }
        }

        [Test]
        public void A_new_kind_sounds_like_its_family()
        {
            foreach (var kind in All)
                foreach (SurfaceKind piece in Enum.GetValues(typeof(SurfaceKind)))
                    Assert.AreEqual(SoundRules.BuildingSurface(BuildingKinds.Family(kind), piece), SoundRules.BuildingSurface(kind, piece), $"{kind}/{piece}");
        }
    }
}
