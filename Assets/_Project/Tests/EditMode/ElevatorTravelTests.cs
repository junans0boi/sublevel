using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-076 (#76): riding an elevator from one region to another during the match, tied to the lockdown.
    public class ElevatorTravelTests
    {
        static ZoneState[] States(params ZoneState[] s) => s;

        const ZoneState S = ZoneState.Safe, W = ZoneState.Warning, C = ZoneState.Closed;

        [Test]
        public void Defaults_MatchTheDecision()
        {
            var r = new DeploymentRules();
            Assert.AreEqual(8f, r.TravelSeconds);
            Assert.AreEqual(60f, r.TravelCooldownSeconds);
            Assert.AreEqual(0.8f, r.TravelHoldSeconds);
        }

        [Test]
        public void Elevators_RunInSafeAndWarningRegions_AndStopOnceConfirmed()
        {
            Assert.IsTrue(ElevatorTravel.Runs(S));
            Assert.IsTrue(ElevatorTravel.Runs(W));
            Assert.IsFalse(ElevatorTravel.Runs(C));
        }

        [Test]
        public void Destinations_AreOtherSafeRegionsOnly()
        {
            var list = new List<int>();
            ElevatorTravel.Destinations(0, States(W, S, C, S, W), list);
            CollectionAssert.AreEqual(new[] { 1, 3 }, list);
        }

        [Test]
        public void Destinations_NoneFromAStoppedRegion()
        {
            var list = new List<int> { 99 };
            ElevatorTravel.Destinations(2, States(S, S, C, S), list);
            Assert.IsEmpty(list);
        }

        [Test]
        public void Destinations_NoneWhenOnlyTheFinalRegionIsLeft()
        {
            var list = new List<int>();
            ElevatorTravel.Destinations(3, States(C, C, C, S), list);
            Assert.IsEmpty(list);
        }

        [Test]
        public void CanRide_ChecksBothEndsAndTheSameRegion()
        {
            var s = States(S, W, C, S);
            Assert.IsTrue(ElevatorTravel.CanRide(0, 3, s));
            Assert.IsTrue(ElevatorTravel.CanRide(1, 0, s), "warning region: still running, to get out");
            Assert.IsFalse(ElevatorTravel.CanRide(0, 1, s), "into a warning region");
            Assert.IsFalse(ElevatorTravel.CanRide(2, 0, s), "out of a locked region");
            Assert.IsFalse(ElevatorTravel.CanRide(0, 0, s), "same region");
            Assert.IsFalse(ElevatorTravel.CanRide(0, 7, s), "no such region");
            Assert.IsFalse(ElevatorTravel.CanRide(-1, 0, s));
        }

        [Test]
        public void Arrival_KeepsTheDestination_WhileItIsSafe()
        {
            Assert.AreEqual(2, ElevatorTravel.ArrivalRegion(0, 2, States(C, S, S, S)));
        }

        [Test]
        public void Arrival_DestinationAnnouncedDuringTheRide_GoesToTheNextSafeRegion()
        {
            // Rode 0 -> 1; region 1 went to warning on the way: the first other safe region (not the start) instead.
            Assert.AreEqual(3, ElevatorTravel.ArrivalRegion(0, 1, States(S, W, C, S)));
            Assert.AreEqual(2, ElevatorTravel.ArrivalRegion(0, 1, States(S, C, S, S)));
        }

        [Test]
        public void Arrival_OnlyTheStartIsSafe_GoesBackThere()
        {
            Assert.AreEqual(0, ElevatorTravel.ArrivalRegion(0, 1, States(S, W, C, C)));
        }

        [Test]
        public void Arrival_NothingSafe_KeepsTheDestination()
        {
            Assert.AreEqual(1, ElevatorTravel.ArrivalRegion(0, 1, States(C, W, C, C)));
        }

        [Test]
        public void Cooldown_CountsFromTheArrival()
        {
            var r = new DeploymentRules();
            Assert.AreEqual(0f, ElevatorTravel.CooldownLeft(100.0, double.NegativeInfinity, r), "never ridden");
            Assert.AreEqual(60f, ElevatorTravel.CooldownLeft(100.0, 100.0, r), 1e-4f);
            Assert.AreEqual(15f, ElevatorTravel.CooldownLeft(145.0, 100.0, r), 1e-4f);
            Assert.AreEqual(0f, ElevatorTravel.CooldownLeft(160.0, 100.0, r));
            Assert.AreEqual(0f, ElevatorTravel.CooldownLeft(500.0, 100.0, r));
        }

        [Test]
        public void ArrivalStation_IsOneOfTheRegionsStations_AndCoversThemAll()
        {
            var regionOf = new[] { 0, 0, 0, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3 };
            var seen = new HashSet<int>();
            var rng = new System.Random(5);
            for (int i = 0; i < 200; i++)
            {
                int station = ElevatorTravel.ArrivalStation(2, regionOf, rng);
                Assert.AreEqual(2, regionOf[station]);
                seen.Add(station);
            }
            Assert.AreEqual(4, seen.Count);
            Assert.AreEqual(-1, ElevatorTravel.ArrivalStation(9, regionOf, rng));
        }

        [Test]
        public void NextDestination_CyclesThroughTheChoices()
        {
            var choices = new List<int> { 1, 3 };
            Assert.AreEqual(1, ElevatorTravel.NextDestination(-1, choices));
            Assert.AreEqual(3, ElevatorTravel.NextDestination(1, choices));
            Assert.AreEqual(1, ElevatorTravel.NextDestination(3, choices));
            Assert.AreEqual(1, ElevatorTravel.NextDestination(2, choices), "a choice that went away: start over");
            Assert.AreEqual(-1, ElevatorTravel.NextDestination(1, new List<int>()));
        }

        [Test]
        public void Selected_StaysWhileValid_ElseFirstChoice()
        {
            var choices = new List<int> { 1, 3 };
            Assert.AreEqual(3, ElevatorTravel.KeepSelection(3, choices));
            Assert.AreEqual(1, ElevatorTravel.KeepSelection(2, choices));
            Assert.AreEqual(1, ElevatorTravel.KeepSelection(-1, choices));
            Assert.AreEqual(-1, ElevatorTravel.KeepSelection(3, new List<int>()));
        }
    
        // ---- Bots (ADR-079) ----

        [Test]
        public void Bot_RidesOutOfAnAnnouncedRegion_WhenTheStationIsNearer()
        {
            var s = States(W, S, S, C);
            Assert.IsTrue(ElevatorTravel.BotShouldRide(0, s, 30f, 200f, 0f));
            Assert.IsTrue(ElevatorTravel.BotShouldRide(0, s, 30f, -1f, 0f), "no way out on foot");
            Assert.IsFalse(ElevatorTravel.BotShouldRide(0, s, 250f, 200f, 0f), "the pass is nearer");
            Assert.IsFalse(ElevatorTravel.BotShouldRide(0, s, 30f, 200f, 5f), "cooling down");
            Assert.IsFalse(ElevatorTravel.BotShouldRide(0, s, -1f, 200f, 0f), "no free station");
        }

        [Test]
        public void Bot_DoesNotRide_FromASafeOrLockedRegion_OrWithNowhereToGo()
        {
            Assert.IsFalse(ElevatorTravel.BotShouldRide(1, States(W, S, S), 10f, 200f, 0f), "safe: no reason");
            Assert.IsFalse(ElevatorTravel.BotShouldRide(0, States(C, S, S), 10f, 200f, 0f), "locked: elevators off");
            Assert.IsFalse(ElevatorTravel.BotShouldRide(0, States(W, W, C), 10f, 200f, 0f), "no safe region");
            Assert.IsFalse(ElevatorTravel.BotShouldRide(5, States(W, S), 10f, 200f, 0f));
        }

        [Test]
        public void Bot_Destination_PrefersTheNamedFinalRegion()
        {
            var s = States(W, S, S, S);
            Assert.AreEqual(3, ElevatorTravel.BotDestination(0, s, 3));
            Assert.AreEqual(1, ElevatorTravel.BotDestination(0, s, -1), "not named yet: the first safe region");
            Assert.AreEqual(1, ElevatorTravel.BotDestination(0, States(W, S, S, W), 3), "final not reachable");
            Assert.AreEqual(-1, ElevatorTravel.BotDestination(0, States(W, C, C, C), -1));
        }

        // ---- Ride cycle (ADR-079) ----

        [Test]
        public void Cycle_LastsTheTravelTime_DoorsCloseMoveOpen()
        {
            var r = new DeploymentRules();
            float d = ElevatorCycle.DoorSeconds(r), m = ElevatorCycle.MoveSeconds(r);
            Assert.AreEqual(r.TravelSeconds, ElevatorCycle.RideSeconds(r), 1e-4f);
            Assert.AreEqual(d + m, ElevatorCycle.SwitchAt(r), 1e-4f);
            Assert.AreEqual(0f, ElevatorCycle.DoorClosed01(0.0, r));
            Assert.AreEqual(0.5f, ElevatorCycle.DoorClosed01(d / 2, r), 1e-3f);
            Assert.AreEqual(1f, ElevatorCycle.DoorClosed01(d + m / 2, r));
            Assert.AreEqual(0.5f, ElevatorCycle.DoorClosed01(d + m + d / 2, r), 1e-3f);
            Assert.AreEqual(0f, ElevatorCycle.DoorClosed01(ElevatorCycle.Length(r) + 0.1, r));
            Assert.IsFalse(ElevatorCycle.Moving(d / 2, r));
            Assert.IsTrue(ElevatorCycle.Moving(d + m / 2, r));
        }

        [Test]
        public void Cycle_DepartGoesDown_ArriveComesUp_OnlyBehindShutDoors()
        {
            var r = new DeploymentRules();
            float d = ElevatorCycle.DoorSeconds(r), m = ElevatorCycle.MoveSeconds(r), depth = r.CarStartDepth;
            Assert.AreEqual(0f, ElevatorCycle.CarOffset(ElevatorCycleKind.Depart, d * 0.5, r), "still up while the doors close");
            Assert.AreEqual(depth, ElevatorCycle.CarOffset(ElevatorCycleKind.Depart, d + m * 0.999, r), 0.05f);
            Assert.AreEqual(depth, ElevatorCycle.CarOffset(ElevatorCycleKind.Arrive, d + 0.0001, r), 0.05f);
            Assert.AreEqual(0f, ElevatorCycle.CarOffset(ElevatorCycleKind.Arrive, d + m * 0.999, r), 0.05f);
            Assert.AreEqual(0f, ElevatorCycle.CarOffset(ElevatorCycleKind.Arrive, d + m + 0.1, r), "up while the doors open");
            float half = ElevatorCycle.CarOffset(ElevatorCycleKind.Depart, d + m * 0.5, r);
            Assert.Greater(half, 0.3f * depth);
            Assert.Less(half, 0.7f * depth);
            // Jumps (the empty departure car back to the top, the arrival car to the bottom) happen with the doors shut.
            Assert.AreEqual(1f, ElevatorCycle.DoorClosed01(d, r), 1e-4f);
            Assert.AreEqual(1f, ElevatorCycle.DoorClosed01(d + m - 1e-4, r), 1e-3f);
        }

        [Test]
        public void Rider_StaysDown_BetweenTheCars()
        {
            var r = new DeploymentRules();
            float d = ElevatorCycle.DoorSeconds(r), m = ElevatorCycle.MoveSeconds(r), depth = r.CarStartDepth;
            Assert.AreEqual(0f, ElevatorCycle.RiderOffset(false, d * 0.5, r));
            Assert.AreEqual(depth, ElevatorCycle.RiderOffset(false, d + m + 0.5, r), "waiting at the bottom for the hand-over");
            Assert.AreEqual(depth, ElevatorCycle.RiderOffset(true, d * 0.5, r), "the arrival doors are still closing");
            Assert.AreEqual(0f, ElevatorCycle.RiderOffset(true, d + m + 0.1, r));
        }

        [Test]
        public void Cycle_ShortTravel_StillMovesForAMoment()
        {
            var r = new DeploymentRules { TravelSeconds = 1f };
            Assert.AreEqual(0.3f, ElevatorCycle.MoveSeconds(r), 1e-4f);
            Assert.Greater(ElevatorCycle.RideSeconds(r), 1f);
        }
}
}
