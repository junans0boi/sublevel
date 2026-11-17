using System;

namespace Game.Core
{
    // What F may reach (ADR-276). Values are data (rules.json "Interact").
    [Serializable]
    public class InteractRules
    {
        public static InteractRules Current = new InteractRules();

        public float ReachMeters = 3f;       // a pickup this close to the feet (the server adds the lag slack)
        public float MaxRiseMeters = 1.6f;   // ... and no higher above the feet than this (a shelf is 1.4 m, the next storey 3 m)
        public float MaxDropMeters = 1.0f;   // ... and no lower below them
        public float ConeCosine = 0.5f;      // within this cosine of the view direction (flat), except right at the feet
        public float NearMeters = 0.9f;      // inside this (flat) the view direction does not matter
        public float SightLiftMeters = 0.12f; // the eye looks at the pickup this high above its floor
        public float SightTopMeters = 0.3f;   // ... or this high (a lamp-post of a pickup over a low sill)
        public float ItemBiasDegrees = 12f;  // a pickup beats a door unless the door is this much nearer the middle of the view
    }

    // Pure rules of choosing what F acts on. Line of sight is a delegate (the world's raycast), so the rules are testable.
    public static class Interact
    {
        // Flat angle in degrees between the view direction and the way to the target; 180 when looking along nothing.
        public static float BearingDegrees(float ex, float ez, float lx, float lz, float tx, float tz)
        {
            float llen = (float)Math.Sqrt(lx * lx + lz * lz);
            float dx = tx - ex, dz = tz - ez;
            float dlen = (float)Math.Sqrt(dx * dx + dz * dz);
            if (llen < 1e-4f) return 180f;
            if (dlen < 1e-3f) return 0f;
            float cos = (dx * lx + dz * lz) / (dlen * llen);
            return (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, cos))) * 180.0 / Math.PI);
        }

        // Same storey: the pickup is not higher above the feet than a table nor lower than a step down.
        public static bool SameFloor(float feetY, float itemY, InteractRules rules)
        {
            float rise = itemY - feetY;
            return rise <= rules.MaxRiseMeters && rise >= -rules.MaxDropMeters;
        }

        // In reach and in view, line of sight aside. `bearing` out: the angle off the view (small is better).
        // Reach is measured from the feet in 3D; the view cone counts beyond NearMeters (flat).
        public static bool Candidate(float feetX, float feetY, float feetZ, float ex, float ez, float lx, float lz,
            float ix, float iy, float iz, InteractRules rules, out float bearing)
        {
            bearing = 180f;
            if (!SameFloor(feetY, iy, rules)) return false;
            float dx = ix - feetX, dy = iy - feetY, dz = iz - feetZ;
            if (dx * dx + dy * dy + dz * dz > rules.ReachMeters * rules.ReachMeters) return false;
            float flat = (float)Math.Sqrt(dx * dx + dz * dz);
            bearing = BearingDegrees(ex, ez, lx, lz, ix, iz);
            if (flat <= rules.NearMeters) return true;
            return Math.Cos(bearing * Math.PI / 180.0) >= rules.ConeCosine;
        }

        // Ordering key of a candidate list: nearest the view first; a small distance term breaks ties.
        public static float Rank(float bearing, float distance) => bearing + distance * 2f;

        // Door or pickup: the pickup wins unless the door lies clearly nearer the middle of the view.
        public static bool PickupBeatsDoor(float pickupBearing, float doorBearing, InteractRules rules) =>
            pickupBearing - rules.ItemBiasDegrees <= doorBearing;
    }
}
