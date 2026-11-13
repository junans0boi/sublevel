using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-104: door state, reach, swing and sound rules.
    public class DoorTests
    {
        static readonly DoorRules Rules = new DoorRules();

        [Test]
        public void Toggle_OpensAwayFromTheUser_AndShutsWhenOpen()
        {
            Assert.AreEqual(DoorState.OpenBack, Doors.Toggle(DoorState.Closed, 1), "pushed from the front: swings to the back");
            Assert.AreEqual(DoorState.OpenAhead, Doors.Toggle(DoorState.Closed, -1));
            Assert.AreEqual(DoorState.Closed, Doors.Toggle(DoorState.OpenAhead, 1));
            Assert.AreEqual(DoorState.Closed, Doors.Toggle(DoorState.OpenBack, -1));
        }

        [Test]
        public void Side_FollowsTheWallNormal()
        {
            Assert.AreEqual(1, Doors.SideOf(0f, 2f, 0f, 0f, 0f, 1f));
            Assert.AreEqual(-1, Doors.SideOf(0f, -2f, 0f, 0f, 0f, 1f));
            Assert.AreEqual(1, Doors.SideOf(3f, 0f, 0f, 0f, 1f, 0f));
        }

        [Test]
        public void Swing_TakesOpenSeconds_AndBlocksOnlyWhileMostlyShut()
        {
            float target = Doors.TargetAngle(DoorState.OpenAhead, Rules);
            Assert.AreEqual(-Rules.OpenAngleDeg, target);
            float angle = 0f, t = 0f;
            while (angle != target && t < 5f) { angle = Doors.StepAngle(angle, target, 0.01f, Rules); t += 0.01f; }
            Assert.AreEqual(Rules.OpenSeconds, t, 0.03f);
            Assert.IsFalse(Doors.Blocks(angle, Rules));
            Assert.IsTrue(Doors.Blocks(0f, Rules));
            Assert.IsTrue(Doors.Blocks(-10f, Rules));
        }

        [Test]
        public void Pick_NearestInReachAndInView_IgnoresOtherStoreys()
        {
            var xs = new[] { 0f, 1.6f, 0f };
            var ys = new[] { 0f, 0f, 3.2f };
            var zs = new[] { 1.8f, 0.7f, 0.5f };
            // Eye at the origin, 1.6 m up, looking along +z: the first door (1.8 m) and the second (1.75 m, off to the side) are in reach.
            Assert.AreEqual(0, Doors.Pick(xs, ys, zs, 3, 0f, 1.6f, 0f, 0f, 1f, Rules), "second is nearer but 66 degrees off the view");
            Assert.AreEqual(-1, Doors.Pick(xs, ys, zs, 3, 0f, 1.6f, 0f, 0f, -1f, Rules), "looking away");
            Assert.AreEqual(2, Doors.Pick(xs, ys, zs, 3, 0f, 4.8f, 0f, 0f, 1f, Rules), "upstairs: the door on that floor");
            Assert.AreEqual(-1, Doors.Pick(xs, ys, zs, 3, 0f, 1.6f, 5f, 0f, 1f, Rules), "out of reach");
        }

        [Test]
        public void ClosedDoors_MuffleSound_AndCrossingIsGeometric()
        {
            Assert.AreEqual(40f, Doors.HeardMeters(20f, 1, Rules), 1e-4f);
            Assert.AreEqual(80f, Doors.HeardMeters(20f, 2, Rules), 1e-4f);
            Assert.AreEqual(20f, Doors.HeardMeters(20f, 0, Rules));
            // A door on the line x = 0 between z = -0.65 and 0.65 (facing +z, width 1.3): a path across crosses, a path beside does not.
            Assert.IsTrue(Doors.Crosses(0f, 3f, 0.2f, -3f, 0f, 0f, 1f, 0f, 0.65f));
            Assert.IsFalse(Doors.Crosses(2f, 3f, 2f, -3f, 0f, 0f, 1f, 0f, 0.65f));
        }
    }
}
