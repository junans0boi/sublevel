using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #312, ADR-542: the hanging / climbing body kept off the wall, the hands brought to the edge.
    public class LedgeFitTests
    {
        [Test]
        public void APointInTheWall_UnderTheTop_PushesTheModelBackPastIt()
        {
            Assert.AreEqual(0.25f, LedgeFit.BackFor(0.1f, 0.5f, 0.15f), 1e-5f, "10 cm in + 15 cm clearance");
            Assert.AreEqual(0.05f, LedgeFit.BackFor(-0.1f, 0.5f, 0.15f), 1e-5f, "in front but closer than the clearance");
            Assert.AreEqual(0f, LedgeFit.BackFor(-0.3f, 0.5f, 0.15f), "well clear of the wall");
        }

        [Test]
        public void APointOverTheTop_IsOnIt_NotInTheWall()
        {
            Assert.AreEqual(0f, LedgeFit.BackFor(0.4f, -0.05f, 0.15f), "a knee brought up onto the top");
        }

        [Test]
        public void AHoldOutOfReachAbove_LiftsTheModelUntilTheArmReaches()
        {
            // Shoulder 0.3 m back and 0.6 m under the hold, arm 0.5 m: reaches 0.4 m up at that distance.
            float lift = LedgeFit.Lift(0.3f, 0.6f, 0.5f, 1f, 1f);
            Assert.AreEqual(0.2f, lift, 1e-4f);
        }

        [Test]
        public void AHoldFarBelowTheShoulders_LowersTheModel_AsFarAsAllowed()
        {
            // A mantle rising before it moves over the top (ADR-492): shoulders 1.3 m over the edge.
            Assert.AreEqual(-0.8f, LedgeFit.Lift(0f, -1.3f, 0.5f, 0.3f, 1f), 1e-4f);
            Assert.AreEqual(-0.4f, LedgeFit.Lift(0f, -1.3f, 0.5f, 0.3f, 0.4f), 1e-4f, "clamped");
        }

        [Test]
        public void AHoldInReach_LeavesTheModelWhereItIs()
        {
            Assert.AreEqual(0f, LedgeFit.Lift(0.3f, 0.2f, 0.5f, 1f, 1f));
            Assert.AreEqual(0f, LedgeFit.Lift(0.3f, -0.2f, 0.5f, 1f, 1f));
        }

        [Test]
        public void AHoldOutOfReachSideways_BringsTheShoulderLevelWithIt()
        {
            Assert.AreEqual(0.3f, LedgeFit.Lift(0.7f, 0.3f, 0.5f, 1f, 1f), 1e-5f);
        }
    }
}
