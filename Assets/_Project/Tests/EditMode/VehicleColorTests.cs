using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // S10 T8 (#348): colour variants chosen from the spawn spot.
    public class VehicleColorTests
    {
        [Test]
        public void Pick_IsDeterministicAndInRange()
        {
            for (int spot = 0; spot < 40; spot++)
            {
                int c = VehicleColors.Pick(spot, spot % 7);
                Assert.IsTrue(VehicleColors.Valid(c));
                Assert.AreEqual(c, VehicleColors.Pick(spot, spot % 7));
            }
        }

        [Test]
        public void Pick_SpreadsOverAtLeastThreeColoursAcrossTwentySpots()
        {
            var seen = Enumerable.Range(0, 20).Select(i => VehicleColors.Pick(i, i % 7)).Distinct().Count();
            Assert.GreaterOrEqual(seen, 3);
            Assert.That(VehicleColors.Count, Is.InRange(3, 5));
        }

        [Test]
        public void Tint_FirstIsTheModelsOwnColour_UnknownIndexFallsBack()
        {
            VehicleColors.Tint(0, out var r, out var g, out var b);
            Assert.AreEqual(1f, r + g + b - 2f, 1e-6f);
            VehicleColors.Tint(VehicleColors.None, out var r2, out _, out _);
            Assert.AreEqual(r, r2);
        }
    }
}
