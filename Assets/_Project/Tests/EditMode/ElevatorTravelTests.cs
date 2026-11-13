using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-076 (#76): riding an elevator from one region to another during the match, tied to the lockdown.
    public class ElevatorTravelTests
    {
        static ZoneState[] States(params ZoneState[] s) => s;

        const ZoneState S = ZoneState.Safe, W = ZoneState.Warning, L = ZoneState.Lockdown, B = ZoneState.Bombardment, C = ZoneState.Closed;

        [Test]
        public void Defaults_MatchTheDecision()
        {
            var r = new DeploymentRules();
            Assert.AreEqual(8f, r.TravelSeconds);
            Assert.AreEqual(60f, r.TravelCooldownSeconds);
            Assert.AreEqual(0.8f, r.TravelHoldSeconds);
        }

        [Test]
        public void Elevators_RunInSafeAndWarningRegions_AndStopFromLockdownOn()
        {
            Assert.IsTrue(ElevatorTravel.Runs(S));
            Assert.IsTrue(ElevatorTravel.Runs(W));
            Assert.IsFalse(ElevatorTravel.Runs(L));
            Assert.IsFalse(ElevatorTravel.Runs(B));
            Assert.IsFalse(ElevatorTravel.Runs(C));
        }

        [Test]
        public void Destinations_AreOtherSafeRegionsOnly()
        {
            var list = new List<int>();
            ElevatorTravel.Destinations(0, States(W, S, L, S, W), list);
            CollectionAssert.AreEqual(new[] { 1, 3 }, list);
        }

        [Test]
        public void Destinations_NoneFromAStoppedRegion()
        {
            var list = new List<int> { 99 };
            ElevatorTravel.Destinations(2, States(S, S, L, S), list);
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
            var s = States(S, W, L, S);
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
            Assert.AreEqual(2, ElevatorTravel.ArrivalRegion(0, 2, States(L, S, S, S)));
        }

        [Test]
        public void Arrival_DestinationAnnouncedDuringTheRide_GoesToTheNextSafeRegion()
        {
            // Rode 0 -> 1; region 1 went to warning on the way: the first other safe region (not the start) instead.
            Assert.AreEqual(3, ElevatorTravel.ArrivalRegion(0, 1, States(S, W, C, S)));
            Assert.AreEqual(2, ElevatorTravel.ArrivalRegion(0, 1, States(S, L, S, S)));
        }

        [Test]
        public void Arrival_OnlyTheStartIsSafe_GoesBackThere()
        {
            Assert.AreEqual(0, ElevatorTravel.ArrivalRegion(0, 1, States(S, W, C, C)));
        }

        [Test]
        public void Arrival_NothingSafe_KeepsTheDestination()
        {
            Assert.AreEqual(1, ElevatorTravel.ArrivalRegion(0, 1, States(L, W, C, C)));
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
    }
}
