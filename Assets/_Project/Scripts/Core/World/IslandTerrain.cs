using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Ground of the C2 island (ADR-204): the design sheet's height function (ellipse shore, ridge lines, peaks, pass dips) with towns levelled,
    // quarries terraced and two rivers cut in. Heights are metres above the sea floor plane: design 0 is SeaLevel here.
    // Everything on top of this (roads, pads) comes from WorldTerrain.
    internal sealed class IslandTerrain : WorldTerrain
    {
        public const float RiverDepth = 1.4f;      // water above the bed
        public const float FordRaise = 1.05f;      // a ford's bed stands this much higher: 0.35 m of water left
        const float Cell = 8f;                     // river / region lookup grid
        const float SeaFloor = 8f;                 // the seabed never goes deeper than this below the water

        readonly IslandSpec isl;
        readonly float sea, size;
        readonly uint seed;
        readonly int n;                            // grid cells per side
        readonly float[][] riverDist;              // [river][cz * n + cx]
        readonly int[][] riverIdx;                 // nearest centre-line sample of each grid corner
        readonly sbyte[] regionGrid;
        readonly float[] borderGrid;
        readonly List<float> fordX = new List<float>(), fordZ = new List<float>();
        public readonly float[] TownHeight;        // plateau height (design metres) of each town
        public readonly float[][] RiverX, RiverZ, RiverBedT;
        readonly float[] regSeedX, regSeedZ;
        readonly int[] regSeedRegion;

        public IslandTerrain(WorldSpec spec) : base(spec)
        {
            isl = spec.Island;
            sea = spec.SeaLevel;
            size = spec.Size;
            seed = unchecked((uint)spec.Seed);
            n = (int)Math.Ceiling(size / Cell) + 1;

            var rx = new List<float[]>(); var rz = new List<float[]>(); var rt = new List<float[]>();
            foreach (var river in isl.Rivers)
            {
                var s = IslandMath.CatmullRom(river.Points, 8f);
                int m = s.Length / 2;
                var xs = new float[m]; var zs = new float[m]; var ts = new float[m];
                float acc = 0f;
                for (int i = 0; i < m; i++)
                {
                    xs[i] = s[i * 2]; zs[i] = s[i * 2 + 1];
                    if (i > 0) acc += IslandMath.Dist(xs[i], zs[i], xs[i - 1], zs[i - 1]);
                    ts[i] = acc;
                }
                for (int i = 0; i < m; i++) ts[i] = acc > 0f ? ts[i] / acc : 0f;
                rx.Add(xs); rz.Add(zs); rt.Add(ts);
            }
            RiverX = rx.ToArray(); RiverZ = rz.ToArray(); RiverBedT = rt.ToArray();

            var seedX = new List<float>(); var seedZ = new List<float>(); var seedR = new List<int>();
            for (int r = 0; r < isl.Regions.Length; r++)
                for (int k = 0; k + 1 < isl.Regions[r].Seeds.Length; k += 2) { seedX.Add(isl.Regions[r].Seeds[k]); seedZ.Add(isl.Regions[r].Seeds[k + 1]); seedR.Add(r); }
            regSeedX = seedX.ToArray(); regSeedZ = seedZ.ToArray(); regSeedRegion = seedR.ToArray();

            // River distance and position along the river, on a grid: the exact nearest-sample search would cost 200 steps per height query.
            riverDist = new float[RiverX.Length][]; riverIdx = new int[RiverX.Length][];
            for (int r = 0; r < RiverX.Length; r++)
            {
                riverDist[r] = new float[n * n]; riverIdx[r] = new int[n * n];
                for (int cz = 0; cz < n; cz++)
                    for (int cx = 0; cx < n; cx++)
                    {
                        float x = cx * Cell, z = cz * Cell, best = float.MaxValue; int bi = 0;
                        for (int i = 0; i < RiverX[r].Length; i++)
                        {
                            float dx = RiverX[r][i] - x, dz = RiverZ[r][i] - z, d = dx * dx + dz * dz;
                            if (d < best) { best = d; bi = i; }
                        }
                        riverDist[r][cz * n + cx] = (float)Math.Sqrt(best);
                        riverIdx[r][cz * n + cx] = bi;
                    }
            }

            regionGrid = new sbyte[n * n];
            for (int cz = 0; cz < n; cz++)
                for (int cx = 0; cx < n; cx++) regionGrid[cz * n + cx] = (sbyte)RegionOf(cx * Cell, cz * Cell);
            borderGrid = new float[n * n];
            Chamfer();

            TownHeight = new float[isl.Towns.Length];
            for (int i = 0; i < TownHeight.Length; i++) TownHeight[i] = PlateauOf(isl.Towns[i]);
        }

        // ---- regions: nearest seed, the west bank pays a penalty on the east side of the river (design sheet) ----

        float RiverX0(float z)
        {
            float best = float.MaxValue, bx = 0f;
            var xs = RiverX[0]; var zs = RiverZ[0];
            for (int i = 0; i < xs.Length; i++) { float d = Math.Abs(zs[i] - z); if (d < best) { best = d; bx = xs[i]; } }
            return bx;
        }

        int RegionOf(float x, float z)
        {
            float best = float.MaxValue; int bi = -1;
            bool westOf = x < RiverX0(z);
            for (int k = 0; k < regSeedX.Length; k++)
            {
                int r = regSeedRegion[k];
                float d = IslandMath.Dist(x, z, regSeedX[k], regSeedZ[k]);
                if (z > 600f && r != 0 && ((isl.Regions[r].West == 1) != westOf)) d += 500f;
                if (r == 0 && z > 700f) d += 150f;
                if (d < best) { best = d; bi = r; }
            }
            return bi;
        }

        void Chamfer()
        {
            // distance (metres) to the nearest cell of another region, two-pass chamfer over the region grid
            const float far = 1e6f;
            for (int i = 0; i < borderGrid.Length; i++) borderGrid[i] = far;
            for (int cz = 0; cz < n; cz++)
                for (int cx = 0; cx < n; cx++)
                {
                    int r = regionGrid[cz * n + cx];
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int x = cx + dx, z = cz + dz;
                            if (x < 0 || z < 0 || x >= n || z >= n || regionGrid[z * n + x] == r) continue;
                            borderGrid[cz * n + cx] = Math.Min(borderGrid[cz * n + cx], Cell * 0.5f);
                        }
                }
            const float a = Cell, b = Cell * 1.4142135f;
            for (int cz = 0; cz < n; cz++)
                for (int cx = 0; cx < n; cx++)
                {
                    float v = borderGrid[cz * n + cx];
                    if (cx > 0) v = Math.Min(v, borderGrid[cz * n + cx - 1] + a);
                    if (cz > 0) v = Math.Min(v, borderGrid[(cz - 1) * n + cx] + a);
                    if (cx > 0 && cz > 0) v = Math.Min(v, borderGrid[(cz - 1) * n + cx - 1] + b);
                    if (cx < n - 1 && cz > 0) v = Math.Min(v, borderGrid[(cz - 1) * n + cx + 1] + b);
                    borderGrid[cz * n + cx] = v;
                }
            for (int cz = n - 1; cz >= 0; cz--)
                for (int cx = n - 1; cx >= 0; cx--)
                {
                    float v = borderGrid[cz * n + cx];
                    if (cx < n - 1) v = Math.Min(v, borderGrid[cz * n + cx + 1] + a);
                    if (cz < n - 1) v = Math.Min(v, borderGrid[(cz + 1) * n + cx] + a);
                    if (cx < n - 1 && cz < n - 1) v = Math.Min(v, borderGrid[(cz + 1) * n + cx + 1] + b);
                    if (cx > 0 && cz < n - 1) v = Math.Min(v, borderGrid[(cz + 1) * n + cx - 1] + b);
                    borderGrid[cz * n + cx] = v;
                }
        }

        public override void Locate(float x, float z, out int region, out float border)
        {
            int cx = (int)(x / Cell + 0.5f), cz = (int)(z / Cell + 0.5f);
            cx = cx < 0 ? 0 : cx >= n ? n - 1 : cx; cz = cz < 0 ? 0 : cz >= n ? n - 1 : cz;
            region = regionGrid[cz * n + cx];
            border = borderGrid[cz * n + cx];
        }

        // ---- the ground ----

        float Raw(float x, float z)
        {
            double dx = (x - isl.CentreX) / isl.RadiusX, dz = (z - isl.CentreZ) / isl.RadiusZ, ang = Math.Atan2(dz, dx);
            double r = Math.Sqrt(dx * dx + dz * dz) + (IslandMath.Fbm(x / 380.0, z / 380.0, 7, 3) - 0.5) * 0.26
                + 0.035 * Math.Sin(ang * 3 + 1) + 0.025 * Math.Sin(ang * 5 + 2) + 0.02 * Math.Sin(ang * 9);
            double hc = 1 - r;
            double bas = IslandMath.Lerp(-20, 7, IslandMath.Smooth(-0.09, 0.12, hc)), inl = IslandMath.Smooth(0.04, 0.28, hc);
            double m = (IslandMath.Fbm(x / 330.0, z / 330.0, 3, 4) - 0.38) * 50 + (IslandMath.Fbm(x / 85.0, z / 85.0, 5, 3) - 0.5) * 14;
            double rid = 0;
            for (int k = 0; k < isl.Ridges.Length; k++)
            {
                var rg = isl.Ridges[k];
                IslandMath.PolyDist(rg.Points, x, z, out double d, out double s);
                if (d > rg.Width * 3) continue;
                double env = Math.Exp(-(d / rg.Width) * (d / rg.Width)), al = 0.6 + 0.4 * IslandMath.Fbm(s / 240.0, k * 3.1, 11, 2) * 1.6;
                rid += rg.Amp * env * Math.Min(1.15, al) * (0.7 + 0.55 * IslandMath.Ridged(x / 150.0, z / 150.0, 21 + k, 3));
            }
            double dip = 1;
            foreach (var p in isl.Passes)
            {
                double dd = IslandMath.Dist(x, z, p.X, p.Z), w = p.Road ? 125.0 : 90.0;
                dip *= 1 - 0.8 * Math.Exp(-(dd / w) * (dd / w));
            }
            rid *= dip;
            double pk = 0;
            foreach (var p in isl.Peaks) { double dd = IslandMath.Dist(x, z, p.X, p.Z); pk += p.H * Math.Exp(-(dd / p.S) * (dd / p.S)); }
            return (float)(bas + inl * (m + isl.ReliefScale * (rid + pk * (0.6 + 0.4 * dip))));
        }

        // Plateau (design metres) a town stands on: the mean raw ground under its box, kept above the river-side floor.
        float PlateauOf(IslandTown t)
        {
            double sum = 0; int count = 0;
            for (int j = -3; j <= 3; j++)
                for (int i = -4; i <= 4; i++)
                {
                    IslandMath.TownToWorld(t, i / 4f * t.Width * 0.5f, j / 3f * t.Depth * 0.5f, out float x, out float z);
                    sum += Raw(x, z); count++;
                }
            float mean = (float)(sum / count);
            return Math.Max(t.Tier == 1 ? 17f : 6f, Math.Min(90f, mean));
        }

        // Distance outside a town's box (0 inside), by the rotated rectangle.
        public static float OutsideTown(IslandTown t, float x, float z)
        {
            IslandMath.WorldToTown(t, x, z, out float lx, out float lz);
            float ex = Math.Max(0f, Math.Abs(lx) - t.Width * 0.5f), ez = Math.Max(0f, Math.Abs(lz) - t.Depth * 0.5f);
            return (float)Math.Sqrt(ex * ex + ez * ez);
        }

        public void AddFord(float x, float z) { fordX.Add(x); fordZ.Add(z); }

        // River under the point: distance to the centre line, position along it (0 source .. 1 mouth), half width, bed height (design metres).
        public bool River(int r, float x, float z, out float dist, out float t, out float halfWidth, out float bed, out float slope)
        {
            float fx = x / Cell, fz = z / Cell;
            int cx = (int)Math.Floor(fx), cz = (int)Math.Floor(fz);
            cx = cx < 0 ? 0 : cx >= n - 1 ? n - 2 : cx; cz = cz < 0 ? 0 : cz >= n - 1 ? n - 2 : cz;
            float u = Math.Min(1f, Math.Max(0f, fx - cx)), v = Math.Min(1f, Math.Max(0f, fz - cz));
            var d = riverDist[r]; var ix = riverIdx[r];
            int o = cz * n + cx;
            dist = (d[o] * (1 - u) + d[o + 1] * u) * (1 - v) + (d[o + n] * (1 - u) + d[o + n + 1] * u) * v;
            int corner = (u > 0.5f ? 1 : 0) + (v > 0.5f ? n : 0);
            t = AlongRiver(r, ix[o + corner], x, z);
            var rv = isl.Rivers[r];
            halfWidth = rv.Hw0 + (rv.Hw1 - rv.Hw0) * (float)Math.Pow(t, rv.HwPow);
            float raise = 0f;
            for (int i = 0; i < fordX.Count; i++)
            {
                float dd = IslandMath.Dist(x, z, fordX[i], fordZ[i]);
                if (dd < 45f) raise = Math.Max(raise, FordRaise * (float)IslandMath.Smooth(45.0, 15.0, dd));
            }
            bed = rv.B0 * (float)Math.Pow(1 - t, rv.BedPow) + rv.B1 + raise;
            slope = rv.Sl0 + (rv.Sl1 - rv.Sl0) * (float)IslandMath.Smooth(0.0, 0.4, t);
            return dist <= 900f;
        }

        // Position along the river (0..1) of the point nearest to (x, z), refined around a nearby centre-line sample.
        float AlongRiver(int r, int near, float x, float z)
        {
            var xs = RiverX[r]; var zs = RiverZ[r]; var ts = RiverBedT[r];
            int lo = Math.Max(0, near - 4), hi = Math.Min(xs.Length - 1, near + 4), best = near;
            float bestD = float.MaxValue;
            for (int j = lo; j <= hi; j++)
            {
                float dx = xs[j] - x, dz = zs[j] - z, dd = dx * dx + dz * dz;
                if (dd < bestD) { bestD = dd; best = j; }
            }
            float t = ts[best], span = float.MaxValue;
            for (int k = Math.Max(0, best - 1); k <= Math.Min(xs.Length - 2, best); k++)
            {
                float ex = xs[k + 1] - xs[k], ez = zs[k + 1] - zs[k], l2 = ex * ex + ez * ez;
                if (l2 < 1e-6f) continue;
                float u = Math.Max(0f, Math.Min(1f, ((x - xs[k]) * ex + (z - zs[k]) * ez) / l2));
                float px = xs[k] + ex * u - x, pz = zs[k] + ez * u - z, off = px * px + pz * pz;
                if (off < span) { span = off; t = ts[k] + (ts[k + 1] - ts[k]) * u; }
            }
            return t;
        }

        // Water surface (world metres) at position t along a river.
        public float RiverSurface(int r, float t, float extraBed = 0f)
        {
            var rv = isl.Rivers[r];
            return sea + Math.Max(0f, rv.B0 * (float)Math.Pow(1 - t, rv.BedPow) + rv.B1 + RiverDepth);
        }

        public override float Natural(float x, float z)
        {
            double h = Raw(x, z);
            foreach (var q in isl.Quarries)
            {
                double d = IslandMath.Dist(x, z, q.X, q.Z);
                if (d >= q.Radius * 1.5) continue;
                double w = 1 - IslandMath.Smooth(q.Radius * 0.7, q.Radius * 1.5, d);
                double st = Math.Round((h - 14 * w) / 9) * 9;
                h = IslandMath.Lerp(h, Math.Max(6, st), w * 0.95);
            }
            for (int k = 0; k < isl.Towns.Length; k++)
            {
                var t = isl.Towns[k];
                float d = OutsideTown(t, x, z);
                if (d >= isl.TownBlend) continue;
                h = IslandMath.Lerp(TownHeight[k], h, IslandMath.Smooth(0.0, isl.TownBlend, d));
            }
            for (int r = 0; r < riverDist.Length; r++)
            {
                if (!River(r, x, z, out float d, out float t, out float hw, out float bed, out float sl)) continue;
                double cone = bed + Math.Max(0f, d - hw) * sl;
                if (h > cone) h = cone;
            }
            float world = h >= 0 ? sea + (float)h : sea + Math.Max(-SeaFloor, (float)h);
            return world;
        }

        // Rough distance inland from the waterline: about 8 m of walk per metre of height on the shore ramp.
        public override float Coast(float x, float z)
        {
            double dx = (x - isl.CentreX) / isl.RadiusX, dz = (z - isl.CentreZ) / isl.RadiusZ;
            if (dx * dx + dz * dz > 2.2) return -300f;     // far outside: sea without computing the ground
            return (Natural(x, z) - sea) * 8f;
        }
    }

    // Helpers of the design sheet's functions (double precision, integer hashing, no tables): same inputs, same island on every machine.
    internal static class IslandMath
    {
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;
        public static double Smooth(double a, double b, double x)
        {
            double t = (x - a) / (b - a); t = t < 0 ? 0 : t > 1 ? 1 : t;
            return t * t * (3 - 2 * t);
        }
        public static float Dist(float ax, float az, float bx, float bz) => (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        static double Hash(int ix, int iy, int s)
        {
            unchecked
            {
                uint h = (uint)ix * 374761393u ^ (uint)iy * 668265263u ^ (uint)s * 1274126177u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h ^ (h >> 16)) / 4294967296.0;
            }
        }

        static double Vn(double x, double y, int s)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y);
            int ix = (int)fx, iy = (int)fy;
            double dx = x - fx, dy = y - fy, u = dx * dx * (3 - 2 * dx), v = dy * dy * (3 - 2 * dy);
            double a = Hash(ix, iy, s), b = Hash(ix + 1, iy, s), c = Hash(ix, iy + 1, s), d = Hash(ix + 1, iy + 1, s);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }

        public static double Fbm(double x, double y, int s, int octaves)
        {
            double a = 0.5, f = 1, t = 0, n = 0;
            for (int i = 0; i < octaves; i++) { t += a * Vn(x * f, y * f, s + i * 17); n += a; a *= 0.5; f *= 2.03; }
            return t / n;
        }

        public static double Ridged(double x, double y, int s, int octaves)
        {
            double a = 0.5, f = 1, t = 0, n = 0;
            for (int i = 0; i < octaves; i++) { double v = 1 - Math.Abs(2 * Vn(x * f, y * f, s + i * 13) - 1); t += a * v * v; n += a; a *= 0.5; f *= 2.1; }
            return t / n;
        }

        public static void PolyDist(float[] p, double x, double z, out double dist, out double along)
        {
            dist = 1e18; along = 0; double acc = 0;
            for (int i = 0; i + 3 < p.Length; i += 2)
            {
                double ax = p[i], az = p[i + 1], vx = p[i + 2] - ax, vz = p[i + 3] - az, len2 = vx * vx + vz * vz;
                double t = len2 > 0 ? ((x - ax) * vx + (z - az) * vz) / len2 : 0; t = t < 0 ? 0 : t > 1 ? 1 : t;
                double d = Math.Sqrt((x - ax - vx * t) * (x - ax - vx * t) + (z - az - vz * t) * (z - az - vz * t)), L = Math.Sqrt(len2);
                if (d < dist) { dist = d; along = acc + t * L; }
                acc += L;
            }
        }

        // Centripetal-free Catmull-Rom through the points (x0, z0, x1, z1, ...), about `step` metres apart.
        public static float[] CatmullRom(float[] pts, float step)
        {
            int count = pts.Length / 2;
            var output = new List<float>();
            if (count < 2) return pts;
            for (int i = 0; i + 1 < count; i++)
            {
                int i0 = Math.Max(0, i - 1), i1 = i, i2 = i + 1, i3 = Math.Min(count - 1, i + 2);
                double ax = pts[i1 * 2], az = pts[i1 * 2 + 1], bx = pts[i2 * 2], bz = pts[i2 * 2 + 1];
                int seg = Math.Max(2, (int)Math.Ceiling(Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)) / step));
                for (int j = 0; j < seg; j++)
                {
                    double t = (double)j / seg, t2 = t * t, t3 = t2 * t;
                    for (int c = 0; c < 2; c++)
                    {
                        double p0 = pts[i0 * 2 + c], p1 = pts[i1 * 2 + c], p2 = pts[i2 * 2 + c], p3 = pts[i3 * 2 + c];
                        output.Add((float)(0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)));
                    }
                }
            }
            output.Add(pts[(count - 1) * 2]); output.Add(pts[(count - 1) * 2 + 1]);
            return output.ToArray();
        }

        // Town frame (origin at the middle of the box, +X along the avenue, +Z across) <-> world, rotated RotDeg.
        public static void TownToWorld(IslandTown t, float lx, float lz, out float x, out float z)
        {
            double r = t.RotDeg * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
            x = t.X + (float)(lx * c - lz * s); z = t.Z + (float)(lx * s + lz * c);
        }

        public static void WorldToTown(IslandTown t, float x, float z, out float lx, out float lz)
        {
            double r = t.RotDeg * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r), dx = x - t.X, dz = z - t.Z;
            lx = (float)(dx * c + dz * s); lz = (float)(-dx * s + dz * c);
        }
    }
}
