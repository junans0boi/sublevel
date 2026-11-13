using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    // The map marker on the compass strip (ADR-065 (6)): where a bearing lands for a view heading, wrapping at north
    // and held at the ends outside the visible arc; and the marker clearing when it is reached.
    public class MapMarkerTests
    {
        const float Ppd = CompassStrip.PixelsPerDegree;
        const float Limit = CompassStrip.MarkerLimit;

        [TearDown]
        public void TearDown() => MapMarker.Clear();

        static float Offset(float heading, float bearing, out bool clamped) => CompassStrip.MarkerOffset(heading, bearing, out clamped);

        [Test]
        public void Limit_IsInsideTheStripAndItsSoftEdge()
        {
            Assert.AreEqual(216f, Limit, 1e-4f, "560 px strip, 64 px inset at each end");
            Assert.Less(Limit + 13f, CompassStrip.StripWidth * 0.5f - 48f, "the arrow beside it stays clear of the mask fade");
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(0f, 30f, 120f)]
        [TestCase(0f, 330f, -120f)]
        [TestCase(90f, 90f, 0f)]
        [TestCase(90f, 120f, 120f)]
        [TestCase(180f, 160f, -80f)]
        [TestCase(0f, 54f, 216f)]     // exactly at the limit: still free
        [TestCase(0f, -54f, -216f)]
        public void Offset_IsTheAngleFromTheViewTimesPixelsPerDegree(float heading, float bearing, float expected)
        {
            Assert.AreEqual(expected, Offset(heading, bearing, out bool clamped), 1e-3f);
            Assert.IsFalse(clamped);
        }

        [TestCase(350f, 10f, 80f)]     // facing just west of north, marker just east of it: right
        [TestCase(10f, 350f, -80f)]    // and the other way round: left
        [TestCase(359.5f, 0.5f, 4f)]
        [TestCase(0.5f, 359.5f, -4f)]
        [TestCase(-10f, 10f, 80f)]     // headings outside 0..360 wrap too
        [TestCase(450f, 90f, 0f)]
        [TestCase(720f, 15f, 60f)]
        public void Offset_WrapsAroundNorth(float heading, float bearing, float expected)
        {
            Assert.AreEqual(expected, Offset(heading, bearing, out bool clamped), 1e-3f);
            Assert.IsFalse(clamped);
        }

        [TestCase(0f, 55f, 216f)]
        [TestCase(0f, 90f, 216f)]      // 100 m east while facing north: held at the right end
        [TestCase(0f, 270f, -216f)]    // west: left end
        [TestCase(0f, 180f, 216f)]     // straight behind goes to the right end
        [TestCase(350f, 80f, 216f)]    // 90 degrees right across north
        [TestCase(10f, 280f, -216f)]   // 90 degrees left across north
        public void Offset_IsHeldAtTheEnds_OutsideTheVisibleArc(float heading, float bearing, float expected)
        {
            Assert.AreEqual(expected, Offset(heading, bearing, out bool clamped), 1e-3f);
            Assert.IsTrue(clamped);
        }

        [Test]
        public void Offset_IsSymmetric()
        {
            for (float rel = -179f; rel < 180f; rel += 7f)
            {
                float right = Offset(40f, 40f + rel, out bool a);
                float left = Offset(40f, 40f - rel, out bool b);
                Assert.AreEqual(-right, left, 1e-3f, "rel " + rel);
                Assert.AreEqual(a, b);
                Assert.LessOrEqual(Mathf.Abs(right), Limit + 1e-3f);
            }
        }

        [Test]
        public void Reach_ClearsOnlyAfterTheMarkerWasFarther_ThanTenMetres()
        {
            var player = new Vector3(100f, 0f, 100f);
            MapMarker.Set(player + new Vector3(5f, 0f, 0f));
            Assert.IsFalse(MapMarker.TickReach(player), "put down next to the player: stays");
            Assert.IsTrue(MapMarker.Has);

            MapMarker.Set(player + new Vector3(100f, 0f, 0f));
            Assert.AreEqual(MapMarker.LocalOwner, MapMarker.OwnerId);
            Assert.IsFalse(MapMarker.TickReach(player));
            Assert.IsFalse(MapMarker.TickReach(player + new Vector3(89f, 0f, 0f)), "11 m short");
            Assert.IsTrue(MapMarker.Has);
            Assert.IsTrue(MapMarker.TickReach(player + new Vector3(91f, 5f, 0f)), "9 m: reached (height ignored)");
            Assert.IsFalse(MapMarker.Has);
            Assert.IsFalse(MapMarker.TickReach(player), "nothing left to clear");
        }

        [Test]
        public void Set_CarriesOwnerAndColour_AndBumpsTheVersion()
        {
            int before = MapMarker.Version;
            MapMarker.Set(new Vector3(1f, 2f, 3f), 7, Color.cyan);
            Assert.AreEqual(7, MapMarker.OwnerId);
            Assert.AreEqual(Color.cyan, MapMarker.Colour);
            Assert.AreEqual(before + 1, MapMarker.Version);
            MapMarker.Set(new Vector3(1f, 2f, 3f));
            Assert.AreEqual(MapMarker.LocalOwner, MapMarker.OwnerId);
            Assert.AreEqual(UiKit.Accent, MapMarker.Colour, "local markers are amber");
            MapMarker.Clear();
            Assert.AreEqual(before + 3, MapMarker.Version);
        }

        [Test]
        public void MetresText_IsCached()
        {
            Assert.AreEqual("235m", MapMarkerText.Metres(235));
            Assert.AreSame(MapMarkerText.Metres(100), MapMarkerText.Metres(100));
            Assert.AreEqual("0m", MapMarkerText.Metres(-3));
        }
    }
}
