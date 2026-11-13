using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #161, ADR-126: what the server accepts as movement of a remotely owned body.
    public class MoveReconcileTests
    {
        static readonly LocomotionParams L = new LocomotionParams();

        [Test]
        public void SprintingForTheWholeWindow_IsPlausible()
        {
            Assert.IsFalse(MoveReconcile.Implausible(0f, 0f, L.SprintSpeed * 0.25f, 0.25f, L, out float excess));
            Assert.AreEqual(0f, excess);
        }

        [Test]
        public void ARunThatArrivesLateInOneBurst_IsPlausible()
        {
            // Half a second of running delivered at once after a lost packet.
            Assert.IsFalse(MoveReconcile.Implausible(0f, 0f, L.SprintSpeed * 0.5f, 0.1f, L, out _));
        }

        [Test]
        public void ATeleport_IsNot_AndSaysByHowMuch()
        {
            Assert.IsTrue(MoveReconcile.Implausible(100f, 0f, 0f, 0.25f, L, out float excess));
            Assert.Greater(excess, 90f);
        }

        [Test]
        public void FallingFast_IsPlausible_ButNotRisingFast()
        {
            Assert.IsFalse(MoveReconcile.Implausible(0f, -30f * 0.25f, 0f, 0.25f, L, out _), "a long fall");
            Assert.IsTrue(MoveReconcile.Implausible(0f, 40f, 0f, 0.25f, L, out _), "40 m up in a quarter second");
        }

        [Test]
        public void NoTimeElapsed_AllowsOnlyTheSlack()
        {
            Assert.IsFalse(MoveReconcile.Implausible(MoveReconcile.SlackMeters - 0.1f, 0f, 0f, 0f, L, out _));
            Assert.IsTrue(MoveReconcile.Implausible(MoveReconcile.SlackMeters + 0.1f, 0f, 0f, -1f, L, out _), "negative time counts as none");
        }
    }
}
