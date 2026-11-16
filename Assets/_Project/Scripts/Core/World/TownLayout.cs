using System;
using System.Collections.Generic;

namespace Game.Core
{
    // One building lot of a town: centre in the town's frame (origin at the middle of its box, +X east, +Z north), width along the front,
    // depth, and the yaw of its front (0 = +Z, 180 = -Z), as Buildings.Generate expects them (#249 T8, replaces PlaceLots for towns).
    public struct TownLot
    {
        public BuildingKind Kind;
        public float X, Z, Width, Depth, YawDeg;
        public int Row;                 // 0/1 = the rows beside the avenue (north, south), 2/3 = the rows behind the back alleys
    }

    public struct TownStreet { public float X0, X1, Z0, Z1; public bool Avenue; }

    public sealed class TownLayoutResult
    {
        public readonly List<TownLot> Lots = new List<TownLot>();
        public readonly List<TownStreet> Streets = new List<TownStreet>();
        public int Requested, Placed;
        public float EndX;              // right edge of the last lot of any row (town frame): where an east station can stand
    }

    // A town as one avenue along X with a row of lots on each side, a second row behind a back alley on each side, and cross alleys
    // that cut the rows into blocks. Deterministic in (box, composition, seed). The big kinds go to the back rows, shops and homes face the avenue.
    public static class TownLayout
    {
        public const float Gap = 0.4f, AlleyWidth = 3.2f, BlockGap = 3.2f, RowDepthCap = 14f, Sidewalk = 1.5f;   // Sidewalk: kerb between the avenue and the first lot

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

        public static TownLayoutResult Place(float width, float depth, float avenueWidth, IReadOnlyList<(BuildingKind kind, int count)> composition, int seed, float reserveWest = 0f, float reserveEast = 0f)
        {
            var result = new TownLayoutResult();
            var pool = new List<BuildingKind>();
            foreach (var (kind, count) in composition) for (int i = 0; i < count; i++) pool.Add(kind);
            result.Requested = pool.Count;
            var rng = new Random(seed);
            // front rows get the low ranks first, back rows the high ones; ties shuffled
            var keyed = new List<(int rank, int tie, BuildingKind kind)>();
            foreach (var k in pool) keyed.Add((Rank(k), rng.Next(), k));
            keyed.Sort((a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.tie.CompareTo(b.tie));
            var queue = new List<BuildingKind>(); foreach (var e in keyed) queue.Add(e.kind);

            float hw = width / 2f, hd = depth / 2f;
            float avenueHalf = avenueWidth / 2f;
            result.Streets.Add(new TownStreet { X0 = -hw, X1 = hw, Z0 = -avenueHalf, Z1 = avenueHalf, Avenue = true });
            avenueHalf += Sidewalk;                                        // from here on the rows start at the kerb
            float side = hd - avenueHalf;                                  // room on each side of the avenue
            float frontDepth = Math.Min(RowDepthCap, (side - AlleyWidth) * 0.5f);
            if (frontDepth < 6f) return result;                            // a box too shallow for two rows
            // rows: 0 north front, 1 south front, 2 north back, 3 south back
            var rowZ = new[] { avenueHalf, -avenueHalf, avenueHalf + frontDepth + AlleyWidth, -(avenueHalf + frontDepth + AlleyWidth) };
            var rowDir = new[] { 1f, -1f, 1f, -1f };
            float backDepth = Math.Min(RowDepthCap, side - frontDepth - AlleyWidth);
            for (int s = 0; s < 2; s++)
            {
                float z0 = s == 0 ? avenueHalf + frontDepth : -(avenueHalf + frontDepth + AlleyWidth);
                result.Streets.Add(new TownStreet { X0 = -hw, X1 = hw, Z0 = Math.Min(z0, z0 + AlleyWidth), Z1 = Math.Max(z0, z0 + AlleyWidth) });
            }

            // Each item goes to the row of its group (shops and homes in front, the rest behind) that is shorter so far, which keeps both
            // sides of the avenue even; a row that is full or too shallow hands it to the other group.
            var cursor = new float[4]; var sinceCut = new int[4]; var cutEvery = new int[4];
            for (int r = 0; r < 4; r++) { cursor[r] = -hw + 1f + reserveWest; cutEvery[r] = 3 + rng.Next(3); }
            float DepthOf(int row) => row < 2 ? frontDepth : backDepth;
            foreach (var kind in queue)
            {
                Buildings.FootprintLimits(kind, out float minW, out float minD, out float maxW, out float maxD);
                int group = Rank(kind) <= 1 ? 0 : 1;
                bool placed = false;
                for (int attempt = 0; attempt < 2 && !placed; attempt++)
                {
                    int g = (group + attempt) % 2;
                    int first = g * 2, second = g * 2 + 1;
                    int[] order = cursor[first] <= cursor[second] ? new[] { first, second } : new[] { second, first };
                    foreach (int row in order)
                    {
                        float depthHere = DepthOf(row);
                        if (depthHere < 5f || minD > depthHere + 0.01f) continue;
                        float w = minW + (maxW - minW) * 0.35f * (float)rng.NextDouble();
                        float d = Math.Min(Math.Min(maxD, depthHere), minD + (maxD - minD) * (float)rng.NextDouble());
                        if (cursor[row] + w > hw - 1f - reserveEast) continue;
                        bool rowHouse = kind == BuildingKind.Shop || kind == BuildingKind.Stall;
                        float zc = rowZ[row] + rowDir[row] * (d * 0.5f);
                        float yaw = row % 2 == 0 ? 180f : 0f;   // north rows face south (-Z), south rows face north (+Z): every front looks at the avenue or the back alley
                        result.Lots.Add(new TownLot { Kind = kind, X = cursor[row] + w * 0.5f, Z = zc, Width = w, Depth = d, YawDeg = yaw, Row = row });
                        cursor[row] += w + (rowHouse ? Gap : 3f);
                        if (++sinceCut[row] >= cutEvery[row])
                        {
                            float cz0 = Math.Min(rowZ[row], rowZ[row] + rowDir[row] * depthHere), cz1 = Math.Max(rowZ[row], rowZ[row] + rowDir[row] * depthHere);
                            result.Streets.Add(new TownStreet { X0 = cursor[row], X1 = cursor[row] + BlockGap, Z0 = cz0, Z1 = cz1 });
                            cursor[row] += BlockGap; sinceCut[row] = 0; cutEvery[row] = 3 + rng.Next(3);
                        }
                        placed = true;
                        break;
                    }
                }
            }
            result.Placed = result.Lots.Count;
            foreach (var l in result.Lots) result.EndX = Math.Max(result.EndX, l.X + l.Width * 0.5f);
            return result;
        }
    }
}
