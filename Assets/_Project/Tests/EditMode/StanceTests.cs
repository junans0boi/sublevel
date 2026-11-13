using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-053: what each stance changes, when the stance changes, and what a fall costs.
    public class StanceTests
    {
        static readonly StanceRules Rules = new StanceRules();
        static readonly FallRules Fall = new FallRules();

        [Test]
        public void EachStance_HasItsOwnMoveSpeed()
        {
            Assert.AreEqual(4.5f, Stances.MoveSpeed(4.5f, Stance.Stand, Rules), 1e-4f);
            Assert.AreEqual(2.475f, Stances.MoveSpeed(4.5f, Stance.Crouch, Rules), 1e-4f);
            Assert.AreEqual(1.125f, Stances.MoveSpeed(4.5f, Stance.Prone, Rules), 1e-4f);
        }

        [Test]
        public void EachStance_HasItsOwnEyeHeight()
        {
            Assert.AreEqual(1.6f, Stances.EyeHeight(1.6f, Stance.Stand, Rules), 1e-4f);
            Assert.AreEqual(0.95f, Stances.EyeHeight(1.6f, Stance.Crouch, Rules), 1e-4f);
            Assert.AreEqual(0.3f, Stances.EyeHeight(1.6f, Stance.Prone, Rules), 1e-4f);
        }

        [Test]
        public void Crouching_ShrinksTheCapsule_TightensTheSpread_AndQuietensFootsteps()
        {
            Assert.AreEqual(1.8f, Rules.For(Stance.Stand).CapsuleHeight, 1e-4f);
            Assert.AreEqual(1.0f, Rules.For(Stance.Crouch).CapsuleHeight, 1e-4f);
            Assert.AreEqual(1f, Rules.For(Stance.Stand).SpreadFactor, 1e-4f);
            Assert.AreEqual(0.7f, Rules.For(Stance.Crouch).SpreadFactor, 1e-4f);
            Assert.AreEqual(1f, Rules.For(Stance.Stand).FootstepVolumeFactor, 1e-4f);
            Assert.AreEqual(0.4f, Rules.For(Stance.Crouch).FootstepVolumeFactor, 1e-4f);
            // The eyes stay inside the lowered capsule.
            Assert.Less(Stances.EyeHeight(1.6f, Stance.Crouch, Rules), Rules.Crouch.CapsuleHeight);
        }

        [Test]
        public void TheCrouchKey_Toggles()
        {
            Assert.AreEqual(Stance.Crouch, Stances.Next(Stance.Stand, true, false, false, false, true, Rules));
            Assert.AreEqual(Stance.Stand, Stances.Next(Stance.Crouch, true, false, false, false, true, Rules));
            Assert.AreEqual(Stance.Crouch, Stances.Next(Stance.Crouch, false, false, false, false, true, Rules), "no key: stays");
            Assert.AreEqual(Stance.Stand, Stances.Next(Stance.Stand, false, false, true, true, true, Rules), "jumping and sprinting upright change nothing");
        }

        [Test]
        public void JumpingOrSprinting_StandsUp()
        {
            Assert.AreEqual(Stance.Stand, Stances.Next(Stance.Crouch, false, false, true, false, true, Rules), "jump");
            Assert.AreEqual(Stance.Stand, Stances.Next(Stance.Crouch, false, false, false, true, true, Rules), "sprint");
        }

        [Test]
        public void StandingUp_NeedsHeadRoom()
        {
            Assert.AreEqual(Stance.Crouch, Stances.Next(Stance.Crouch, true, false, false, false, false, Rules), "key");
            Assert.AreEqual(Stance.Crouch, Stances.Next(Stance.Crouch, false, false, true, false, false, Rules), "jump");
            Assert.AreEqual(Stance.Crouch, Stances.Next(Stance.Crouch, false, false, false, true, false, Rules), "sprint");
            Assert.AreEqual(Stance.Crouch, Stances.Next(Stance.Stand, true, false, false, false, false, Rules), "getting down never needs room");
        }

        [Test]
        public void Prone_IsOnlyAnInput_UntilItIsSwitchedOn()
        {
            Assert.IsFalse(Rules.ProneEnabled);
            Assert.AreEqual(Stance.Stand, Stances.Next(Stance.Stand, false, true, false, false, true, Rules));
            Assert.AreEqual(Stance.Crouch, Stances.Next(Stance.Crouch, false, true, false, false, true, Rules));

            var withProne = new StanceRules { ProneEnabled = true };
            Assert.AreEqual(Stance.Prone, Stances.Next(Stance.Stand, false, true, false, false, true, withProne));
            Assert.AreEqual(Stance.Stand, Stances.Next(Stance.Prone, false, true, false, false, true, withProne));
            Assert.AreEqual(Stance.Crouch, Stances.Next(Stance.Prone, true, false, false, false, true, withProne));
            Assert.AreEqual(Stance.Prone, Stances.Next(Stance.Prone, true, false, false, false, false, withProne), "no room to rise");
        }

        [Test]
        public void BentKnees_LowerTheHips()
        {
            Assert.AreEqual(0f, Stances.KneeBendDrop(0.9f, 0f), 1e-5f);
            Assert.AreEqual(0.45f, Stances.KneeBendDrop(0.9f, 60f), 1e-4f);
            Assert.AreEqual(0.9f, Stances.KneeBendDrop(0.9f, 90f), 1e-4f);
            // The default crouch: hips down about as far as the eyes.
            Assert.AreEqual(0.6219f, Stances.KneeBendDrop(0.9f, Rules.CrouchKneeDeg), 1e-3f);
        }

        [Test]
        public void ShortFalls_DoNotHurt()
        {
            Assert.AreEqual(0f, Locomotion.FallDamage(0f, Fall));
            Assert.AreEqual(0f, Locomotion.FallDamage(0.8f, Fall), "a jump");
            Assert.AreEqual(0f, Locomotion.FallDamage(2.99f, Fall));
            Assert.AreEqual(0f, Locomotion.FallDamage(3f, Fall), "exactly the safe height");
            Assert.AreEqual(0f, Locomotion.FallDamage(-5f, Fall));
            Assert.AreEqual(0f, Locomotion.FallDamage(float.NaN, Fall));
        }

        [Test]
        public void HigherFalls_HurtMoreWithEveryMetre_AndKillAroundTwelve()
        {
            Assert.AreEqual(11.1111f, Locomotion.FallDamage(4f, Fall), 1e-3f);
            Assert.AreEqual(33.3333f, Locomotion.FallDamage(6f, Fall), 1e-3f);
            Assert.AreEqual(50f, Locomotion.FallDamage(7.5f, Fall), 1e-3f);
            Assert.AreEqual(100f, Locomotion.FallDamage(12f, Fall), 1e-3f);
            Assert.AreEqual(133.3333f, Locomotion.FallDamage(15f, Fall), 1e-3f);
        }

        [Test]
        public void AReportedFall_CountsOnlyUpToWhatTheServerSaw()
        {
            Assert.AreEqual(6f, Locomotion.AcceptedFall(6f, 5.7f, Fall), 1e-4f, "an honest report passes as it is");
            Assert.AreEqual(6.7f, Locomotion.AcceptedFall(20f, 5.7f, Fall), 1e-4f, "an inflated one is cut to seen + 1 m");
            Assert.AreEqual(1f, Locomotion.AcceptedFall(10f, 0f, Fall), 1e-4f, "nothing seen: below the safe height");
            Assert.AreEqual(0f, Locomotion.AcceptedFall(-3f, 5f, Fall));
            Assert.AreEqual(0f, Locomotion.AcceptedFall(float.NaN, 5f, Fall));
            Assert.AreEqual(0f, Locomotion.AcceptedFall(float.PositiveInfinity, 5f, Fall));
        }

        [Test]
        public void FallTracker_MeasuresFromTheHighestPoint_ToTheLanding()
        {
            var fall = new FallTracker();
            Assert.AreEqual(0f, fall.Tick(true, 0f), "standing");
            Assert.AreEqual(0f, fall.Tick(false, 0.5f), "jumped off a ledge");
            Assert.AreEqual(0f, fall.Tick(false, 0.8f));
            Assert.AreEqual(0f, fall.Tick(false, -3f));
            Assert.IsTrue(fall.InAir);
            Assert.AreEqual(3.8f, fall.DroppedSoFar(-3f), 1e-4f);
            Assert.AreEqual(6f, fall.Tick(true, -5.2f), 1e-4f, "0.8 down to -5.2");
            Assert.IsFalse(fall.InAir);
            Assert.AreEqual(0f, fall.Tick(true, -5.2f), "reported once");

            // A new fall starts from its own highest point, not the previous one.
            Assert.AreEqual(0f, fall.Tick(false, -5.2f));
            Assert.AreEqual(2f, fall.Tick(true, -7.2f), 1e-4f);
        }

        [Test]
        public void FallTracker_ForgetsAFallWhenReset()
        {
            var fall = new FallTracker();
            fall.Tick(false, 10f);
            fall.Reset();
            Assert.AreEqual(0f, fall.Tick(true, 0f), "moved to the ground, not fallen");
            Assert.AreEqual(0f, fall.DroppedSoFar(0f));
        }
    }
}
