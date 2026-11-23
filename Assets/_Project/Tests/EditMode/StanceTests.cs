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
            // PUBG's table (ADR-095): running 4.7, crouched 3.4, crawling 1.2 m/s.
            Assert.AreEqual(4.7f, Stances.MoveSpeed(4.7f, Stance.Stand, Rules), 1e-4f);
            Assert.AreEqual(3.4f, Stances.MoveSpeed(4.7f, Stance.Crouch, Rules), 0.12f);
            Assert.AreEqual(1.2f, Stances.MoveSpeed(4.7f, Stance.Prone, Rules), 0.01f, "crawling: 1.2 m/s");
        }

        [Test]
        public void EachStance_HasItsOwnEyeHeight()
        {
            Assert.AreEqual(1.6f, Stances.EyeHeight(1.6f, Stance.Stand, Rules), 1e-4f);
            Assert.AreEqual(0.95f, Stances.EyeHeight(1.6f, Stance.Crouch, Rules), 1e-4f);
            Assert.AreEqual(0.4f, Stances.EyeHeight(1.6f, Stance.Prone, Rules), 1e-4f);
        }

        [Test]
        public void Crouching_ShrinksTheCapsule_TightensTheSpread_AndQuietensFootsteps()
        {
            Assert.AreEqual(1.8f, Rules.For(Stance.Stand).CapsuleHeight, 1e-4f);
            Assert.AreEqual(1.0f, Rules.For(Stance.Crouch).CapsuleHeight, 1e-4f);
            Assert.AreEqual(1f, Rules.For(Stance.Stand).SpreadFactor, 1e-4f);
            Assert.AreEqual(0.5f, Rules.For(Stance.Crouch).SpreadFactor, 1e-4f);
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
        public void Jumping_StandsUp_SprintingCrouchedStaysDown()
        {
            Assert.AreEqual(Stance.Stand, Stances.Next(Stance.Crouch, false, false, true, false, true, Rules), "jump");
            Assert.AreEqual(Stance.Crouch, Stances.Next(Stance.Crouch, false, false, false, true, true, Rules), "crouch-sprint (ADR-095)");
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
        public void Prone_IsEnabled_ZToggles_CCrouches_AndJumpOrSprintStandsUp()
        {
            Assert.IsTrue(Rules.ProneEnabled);
            Assert.AreEqual(Stance.Prone, Stances.Next(Stance.Stand, false, true, false, false, true, Rules));
            Assert.AreEqual(Stance.Prone, Stances.Next(Stance.Crouch, false, true, false, false, true, Rules), "Z from a crouch goes down");
            Assert.AreEqual(Stance.Stand, Stances.Next(Stance.Prone, false, true, false, false, true, Rules), "Z again gets up");
            Assert.AreEqual(Stance.Crouch, Stances.Next(Stance.Prone, true, false, false, false, true, Rules), "C from prone: crouch");
            Assert.AreEqual(Stance.Stand, Stances.Next(Stance.Prone, false, false, true, false, true, Rules), "Space stands up");
            Assert.AreEqual(Stance.Stand, Stances.Next(Stance.Prone, false, false, false, true, true, Rules), "sprint stands up");
            Assert.AreEqual(Stance.Prone, Stances.Next(Stance.Prone, true, false, false, false, false, Rules), "no room to rise");
            Assert.AreEqual(Stance.Prone, Stances.Next(Stance.Prone, false, false, true, false, false, Rules), "jump without room");
            Assert.AreEqual(Stance.Prone, Stances.Next(Stance.Stand, false, true, false, false, false, Rules), "getting down never needs room");
        }

        [Test]
        public void Prone_CanBeSwitchedOffByData()
        {
            var off = new StanceRules { ProneEnabled = false };
            Assert.AreEqual(Stance.Stand, Stances.Next(Stance.Stand, false, true, false, false, true, off));
            Assert.AreEqual(Stance.Crouch, Stances.Next(Stance.Crouch, false, true, false, false, true, off));
        }

        [Test]
        public void Prone_Constants_MatchTheAgreedValues()
        {
            var prone = Rules.Prone;
            Assert.AreEqual(1.2f, prone.EyeDropMeters, 1e-5f);
            Assert.AreEqual(0.4f, Stances.EyeHeight(1.6f, Stance.Prone, Rules), 1e-5f, "eyes 0.4 m over the ground");
            Assert.AreEqual(0.255f, prone.MoveFactor, 1e-5f);
            Assert.AreEqual(1.2f, 4.7f * prone.MoveFactor, 0.01f, "1.2 m/s (ADR-095)");
            Assert.AreEqual(0.2f, prone.SpreadFactor, 1e-5f);
            Assert.Less(prone.SpreadFactor, Rules.Crouch.SpreadFactor, "the steadiest stance");
            Assert.AreEqual(0.1f, prone.FootstepVolumeFactor, 1e-5f);
            Assert.Less(prone.FootstepVolumeFactor, Rules.Crouch.FootstepVolumeFactor, "near silent");
        }

        [Test]
        public void Prone_Capsule_StaysAtLeastAsTallAsItIsWide_AndTheEyesStayInsideIt()
        {
            const float radius = 0.35f; // the character controller's radius
            Assert.AreEqual(0.7f, Rules.Prone.CapsuleHeight, 1e-5f);
            Assert.GreaterOrEqual(Rules.Prone.CapsuleHeight, 2f * radius, "a controller capsule cannot be lower than two radii");
            Assert.GreaterOrEqual(Rules.Prone.CapsuleHeight, 0.6f);
            Assert.Less(Rules.Prone.CapsuleHeight, Rules.Crouch.CapsuleHeight);
            Assert.Less(Stances.EyeHeight(1.6f, Stance.Prone, Rules), Rules.Prone.CapsuleHeight, "the eyes are inside the lowered capsule");
            Assert.Greater(Stances.EyeHeight(1.6f, Stance.Prone, Rules), 0.3f, "and not on the ground");
        }

        [Test]
        public void Prone_EyeDrop_IsLargerThanTheCrouchDrop_AndLeavesTheCapsuleTopAboveTheEyes()
        {
            Assert.Greater(Rules.Prone.EyeDropMeters, Rules.Crouch.EyeDropMeters);
            float eyeOverCapsule = Stances.EyeHeight(1.6f, Stance.Prone, Rules) - Rules.Prone.CapsuleHeight;
            Assert.Less(eyeOverCapsule, 0f);
            Assert.IsTrue(Stances.Rises(Stance.Prone, Stance.Crouch, Rules));
            Assert.IsTrue(Stances.Rises(Stance.Crouch, Stance.Stand, Rules));
            Assert.IsFalse(Stances.Rises(Stance.Stand, Stance.Prone, Rules));
        }

        [Test]
        public void Swimming_PlacesTheFlatClip_WithTheLiftHeightAndBackShift()
        {
            // Lying is the prone clip (ADR-231); only swimming still lifts the chest and moves the flat body back in code.
            Assert.AreEqual(40f, Rules.ProneHeadLiftDeg, 1e-5f);
            Assert.AreEqual(0.15f, Rules.ProneBodyHeight, 1e-5f);
            Assert.AreEqual(1.35f, Rules.ProneModelBackShift, 1e-5f);
        }

        [Test]
        public void BentLegs_LowerTheHips()
        {
            Assert.AreEqual(0f, Stances.CrouchHipDrop(0.9f, 0f, 0f), 1e-5f);
            // Thigh 46 and shin 40 degrees from vertical on a 0.9 m leg (the ledge pull-up pose, ADR-542).
            Assert.AreEqual(0.2427f, Stances.CrouchHipDrop(0.9f, 46f, 40f), 1e-3f);
        }

        [Test]
        public void CrouchWalk_PlaysTheLegsInProportionToTheSpeed()
        {
            Assert.AreEqual(0.495f, Stances.CrouchLegPace(2.475f, 5f, 0.4f), 1e-4f);
            Assert.AreEqual(0.865f, Stances.CrouchLegPace(2.475f, 2.86f, 0.4f), 1e-3f);
            Assert.AreEqual(0.4f, Stances.CrouchLegPace(0.2f, 5f, 0.4f), 1e-4f, "never frozen");
            Assert.AreEqual(1f, Stances.CrouchLegPace(9f, 5f, 0.4f), 1e-4f, "never faster than the clip");
            Assert.AreEqual(1f, Stances.CrouchLegPace(2f, 0f, 0.4f), 1e-4f);
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
        public void HigherFalls_HurtMoreWithEveryMetre_AndKillOnlyFromFifteen()
        {
            // ADR-491: a 2-3 storey drop (6-10 m) takes a chunk, not the whole bar.
            Assert.AreEqual(4f, Locomotion.FallDamage(3.001f, Fall), 0.01f, "a hard landing just past the safe height");
            Assert.AreEqual(12f, Locomotion.FallDamage(4f, Fall), 1e-3f);
            Assert.AreEqual(28f, Locomotion.FallDamage(6f, Fall), 1e-3f);
            Assert.AreEqual(44f, Locomotion.FallDamage(8f, Fall), 1e-3f);
            Assert.AreEqual(60f, Locomotion.FallDamage(10f, Fall), 1e-3f);
            Assert.Less(Locomotion.FallDamage(14.9f, Fall), 100f, "not lethal below 15 m");
            Assert.AreEqual(100f, Locomotion.FallDamage(15f, Fall), 1e-3f);
            Assert.Greater(Locomotion.FallDamage(20f, Fall), 100f);
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
            Assert.AreEqual(19f, Locomotion.AcceptedFall(3.5f, 20f, Fall), 1e-4f, "a fall played down is cut back up to seen - 1 m (#501)");
            Assert.AreEqual(19f, Locomotion.UnreportedFall(20f, Fall), 1e-4f, "a watched landing with no report counts by itself (#501)");
            Assert.AreEqual(0f, Locomotion.UnreportedFall(0.5f, Fall));
            Assert.AreEqual(0f, Locomotion.UnreportedFall(float.NaN, Fall));
            Assert.AreEqual(0f, Locomotion.UnreportedFall(float.PositiveInfinity, Fall));
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
