using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Road height along a route, kept under the grade limit by cut and fill (WP3, #248): the 12 degree redesign of the sections
    // roads.json measured above the limit. Pure numbers: the caller samples the ground along the road.
    public static class RoadProfile
    {
        // Heights of the road surface at each sample. It follows the ground but no neighbouring pair of samples is steeper than
        // maxGradeDeg; the ends keep the ground height. `feasible` is false when the ends are too far apart in height for the length
        // (then the surface climbs at the limit from the start and does not reach the far end: the route needs switchbacks).
        public static float[] Solve(IReadOnlyList<float> distance, IReadOnlyList<float> ground, float maxGradeDeg, out bool feasible)
        {
            if (distance == null || ground == null || distance.Count != ground.Count || distance.Count < 2) throw new ArgumentException("one ground height per distance, at least two");
            if (maxGradeDeg <= 0f || maxGradeDeg >= 90f) throw new ArgumentOutOfRangeException(nameof(maxGradeDeg));
            int n = distance.Count;
            float g = (float)Math.Tan(maxGradeDeg * Math.PI / 180.0);
            var y = new float[n];
            for (int i = 0; i < n; i++) y[i] = ground[i];
            feasible = Math.Abs(ground[n - 1] - ground[0]) <= g * (distance[n - 1] - distance[0]) + 1e-4f;

            // forward from the start, then backward from the end: each pass clamps a sample to what its neighbour allows
            for (int i = 1; i < n; i++)
            {
                float reach = g * (distance[i] - distance[i - 1]);
                y[i] = Math.Max(y[i - 1] - reach, Math.Min(y[i - 1] + reach, y[i]));
            }
            if (feasible)
            {
                y[n - 1] = ground[n - 1];
                for (int i = n - 2; i >= 0; i--)
                {
                    float reach = g * (distance[i + 1] - distance[i]);
                    y[i] = Math.Max(y[i + 1] - reach, Math.Min(y[i + 1] + reach, y[i]));
                }
                y[0] = ground[0];
                // the backward pass can break the forward one near the start: once more forward, from the fixed start
                for (int i = 1; i < n; i++)
                {
                    float reach = g * (distance[i] - distance[i - 1]);
                    y[i] = Math.Max(y[i - 1] - reach, Math.Min(y[i - 1] + reach, y[i]));
                }
                if (Math.Abs(y[n - 1] - ground[n - 1]) > 1e-3f) feasible = false;
            }
            return y;
        }

        // Metres of road a climb needs at the grade limit.
        public static float RequiredLength(float climb, float maxGradeDeg) => Math.Abs(climb) / (float)Math.Tan(maxGradeDeg * Math.PI / 180.0);

        // Hairpin turns a route needs when the straight way between its ends is too short for the climb: each turn adds about
        // `legLength` metres of road (a zigzag across the slope).
        public static int Hairpins(float climb, float straightLength, float maxGradeDeg, float legLength)
        {
            if (legLength <= 0f) throw new ArgumentOutOfRangeException(nameof(legLength));
            float missing = RequiredLength(climb, maxGradeDeg) - straightLength;
            return missing <= 0f ? 0 : (int)Math.Ceiling(missing / legLength);
        }

        public static float MaxGradeDeg(IReadOnlyList<float> distance, IReadOnlyList<float> height)
        {
            float best = 0f;
            for (int i = 1; i < distance.Count; i++)
            {
                float run = distance[i] - distance[i - 1];
                if (run <= 0f) continue;
                best = Math.Max(best, (float)(Math.Atan(Math.Abs(height[i] - height[i - 1]) / run) * 180.0 / Math.PI));
            }
            return best;
        }
    }
}
