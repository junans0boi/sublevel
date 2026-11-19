using System.Collections.Generic;
using Game.Core;
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

        // ---- Region states on the minimap (#178) ----

        // Texels of the zone overlay along one side of the island.
        public const int ZoneRes = 192;
        const int StripePeriod = 8; // texels: half on, half off

        // Colour of the state: warning amber, lockdown radiation green, bombardment orange, closed dark red.
        public static Color ZoneColour(ZoneState state)
        {
            switch (state)
            {
                case ZoneState.Warning: return new Color(1f, 0.78f, 0.2f);
                case ZoneState.Closed: return new Color(0.62f, 0.07f, 0.06f);
                default: return Color.clear;
            }
        }

        // One texel of the overlay. Next to close (warning): faint fill, dashed edge. Closing now (lockdown, bombardment):
        // diagonal stripes and a solid bright edge. Closed: heavy fill. Safe: nothing.
        public static Color32 ZoneTexel(ZoneState state, bool edge, int x, int y)
        {
            if (state == ZoneState.Safe) return new Color32(0, 0, 0, 0);
            bool stripe = (x + y) % StripePeriod < StripePeriod / 2;
            float a;
            switch (state)
            {
                case ZoneState.Warning: a = edge ? (stripe ? 0.95f : 0f) : 0.18f; break;
                case ZoneState.Closed: a = edge ? 1f : (stripe ? 0.72f : 0.55f); break;
                default: a = edge ? 1f : (stripe ? 0.5f : 0.2f); break;
            }
            var c = ZoneColour(state);
            return new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(a * 255f));
        }

        // A warned region blinks on the maps (#270): bright half a second, dim half a second.
        public const float BlinkPeriod = 1f;
        public static bool BlinkOn(float time) => Mathf.Repeat(time, BlinkPeriod) < BlinkPeriod * 0.5f;

        // The dim half of the blink: the warned region's texels at a third of their alpha.
        public static Color32 Dimmed(Color32 c) => new Color32(c.r, c.g, c.b, (byte)(c.a / 3));

        // The gas front (#270): where the gas has really got to, a bright line on the edge of the contaminated cells.
        public static readonly Color32 FrontTexel = new Color32(255, 70, 30, 255);

        // Short tag under a region's name on the minimap; "" while it is safe.
        public static string ZoneTag(ZoneState state)
        {
            switch (state)
            {
                case ZoneState.Warning: return "봉쇄 예정";
                case ZoneState.Closed: return "폐쇄";
                default: return "";
            }
        }
    }
}
