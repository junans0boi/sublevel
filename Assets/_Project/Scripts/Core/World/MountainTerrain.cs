using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Height of one mountain massif above its foot (#247 T6, docs/research/03_mountains.md 2.4): a skeleton of ridge lines with
    // summits and saddles, bent by a domain warp, with terraces on the middle slopes, gullies running down from the crests
    // and a few short cliffs. Integer-hash noise and plain arithmetic only, so the same seed gives the same mountain on every machine.
    public sealed class MountainTerrain
    {
        public struct Node { public float X, Z, Peak; }

        // Tuning (metres). Slopes are kept under the walking limit by the widths: relief over half width stays under tan 30 degrees.
        const float RidgeHalfWidth = 295f;      // how far a ridge's flank reaches
        const float WarpAmplitude = 20f, WarpScale = 220f;
        const float DetailAmplitude = 2.4f, DetailScale = 55f;
        const float BenchRadius = 30f, BenchBlend = 40f;
        const int Benches = 10;
        const float SaddleDip = 0.2f;           // fraction of the lower summit between two summits
        const int Gullies = 4, Cliffs = 3;

        readonly uint seed;
        readonly float cx, cz, radius, relief;
        readonly Node[] nodes;
        readonly int[] armFrom, armTo;
        readonly float[] benchX, benchZ, benchH;
        readonly float[] gullyAx, gullyAz, gullyBx, gullyBz;
        readonly float[] cliffX, cliffZ, cliffDx, cliffDz, cliffLen, cliffHeight;

        public IReadOnlyList<Node> Summits => nodes;

        public MountainTerrain(uint seed, float centreX, float centreZ, float radius, float relief)
        {
            this.seed = seed;
            cx = centreX; cz = centreZ; this.radius = radius; this.relief = relief;
            float a0 = Hash(0) * 6.2831853f;
            // Three summits: the main one off-centre, one along each of two arms that part by 100-130 degrees.
            float a1 = a0 + 3.14159f + (Hash(1) - 0.5f) * 0.6f;
            float a2 = a1 + (1.75f + Hash(2) * 0.5f) * (Hash(3) < 0.5f ? 1f : -1f);
            nodes = new[]
            {
                new Node { X = cx + 0.12f * radius * (float)Math.Cos(a0), Z = cz + 0.12f * radius * (float)Math.Sin(a0), Peak = 1f },
                new Node { X = 0f, Z = 0f, Peak = 0.82f },
                new Node { X = 0f, Z = 0f, Peak = 0.74f },
            };
            float l1 = (0.50f + 0.12f * Hash(4)) * radius, l2 = (0.46f + 0.12f * Hash(5)) * radius;
            nodes[1].X = nodes[0].X + l1 * (float)Math.Cos(a1); nodes[1].Z = nodes[0].Z + l1 * (float)Math.Sin(a1);
            nodes[2].X = nodes[0].X + l2 * (float)Math.Cos(a2); nodes[2].Z = nodes[0].Z + l2 * (float)Math.Sin(a2);
            armFrom = new[] { 0, 0 };
            armTo = new[] { 1, 2 };

            // Benches sit on the flanks. Each tries a few places from the seed and takes the first whose ground is on a middle slope
            // (a quarter to two thirds of the relief) and not close to the bench before it.
            benchX = new float[Benches]; benchZ = new float[Benches]; benchH = new float[Benches];
            for (int b = 0; b < Benches; b++)
            {
                int arm = b % 2;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    float t = 0.1f + 0.8f * Hash(90 + b * 8 + attempt);
                    float ax = Lerp(nodes[armFrom[arm]].X, nodes[armTo[arm]].X, t), az = Lerp(nodes[armFrom[arm]].Z, nodes[armTo[arm]].Z, t);
                    float dx = nodes[armTo[arm]].X - nodes[armFrom[arm]].X, dz = nodes[armTo[arm]].Z - nodes[armFrom[arm]].Z;
                    float len = (float)Math.Sqrt(dx * dx + dz * dz);
                    float side = Hash(200 + b * 8 + attempt) < 0.5f ? 1f : -1f;
                    float off = 45f + 90f * Hash(300 + b * 8 + attempt);
                    float px = ax - dz / len * side * off, pz = az + dx / len * side * off;
                    float h = Shape(px, pz);
                    bool apart = true;
                    for (int o = 0; o < b; o++)
                    {
                        float ddx = px - benchX[o], ddz = pz - benchZ[o];
                        if (ddx * ddx + ddz * ddz < 4f * BenchRadius * BenchRadius) apart = false;
                    }
                    benchX[b] = px; benchZ[b] = pz;
                    if (apart && h > 0.25f * relief && h < 0.66f * relief) break;
                }
            }
            for (int b = 0; b < Benches; b++) benchH[b] = Shape(benchX[b], benchZ[b]);
            gullyAx = new float[Gullies]; gullyAz = new float[Gullies]; gullyBx = new float[Gullies]; gullyBz = new float[Gullies];
            for (int g = 0; g < Gullies; g++)
            {
                // from a point on a ridge down the fall line (away from the main summit)
                int arm = g % 2;
                float t = 0.25f + 0.5f * Hash(10 + g);
                float ax = Lerp(nodes[armFrom[arm]].X, nodes[armTo[arm]].X, t), az = Lerp(nodes[armFrom[arm]].Z, nodes[armTo[arm]].Z, t);
                float side = Hash(20 + g) < 0.5f ? 1f : -1f;
                float dx = nodes[armTo[arm]].X - nodes[armFrom[arm]].X, dz = nodes[armTo[arm]].Z - nodes[armFrom[arm]].Z;
                float len = (float)Math.Sqrt(dx * dx + dz * dz);
                float nx = -dz / len * side, nz = dx / len * side;
                float run = 120f + 80f * Hash(30 + g);
                gullyAx[g] = ax + nx * 30f; gullyAz[g] = az + nz * 30f;
                gullyBx[g] = ax + nx * (30f + run); gullyBz[g] = az + nz * (30f + run) + dz / len * 25f;
            }
            cliffX = new float[Cliffs]; cliffZ = new float[Cliffs]; cliffDx = new float[Cliffs]; cliffDz = new float[Cliffs];
            cliffLen = new float[Cliffs]; cliffHeight = new float[Cliffs];
            // Cliffs go where the ground is moderate already (gradient under 0.3 on the shape alone), so a step does not stack on a steep flank.
            for (int c = 0; c < Cliffs; c++)
            {
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float ang = Hash(400 + c * 12 + attempt) * 6.2831853f, r = (0.25f + 0.45f * Hash(500 + c * 12 + attempt)) * radius;
                    cliffX[c] = cx + r * (float)Math.Cos(ang); cliffZ[c] = cz + r * (float)Math.Sin(ang);
                    float h = Shape(cliffX[c], cliffZ[c]);
                    if (h > 0.2f * relief && Gradient(cliffX[c], cliffZ[c]) < 0.3f) break;
                }
                float d = Hash(60 + c) * 3.14159f;
                cliffDx[c] = (float)Math.Cos(d); cliffDz[c] = (float)Math.Sin(d);
                cliffLen[c] = 20f + 30f * Hash(70 + c);
                cliffHeight[c] = 5f + 4f * Hash(80 + c);
            }
        }

        float Gradient(float x, float z)
        {
            float gx = (Shape(x + 6f, z) - Shape(x - 6f, z)) / 12f, gz = (Shape(x, z + 6f) - Shape(x, z - 6f)) / 12f;
            return (float)Math.Sqrt(gx * gx + gz * gz);
        }

        float Hash(int i) => WorldNoise.Hash01(i, 77, unchecked(seed + 4001u));
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Smooth01(float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }

        // Skeleton, swell and warp only: metres above the foot, before benches, relief, gullies and cliffs.
        float Shape(float x, float z)
        {
            // domain warp: straight ridge lines become bent ones
            float wx = x + WarpAmplitude * WorldNoise.Fbm(x / WarpScale, z / WarpScale, unchecked(seed + 4011u), 2);
            float wz = z + WarpAmplitude * WorldNoise.Fbm(x / WarpScale + 17.3f, z / WarpScale - 9.1f, unchecked(seed + 4013u), 2);

            // skeleton: the highest of the ridge flanks, joined smoothly
            float sum = 0f;
            float skeleton = 0f;
            for (int a = 0; a < armFrom.Length; a++)
            {
                var n0 = nodes[armFrom[a]]; var n1 = nodes[armTo[a]];
                float ex = n1.X - n0.X, ez = n1.Z - n0.Z;
                float l2 = ex * ex + ez * ez;
                float t = Math.Max(0f, Math.Min(1f, ((wx - n0.X) * ex + (wz - n0.Z) * ez) / l2));
                float px = n0.X + ex * t - wx, pz = n0.Z + ez * t - wz;
                float d = (float)Math.Sqrt(px * px + pz * pz);
                // crest height: from one summit to the other, dipping into a saddle between them
                float lowPeak = Math.Min(n0.Peak, n1.Peak);
                float crest = Lerp(n0.Peak, n1.Peak, t) - SaddleDip * lowPeak * 4f * t * (1f - t);
                float flank = Smooth01(1f - d / RidgeHalfWidth);
                float h = crest * flank;
                skeleton = SmoothMax(skeleton, h, 0.12f);
                sum += h;
            }
            // a broad swell under everything so the lowest slopes do not start from a plate
            float dc = (float)Math.Sqrt((wx - cx) * (wx - cx) + (wz - cz) * (wz - cz));
            float swell = 0.22f * Smooth01(1f - dc / (radius * 1.05f));
            float shape = SmoothMax(skeleton, swell, 0.12f);
            return relief * shape;
        }

        // Metres above the foot (0 .. about `relief`); 0 outside the massif.
        public float Height(float x, float z)
        {
            float height = Shape(x, z);
            float dc = (float)Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));

            // benches: flat shelves on the middle slopes, 40-50 m across, the ground blending into them over 26 m
            for (int b = 0; b < benchX.Length; b++)
            {
                float dx = x - benchX[b], dz = z - benchZ[b];
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d >= BenchRadius + BenchBlend) continue;
                float t = Smooth01((d - BenchRadius) / BenchBlend);
                height = benchH[b] + (height - benchH[b]) * t;
            }

            // small relief that is calmer where the ground is already steep
            height += DetailAmplitude * WorldNoise.Fbm(x / DetailScale, z / DetailScale, unchecked(seed + 4021u), 2) * Smooth01(height / 12f);

            // gullies: 4 m deep, 9 m wide channels down the flanks
            for (int g = 0; g < Gullies; g++)
            {
                float d = DistanceToSegment(x, z, gullyAx[g], gullyAz[g], gullyBx[g], gullyBz[g]);
                float w = 1f - d / 16f;
                if (w > 0f) height -= 3.5f * Smooth01(w) * Smooth01(height / 14f);
            }
            // cliffs: a step of 6-14 m across about 9 m on one line, short ones only
            for (int c = 0; c < Cliffs; c++)
            {
                float rx = x - cliffX[c], rz = z - cliffZ[c];
                float along = rx * cliffDx[c] + rz * cliffDz[c];
                float across = -rx * cliffDz[c] + rz * cliffDx[c];
                if (Math.Abs(along) > cliffLen[c] * 0.5f + 6f) continue;
                float ends = 1f - Smooth01((Math.Abs(along) - cliffLen[c] * 0.5f) / 6f);
                height += cliffHeight[c] * ends * (Smooth01(across / 16f + 0.5f) - 0.5f);
            }

            // fade out at the edge of the massif
            float edge = Smooth01((radius * 1.1f - dc) / (radius * 0.25f));
            return Math.Max(0f, height * edge);
        }

        static float SmoothMax(float a, float b, float k)
        {
            float h = Math.Max(k - Math.Abs(a - b), 0f) / k;
            return Math.Max(a, b) + h * h * k * 0.25f;
        }

        static float DistanceToSegment(float x, float z, float ax, float az, float bx, float bz)
        {
            float ex = bx - ax, ez = bz - az;
            float l2 = ex * ex + ez * ez;
            float t = l2 > 1e-6f ? Math.Max(0f, Math.Min(1f, ((x - ax) * ex + (z - az) * ez) / l2)) : 0f;
            float px = ax + ex * t - x, pz = az + ez * t - z;
            return (float)Math.Sqrt(px * px + pz * pz);
        }
    }
}
