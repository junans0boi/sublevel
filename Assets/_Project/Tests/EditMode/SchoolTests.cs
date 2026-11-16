using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #249 T8: the school (2-3 storeys): stair halls at both ends, a corridor, classrooms on both sides, flat closed roof.
    public class SchoolTests
    {
        static (float w, float d)[] Sizes()
        {
            Buildings.FootprintLimits(BuildingKind.School, out float w0, out float d0, out float w1, out float d1);
            return new[] { (w0, d0), ((w0 + w1) / 2f, (d0 + d1) / 2f), (w1, d1) };
        }

        [Test]
        public void EverySeedAndSize_SatisfiesEveryRule()
        {
            foreach (var (w, d) in Sizes())
                for (int seed = 0; seed < 30; seed++)
                {
                    var plan = Buildings.Generate(BuildingKind.School, w, d, seed);
                    var violations = BuildingRules.Validate(plan);
                    Assert.IsEmpty(violations, $"School {w}x{d} seed {seed}: {string.Join("; ", violations.Select(v => v.ToString()))}");
                }
        }

        [Test]
        public void A_school_has_a_corridor_two_halls_and_classrooms_on_every_floor()
        {
            var plan = Buildings.Generate(BuildingKind.School, 30f, 12f, 4);
            Assert.That(plan.Storeys, Is.InRange(2, 3));
            for (int s = 0; s < plan.Storeys; s++)
            {
                var rooms = plan.Rooms.Where(r => r.Storey == s).ToList();
                Assert.AreEqual(1, rooms.Count(r => r.Name.EndsWith("Corridor")), "one corridor");
                Assert.AreEqual(2, rooms.Count(r => r.Name.Contains("Hall")), "a stair hall at each end");
                Assert.GreaterOrEqual(rooms.Count(r => r.Name.Contains("Class")), 6, "classrooms on both sides");
            }
            Assert.AreEqual(plan.Storeys - 1, plan.Stairs.Count, "no flight on to the roof");
        }

        [Test]
        public void Every_classroom_opens_on_the_corridor()
        {
            var plan = Buildings.Generate(BuildingKind.School, 30f, 12f, 4);
            foreach (var room in plan.Rooms.Where(r => r.Name.Contains("Class")))
                Assert.IsTrue(plan.Doors.Any(d => !d.Exterior && (d.Room == room.Name || d.OtherRoom == room.Name) && (d.Room.EndsWith("Corridor") || d.OtherRoom.EndsWith("Corridor"))), room.Name);
        }

        [Test]
        public void The_entrance_and_the_second_door_are_on_different_sides()
        {
            var plan = Buildings.Generate(BuildingKind.School, 30f, 12f, 4);
            var doors = plan.Doors.Where(d => d.Exterior).ToList();
            Assert.AreEqual(2, doors.Count);
            Assert.AreNotEqual(System.Math.Sign(doors[0].Z), System.Math.Sign(doors[1].Z));
        }

        [Test]
        public void SameInputs_GiveTheSamePlan()
        {
            var a = Buildings.Generate(BuildingKind.School, 28f, 12f, 8);
            var b = Buildings.Generate(BuildingKind.School, 28f, 12f, 8);
            Assert.AreEqual(a.Pieces.Count, b.Pieces.Count);
            CollectionAssert.AreEqual(a.LootPoints, b.LootPoints);
        }
    }
}
