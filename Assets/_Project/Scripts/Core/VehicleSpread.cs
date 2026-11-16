using System;
using System.Collections.Generic;

namespace Game.Core
{
    // ADR-198: where the world's vehicles stand. Every region gets one vehicle first (when it has a candidate),
    // the rest go where they are farthest from the others. Deterministic; candidates keep their order for ties.
    public static class VehicleSpread
    {
        public static List<ParkingSpot> Pick(IReadOnlyList<ParkingSpot> candidates, Func<ParkingSpot, int> regionOf, int regionCount, int count, float spacing)
        {
            var chosen = new List<ParkingSpot>();
            if (candidates == null || count <= 0) return chosen;
            for (int r = 0; r < regionCount && chosen.Count < count; r++)
            {
                int best = Farthest(candidates, chosen, spacing, s => regionOf(s) == r);
                if (best >= 0) chosen.Add(candidates[best]);
            }
            while (chosen.Count < count)
            {
                int best = Farthest(candidates, chosen, spacing, null);
                if (best < 0) break;
                chosen.Add(candidates[best]);
            }
            return chosen;
        }

        // Index of the candidate farthest from every chosen spot (more than `spacing` away), or -1. The first pick takes the first allowed one.
        static int Farthest(IReadOnlyList<ParkingSpot> candidates, List<ParkingSpot> chosen, float spacing, Func<ParkingSpot, bool> allowed)
        {
            int best = -1;
            float bestSqr = chosen.Count == 0 ? -1f : spacing * spacing;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (allowed != null && !allowed(candidates[i])) continue;
                float near = float.MaxValue;
                foreach (var c in chosen)
                {
                    float dx = candidates[i].X - c.X, dz = candidates[i].Z - c.Z;
                    near = Math.Min(near, dx * dx + dz * dz);
                }
                if (near > bestSqr) { bestSqr = near; best = i; }
            }
            return best;
        }
    }
}
