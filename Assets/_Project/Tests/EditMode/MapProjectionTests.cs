using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // Map picture projection (batch F/R): the baker and the runtime map share it, so it has to round-trip.
    public class MapProjectionTests
    {
        const float Size = 1024f;

        [Test]
        public void WorldToUvToWorld_RoundTripsWithin1cm()
        {
            var rng = new System.Random(77);
            for (int i = 0; i < 100; i++)
            {
                float x = (float)rng.NextDouble() * Size, z = (float)rng.NextDouble() * Size;
                MapProjection.WorldToUv(Size, x, z, out float u, out float v);
                MapProjection.UvToWorld(Size, u, v, out float x2, out float z2);
                Assert.Less(MapProjection.Distance(x, z, x2, z2), 0.01f);

                MapProjection.WorldToPixel(Size, MapProjection.PictureSize, x, z, out float px, out float py);
                MapProjection.PixelToWorld(Size, MapProjection.PictureSize, px, py, out x2, out z2);
                Assert.Less(MapProjection.Distance(x, z, x2, z2), 0.01f);
            }
        }

        [Test]
        public void Corners_AndCentre()
        {
            MapProjection.WorldToUv(Size, 0f, 0f, out float u, out float v);
            Assert.AreEqual(0f, u);
            Assert.AreEqual(0f, v);
            MapProjection.WorldToUv(Size, Size, Size, out u, out v);
            Assert.AreEqual(1f, u);
            Assert.AreEqual(1f, v);
            MapProjection.WorldToUv(Size, 512f, 256f, out u, out v);
            Assert.AreEqual(0.5f, u, 1e-6f);
            Assert.AreEqual(0.25f, v, 1e-6f);
            MapProjection.WorldToPixel(Size, 2048, Size, Size, out float px, out float py);
            Assert.AreEqual(2048f, px, 1e-3f);
            Assert.AreEqual(2048f, py, 1e-3f);
        }

        [Test]
        public void Clamp_PullsOutsidePointsToTheEdge()
        {
            float x = -50f, z = 2000f;
            MapProjection.Clamp(Size, ref x, ref z);
            Assert.AreEqual(0f, x);
            Assert.AreEqual(Size, z);
            Assert.IsFalse(MapProjection.Contains(Size, -1f, 5f));
            Assert.IsTrue(MapProjection.Contains(Size, 0f, Size));
            x = 300f;
            z = 400f;
            MapProjection.Clamp(Size, ref x, ref z);
            Assert.AreEqual(300f, x);
            Assert.AreEqual(400f, z);
        }

        [Test]
        public void BearingAndDistance_Literals()
        {
            Assert.AreEqual(0f, MapProjection.BearingDeg(0f, 0f, 0f, 10f), 1e-3f);      // north
            Assert.AreEqual(90f, MapProjection.BearingDeg(0f, 0f, 10f, 0f), 1e-3f);     // east
            Assert.AreEqual(180f, MapProjection.BearingDeg(0f, 0f, 0f, -10f), 1e-3f);   // south
            Assert.AreEqual(270f, MapProjection.BearingDeg(0f, 0f, -10f, 0f), 1e-3f);   // west
            Assert.AreEqual(45f, MapProjection.BearingDeg(5f, 5f, 15f, 15f), 1e-3f);
            Assert.AreEqual(0f, MapProjection.BearingDeg(3f, 3f, 3f, 3f));
            Assert.AreEqual(5f, MapProjection.Distance(0f, 0f, 3f, 4f), 1e-4f);
            Assert.AreEqual("북", MapProjection.CompassName(359f));
            Assert.AreEqual("동", MapProjection.CompassName(90f));
            Assert.AreEqual("남서", MapProjection.CompassName(225f));
        }

        [Test]
        public void Grid_LabelsReadNorthWestFirst()
        {
            Assert.AreEqual("A1", MapProjection.GridLabel(Size, 8, 1f, Size - 1f));
            Assert.AreEqual("H8", MapProjection.GridLabel(Size, 8, Size - 1f, 1f));
            Assert.AreEqual("H8", MapProjection.GridLabel(Size, 8, 5000f, -5000f)); // clamped
            Assert.AreEqual("D5", MapProjection.GridLabel(Size, 8, 500f, 400f));
            Assert.AreEqual(0.25f, MapProjection.GridLine(2, 8), 1e-6f);
        }
    }
}
