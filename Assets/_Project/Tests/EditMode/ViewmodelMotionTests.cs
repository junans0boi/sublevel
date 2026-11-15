using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class ViewmodelMotionTests
    {
        static ViewmodelInput In(float speed = 0f, float sprint = 0f, float aim = 0f, bool air = false, float vy = 0f, float yawRate = 0f) =>
            new ViewmodelInput { Dt = 1f / 60f, Speed = speed, RunSpeed = 4.7f, Sprint01 = sprint, Aim01 = aim, Airborne = air, VerticalSpeed = vy, LookYawRate = yawRate };

        static (float minY, float maxY, float minX, float maxX) Range(ViewmodelMotion m, ViewmodelInput i, int frames = 240)
        {
            float a = 9f, b = -9f, c = 9f, d = -9f;
            for (int n = 0; n < frames; n++)
            {
                var p = m.Step(i);
                if (n < frames / 3) continue; // let the blend settle
                a = System.Math.Min(a, p.Y); b = System.Math.Max(b, p.Y); c = System.Math.Min(c, p.X); d = System.Math.Max(d, p.X);
            }
            return (a, b, c, d);
        }

        [Test]
        public void Walking_BobsUpAndDownAndSideways_StandingBarelyMoves()
        {
            var walk = Range(new ViewmodelMotion(), In(4.7f));
            var idle = Range(new ViewmodelMotion(), In());
            Assert.Greater(walk.maxY - walk.minY, 0.008f, "vertical bob visible");
            Assert.Greater(walk.maxX - walk.minX, 0.01f, "side bob visible");
            Assert.Less(idle.maxY - idle.minY, 0.0055f, "idle is only breathing");
            Assert.Greater(idle.maxY - idle.minY, 0.001f, "but it does breathe");
        }

        [Test]
        public void Aiming_CutsTheBob()
        {
            var hip = Range(new ViewmodelMotion(), In(4.7f));
            var ads = Range(new ViewmodelMotion(), In(4.7f, aim: 1f));
            Assert.Less(ads.maxY - ads.minY, (hip.maxY - hip.minY) * 0.4f);
        }

        [Test]
        public void Sprint_LowersAndTipsTheGun_ButNotWhileAiming()
        {
            var m = new ViewmodelMotion(); ViewmodelPose p = default;
            for (int n = 0; n < 120; n++) p = m.Step(In(6.3f, sprint: 1f));
            Assert.Less(p.Y, -0.05f); Assert.Greater(p.PitchDeg, 12f);
            var a = new ViewmodelMotion(); ViewmodelPose q = default;
            for (int n = 0; n < 120; n++) q = a.Step(In(6.3f, sprint: 1f, aim: 1f));
            Assert.Greater(q.Y, -0.03f); Assert.Less(q.PitchDeg, 6f);
        }

        [Test]
        public void Landing_DipsThenReturns()
        {
            var m = new ViewmodelMotion();
            for (int n = 0; n < 30; n++) m.Step(In(air: true, vy: -8f));
            float low = 0f;
            for (int n = 0; n < 20; n++) low = System.Math.Min(low, m.Step(In()).Y);
            Assert.Less(low, -0.03f, "the gun dips on landing");
            ViewmodelPose end = default;
            for (int n = 0; n < 240; n++) end = m.Step(In());
            Assert.AreEqual(0f, end.Y, 0.004f, "and settles back");
        }

        [Test]
        public void Turning_DragsTheGunTheOtherWay_AndShotKicksBack()
        {
            var m = new ViewmodelMotion(); ViewmodelPose p = default;
            for (int n = 0; n < 10; n++) p = m.Step(In(yawRate: 200f));
            Assert.Less(p.YawDeg, -0.5f, "turning right lags the gun to the left");
            var k = new ViewmodelMotion(); k.Kick();
            var s = k.Step(In());
            Assert.Less(s.Z, -0.02f); Assert.Less(s.PitchDeg, -1.5f);
        }
    }
}
