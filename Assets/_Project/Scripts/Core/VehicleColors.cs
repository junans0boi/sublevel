using System;

namespace Game.Core
{
    // Paint variants of a car (S10 T8, dv2): a handful of tints multiplied onto the model's own paint texture, chosen
    // by the spawn spot so every machine agrees. Only the index is replicated. No new asset, so no credits change.
    public static class VehicleColors
    {
        public const int None = -1;

        // RGB multipliers; index 0 is white = the model's own colour.
        static readonly float[][] Tints =
        {
            new[] { 1f, 1f, 1f },
            new[] { 0.55f, 0.6f, 0.75f },
            new[] { 0.35f, 0.35f, 0.37f },
            new[] { 0.95f, 0.9f, 0.55f },
            new[] { 0.5f, 0.7f, 0.55f },
            new[] { 1f, 0.32f, 0.28f },   // red: chosen in the race garage only (#514), never picked for a spawn spot
        };

        public static int Count => 5;   // the spawn palette (Pick); Valid also takes the garage-only tints after it

        // Same spot and model always give the same colour; the spots of one map spread over all of them.
        public static int Pick(int spot, int model)
        {
            unchecked
            {
                uint h = (uint)spot * 2654435761u ^ (uint)(model + 1) * 40503u;
                h ^= h >> 15;
                return (int)(h % (uint)Count);
            }
        }

        public static bool Valid(int index) => index >= 0 && index < Tints.Length;

        public static void Tint(int index, out float r, out float g, out float b)
        {
            var t = Tints[Valid(index) ? index : 0];
            r = t[0]; g = t[1]; b = t[2];
        }
    }
}
