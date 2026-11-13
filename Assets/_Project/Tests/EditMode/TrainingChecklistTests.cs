using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #153: the training range's checklist ticks each step on the action it names, once, and never from a stale counter.
    public class TrainingChecklistTests
    {
        static TrainingObservation Idle() => new TrainingObservation { Stance = Stance.Stand };

        [Test]
        public void EveryStep_HasATitle_AndTheListIsSeventeenLong()
        {
            Assert.AreEqual(17, TrainingChecklist.Count);
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (TrainingStep s in Enum.GetValues(typeof(TrainingStep)))
            {
                string title = TrainingChecklist.Title(s);
                Assert.IsFalse(string.IsNullOrEmpty(title), s + " has no title");
                Assert.IsTrue(seen.Add(title), "duplicate title " + title);
            }
        }

        [Test]
        public void Standing_TicksNothing()
        {
            var list = new TrainingChecklist();
            Assert.AreEqual(0, list.Observe(Idle()));
            Assert.AreEqual(0, list.DoneCount);
        }

        [Test]
        public void MovementStates_TickTheirOwnStep()
        {
            var list = new TrainingChecklist();
            list.Observe(Idle());
            var o = Idle(); o.PlanarSpeed = 2f;
            list.Observe(o);
            Assert.IsTrue(list.IsDone(TrainingStep.Move));
            Assert.IsFalse(list.IsDone(TrainingStep.Sprint));
            o.Sprinting = true;
            list.Observe(o);
            Assert.IsTrue(list.IsDone(TrainingStep.Sprint));
            o = Idle(); o.Stance = Stance.Crouch; list.Observe(o);
            Assert.IsTrue(list.IsDone(TrainingStep.Crouch));
            Assert.IsFalse(list.IsDone(TrainingStep.Prone));
            o.Stance = Stance.Prone; list.Observe(o);
            Assert.IsTrue(list.IsDone(TrainingStep.Prone));
        }

        [Test]
        public void SprintKeyWithoutMoving_AndMovingInACar_DoNotTick()
        {
            var list = new TrainingChecklist();
            var o = Idle(); o.Sprinting = true; list.Observe(o);
            Assert.IsFalse(list.IsDone(TrainingStep.Sprint), "sprint needs speed");
            o = Idle(); o.Seated = true; o.PlanarSpeed = 12f; list.Observe(o);
            Assert.IsFalse(list.IsDone(TrainingStep.Move), "riding is not walking");
            Assert.IsTrue(list.IsDone(TrainingStep.Vehicle));
        }

        [Test]
        public void Parkour_VaultAndClimbTickVault_OnlyTheLedgePullUpTicksLedge()
        {
            var list = new TrainingChecklist();
            var o = Idle(); o.Parkour = ParkourMove.Hang; list.Observe(o);
            Assert.IsFalse(list.IsDone(TrainingStep.Ledge), "hanging is not yet up");
            o.Parkour = ParkourMove.LedgeClimb; list.Observe(o);
            Assert.IsTrue(list.IsDone(TrainingStep.Ledge));
            Assert.IsFalse(list.IsDone(TrainingStep.Vault));
            o.Parkour = ParkourMove.Vault; list.Observe(o);
            Assert.IsTrue(list.IsDone(TrainingStep.Vault));
        }

        [Test]
        public void Swimming_TicksSwim_NotProne()
        {
            var list = new TrainingChecklist();
            var o = Idle(); o.Swimming = true; o.Stance = Stance.Prone; list.Observe(o);
            Assert.IsTrue(list.IsDone(TrainingStep.Swim));
            Assert.IsFalse(list.IsDone(TrainingStep.Prone), "the swim pose is not lying down on purpose");
        }

        [Test]
        public void Counters_TickWhenTheyGrow_NotFromTheirStartingValue()
        {
            var list = new TrainingChecklist();
            var o = Idle(); o.Shots = 5; o.Pickups = 3; o.Throws = 1; o.Heals = 2; o.Attachments = 1; o.DoorOpens = 4;
            list.Observe(o);
            Assert.AreEqual(0, list.DoneCount, "values from before the range opened are the baseline");
            o.Shots++; Assert.AreEqual(1, list.Observe(o)); Assert.IsTrue(list.IsDone(TrainingStep.Shoot));
            o.Pickups++; list.Observe(o); Assert.IsTrue(list.IsDone(TrainingStep.Pickup));
            o.Attachments++; list.Observe(o); Assert.IsTrue(list.IsDone(TrainingStep.Attach));
            o.Heals++; list.Observe(o); Assert.IsTrue(list.IsDone(TrainingStep.Heal));
            o.Throws++; list.Observe(o); Assert.IsTrue(list.IsDone(TrainingStep.Throw));
            o.DoorOpens++; list.Observe(o); Assert.IsTrue(list.IsDone(TrainingStep.Door));
        }

        [Test]
        public void ReloadAimAndZero_TickOnTheirAction()
        {
            var list = new TrainingChecklist();
            var o = Idle(); list.Observe(o);
            o.Aim = AimMode.Shoulder; list.Observe(o);
            Assert.IsFalse(list.IsDone(TrainingStep.Aim), "shouldering is not looking through the sights");
            o.Aim = AimMode.Sights; list.Observe(o);
            Assert.IsTrue(list.IsDone(TrainingStep.Aim));
            o.Reloading = true; list.Observe(o);
            Assert.IsTrue(list.IsDone(TrainingStep.Reload));
            Assert.IsFalse(list.IsDone(TrainingStep.Zero));
            o.ZeroChanges++; list.Observe(o);
            Assert.IsTrue(list.IsDone(TrainingStep.Zero));
        }

        [Test]
        public void ADoneStep_StaysDone_AndFiresOnce_AndAllDoneComesAtTheEnd()
        {
            var list = new TrainingChecklist();
            int fired = 0;
            list.Completed += _ => fired++;
            var o = Idle(); o.PlanarSpeed = 3f;
            list.Observe(o); list.Observe(o);
            Assert.AreEqual(1, fired);
            o.PlanarSpeed = 0f; list.Observe(o);
            Assert.IsTrue(list.IsDone(TrainingStep.Move));

            o = new TrainingObservation
            {
                PlanarSpeed = 3f, Sprinting = true, Stance = Stance.Crouch, Parkour = ParkourMove.Vault, Swimming = true, Seated = true,
                Reloading = true, Aim = AimMode.Sights,
            };
            list.Observe(o);
            o.Stance = Stance.Prone; o.Swimming = false; o.Parkour = ParkourMove.LedgeClimb; list.Observe(o);
            o.Swimming = true; o.Parkour = ParkourMove.Vault; list.Observe(o);
            Assert.IsFalse(list.AllDone);
            o.Shots = 1; o.Pickups = 1; o.Attachments = 1; o.Heals = 1; o.Throws = 1; o.DoorOpens = 1; o.ZeroChanges = 1;
            list.Observe(o);
            Assert.IsTrue(list.AllDone, "done " + list.DoneCount + " of " + TrainingChecklist.Count);
        }
    }
}
