using System;
using System.Collections.Generic;

namespace Game.Core
{
    // One building lot of a town: centre in the town's frame (origin at the middle of its box, +X along the avenue, +Z across), width along the front,
    // depth, and the yaw of its front (0 = +Z, 180 = -Z), as Buildings.Generate expects them.
    public struct TownLot
    {
        public BuildingKind Kind;
        public float X, Z, Width, Depth, YawDeg;
        public int Row;                 // 0 north / 1 south: the rows that face the avenue; 2 north / 3 south: every row farther out
    }

    public struct TownStreet { public float X0, X1, Z0, Z1; public bool Avenue; }

    public sealed class TownLayoutResult
    {
        public readonly List<TownLot> Lots = new List<TownLot>();
        public readonly List<TownStreet> Streets = new List<TownStreet>();
        public int Requested, Placed;
        public float EndX;              // right edge of the last lot of any row (town frame)
    }

    // A town as blocks (ADR-208): one avenue along X, parallel streets on both sides, and cross streets, so the box fills with blocks. A block is two rows of lots
    // back to back with a yard between them; every lot faces a street. Shops stand in short attached terraces on the avenue; houses, villas and the big kinds
    // are spread over the farther rows with yards, empty plots and varied depth between them. Deterministic in (box, composition, seed).
    public static class TownLayout
    {
        public const float Verge = 12f, Gap = 0.4f, StreetWidth = 5f, RowDepthCap = 16f, Sidewalk = 1.5f, Yard = 4f, MinYardGap = 2.5f, Fill = 0.62f;
        const float CrossPitch = 62f;

        static int Rank(BuildingKind k)
        {
            switch (k)
            {
                case BuildingKind.Stall: case BuildingKind.Shop: return 0;           // along the avenue
                case BuildingKind.House: case BuildingKind.Villa: return 1;
                case BuildingKind.Market: case BuildingKind.School: case BuildingKind.Apartment: return 2;
                default: return 3;                                                   // barracks, factories, depots, towers, sheds at the back
            }
        }

        sealed class Seg { public float X0, X1, Used; public List<(BuildingKind kind, float w, float d)> Items = new List<(BuildingKind, float, float)>(); }
        sealed class Row { public int Band; public bool North, Inward; public float Near, Depth; public List<Seg> Segs = new List<Seg>(); }

        public static TownLayoutResult Place(float width, float depth, float avenueWidth, IReadOnlyList<(BuildingKind kind, int count)> composition, int seed, float reserveWest = 0f, float reserveEast = 0f)
        {
            var result = new TownLayoutResult();
            var pool = new List<BuildingKind>();
            foreach (var (kind, count) in composition) for (int i = 0; i < count; i++) pool.Add(kind);
            result.Requested = pool.Count;
            var rng = new Random(seed);
            float hw = width / 2f, hd = depth / 2f, av = avenueWidth / 2f;
            float edge = Math.Min(hd, hd - Verge);                         // rows keep a verge inside the box: the ground melts into the hills there
            result.Streets.Add(new TownStreet { X0 = -hw, X1 = hw, Z0 = -av, Z1 = av, Avenue = true });
            float firstEdge = av + Sidewalk;
            if (edge - firstEdge < 6f) return result;                        // a box too shallow for a row

            // cross streets, every ~62 m, clear of the station bays
            var cuts = new List<float>();
            float xa = -hw + 1f + reserveWest, xb = hw - 1f - reserveEast;
            if (xb - xa > 1.6f * CrossPitch)
            {
                int n = (int)((xb - xa) / CrossPitch);
                for (int i = 1; i < n; i++) cuts.Add(xa + (xb - xa) * i / n + ((float)rng.NextDouble() - 0.5f) * 10f);
                foreach (float c in cuts) result.Streets.Add(new TownStreet { X0 = c - StreetWidth / 2f, X1 = c + StreetWidth / 2f, Z0 = -hd, Z1 = hd });
            }

            // rows outward from the avenue on both sides: inward-facing, [yard], outward-facing, street, inward-facing, ...
            var rows = new List<Row>();
            for (int s = 0; s < 2; s++)
            {
                bool north = s == 0;
                float p = firstEdge;
                int band = 0;
                bool inward = true;
                while (true)
                {
                    float d = Math.Min(RowDepthCap, edge - p);
                    if (d < 8f) break;
                    if (!inward)
                    {
                        // an outward row needs a street and a row beyond it, or it would face the edge of the box
                        float after = p + d + StreetWidth + Sidewalk * 2f;
                        if (edge - after < 8f) break;
                    }
                    var row = new Row { Band = band++, North = north, Inward = inward, Near = p, Depth = d };
                    float u0 = xa, u1 = xb;
                    var edges = new List<float> { u0 };
                    foreach (float c in cuts) { edges.Add(c - StreetWidth / 2f - Sidewalk); edges.Add(c + StreetWidth / 2f + Sidewalk); }
                    edges.Add(u1);
                    for (int i = 0; i + 1 < edges.Count; i += 2) if (edges[i + 1] - edges[i] > 8f) row.Segs.Add(new Seg { X0 = edges[i], X1 = edges[i + 1] });
                    rows.Add(row);
                    if (inward) { p += d + Yard; }
                    else
                    {
                        float zs0 = p + d;                                  // the street between this row and the next
                        result.Streets.Add(new TownStreet { X0 = -hw, X1 = hw, Z0 = north ? zs0 : -(zs0 + StreetWidth + 2 * Sidewalk), Z1 = north ? zs0 + StreetWidth + 2 * Sidewalk : -zs0 });
                        p = zs0 + StreetWidth + 2 * Sidewalk;
                    }
                    inward = !inward;
                }
            }
            // streets listed above include the sidewalks; tighten them to the carriageway
            for (int i = 0; i < result.Streets.Count; i++)
            {
                var st = result.Streets[i];
                if (st.Avenue || st.Z0 == -hd) continue;
                float mid = (st.Z0 + st.Z1) / 2f;
                st.Z0 = mid - StreetWidth / 2f; st.Z1 = mid + StreetWidth / 2f; result.Streets[i] = st;
            }

            // order the kinds by rank (ties shuffled), then give each a segment: the low ranks to the rows beside the avenue, the rest outward
            var keyed = new List<(int rank, int tie, BuildingKind kind)>();
            foreach (var k in pool) keyed.Add((Rank(k), rng.Next(), k));
            keyed.Sort((a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.tie.CompareTo(b.tie));
            foreach (var (rank, _, kind) in keyed)
            {
                Buildings.FootprintLimits(kind, out float minW, out float minD, out float maxW, out float maxD);
                float w = minW + (maxW - minW) * 0.35f * (float)rng.NextDouble();
                bool terrace = kind == BuildingKind.Shop || kind == BuildingKind.Stall;
                Seg best = null; Row bestRow = null; float bestScore = float.MaxValue;
                for (int pass = 0; pass < 2 && best == null; pass++)
                    foreach (var row in rows)
                    {
                        if (row.Depth + 0.01f < minD) continue;
                        if (rank == 0 && row.Band != 0) continue;
                        if (rank >= 2 && row.Band == 0 && rows.Exists(r => r.Band > 0 && r.Depth >= minD)) continue;
                        foreach (var seg in row.Segs)
                        {
                            float free = seg.X1 - seg.X0 - seg.Used;
                            float need = w + (terrace ? Gap : MinYardGap);
                            if (need > free) continue;
                            float len = seg.X1 - seg.X0;
                            if (pass == 0 && seg.Used + need > Fill * len) continue;
                            // prefer the emptiest segment, nearer rows for lower ranks, a little noise so towns differ
                            float score = seg.Used / len + (rank <= 1 ? 0.18f : -0.12f) * row.Band + 0.25f * (float)rng.NextDouble();
                            if (score < bestScore) { bestScore = score; best = seg; bestRow = row; }
                        }
                    }
                if (best == null) continue;
                float d = Math.Min(Math.Min(maxD, bestRow.Depth), minD + (maxD - minD) * (float)rng.NextDouble());
                best.Items.Add((kind, w, d));
                best.Used += w + (terrace ? Gap : MinYardGap);
            }

            // lay out each segment: shops in terraces of up to three, the leftover length spread as yards and empty plots between the units
            foreach (var row in rows)
                foreach (var seg in row.Segs)
                {
                    if (seg.Items.Count == 0) continue;
                    var items = seg.Items;
                    for (int i = items.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = items[i]; items[i] = items[j]; items[j] = t; }
                    var units = new List<List<(BuildingKind kind, float w, float d)>>();
                    foreach (var it in items)
                    {
                        bool shop = it.kind == BuildingKind.Shop || it.kind == BuildingKind.Stall;
                        var last = units.Count > 0 ? units[units.Count - 1] : null;
                        if (shop && last != null && last.Count < 3 && (last[0].kind == BuildingKind.Shop || last[0].kind == BuildingKind.Stall)) last.Add(it);
                        else units.Add(new List<(BuildingKind, float, float)> { it });
                    }
                    float used = 0f;
                    foreach (var u in units) { foreach (var it in u) used += it.w; used += Gap * (u.Count - 1); }
                    float spare = seg.X1 - seg.X0 - used;
                    var weights = new float[units.Count + 1]; float sum = 0f;
                    for (int i = 0; i < weights.Length; i++) { weights[i] = 0.4f + (float)rng.NextDouble(); sum += weights[i]; }
                    float cursor = seg.X0;
                    for (int i = 0; i < units.Count; i++)
                    {
                        cursor += spare * weights[i] / sum;
                        foreach (var it in units[i])
                        {
                            float front = row.Inward ? row.Near : row.Near + row.Depth;           // distance from the centre line to the front edge
                            float centre = row.Inward ? front + it.d / 2f : front - it.d / 2f;
                            float z = row.North ? centre : -centre;
                            bool faceSouth = row.North == row.Inward;                              // north inward rows look to -Z; north outward rows look to +Z
                            result.Lots.Add(new TownLot
                            {
                                Kind = it.kind, X = cursor + it.w / 2f, Z = z, Width = it.w, Depth = it.d, YawDeg = faceSouth ? 180f : 0f,
                                Row = row.Band == 0 ? (row.North ? 0 : 1) : (row.North ? 2 : 3),
                            });
                            cursor += it.w + Gap;
                        }
                        cursor -= Gap;
                    }
                }
            result.Placed = result.Lots.Count;
            foreach (var l in result.Lots) result.EndX = Math.Max(result.EndX, l.X + l.Width * 0.5f);
            return result;
        }
    }
}
