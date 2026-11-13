using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay
{
    // Map maths shared by the minimap and the region card. Pure functions, no scene access.
    // World axes: +Z is north (heading 0), +X is east (heading 90).
    public static class Minimap
    {
        public const float ViewSize = 220f;      // reference pixels, square
        public const float ViewMeters = 300f;    // the square shows this many metres on a side, centred on the player
        public const float PixelsPerMeter = ViewSize / ViewMeters;

        // Where a world point lands inside the view, relative to its centre, north up.
        public static Vector2 ToView(Vector3 world, Vector3 centre)
        {
            return new Vector2((world.x - centre.x) * PixelsPerMeter, (world.z - centre.z) * PixelsPerMeter);
        }

        // Heading of the direction from `from` to `to` in degrees, 0 = north, 90 = east, 0..360.
        public static float Bearing(Vector3 from, Vector3 to)
        {
            return Mathf.Repeat(Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg, 360f);
        }

        // The player arrow's art points up (north); this is the z rotation that makes it point along `yaw`.
        public static float ArrowRotationZ(float yaw) => Mathf.Repeat(-yaw, 360f);

        // Rotation of an arrow that should point at a bearing while the viewer faces `yaw` (up = straight ahead).
        public static float RelativeArrowZ(float bearing, float yaw) => Mathf.Repeat(-Mathf.DeltaAngle(yaw, bearing), 360f);

        // Index in `regions` of the region whose centre is closest on the ground; -1 for none.
        public static int NearestRegion(IReadOnlyList<WorldInfo.Region> regions, Vector3 position)
        {
            int best = -1;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < regions.Count; i++)
            {
                float dx = regions[i].Centre.x - position.x, dz = regions[i].Centre.z - position.z;
                float sqr = dx * dx + dz * dz;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = i;
            }
            return best;
        }

        // The border between two neighbouring regions is drawn as the line halfway between their centres.
        public static Vector3 BorderPoint(Vector3 centreA, Vector3 centreB) => (centreA + centreB) * 0.5f;

        // Screen angle (z rotation of a horizontal line) of that border.
        public static float BorderAngleDeg(Vector3 centreA, Vector3 centreB)
        {
            // The border runs perpendicular to the line between the centres.
            float dx = centreB.x - centreA.x, dz = centreB.z - centreA.z;
            return Mathf.Atan2(dx, -dz) * Mathf.Rad2Deg;
        }
    }
}
