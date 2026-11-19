using System;

namespace Game.Core
{
    // Hand and weapon motion of a reload and of drawing a weapon (CHARACTER_AUDIT #200), as pure functions of progress
    // so third and first person (and the tests) share them. Offsets are in the weapon's own frame: A along the muzzle,
    // U up, C across to the weapon's right (meters), measured from the right hand's grip.
    public enum ReloadKind { Magazine = 0, Pistol = 1, Shell = 2 }

    public struct ActionPose
    {
        public float Down, PitchDeg, RollDeg;      // the whole weapon: lowered (m), muzzle down (deg), top rolled right (deg)
        public float LeftA, LeftU, LeftC, LeftBlend; // the left hand's spot and how far it leaves its normal hold (0..1)
        public bool MagInHand;                     // a magazine (or a shell) is in the left hand
    }

    public static class ReloadMotion
    {
        public const float ShellCycleSeconds = 0.75f;
        // Share of a magazine reload at which the magazine leaves the well and is seated again.
        public const float MagOutAt = 0.2f, MagInAt = 0.62f;

        static float Ramp(float t, float a, float b) => t <= a ? 0f : t >= b ? 1f : (t - a) / (b - a);
        static float Ease(float x) => x * x * (3f - 2f * x);
        static float L(float a, float b, float k) => a + (b - a) * k;

        // t = 0..1 through the reload (shell reloads: seconds since the start in `shellSeconds`, looping while loading).
        public static ActionPose Reload(float t, ReloadKind kind, float shellSeconds = 0f)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            // The weapon comes in toward the chest and tips so the well faces the hand, then goes back.
            float body = Ease(Ramp(t, 0f, 0.15f)) * (1f - Ease(Ramp(t, 0.9f, 1f)));
            var p = new ActionPose { Down = 0.04f * body, PitchDeg = 18f * body, RollDeg = (kind == ReloadKind.Pistol ? 12f : 30f) * body };
            // Spots: the hold, the magazine well, the belt/pouch (away), the charging handle at the back.
            float wA = kind == ReloadKind.Pistol ? -0.02f : 0.06f, wU = kind == ReloadKind.Pistol ? -0.1f : -0.13f, wC = 0f;
            float aA = wA - 0.04f, aU = wU - 0.17f, aC = -0.14f;
            float rA = -0.1f, rU = 0.03f, rC = 0f;
            float hA = 0.14f, hU = -0.03f, hC = 0f; // normal hold (the caller blends back to its exact hold with LeftBlend)
            float a, u, c, blend = 1f;
            if (kind == ReloadKind.Shell)
            {
                // Hand shuttles between the shells (at the belt) and the tube; ends by returning to the fore-end.
                float into = Ease(Ramp(t, 0f, 0.1f)), outOf = Ease(Ramp(t, 0.9f, 1f));
                float cyc = (shellSeconds / ShellCycleSeconds) % 1f;
                float go = cyc < 0.5f ? Ease(cyc * 2f) : Ease((1f - cyc) * 2f); // 0 at the tube, 1 at the belt
                a = L(wA, aA, go); u = L(wU, aU, go); c = L(wC, aC, go);
                blend = into * (1f - outOf);
                p.MagInHand = go > 0.45f && blend > 0.9f;
                p.LeftBlend = blend; p.LeftA = a; p.LeftU = u; p.LeftC = c;
                return p;
            }
            if (t < 0.2f) { float k = Ease(t / 0.2f); a = L(hA, wA, k); u = L(hU, wU, k); c = L(hC, wC, k); }
            else if (t < 0.4f) { float k = Ease((t - 0.2f) / 0.2f); a = L(wA, aA, k); u = L(wU, aU, k); c = L(wC, aC, k); }
            else if (t < 0.62f) { float k = Ease((t - 0.4f) / 0.22f); a = L(aA, wA, k); u = L(aU, wU, k); c = L(aC, wC, k); }
            else if (t < 0.78f) { a = wA; u = wU; c = wC; }
            else if (t < 0.92f) { float k = Ease((t - 0.78f) / 0.14f); a = L(wA, rA, k); u = L(wU, rU, k); c = L(wC, rC, k); }
            else { float k = Ease((t - 0.92f) / 0.08f); a = L(rA, hA, k); u = L(rU, hU, k); c = L(rC, hC, k); }
            // The hand takes the old magazine out of the well (0.2), swaps it at the belt and seats the new one (0.62):
            // in between the gun shows an empty well (#200).
            p.MagInHand = (t > MagOutAt && t < MagInAt);
            p.LeftA = a; p.LeftU = u; p.LeftC = c; p.LeftBlend = blend;
            return p;
        }

        // Inspecting (#200, one key, cosmetic): the weapon comes in and rolls its right side up to the eye, then its left
        // with the support hand cupping the magazine well, then back into the hold. t = 0..1 over InspectSeconds.
        public const float InspectSeconds = 2.6f;
        public static ActionPose Inspect(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            float inOut = Ease(Ramp(t, 0f, 0.15f)) * (1f - Ease(Ramp(t, 0.85f, 1f)));
            // Roll: right side up (negative = top to the left) for the first half, left side up for the second.
            float side = L(-1f, 1f, Ease(Ramp(t, 0.4f, 0.6f)));
            var p = new ActionPose { Down = 0.05f * inOut, PitchDeg = -8f * inOut, RollDeg = 55f * side * inOut };
            float cup = Ease(Ramp(t, 0.45f, 0.6f)) * (1f - Ease(Ramp(t, 0.8f, 0.9f)));
            p.LeftA = 0.04f; p.LeftU = -0.1f; p.LeftC = 0f; p.LeftBlend = cup;
            return p;
        }

        // Drawing: from low at the hip up into the hold; progress 0..1 over the weapon's draw time.
        public static ActionPose Draw(float t)
        {
            float k = 1f - Ease(Math.Max(0f, Math.Min(1f, t)));
            return new ActionPose { Down = 0.28f * k, PitchDeg = 55f * k, RollDeg = -20f * k };
        }
    }
}
