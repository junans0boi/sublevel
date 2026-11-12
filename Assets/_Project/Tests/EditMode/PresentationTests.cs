using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class PresentationTests
    {
        [Test]
        public void MoveAnim_FollowsDominantLocalDirection()
        {
            Assert.AreEqual(MoveAnim.Idle, MoveAnimSelector.Pick(0.05f, 0.05f, false, 0.2f));
            Assert.AreEqual(MoveAnim.Forward, MoveAnimSelector.Pick(1f, 4f, false, 0.2f));
            Assert.AreEqual(MoveAnim.Back, MoveAnimSelector.Pick(1f, -4f, false, 0.2f));
            Assert.AreEqual(MoveAnim.Right, MoveAnimSelector.Pick(4f, 1f, false, 0.2f));
            Assert.AreEqual(MoveAnim.Left, MoveAnimSelector.Pick(-4f, -1f, false, 0.2f));
        }

        [Test]
        public void MoveAnim_DeadWinsOverMovement()
        {
            Assert.AreEqual(MoveAnim.Dead, MoveAnimSelector.Pick(0f, 5f, true, 0.2f));
        }

        [Test]
        public void Footsteps_LandOncePerStride()
        {
            var cadence = new FootstepCadence(2f);
            int steps = 0;
            for (int i = 0; i < 100; i++) // 100 x 0.1 m = 10 m
                if (cadence.Advance(0.1f, true)) steps++;
            Assert.AreEqual(5, steps);
        }

        [Test]
        public void Footsteps_StoppingResetsTheStride()
        {
            var cadence = new FootstepCadence(2f);
            Assert.IsFalse(cadence.Advance(1.9f, true));
            Assert.IsFalse(cadence.Advance(0f, false));
            Assert.IsFalse(cadence.Advance(0.2f, true), "the distance walked before stopping must not count");
        }

        [Test]
        public void ShotGrouper_CollapsesPelletsButKeepsSeparateShots()
        {
            var grouper = new ShotGrouper(0.03);
            Assert.IsTrue(grouper.IsNewShot(1.000));
            Assert.IsFalse(grouper.IsNewShot(1.000));
            Assert.IsFalse(grouper.IsNewShot(1.010));
            Assert.IsTrue(grouper.IsNewShot(1.075));
        }
    }
}
