using System;
using System.Collections.Generic;
using System.Numerics;

namespace Game.Core
{
    // Finds the magazine inside a weapon model's single mesh (#200), so a reload can take it off the gun and put it in
    // the hand. The low-poly guns are built from separate pieces (islands of connected triangles); the magazine is the
    // piece hanging lowest below the bore just ahead of the pistol grip (behind it on a bullpup), thin, and taller than
    // it is long. Pieces wholly inside it (a base plate, a window) go with it. Vertices are in the fitted weapon frame,
    // metres, with the right hand's grip at the origin: x right, y up, z toward the muzzle.
    public static class MagazineSplit
    {
        // Where a magazine may sit relative to the grip, and its shape limits.
        public const float MinGripGap = 0.03f, MaxGripGap = 0.32f, MinDrop = 0.03f, MaxWidth = 0.07f, MaxLength = 0.16f, MinHeight = 0.04f;
        public const float MaxShare = 0.4f; // of the gun's triangles; anything bigger is not a magazine
        const float Weld = 1e-4f, Inside = 0.006f;

        // Per triangle: true when it belongs to the magazine. minGripGap: a magazine well right over the grip (the
        // Bulwark) is found with a smaller (even negative) gap. Null when no piece looks like one (pistols: the magazine
        // is inside the grip; tube-fed and break-action guns have none).
        public static bool[] Find(Vector3[] v, int[] tris, bool behindGrip = false, float minGripGap = MinGripGap)
        {
            int triCount = tris.Length / 3;
            if (v == null || triCount == 0) return null;
            var comp = Islands(v, tris);
            // Bounds per island.
            var min = new Dictionary<int, Vector3>();
            var max = new Dictionary<int, Vector3>();
            var count = new Dictionary<int, int>();
            for (int t = 0; t < triCount; t++)
            {
                int c = comp[t];
                for (int k = 0; k < 3; k++)
                {
                    var p = v[tris[t * 3 + k]];
                    min[c] = min.TryGetValue(c, out var a) ? Vector3.Min(a, p) : p;
                    max[c] = max.TryGetValue(c, out var b) ? Vector3.Max(b, p) : p;
                }
                count[c] = count.TryGetValue(c, out var n) ? n + 1 : 1;
            }
            int best = -1;
            float bestLow = float.MaxValue;
            foreach (var c in count.Keys)
            {
                var lo = min[c]; var hi = max[c]; var size = hi - lo; var mid = (lo + hi) * 0.5f;
                float gap = behindGrip ? -mid.Z : mid.Z;
                if (gap < minGripGap || gap > MaxGripGap) continue;
                if (lo.Y > -MinDrop) continue; // must hang below the grip point
                if (size.X > MaxWidth || size.Z > MaxLength || size.Y < MinHeight || size.Y < size.Z * 0.8f) continue;
                if (count[c] > triCount * MaxShare) continue;
                if (lo.Y < bestLow) { bestLow = lo.Y; best = c; }
            }
            if (best < 0) return null;
            Vector3 bmin = min[best] - new Vector3(Inside), bmax = max[best] + new Vector3(Inside);
            var take = new HashSet<int> { best };
            foreach (var c in count.Keys)
                if (c != best && min[c].X >= bmin.X && min[c].Y >= bmin.Y && min[c].Z >= bmin.Z && max[c].X <= bmax.X && max[c].Y <= bmax.Y && max[c].Z <= bmax.Z)
                    take.Add(c);
            var result = new bool[triCount];
            for (int t = 0; t < triCount; t++) result[t] = take.Contains(comp[t]);
            return result;
        }

        // Island id per triangle: triangles sharing a vertex position (welded, so split UV seams stay joined) are one.
        static int[] Islands(Vector3[] v, int[] tris)
        {
            var weld = new Dictionary<(long, long, long), int>();
            var id = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var key = ((long)Math.Round(v[i].X / Weld), (long)Math.Round(v[i].Y / Weld), (long)Math.Round(v[i].Z / Weld));
                if (!weld.TryGetValue(key, out int w)) weld[key] = w = weld.Count;
                id[i] = w;
            }
            var parent = new int[weld.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Root(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = Root(id[tris[t]]), b = Root(id[tris[t + 1]]), c = Root(id[tris[t + 2]]);
                parent[b] = a; parent[Root(c)] = a;
            }
            var comp = new int[tris.Length / 3];
            for (int t = 0; t < comp.Length; t++) comp[t] = Root(id[tris[t * 3]]);
            return comp;
        }
    }
}
