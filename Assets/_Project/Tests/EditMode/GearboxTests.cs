using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-558: the automatic gearbox.
    public class GearboxTests
    {
        static readonly GearboxRules R = new GearboxRules();
        const float Top = 50f; // m/s
        const float Dt = 0.02f;

        // Full throttle from rest to the top speed in a straight line: the gears go 1..6 in order, the needle stays under the redline.
        [Test]
        public void FullThrottle_ShiftsUpThroughEveryGear_InOrder()
        {
            var s = new GearboxState();
            int last = 0, ups = 0;
            float peak = 0f;
            for (float v = 0f; v <= Top; v += 0.05f)
            {
                s = Gearbox.Step(s, v, 1f, Top, true, Dt, R);
                if (s.Gear != last) { Assert.AreEqual(last + 1, s.Gear, $"one gear at a time at {v:F1} m/s"); last = s.Gear; ups++; }
                peak = System.Math.Max(peak, s.Rpm);
            }
            Assert.AreEqual(R.Gears, last, "top gear at the top speed");
            Assert.AreEqual(R.Gears, ups);
            Assert.LessOrEqual(peak, R.RedlineRpm);
        }

        [Test]
        public void AShift_CutsThePull_ThenGivesItBack()
        {
            var s = new GearboxState { Gear = 1 };
            float v = 0f;
            while (s.Gear == 1) { v += 0.05f; s = Gearbox.Step(s, v, 1f, Top, true, Dt, R); }
            Assert.IsTrue(s.Shifting);
            Assert.AreEqual(R.ShiftPull, Gearbox.Pull(s, R));
            for (float t = 0f; t < R.ShiftSeconds + Dt; t += Dt) s = Gearbox.Step(s, v, 1f, Top, true, Dt, R);
            Assert.IsFalse(s.Shifting);
            Assert.AreEqual(1f, Gearbox.Pull(s, R));
        }

        [Test]
        public void Slowing_ShiftsDown_AndTheNeedleDropsAfterAnUpshift()
        {
            var s = new GearboxState();
            for (float v = 0f; v <= Top; v += 0.05f) s = Gearbox.Step(s, v, 1f, Top, true, Dt, R);
            for (float v = Top; v >= 3f; v -= 0.05f) s = Gearbox.Step(s, v, -1f, Top, true, Dt, R);
            Assert.AreEqual(1, s.Gear, "back in first near a standstill");

            s = new GearboxState { Gear = 1 };
            float before = 0f, v2 = 0f;
            while (s.Gear == 1) { v2 += 0.05f; before = s.Rpm; s = Gearbox.Step(s, v2, 1f, Top, true, Dt, R); }
            for (int i = 0; i < 10; i++) s = Gearbox.Step(s, v2, 1f, Top, true, Dt, R);
            Assert.Less(s.Rpm, before * 0.85f, "the needle falls into the next gear");
        }

        [Test]
        public void Reverse_AndEngineOff_ReadRandN()
        {
            var s = Gearbox.Step(new GearboxState(), -2f, -1f, Top, true, Dt, R);
            Assert.AreEqual(-1, s.Gear);
            s = Gearbox.Step(s, 0f, 0f, Top, false, Dt, R);
            Assert.AreEqual(0, s.Gear);
        }
    }
}
