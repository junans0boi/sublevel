using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #508 (ADR-562): the race start config — laps, bot count, each racer's car and paint — and what a race is without one.
    public class RaceStartTests
    {
        static RaceEvent.CourseInfo Snake => RaceEvent.ById("snake");
        static RaceEvent.CourseInfo Sprint => RaceEvent.ById("pass_up");

        [Test]
        public void NoConfig_IsTodaysRace()
        {
            Assert.AreEqual(Snake.Laps, RaceEvent.LapsOf(null, Snake));
            Assert.AreEqual(RaceEvent.Racers, RaceEvent.FieldSize(null, 1, RaceEvent.GridSlots));
            Assert.AreEqual(RaceEvent.Racers, RaceEvent.FieldSize(new RaceStart { CourseId = "snake" }, 3, RaceEvent.GridSlots), "bots unset: fill to Racers");
            Assert.AreEqual(Snake.Laps, RaceEvent.LapsOf(new RaceStart { CourseId = "snake" }, Snake), "laps unset: the course's");
            Assert.IsNull(RaceEvent.EntryFor(null, null, true));
        }

        [Test]
        public void Laps_FromTheConfig_ButASprintIsOneRun()
        {
            var s = new RaceStart { CourseId = "snake", Laps = 3 };
            Assert.AreEqual(3, RaceEvent.LapsOf(s, Snake));
            Assert.AreEqual(Sprint.Laps, RaceEvent.LapsOf(new RaceStart { CourseId = "pass_up", Laps = 3 }, Sprint), "an open course has no laps to choose");
        }

        [Test]
        public void Field_IsPeoplePlusBots_HeldToTheGrid()
        {
            Assert.AreEqual(5, RaceEvent.FieldSize(new RaceStart { Bots = 4 }, 1, RaceEvent.GridSlots));
            Assert.AreEqual(2, RaceEvent.FieldSize(new RaceStart { Bots = 0 }, 2, RaceEvent.GridSlots), "no bots: just the people");
            Assert.AreEqual(RaceEvent.GridSlots, RaceEvent.FieldSize(new RaceStart { Bots = 20 }, 3, RaceEvent.GridSlots));
        }

        [Test]
        public void Entry_ByTokenOrLocal()
        {
            var s = new RaceStart
            {
                Racers = new List<RaceStart.Entry>
                {
                    new RaceStart.Entry { Car = "Shvan92", Paint = 2 },
                    new RaceStart.Entry { Token = "abc", Car = "GoblinV12", Paint = 1 },
                },
            };
            Assert.AreEqual("Shvan92", RaceEvent.EntryFor(s, null, true)?.Car, "the host's own entry has no token");
            Assert.AreEqual("GoblinV12", RaceEvent.EntryFor(s, "abc", false)?.Car);
            Assert.IsNull(RaceEvent.EntryFor(s, "zzz", false), "unknown session: today's slot car");
            Assert.IsNull(RaceEvent.EntryFor(s, null, false), "a remote client with no token is not the local entry");
        }
    }
}
