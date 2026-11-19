using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-198/199 on the island (world.json values): 22 cars + 10 motorcycles (ADR-480), one in every region, off bridges and fords, clear of lots.
    public class VehicleWorldPlacementTests
    {
        static WorldPlan _plan;
        static WorldPlan Plan => _plan ?? (_plan = WorldGenerator.Generate(WorldSpec.CreateIsland()));

        static System.Collections.Generic.List<ParkingSpot> Picked()
        {
            var plan = Plan;
            return VehicleSpread.Pick(VehicleParking.Candidates(plan), c => plan.RegionAt(c.X, c.Z), plan.Regions.Count,
                new VehicleRules().SpawnCount + VehicleRules.MotorcycleDefaults().SpawnCount, new VehicleRules().ParkingSpacingMeters);
        }

        [Test]
        public void Picks32_OneInEveryRegion_SpacingHolds()
        {
            var got = Picked();
            Assert.AreEqual(32, got.Count, "22 cars + 10 motorcycles (ADR-480)");
            for (int r = 0; r < Plan.Regions.Count; r++)
                Assert.IsTrue(got.Any(s => Plan.RegionAt(s.X, s.Z) == r), $"region {r} has a vehicle");
            for (int a = 0; a < got.Count; a++)
                for (int b = a + 1; b < got.Count; b++)
                {
                    float dx = got[a].X - got[b].X, dz = got[a].Z - got[b].Z;
                    Assert.Greater(dx * dx + dz * dz, 40f * 40f - 1f, $"{a} and {b} too close");
                }
        }

        [Test]
        public void EverySpot_BesideARoad_ClearOfLots_NotOnWater()
        {
            foreach (var s in Picked())
            {
                float road = Plan.RoadDistance(s.X, s.Z);
                Assert.GreaterOrEqual(road, VehicleParking.HalfWidth + 0.3f, "off the road surface");
                Assert.LessOrEqual(road, 8f, "on the shoulder");
                Assert.GreaterOrEqual(Plan.Height(s.X, s.Z), Plan.SeaLevel + 0.8f, "dry ground");
                foreach (var lot in Plan.Lots)
                    Assert.GreaterOrEqual(lot.Distance(s.X, s.Z), VehicleParking.ClearLotMeters + VehicleParking.HalfLength, "no overlap with a building lot");
            }
        }

        [Test]
        public void NoCandidate_OnOrNearABridgeOrFord()
        {
            var deck = new System.Collections.Generic.List<float[]>();
            foreach (var road in Plan.Roads)
                for (int p = 0; p + 1 < road.X.Length; p++)
                    if ((road.Bridge != null && p < road.Bridge.Length && road.Bridge[p]) || (road.Ford != null && p < road.Ford.Length && road.Ford[p]))
                        deck.Add(new[] { road.X[p], road.Z[p], road.X[p + 1], road.Z[p + 1] });
            Assert.Greater(deck.Count, 0, "the island has bridge or ford segments");
            foreach (var c in VehicleParking.Candidates(Plan))
                foreach (var d in deck)
                {
                    float dx = d[2] - d[0], dz = d[3] - d[1], l2 = dx * dx + dz * dz;
                    float t = l2 < 1e-6f ? 0f : System.Math.Max(0f, System.Math.Min(1f, ((c.X - d[0]) * dx + (c.Z - d[1]) * dz) / l2));
                    float ex = d[0] + dx * t - c.X, ez = d[1] + dz * t - c.Z;
                    Assert.GreaterOrEqual(System.Math.Sqrt(ex * ex + ez * ez), VehicleParking.CrossingMarginMeters, "candidate near a crossing");
                }
        }
    }
}
