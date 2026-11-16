using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Which regions are confirmed in which order (ADR-169, SPEC 3-3): the final region is drawn first, then one
    // region at a time whose removal keeps the regions still open connected by land. Same algorithm and PRNG as
    // pollution_sim.html `closeOrder` so the same seed gives the same order there and here.
    public static class PollutionOrder
    {
        public const byte NoRegion = 255;

        // mulberry32 (the simulator's PRNG): the next value in [0, 1).
        public static double Next(ref uint state)
        {
            unchecked
            {
                state += 0x6D2B79F5u;
                uint t = (state ^ (state >> 15)) * (state | 1u);
                t = (t + (t ^ (t >> 7)) * (t | 61u)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        // Regions in confirmation order; the last entry is the final region. `adjacency[r]` lists the regions that
        // touch r by land. A map that is not connected still gets a full order (the connection rule is dropped
        // for steps where no region would keep it).
        public static int[] Plan(int regionCount, IReadOnlyList<int[]> adjacency, int seed, int forcedFinal = -1)
        {
            int n = Math.Max(0, regionCount);
            var order = new int[n];
            if (n == 0) return order;
            uint rng = unchecked((uint)seed);
            int fin = (int)(Next(ref rng) * n);
            if (forcedFinal >= 0 && forcedFinal < n) fin = forcedFinal; // a dedicated server's --region; the draw above still runs, so other seeds keep their order
            var open = new bool[n];
            for (int i = 0; i < n; i++) open[i] = true;
            bool wasConnected = IsConnected(n, adjacency, open);
            var cand = new List<int>(n);
            for (int step = 0; step < n - 1; step++)
            {
                cand.Clear();
                for (int r = 0; r < n; r++)
                {
                    if (!open[r] || r == fin) continue;
                    open[r] = false;
                    if (!wasConnected || IsConnected(n, adjacency, open)) cand.Add(r);
                    open[r] = true;
                }
                if (cand.Count == 0) for (int r = 0; r < n; r++) if (open[r] && r != fin) cand.Add(r);
                int pick = cand[(int)(Next(ref rng) * cand.Count)];
                order[step] = pick;
                open[pick] = false;
            }
            order[n - 1] = fin;
            return order;
        }

        // True when the open regions form one piece (zero or one open region counts as connected).
        public static bool IsConnected(int n, IReadOnlyList<int[]> adjacency, bool[] open)
        {
            int start = -1, count = 0;
            for (int i = 0; i < n; i++) if (open[i]) { count++; if (start < 0) start = i; }
            if (count <= 1) return true;
            var seen = new bool[n];
            var stack = new Stack<int>();
            seen[start] = true; stack.Push(start);
            int reached = 1;
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                var nb = u < adjacency.Count ? adjacency[u] : null;
                if (nb == null) continue;
                foreach (int v in nb)
                    if (v >= 0 && v < n && open[v] && !seen[v]) { seen[v] = true; reached++; stack.Push(v); }
            }
            return reached == count;
        }
    }
}
