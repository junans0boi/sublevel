using UnityEngine;

namespace Game.Gameplay
{
    // One place that makes a world sound 3D (ADR-086, #106): full spatial blend (panned from the listener), a custom
    // rolloff that falls like the logarithmic one (min/d) but reaches silence at the category's max distance (Unity's
    // own logarithmic rolloff holds its level past max, so a far shot sounded as loud as one at max), no Doppler.
    // Gunshots also lose their highs with distance (DistantCutoff), the "distant shot" character.
    public static class SpatialAudio
    {
        public enum Category { Body, Gunshot, Explosion, Vehicle, Elevator, Impact }

        // Full volume up to Min metres, silent from Max metres.
        public readonly struct Range
        {
            public readonly float Min, Max;
            public Range(float min, float max) { Min = min; Max = max; }
        }

        const int CurveKeys = 32;
        const float OpenCutoff = 22000f, FarCutoff = 1200f, MuffleFrom = 50f;

        public static Range For(Category category) => category switch
        {
            Category.Body => new Range(2f, 40f),         // footsteps, hits, punches, reloads, melee swings
            Category.Gunshot => new Range(12f, 750f),
            Category.Explosion => new Range(15f, 800f),  // grenades, vehicle explosions
            Category.Vehicle => new Range(6f, 150f),     // engine and horn
            Category.Elevator => new Range(3f, 60f),
            _ => new Range(3f, 60f),                     // Impact: glass and other short crashes
        };

        // Volume factor at `meters` from the listener: 1 inside Min, min/d beyond it, faded to 0 at Max.
        public static float Gain(Category category, float meters)
        {
            var r = For(category);
            if (meters <= r.Min) return 1f;
            if (meters >= r.Max) return 0f;
            float t = (meters - r.Min) / (r.Max - r.Min);
            return r.Min / meters * (1f - t * t);
        }

        // Low-pass cutoff (Hz) for a gunshot heard from `meters`: open up close, muffled toward the far end.
        public static float DistantCutoff(float meters)
        {
            var r = For(Category.Gunshot);
            float t = Mathf.InverseLerp(MuffleFrom, r.Max, meters);
            return Mathf.Exp(Mathf.Lerp(Mathf.Log(OpenCutoff), Mathf.Log(FarCutoff), t));
        }

        // The rolloff curve Unity reads over distance / max distance; sampled from Gain, denser close up.
        public static AnimationCurve Curve(Category category)
        {
            var r = For(category);
            var keys = new Keyframe[CurveKeys + 1];
            keys[0] = new Keyframe(0f, 1f);
            for (int i = 0; i < CurveKeys; i++)
            {
                float d = r.Min * Mathf.Pow(r.Max / r.Min, i / (float)(CurveKeys - 1));
                keys[i + 1] = new Keyframe(d / r.Max, Gain(category, d));
            }
            // Straight lines between keys, so the curve never overshoots between samples.
            for (int i = 0; i < keys.Length; i++)
            {
                float inSlope = i > 0 ? (keys[i].value - keys[i - 1].value) / (keys[i].time - keys[i - 1].time) : 0f;
                float outSlope = i < keys.Length - 1 ? (keys[i + 1].value - keys[i].value) / (keys[i + 1].time - keys[i].time) : 0f;
                keys[i].inTangent = inSlope;
                keys[i].outTangent = outSlope;
            }
            return new AnimationCurve(keys);
        }

        public static AudioSource Apply(AudioSource source, Category category)
        {
            if (source == null) return null;
            var r = For(category);
            source.spatialBlend = 1f;
            source.spread = 0f;
            source.dopplerLevel = 0f;
            source.minDistance = r.Min;
            source.maxDistance = r.Max;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Curve(category));
            return source;
        }

        static AudioListener _listener;

        // Metres from the active listener (the local player's camera), or 0 when there is none.
        public static float DistanceToListener(Vector3 position)
        {
            if (_listener == null || !_listener.isActiveAndEnabled) _listener = Object.FindAnyObjectByType<AudioListener>();
            return _listener != null ? Vector3.Distance(_listener.transform.position, position) : 0f;
        }
    }
}
