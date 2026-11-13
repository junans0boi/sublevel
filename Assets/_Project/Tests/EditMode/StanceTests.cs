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
            Assert.AreEqual(0.81f, Stances.MoveSpeed(4.5f, Stance.Prone, Rules), 1e-4f, "crawling: about 0.8 m/s");
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
            Assert.AreEqual(0.18f, prone.MoveFactor, 1e-5f);
            Assert.AreEqual(0.81f, 4.5f * prone.MoveFactor, 1e-4f, "about 0.8 m/s");
            Assert.AreEqual(0.35f, prone.SpreadFactor, 1e-5f);
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
        public void ProneBodyPose_Angles_AreConsistent()
        {
            // The body lies down by 85 degrees; lifting the chest 40 leaves the arms 45 to turn back up.
            Assert.AreEqual(85f, Rules.ProneModelPitchDeg, 1e-5f);
            Assert.AreEqual(40f, Rules.ProneHeadLiftDeg, 1e-5f);
            Assert.AreEqual(45f, Stances.ProneArmCounterDeg(Rules), 1e-5f);
            // Pitching by a: the body's up (0,1,0) goes to (0,cos a,sin a), its facing (0,0,1) to (0,-sin a,cos a).
            double a = Rules.ProneModelPitchDeg * System.Math.PI / 180.0;
            Assert.AreEqual(0.087, System.Math.Cos(a), 1e-3, "the spine is almost horizontal");
            Assert.AreEqual(0.996, System.Math.Sin(a), 1e-3, "and points forward");
            // The spine line lies at the body height, the head at the eyes: the model is moved back by the stature minus its reach.
            double reach = 1.8 * System.Math.Sin(a);
            Assert.AreEqual(1.79, reach, 0.01);
            Assert.AreEqual(0.45, reach - Rules.ProneModelBackShift, 0.02, "the crown ends up 0.45 m ahead of the capsule, the eyes about under the face");
            Assert.AreEqual(0.15f, Rules.ProneBodyHeight, 1e-5f);
        }

        [Test]
        public void BentKnees_LowerTheHips()
        {
            Assert.AreEqual(0f, Stances.KneeBendDrop(0.9f, 0f), 1e-5f);
            Assert.AreEqual(0.45f, Stances.KneeBendDrop(0.9f, 60f), 1e-4f);
            Assert.AreEqual(0.9f, Stances.KneeBendDrop(0.9f, 90f), 1e-4f);
            // The default crouch: thigh 58 and shin 50 degrees from vertical on a 0.9 m leg.
            Assert.AreEqual(0.3723f, Stances.CrouchHipDrop(0.9f, Rules.CrouchThighDeg, Rules.CrouchShinDeg), 1e-3f);
        }

        [Test]
        public void CrouchKnee_FoldsToAboutAQuarterTurnNeverPast110()
        {
            Assert.AreEqual(108f, Stances.KneeFlexion(Rules.CrouchThighDeg, Rules.CrouchShinDeg), 1e-4f);
            Assert.That(Stances.KneeFlexion(Rules.CrouchThighDeg, Rules.CrouchShinDeg), Is.InRange(80f, 110f));
            Assert.That(Rules.CrouchThighDeg, Is.InRange(55f, 65f));
            Assert.That(Rules.CrouchTorsoPitchDeg, Is.InRange(8f, 12f));
            // Landing bend (thigh 28, shin 28 from vertical) stays far from the limit too.
            Assert.AreEqual(56f, Stances.KneeFlexion(28f, 28f), 1e-4f);
        }

        [Test]
        public void CrouchBend_ShrinksWithWalkingSpeed()
        {
            // Blend 0 = standing crouch: full 58 / 50. Blend 1 = walking: 0.65 of that. Half way in between.
            foreach (var c in new[] { (0f, 1f, 58f, 50f), (0.5f, 0.825f, 47.85f, 41.25f), (1f, 0.65f, 37.7f, 32.5f), (2f, 0.65f, 37.7f, 32.5f), (-1f, 1f, 58f, 50f) })
            {
                float scale = Stances.CrouchBendScale(c.Item1, Rules.CrouchWalkBend);
                Assert.AreEqual(c.Item2, scale, 1e-4f, "scale at blend " + c.Item1);
                Assert.AreEqual(c.Item3, Rules.CrouchThighDeg * scale, 1e-2f, "thigh");
                Assert.AreEqual(c.Item4, Rules.CrouchShinDeg * scale, 1e-2f, "shin");
            }
            // Walking bend: hips 0.9 * 0.5 * (2 - cos 37.7 - cos 32.5) = 0.1644 m down; still well above the full bend.
            Assert.AreEqual(0.1644f, Stances.CrouchHipDrop(0.9f, 37.7f, 32.5f), 1e-3f);
        }

        [Test]
        public void CrouchFeet_StayOnTheGround_ForEveryBlend()
        {
            const float thighLength = 0.45f, shinLength = 0.45f, hipHeight = 0.9f;
            foreach (float blend in new[] { 0f, 0.5f, 1f })
            {
                float scale = Stances.CrouchBendScale(blend, Rules.CrouchWalkBend);
                float thighDeg = Rules.CrouchThighDeg * scale, shinDeg = Rules.CrouchShinDeg * scale;
                float drop = Stances.CrouchHipDrop(thighLength + shinLength, thighDeg, shinDeg);
                // x forward, y up. The thigh points down and forward, the shin down and back.
                var hip = new UnityLikeVector(0f, hipHeight - drop);
                var knee = hip.Plus(thighLength * (float)System.Math.Sin(thighDeg * System.Math.PI / 180.0), -thighLength * (float)System.Math.Cos(thighDeg * System.Math.PI / 180.0));
                var foot = knee.Plus(-shinLength * (float)System.Math.Sin(shinDeg * System.Math.PI / 180.0), -shinLength * (float)System.Math.Cos(shinDeg * System.Math.PI / 180.0));
                Assert.AreEqual(0f, foot.Y, 0.03f, "foot height at blend " + blend);
                Assert.Less(System.Math.Abs(foot.X), 0.06f, "foot under the hips at blend " + blend);
                Assert.Greater(knee.X, 0f, "the knee is in front");
            }
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

        struct UnityLikeVector
        {
            public readonly float X, Y;
            public UnityLikeVector(float x, float y) { X = x; Y = y; }
            public UnityLikeVector Plus(float dx, float dy) => new UnityLikeVector(X + dx, Y + dy);
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
