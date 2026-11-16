using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-198: every region gets a vehicle, spacing holds, the pick is deterministic.
    public class VehicleSpreadTests
    {
        // Region = which 1000 m column the spot is in; a lopsided map: column 0 has 60 spots, columns 1..2 have 3 each.
        static List<ParkingSpot> Spots()
        {
            var l = new List<ParkingSpot>();
            for (int i = 0; i < 60; i++) l.Add(new ParkingSpot { X = i * 15f, Z = 0f });
            for (int c = 1; c <= 2; c++) for (int i = 0; i < 3; i++) l.Add(new ParkingSpot { X = c * 1000f + i * 50f, Z = 0f });
            return l;
        }
        static int Region(ParkingSpot s) => (int)(s.X / 1000f);

        [Test]
        public void EveryRegionGetsOne_EvenWhenOthersHaveManyMoreCandidates()
        {
            var got = VehicleSpread.Pick(Spots(), Region, 3, 3, 40f);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, got.Select(Region).ToArray());
        }

        [Test]
        public void SpacingHolds_AndCountIsCapped()
        {
            var got = VehicleSpread.Pick(Spots(), Region, 3, 20, 40f);
            Assert.LessOrEqual(got.Count, 20);
            for (int a = 0; a < got.Count; a++)
                for (int b = a + 1; b < got.Count; b++)
                    Assert.Greater(System.Math.Abs(got[a].X - got[b].X) + System.Math.Abs(got[a].Z - got[b].Z), 40f - 0.01f);
        }

        [Test]
        public void EmptyRegionIsSkipped_Deterministic()
        {
            var a = VehicleSpread.Pick(Spots(), Region, 5, 6, 40f);   // regions 3, 4 have no spots
            var b = VehicleSpread.Pick(Spots(), Region, 5, 6, 40f);
            Assert.AreEqual(6, a.Count);
            CollectionAssert.AreEqual(a.Select(s => s.X).ToArray(), b.Select(s => s.X).ToArray());
        }

        [Test]
        public void NoCandidates_ReturnsEmpty() =>
            Assert.AreEqual(0, VehicleSpread.Pick(new List<ParkingSpot>(), Region, 3, 5, 40f).Count);
    }
}
