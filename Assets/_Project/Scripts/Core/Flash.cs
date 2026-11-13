using System;

namespace Game.Core
{
    // The flash grenade (ADR-105, PUBG_GAP_V2 G9): who it blinds, for how long, and the ringing in the ears that
    // follows. Pure numbers; the screen overlay and the sound duck read them (FlashOverlay).
    [Serializable]
    public class FlashRules
    {
        public static FlashRules Current = new FlashRules();

        public int MaxCarried = 3;
        public float FuseSeconds = 2f;       // counted from the throw
        public float BlindRadius = 20f;      // with a clear line of sight, nobody further than this is blinded
        public float MaxBlindSeconds = 5.5f; // point blank, looking at it
        public float MinBlindSeconds = 1f;   // the far edge, or looking away
        public float FrontDeg = 60f;         // angle between the view and the flash: this much or less counts as looking at it
        public float BackDeg = 150f;         // ... and this much or more as turned away
        public float BackFactor = 0.3f;      // share of the blindness left when turned away
        public float HoldShare = 0.6f;       // of the blind time the screen stays solid white; then it fades out
        public float TinnitusRadius = 6.5f;  // through a wall, ringing only inside this
        public float TinnitusMaxSeconds = 3f;
        public float TinnitusExtraSeconds = 2f; // ringing outlasts the blindness by this much
        public float DuckVolume = 0.12f;     // master volume while the ears ring
        public float DuckRecoverShare = 0.4f; // the last share of the ringing the volume climbs back in
    }

    public static class Flash
    {
        // Seconds blind for someone `distance` metres from the flash whose view is `angleDeg` off it. 0 without a
        // clear line of sight or beyond the radius; never under the minimum otherwise.
        public static float Blind(float distance, float angleDeg, bool hasSight, FlashRules r)
        {
            if (!hasSight || distance >= r.BlindRadius || r.BlindRadius <= 0f) return 0f;
            float d = distance < 0f ? 0f : distance;
            float baseSeconds = r.MaxBlindSeconds + (r.MinBlindSeconds - r.MaxBlindSeconds) * (d / r.BlindRadius);
            float t = r.BackDeg <= r.FrontDeg ? (angleDeg >= r.BackDeg ? 1f : 0f)
                : Math.Min(1f, Math.Max(0f, (angleDeg - r.FrontDeg) / (r.BackDeg - r.FrontDeg)));
            float factor = 1f + (r.BackFactor - 1f) * t;
            return Math.Max(r.MinBlindSeconds, baseSeconds * factor);
        }

        // Ringing: the blindness plus a tail; or, without being blinded, a short ring close by (through a wall).
        public static float Tinnitus(float distance, float blindSeconds, FlashRules r)
        {
            if (blindSeconds > 0f) return blindSeconds + r.TinnitusExtraSeconds;
            if (distance >= r.TinnitusRadius || r.TinnitusRadius <= 0f) return 0f;
            return r.TinnitusMaxSeconds * (1f - Math.Max(0f, distance) / r.TinnitusRadius);
        }

        // White-out opacity `elapsed` seconds into `blindSeconds`: solid for the hold share, then out.
        public static float Whiteness(float elapsed, float blindSeconds, FlashRules r)
        {
            if (blindSeconds <= 0f || elapsed < 0f || elapsed >= blindSeconds) return 0f;
            float hold = blindSeconds * r.HoldShare;
            if (elapsed <= hold) return 1f;
            float fade = 1f - (elapsed - hold) / Math.Max(1e-4f, blindSeconds - hold);
            return fade * fade; // eases out
        }

        // Master volume factor `elapsed` seconds into `ringSeconds` of ringing: ducked, then climbing back.
        public static float Volume(float elapsed, float ringSeconds, FlashRules r)
        {
            if (ringSeconds <= 0f || elapsed < 0f || elapsed >= ringSeconds) return 1f;
            float recover = ringSeconds * r.DuckRecoverShare;
            float left = ringSeconds - elapsed;
            if (left >= recover) return r.DuckVolume;
            return r.DuckVolume + (1f - r.DuckVolume) * (1f - left / Math.Max(1e-4f, recover));
        }
    }
}
