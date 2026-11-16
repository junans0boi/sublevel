using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // Revive chips (ADR-171, S6): drop rule, pick up, deposit within 90 s, box destroyed by pollution, revive.
    public class ChipReviveTests
    {
        static readonly PollutionRules R = new PollutionRules();
        static readonly PollutionState[] Calm = { PollutionState.Safe, PollutionState.Safe, PollutionState.Safe };

        [Test]
        public void ShouldDrop_OnlyInASquad_AndOnlyWhileSomeoneElseLives()
        {
            Assert.IsTrue(ReviveChip.ShouldDrop(TeamMode.Squad, 1));
            Assert.IsFalse(ReviveChip.ShouldDrop(TeamMode.Squad, 0), "last one of the team");
            Assert.IsFalse(ReviveChip.ShouldDrop(TeamMode.Solo, 3));
        }

        [Test]
        public void Chip_DropPickUpDepositRevive()
        {
            var chip = new ReviveChip(owner: 1, team: 5, diedAt: 100);
            Assert.AreEqual(ChipState.Dropped, chip.State);
            Assert.AreEqual(ChipResult.NotTeammate, chip.PickUp(110, 9, 6, R), "enemy");
            Assert.AreEqual(ChipResult.NotTeammate, chip.PickUp(110, 1, 5, R), "its owner cannot");
            Assert.AreEqual(ChipResult.Ok, chip.PickUp(110, 2, 5, R));
            Assert.AreEqual(ChipState.Carried, chip.State);
            Assert.AreEqual(ChipResult.Ok, chip.Deposit(150, 1, Calm, R));
            Assert.AreEqual(ChipState.Stored, chip.State);
            Assert.AreEqual(1, chip.BoxRegion);
            Assert.AreEqual(ChipResult.Ok, chip.Revive(2, Calm));
            Assert.AreEqual(ChipState.Revived, chip.State);
        }

        [Test]
        public void Chip_NinetySecondsFromTheDeath_ForPickUpAndDeposit()
        {
            var late = new ReviveChip(1, 5, 0);
            Assert.AreEqual(ChipResult.TooLate, late.PickUp(90, 2, 5, R));
            late.Tick(90, Calm, R);
            Assert.AreEqual(ChipState.Expired, late.State);

            var slow = new ReviveChip(1, 5, 0);
            slow.PickUp(80, 2, 5, R);
            Assert.AreEqual(ChipResult.TooLate, slow.Deposit(90, 0, Calm, R));
            slow.Tick(90, Calm, R);
            Assert.AreEqual(ChipState.Expired, slow.State, "carried too long");
        }

        [Test]
        public void Chip_StoredChipHasNoTimeLimit_ButTheBoxRegionBeingContaminatedDestroysIt()
        {
            var chip = new ReviveChip(1, 5, 0);
            chip.PickUp(10, 2, 5, R); chip.Deposit(20, 1, Calm, R);
            chip.Tick(1000, Calm, R);
            Assert.AreEqual(ChipState.Stored, chip.State);
            var hit = new[] { PollutionState.Safe, PollutionState.Contaminated, PollutionState.Safe };
            chip.Tick(1001, hit, R);
            Assert.AreEqual(ChipState.Destroyed, chip.State);
            Assert.AreEqual(ChipResult.WrongState, chip.Revive(0, Calm));
        }

        [Test]
        public void Chip_CannotBeDepositedInOrRevivedIntoAContaminatedRegion()
        {
            var hit = new[] { PollutionState.Contaminated, PollutionState.Safe, PollutionState.Warning };
            var chip = new ReviveChip(1, 5, 0);
            chip.PickUp(5, 2, 5, R);
            Assert.AreEqual(ChipResult.BadDestination, chip.Deposit(10, 0, hit, R));
            Assert.AreEqual(ChipResult.Ok, chip.Deposit(10, 2, hit, R), "an announced region still takes it");
            Assert.AreEqual(ChipResult.BadDestination, chip.Revive(0, hit));
            Assert.AreEqual(ChipResult.Ok, chip.Revive(1, hit));
        }

        [Test]
        public void Chip_CarrierGoingDown_DropsItWhereTheyAre_AndTheClockKeepsRunning()
        {
            var chip = new ReviveChip(1, 5, 0);
            chip.PickUp(10, 2, 5, R);
            chip.Drop();
            Assert.AreEqual(ChipState.Dropped, chip.State);
            Assert.AreEqual(ChipResult.Ok, chip.PickUp(20, 3, 5, R));
            Assert.AreEqual(ChipResult.TooLate, new ReviveChip(1, 5, 0).PickUp(95, 3, 5, R));
        }
    }
}
