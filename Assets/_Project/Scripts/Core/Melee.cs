using System;

namespace Game.Core
{
    // Bare-handed punching (ADR-059 (1)). Loaded from the "Melee" block of rules.json.
    [Serializable]
    public class MeleeRules
    {
        // The rules in use. Defaults until the data loader replaces them.
        public static MeleeRules Current = new MeleeRules();

        public float PunchDamage = 12f;
        public float PunchRange = 1.3f;     // metres from the eyes to the target's chest
        public float PunchConeDeg = 60f;    // full opening angle of the cone around where the punch is aimed
        public float PunchInterval = 0.55f; // seconds between punches
        public float HeadMultiplier = 1.5f; // a punch that lands in the head zone
        public float ChestFraction = 0.61f; // the chest's height as a share of the target's current height
        public float BotPunchMeters = 1.5f; // an unarmed bot punches an enemy this close (feet to feet)
    }

    public static class Melee
    {
        // Is a point (given relative to the eyes: dx, dy, dz) within `range` of the eyes and inside the cone of
        // full opening angle `coneDeg` around the axis (ax, ay, az, any length)? A point right at the eyes is.
        public static bool InReach(float dx, float dy, float dz, float ax, float ay, float az, float range, float coneDeg)
        {
            double sqr = (double)dx * dx + (double)dy * dy + (double)dz * dz;
            if (!(sqr <= (double)range * range)) return false;
            if (sqr < 1e-10) return true;
            double axisLength = Math.Sqrt((double)ax * ax + (double)ay * ay + (double)az * az);
            if (axisLength < 1e-6) return false;
            double cos = (dx * (double)ax + dy * (double)ay + dz * (double)az) / (Math.Sqrt(sqr) * axisLength);
            double half = Math.Max(0.0, Math.Min(180.0, coneDeg)) * 0.5 * Math.PI / 180.0;
            return cos >= Math.Cos(half) - 1e-9;
        }

        // How high above the target's feet a punch aimed along the axis lands: where the aim line passes the
        // target at `horizontalDistance`, starting from eyes `eyeAboveFeet` over the target's feet. Clamped to
        // the target's body (0 .. `top`). `axisY` and `axisHorizontal` are the axis' vertical and horizontal parts.
        public static float HitHeight(float eyeAboveFeet, float horizontalDistance, float axisY, float axisHorizontal, float top)
        {
            float height;
            if (axisHorizontal > 1e-4f) height = eyeAboveFeet + axisY / axisHorizontal * Math.Max(0f, horizontalDistance);
            else height = axisY < 0f ? 0f : top; // straight down or up
            return Math.Max(0f, Math.Min(top, height));
        }

        // One punch on a zone: the head takes the head multiplier, everything else the plain damage.
        public static float Damage(HitZone zone, MeleeRules rules) =>
            rules.PunchDamage * (zone == HitZone.Head ? rules.HeadMultiplier : 1f);
    }
}
