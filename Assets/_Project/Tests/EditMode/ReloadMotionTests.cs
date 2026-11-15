using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class ReloadMotionTests
    {
        [Test]
        public void Reload_StartsAndEndsAtTheNormalHold_AndMovesBetween()
        {
            foreach (var kind in new[] { ReloadKind.Magazine, ReloadKind.Pistol })
            {
                var s = ReloadMotion.Reload(0f, kind); var e = ReloadMotion.Reload(1f, kind);
                Assert.AreEqual(0f, s.Down, 1e-4f); Assert.AreEqual(0f, e.Down, 1e-4f); Assert.AreEqual(0f, e.RollDeg, 1e-4f);
                var mid = ReloadMotion.Reload(0.3f, kind);
                Assert.Greater(mid.RollDeg, 5f, "weapon tipped toward the hand");
                Assert.Less(mid.LeftU, s.LeftU - 0.05f, "left hand went down to the well / belt");
            }
        }

        [Test]
        public void Reload_MagazineInHandOnlyWhileSwapping_NoJumpsBetweenFrames()
        {
            Assert.IsFalse(ReloadMotion.Reload(0.1f, ReloadKind.Magazine).MagInHand);
            Assert.IsTrue(ReloadMotion.Reload(0.5f, ReloadKind.Magazine).MagInHand);
            Assert.IsFalse(ReloadMotion.Reload(0.85f, ReloadKind.Magazine).MagInHand);
            var prev = ReloadMotion.Reload(0f, ReloadKind.Magazine);
            for (float t = 0.01f; t <= 1f; t += 0.01f)
            {
                var p = ReloadMotion.Reload(t, ReloadKind.Magazine);
                Assert.Less(System.Math.Abs(p.LeftU - prev.LeftU), 0.06f, "hand moves smoothly at t=" + t);
                Assert.Less(System.Math.Abs(p.LeftA - prev.LeftA), 0.06f);
                prev = p;
            }
        }

        [Test]
        public void ShellReload_LoopsBetweenBeltAndTube()
        {
            var tube = ReloadMotion.Reload(0.5f, ReloadKind.Shell, 0f);
            var belt = ReloadMotion.Reload(0.5f, ReloadKind.Shell, ReloadMotion.ShellCycleSeconds * 0.5f);
            Assert.Less(belt.LeftU, tube.LeftU - 0.1f);
            Assert.IsTrue(belt.MagInHand); Assert.IsFalse(tube.MagInHand);
        }

        [Test]
        public void Draw_RisesFromLowToTheHold()
        {
            Assert.Greater(ReloadMotion.Draw(0f).Down, 0.2f);
            Assert.AreEqual(0f, ReloadMotion.Draw(1f).Down, 1e-4f);
            Assert.Less(ReloadMotion.Draw(0.5f).PitchDeg, ReloadMotion.Draw(0f).PitchDeg);
        }
    }
}
