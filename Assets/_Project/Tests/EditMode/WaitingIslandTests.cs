using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // #414 (ADR-381): where the waiting island is and what stands on it.
    public class WaitingIslandTests
    {
        const float MapSize = 2400f, FarClip = 700f, FogEnd = 640f, SeaMargin = 3000f, FloatSafe = 5000f;

        [Test]
        public void IsOutOfSightOfTheMap_AndTheMapOfIt()
        {
            Assert.GreaterOrEqual(WaitingIsland.GapToMap(MapSize), FarClip * 2f, "past the far clip twice over: neither side sees the other");
            Assert.Greater(WaitingIsland.GapToMap(MapSize), FogEnd);
        }

        [Test]
        public void StaysInsideTheSeaPlaneAndFloatPrecision()
        {
            float far = (float)System.Math.Sqrt(WaitingIsland.CentreX * WaitingIsland.CentreX + WaitingIsland.CentreZ * WaitingIsland.CentreZ) + WaitingIsland.Radius;
            Assert.Less(far, FloatSafe);
            Assert.GreaterOrEqual(WaitingIsland.CentreZ - WaitingIsland.Radius, -SeaMargin, "the platform stands on the sea plane");
        }

        [Test]
        public void Diameter_IsAbout80m()
        {
            Assert.AreEqual(80f, WaitingIsland.Radius * 2f, 0.01f);
            Assert.Less(WaitingIsland.WalkRadius, WaitingIsland.Radius);
        }

        [Test]
        public void Spawns_AreEnoughForAFullMatch_InsideTheWalkableCircle_AndApart()
        {
            var s = WaitingIsland.SpawnPoints();
            Assert.GreaterOrEqual(s.Count, 80);
            foreach (var p in s) Assert.IsTrue(WaitingIsland.Inside(p.x, p.z, WaitingIsland.Radius - WaitingIsland.WalkRadius), "inside the walk circle");
            float min = float.MaxValue;
            for (int i = 0; i < s.Count; i++)
                for (int j = i + 1; j < s.Count; j++)
                {
                    float dx = s[i].x - s[j].x, dz = s[i].z - s[j].z;
                    min = System.Math.Min(min, (float)System.Math.Sqrt(dx * dx + dz * dz));
                }
            Assert.GreaterOrEqual(min, 1.5f, "nobody spawns inside somebody");
        }

        [Test]
        public void Spawns_AvoidTheTowerBoothsAndLift()
        {
            foreach (var p in WaitingIsland.SpawnPoints())
            {
                float lx = p.x - WaitingIsland.CentreX, lz = p.z - WaitingIsland.CentreZ;
                Assert.Greater(Dist(lx, lz, WaitingIsland.Tower.x, WaitingIsland.Tower.z), WaitingIsland.TowerRadius);
                Assert.Greater(Dist(lx, lz, WaitingIsland.Elevator.x, WaitingIsland.Elevator.z), WaitingIsland.ElevatorRadius);
                foreach (var b in WaitingIsland.Booths) Assert.Greater(Dist(lx, lz, b.x, b.z), WaitingIsland.BoothRadius);
            }
        }

        [Test]
        public void Features_StandOnTheWalkableDeck_AndApart()
        {
            var all = new[] { WaitingIsland.Tower, WaitingIsland.Board, WaitingIsland.Elevator }.Concat(WaitingIsland.Booths).ToArray();
            foreach (var f in all) Assert.LessOrEqual(Dist(f.x, f.z, 0, 0), WaitingIsland.Radius - 3f, "on the deck");
            Assert.Greater(Dist(WaitingIsland.Tower.x, WaitingIsland.Tower.z, WaitingIsland.Elevator.x, WaitingIsland.Elevator.z), WaitingIsland.TowerRadius + WaitingIsland.ElevatorRadius);
        }

        [Test]
        public void Clamp_PullsStrayersBackToTheWalkCircle_AndLeavesInsidersAlone()
        {
            float x = WaitingIsland.CentreX + 100f, z = WaitingIsland.CentreZ;
            WaitingIsland.Clamp(ref x, ref z);
            Assert.AreEqual(WaitingIsland.CentreX + WaitingIsland.WalkRadius, x, 1e-3f);
            Assert.AreEqual(WaitingIsland.CentreZ, z, 1e-3f);
            float ix = WaitingIsland.CentreX + 5f, iz = WaitingIsland.CentreZ - 7f;
            float cx = ix, cz = iz;
            WaitingIsland.Clamp(ref cx, ref cz);
            Assert.AreEqual(ix, cx); Assert.AreEqual(iz, cz);
        }

        static float Dist(float ax, float az, float bx, float bz) { float dx = ax - bx, dz = az - bz; return (float)System.Math.Sqrt(dx * dx + dz * dz); }
    }
}
