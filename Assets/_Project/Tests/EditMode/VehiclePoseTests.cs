using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // #102 / U2-U5: the pure parts of the seated pose, the vehicle camera's orbit and the seat names.
    public class VehiclePoseTests
    {
        [Test]
        public void Hands_FollowTheWheel_OnlyToNinetyDegrees()
        {
            Assert.AreEqual(45f, CombatantAnimation.HandWheelDeg(45f), 1e-4f);
            Assert.AreEqual(90f, CombatantAnimation.HandWheelDeg(150f), 1e-4f);
            Assert.AreEqual(-90f, CombatantAnimation.HandWheelDeg(-150f), 1e-4f);
        }

        [Test]
        public void LeanToReach_NoLeanWhenInReach_AndJustEnoughWhenNot()
        {
            var pivot = Vector3.zero;
            Vector3 sL = new Vector3(-0.2f, 0.5f, 0f), sR = new Vector3(0.2f, 0.5f, 0f);
            Vector3 tL = new Vector3(-0.2f, 0.3f, 0.5f), tR = new Vector3(0.2f, 0.3f, 0.5f);
            Assert.AreEqual(0f, CombatantAnimation.LeanToReach(pivot, Vector3.right, sL, tL, sR, tR, 0.6f, 40f), 1e-4f);

            var far = new Vector3(0f, 0f, 0.4f);
            float lean = CombatantAnimation.LeanToReach(pivot, Vector3.right, sL, tL + far, sR, tR + far, 0.6f, 40f);
            Assert.That(lean, Is.GreaterThan(1f).And.LessThan(40f));
            var leaned = Quaternion.AngleAxis(lean, Vector3.right) * sL;
            Assert.AreEqual(0.6f, Vector3.Distance(leaned, tL + far), 0.01f, "the shoulder comes forward to just the reach");
            Assert.Greater(leaned.z, sL.z, "a positive turn about the right axis tips the shoulders forward");

            Assert.AreEqual(40f, CombatantAnimation.LeanToReach(pivot, Vector3.right, sL, tL * 10f, sR, tR * 10f, 0.6f, 40f), 1e-4f);
        }

        [Test]
        public void HandFrame_KnucklesAlongTheBar_PalmTheWantedWay_RightAndLeftMirrored()
        {
            // A handlebar along X, reached from behind and above: the palms face forward / down.
            var toBar = new Vector3(0f, -0.4f, 0.6f);
            var want = new Vector3(0f, -1f, 1f);
            for (int hand = 0; hand < 2; hand++)
            {
                bool right = hand == 1;
                CombatantAnimation.HandFrame(toBar, Vector3.right, want, right, out var line, out var knuckles, out var palm);
                Assert.AreEqual(0f, Vector3.Dot(line, Vector3.right), 1e-4f, "the hand points square to the bar");
                Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(knuckles, Vector3.right)), 1e-4f, "knuckles along the bar");
                Assert.Greater(Vector3.Dot(palm, want), 0f, "palm the wanted way");
                // Palm down, fingers forward: the right hand's index finger is on its left, the left hand's on its right.
                Assert.AreEqual(right ? -1f : 1f, Mathf.Sign(knuckles.x), "thumb side");
            }
        }

        [Test]
        public void Orbit_SizedFromTheModel_PivotOverTheRoof_FurtherForLongerVehicles()
        {
            var low = new Bounds(new Vector3(0f, 0.55f, 0f), new Vector3(1.9f, 1.1f, 4.3f));
            var tall = new Bounds(new Vector3(0f, 1.0f, 0f), new Vector3(2.0f, 2.0f, 5.2f));
            var bike = new Bounds(new Vector3(0f, 0.55f, 0f), new Vector3(0.8f, 1.1f, 2.1f));
            VehicleCamera.Orbit(low, false, out float lowPivot, out float lowDistance);
            VehicleCamera.Orbit(tall, false, out float tallPivot, out float tallDistance);
            VehicleCamera.Orbit(bike, true, out float bikePivot, out float bikeDistance);
            Assert.Greater(lowPivot, low.max.y, "over the roof");
            Assert.Greater(tallPivot, tall.max.y, "over the roof");
            Assert.Less(lowPivot, tallPivot);
            Assert.Greater(tallDistance, lowDistance);
            Assert.Greater(lowDistance, low.size.z, "behind the whole car");
            Assert.Less(bikeDistance, lowDistance);
            Assert.GreaterOrEqual(bikePivot, 1.5f, "over the rider");
        }

        [Test]
        public void SeatNames_CarSeats_AndMotorcycleRiderAndPillion()
        {
            Assert.AreEqual("운전석", HudBackend.SeatName(0, false));
            Assert.AreEqual("조수석", HudBackend.SeatName(1, false));
            Assert.AreEqual("뒷좌석", HudBackend.SeatName(3, false));
            Assert.AreEqual("운전", HudBackend.SeatName(0, true));
            Assert.AreEqual("동승", HudBackend.SeatName(1, true));
            Assert.AreEqual("", HudBackend.SeatName(-1, true));
        }
    }
}
