using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // ADR-201: a parked prop car sits on a plane fitted to its four corners. The corner test also proves the pitch/roll signs.
    public class PropGroundingTests
    {
        const float HL = 2.2f, HW = 0.95f;
        static float Slope(float x, float z) => 10f + 0.05f * x + 0.1f * z;

        [Test]
        public void FlatGround_NoTiltAndPivotOnTheGround()
        {
            var f = PropGrounding.Place((x, z) => 5f, 100f, 100f, 0f, HL, HW);
            Assert.AreEqual(5f, f.Y, 1e-4f);
            Assert.AreEqual(0f, f.PitchDeg, 1e-3f);
            Assert.AreEqual(0f, f.RollDeg, 1e-3f);
        }

        [TestCase(0f)] [TestCase(37f)] [TestCase(123f)] [TestCase(250f)]
        public void Slope_FourCornersTouchTheGround(float yaw)
        {
            var f = PropGrounding.Place(Slope, 300f, 200f, yaw, HL, HW);
            var rot = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-f.PitchDeg, 0f, -f.RollDeg);
            foreach (var (l, r) in new[] { (HL, HW), (HL, -HW), (-HL, HW), (-HL, -HW) })
            {
                var p = new Vector3(300f, f.Y, 200f) + rot * new Vector3(r, 0f, l);
                Assert.AreEqual(Slope(p.x, p.z), p.y, 0.03f, $"corner ({l},{r}) yaw {yaw}");
            }
        }

        [Test]
        public void Slope_PivotStaysNearTheGroundUnderIt()
        {
            var f = PropGrounding.Place(Slope, 300f, 200f, 0f, HL, HW);
            Assert.LessOrEqual(Mathf.Abs(f.Y - Slope(300f, 200f)), 0.05f);
        }
    }
}
