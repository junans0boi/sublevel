using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // Keys and the key elevator (ADR-170, S6): placement, the use sequence, void, cooldown, recharge.
    public class KeyElevatorTests
    {
        static readonly PollutionRules R = new PollutionRules();
        static readonly PollutionState[] Open = { PollutionState.Safe, PollutionState.Safe, PollutionState.Warning, PollutionState.Contaminated };

        [Test]
        public void KeyPlan_SixToEightKeys_EachCountComes_AndSupplyCratesAreOneOrTwo()
        {
            var order = new[] { 0, 5, 2, 1, 4, 3 };
            var counts = new int[9];
            for (int seed = 0; seed < 600; seed++)
            {
                var keys = KeyPlan.Plan(seed, order, null, R);
                Assert.That(keys.Length, Is.InRange(6, 8));
                counts[keys.Length]++;
                int crates = keys.Count(k => k.InSupplyCrate);
                Assert.That(crates, Is.InRange(1, 2));
                foreach (var k in keys) Assert.That(k.Region, Is.InRange(0, 5));
            }
            for (int n = 6; n <= 8; n++) Assert.That(counts[n] / 600f, Is.InRange(0.25f, 0.42f), "count " + n);
        }

        [Test]
        public void KeyPlan_IsDeterministic()
        {
            var order = new[] { 4, 2, 5, 3, 1, 0 };
            var a = KeyPlan.Plan(99, order, null, R); var b = KeyPlan.Plan(99, order, null, R);
            CollectionAssert.AreEqual(a.Select(k => k.Region).ToArray(), b.Select(k => k.Region).ToArray());
        }

        [Test]
        public void KeyPlan_BiasPutsFewerKeysWhereTheFirstClosingIs()
        {
            var order = new[] { 0, 1, 2, 3, 4, 5 };
            var share = new double[6]; int total = 0;
            for (int seed = 0; seed < 3000; seed++)
                foreach (var k in KeyPlan.Plan(seed, order, null, R)) { share[k.Region]++; total++; }
            for (int i = 0; i < 6; i++) share[i] /= total;
            // Weights .6 .7 .8 .9 1 1 over 5.0: 12 / 14 / 16 / 18 / 20 / 20 %.
            double[] want = { 0.12, 0.14, 0.16, 0.18, 0.20, 0.20 };
            for (int i = 0; i < 6; i++) Assert.AreEqual(want[i], share[i], 0.015, "closing " + i);
        }

        [Test]
        public void KeyPlan_NoBias_IsEvenAcrossRegions()
        {
            var order = new[] { 0, 1, 2, 3, 4, 5 };
            var rules = new PollutionRules { KeyBias = 0f };
            var share = new double[6]; int total = 0;
            for (int seed = 0; seed < 3000; seed++)
                foreach (var k in KeyPlan.Plan(seed, order, null, rules)) { share[k.Region]++; total++; }
            for (int i = 0; i < 6; i++) Assert.AreEqual(1.0 / 6, share[i] / total, 0.015);
        }

        // ---- Station ----

        static KeyElevatorStation Started(out KeyCooldowns cd, double now = 0, PollutionState regionState = PollutionState.Safe, int holder = 1, int team = 10)
        {
            cd = new KeyCooldowns();
            var s = new KeyElevatorStation();
            Assert.AreEqual(KeyResult.Ok, s.Insert(now, holder, team, true, cd, regionState, R));
            return s;
        }

        [Test]
        public void Station_FullRide_InsertBoardCloseSelectRide()
        {
            var s = Started(out var cd);
            Assert.AreEqual(KeyPhase.DoorOpen, s.Phase);
            Assert.AreEqual(30f, s.PhaseLeft(0), 1e-4f);
            Assert.AreEqual(KeyResult.Ok, s.Board(1, R));
            Assert.AreEqual(KeyResult.Ok, s.Board(2, R));
            Assert.AreEqual(KeyResult.Ok, s.CloseDoors(10, 1, R));
            Assert.AreEqual(KeyPhase.Selecting, s.Phase);
            Assert.AreEqual(15f, s.PhaseLeft(10), 1e-4f);
            Assert.AreEqual(KeyResult.Ok, s.Select(12, 1, 1, 0, Open, 8f, cd, R));
            Assert.AreEqual(KeyPhase.Riding, s.Phase);
            Assert.AreEqual(1, s.Destination);
            Assert.IsFalse(cd.CanUse(10, 13));
            Assert.AreEqual(KeyEvent.None, s.Tick(19, R));
            Assert.AreEqual(KeyEvent.RideEnded, s.Tick(20, R));
            Assert.AreEqual(KeyPhase.Idle, s.Phase, "a safe region's station is ready again at once");
        }

        [Test]
        public void Station_DoorTimeout_StartsSelection_AndSelectionTimeout_VoidsTheUse()
        {
            var s = Started(out var cd);
            s.Board(1, R);
            Assert.AreEqual(KeyEvent.None, s.Tick(29.9, R));
            Assert.AreEqual(KeyEvent.SelectionStarted, s.Tick(30, R));
            Assert.AreEqual(KeyPhase.Selecting, s.Phase);
            Assert.AreEqual(KeyEvent.None, s.Tick(44.9, R));
            Assert.AreEqual(KeyEvent.Voided, s.Tick(45, R));
            Assert.IsTrue(s.KeyRefunded, "the key comes back");
            Assert.IsTrue(cd.CanUse(10, 46), "a void use starts no team cooldown");
            Assert.AreEqual(KeyPhase.Recharging, s.Phase, "a safe region's station has no recharge time");
            Assert.AreEqual(KeyEvent.Recharged, s.Tick(45, R));
            Assert.AreEqual(KeyPhase.Idle, s.Phase);
        }

        [Test]
        public void Station_VoidInAContaminatedRegion_RechargesForNinetySeconds()
        {
            var s = Started(out _, 0, PollutionState.Contaminated);
            s.Board(1, R);
            s.Tick(30, R);
            Assert.AreEqual(KeyEvent.Voided, s.Tick(45, R));
            Assert.AreEqual(KeyPhase.Recharging, s.Phase);
            Assert.AreEqual(KeyResult.Busy, s.Insert(100, 2, 11, true, new KeyCooldowns(), PollutionState.Contaminated, R));
            Assert.AreEqual(KeyEvent.None, s.Tick(134.9, R));
            Assert.AreEqual(KeyEvent.Recharged, s.Tick(135, R));
            Assert.AreEqual(KeyPhase.Idle, s.Phase);
        }

        [Test]
        public void Station_RideInAContaminatedRegion_RechargesAfterTheRide()
        {
            var s = Started(out var cd, 0, PollutionState.Contaminated);
            s.Board(1, R);
            s.CloseDoors(5, 1, R);
            s.Select(6, 1, 1, 0, Open, 8f, cd, R);
            Assert.AreEqual(KeyEvent.RideEnded, s.Tick(14, R));
            Assert.AreEqual(KeyPhase.Recharging, s.Phase);
            Assert.AreEqual(KeyEvent.Recharged, s.Tick(104, R));
        }

        [Test]
        public void Station_HolderNotAboard_VoidsWhenTheDoorsTimeOut_AndCannotCloseOrSelect()
        {
            var s = Started(out var cd);
            s.Board(2, R);
            Assert.AreEqual(KeyResult.HolderNotAboard, s.CloseDoors(5, 1, R));
            Assert.AreEqual(KeyResult.NotHolder, s.CloseDoors(5, 2, R));
            Assert.AreEqual(KeyEvent.Voided, s.Tick(30, R));
            Assert.IsTrue(s.KeyRefunded);
        }

        [Test]
        public void Station_AnybodyBoardsIncludingEnemies_UpToFour_AndOnlyWhileTheDoorsAreOpen()
        {
            var s = Started(out var cd);
            for (int id = 1; id <= 4; id++) Assert.AreEqual(KeyResult.Ok, s.Board(id, R));
            Assert.AreEqual(KeyResult.CarFull, s.Board(5, R));
            Assert.AreEqual(KeyResult.Ok, s.Board(3, R), "already aboard");
            Assert.AreEqual(KeyResult.Ok, s.Alight(4));
            Assert.AreEqual(KeyResult.Ok, s.Board(5, R));
            s.CloseDoors(3, 1, R);
            Assert.AreEqual(KeyResult.WrongPhase, s.Board(6, R));
            Assert.AreEqual(KeyResult.WrongPhase, s.Alight(2));
        }

        [Test]
        public void Station_OnlyTheHolderSelects_AndOnlyANonContaminatedOtherRegion()
        {
            var s = Started(out var cd);
            s.Board(1, R); s.Board(2, R);
            s.CloseDoors(1, 1, R);
            Assert.AreEqual(KeyResult.NotHolder, s.Select(2, 2, 1, 0, Open, 8f, cd, R));
            Assert.AreEqual(KeyResult.BadDestination, s.Select(2, 1, 3, 0, Open, 8f, cd, R), "contaminated");
            Assert.AreEqual(KeyResult.BadDestination, s.Select(2, 1, 0, 0, Open, 8f, cd, R), "here");
            Assert.AreEqual(KeyResult.BadDestination, s.Select(2, 1, 9, 0, Open, 8f, cd, R));
            Assert.AreEqual(KeyResult.Ok, s.Select(2, 1, 2, 0, Open, 8f, cd, R), "an announced region is not contaminated yet");
        }

        [Test]
        public void Station_TeamCooldown_BlocksTheNextKey_WithoutUsingIt()
        {
            var s = Started(out var cd);
            s.Board(1, R); s.CloseDoors(1, 1, R); s.Select(2, 1, 1, 0, Open, 8f, cd, R); s.Tick(10, R);
            var other = new KeyElevatorStation();
            Assert.AreEqual(KeyResult.TeamCooldown, other.Insert(50, 3, 10, true, cd, PollutionState.Safe, R));
            Assert.AreEqual(KeyPhase.Idle, other.Phase, "nothing started, the key is not used");
            Assert.AreEqual(KeyResult.Ok, other.Insert(50, 9, 77, true, cd, PollutionState.Safe, R), "another team is free");
            var third = new KeyElevatorStation();
            Assert.AreEqual(KeyResult.Ok, third.Insert(2 + 180, 3, 10, true, cd, PollutionState.Safe, R), "180 s after the ride began");
        }

        [Test]
        public void Station_NoKey_NoInsert()
        {
            var s = new KeyElevatorStation();
            Assert.AreEqual(KeyResult.NoKey, s.Insert(0, 1, 1, false, null, PollutionState.Safe, R));
        }

        [Test]
        public void Travel_Destinations_ArrivalAndBotChoice()
        {
            var list = new List<int>();
            KeyElevatorTravel.Destinations(0, Open, list);
            CollectionAssert.AreEqual(new[] { 1, 2 }, list);
            Assert.AreEqual(1, KeyElevatorTravel.ArrivalRegion(0, 1, Open));
            var after = new[] { PollutionState.Safe, PollutionState.Contaminated, PollutionState.Warning, PollutionState.Contaminated };
            Assert.AreEqual(2, KeyElevatorTravel.ArrivalRegion(0, 1, after), "destination confirmed during the ride: the next open region");
            var none = new[] { PollutionState.Safe, PollutionState.Contaminated };
            Assert.AreEqual(1, KeyElevatorTravel.ArrivalRegion(0, 1, none), "nothing left");
            Assert.AreEqual(2, KeyElevatorTravel.BotDestination(0, Open, 2));
            Assert.AreEqual(1, KeyElevatorTravel.BotDestination(0, Open, 3), "final region not reachable: the first open one");
            Assert.AreEqual(-1, KeyElevatorTravel.BotDestination(0, none, 1));
        }
    }
}
