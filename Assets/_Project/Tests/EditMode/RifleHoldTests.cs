using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-083 (#111): a long gun held in both hands, low ready unless aiming, posed in code.
    public class RifleHoldTests
    {
        static readonly RifleHoldRules R = new RifleHoldRules();

        [Test]
        public void Raising_TakesAboutAFifthOfASecond_LoweringIsSlower()
        {
            float ready = 0f;
            for (int i = 0; i < 9; i++) ready = RifleHold.ReadyStep(ready, true, 1f / 60f, R);
            Assert.That(ready, Is.EqualTo(1f), "fully up within 0.15 s");
            float down = RifleHold.ReadyStep(1f, false, 0.15f, R);
            Assert.That(down, Is.GreaterThan(0f).And.LessThan(1f), "still coming down after 0.15 s");
        }

        [Test]
        public void Aiming_IsLevel_LowReadyPointsDown_SprintMost()
        {
            var aim = RifleHold.Pose(5f, 0f, 1f, R);
            Assert.That(aim.Pitch, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(aim.Yaw, Is.EqualTo(0f).Within(1e-4f));
            var idle = RifleHold.Pose(0f, 0f, 0f, R);
            var run = RifleHold.Pose(5f, 0f, 0f, R);
            var sprint = RifleHold.Pose(7f, 1f, 0f, R);
            Assert.That(idle.Pitch, Is.InRange(15f, 35f));
            Assert.That(run.Pitch, Is.InRange(25f, 45f));
            Assert.That(sprint.Pitch, Is.InRange(30f, 50f));
            Assert.Less(idle.Pitch, run.Pitch);
            Assert.Less(run.Pitch, sprint.Pitch);
            Assert.Less(sprint.Yaw, 0f, "sprinting the muzzle is turned in across the body (left)");
            var half = RifleHold.Pose(5f, 0f, 0.5f, R);
            Assert.That(half.Pitch, Is.EqualTo(run.Pitch * 0.5f).Within(1e-3f), "raising blends, no pop");
        }

        [Test]
        public void Body_PitchesWithSpeed_RollsIntoASidestep_LeansIntoAStart()
        {
            Assert.That(RifleHold.TorsoPitch(0f, 0f, 0f, R), Is.EqualTo(0f));
            float run = RifleHold.TorsoPitch(5f, 0f, 0f, R), sprint = RifleHold.TorsoPitch(7f, 1f, 0f, R);
            Assert.Greater(run, 0f);
            Assert.Greater(sprint, run);
            Assert.Greater(RifleHold.TorsoPitch(0f, 0f, 20f, R), 0f, "speeding up leans forward");
            Assert.Less(RifleHold.TorsoPitch(0f, 0f, -20f, R), 0f, "stopping leans back");
            Assert.That(RifleHold.TorsoPitch(0f, 0f, 1000f, R), Is.EqualTo(R.MaxAccelPitchDeg).Within(1e-4f));
            Assert.Greater(RifleHold.StrafeRoll(4f, R), 0f);
            Assert.That(RifleHold.StrafeRoll(-100f, R), Is.EqualTo(-R.MaxStrafeRollDeg));
        }

        [Test]
        public void Bob_FollowsTheSteps_AndStopsWhenStanding()
        {
            RifleHold.Bob(0f, 1f, R, out float downAtStep, out _, out _);
            RifleHold.Bob(0.25f, 1f, R, out float downMid, out float sideMid, out _);
            RifleHold.Bob(0.5f, 1f, R, out float downNextStep, out _, out _);
            Assert.That(downAtStep, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(downMid, Is.EqualTo(R.BobDownMeters).Within(1e-5f), "two dips per cycle");
            Assert.That(downNextStep, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(sideMid, Is.EqualTo(R.BobSideMeters).Within(1e-5f));
            RifleHold.Bob(0.25f, 0f, R, out float still, out float stillSide, out float stillRoll);
            Assert.That(still + stillSide + stillRoll, Is.EqualTo(0f));
        }

        [Test]
        public void StartAndStop_TakeAMoment_NotAFrame_NotFloaty()
        {
            var p = new LocomotionParams();
            float vx = 0f, vz = 0f, t = 0f;
            Locomotion.Accelerate(ref vx, ref vz, 0f, p.WalkSpeed, 1f / 60f, p);
            Assert.That(vz, Is.GreaterThan(0f).And.LessThan(p.WalkSpeed * 0.2f), "one frame is not the full speed");
            for (t = 1f / 60f; vz < p.WalkSpeed; t += 1f / 60f) Locomotion.Accelerate(ref vx, ref vz, 0f, p.WalkSpeed, 1f / 60f, p);
            Assert.That(t, Is.InRange(0.1f, 0.2f), "up to a run in about 0.15 s");
            for (t = 0f; vz > 0f; t += 1f / 60f) Locomotion.Accelerate(ref vx, ref vz, 0f, 0f, 1f / 60f, p);
            Assert.That(t, Is.InRange(0.05f, 0.15f), "a stop is quicker than a start");
            Assert.That(vx, Is.EqualTo(0f));
        }

        [Test]
        public void TheWeaponFollowsTheLookPitch_AllShouldered_HalfAtLowReady_Clamped()
        {
            var r = new RifleHoldRules();
            Assert.AreEqual(30f, RifleHold.FollowedPitch(30f, 1f, r), 1e-4f, "shouldered: all of it (#121)");
            Assert.AreEqual(-15f, RifleHold.FollowedPitch(-30f, 0f, r), 1e-4f, "low ready: half");
            Assert.AreEqual(r.MaxLookPitchDeg, RifleHold.FollowedPitch(89f, 1f, r), 1e-4f, "no further than the limit");
            Assert.AreEqual(0f, RifleHold.FollowedPitch(0f, 0.5f, r), 1e-6f);
        }

        [Test]
        public void Shouldered_TheGripSitsAboveTheShoulder_SoTheSightsReachTheEyes()
        {
            var r = new RifleHoldRules();
            var aim = RifleHold.Pose(0f, 0f, 1f, r);
            Assert.Greater(aim.Y, 0f, "raised above the shoulder joint, not held at the chest (#121)");
            Assert.AreEqual(0f, aim.Pitch, 1e-4f, "level before the look pitch");
        }
    }
}
