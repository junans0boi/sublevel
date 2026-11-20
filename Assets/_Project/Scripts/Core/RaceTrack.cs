using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The race circuit of the waiting island (ADR-505, #438): a sealed hall east of the island, reached through a gate in the island's wall
    // into the pit, where the race cars stand. The circuit is an original layout (no real track copied): a long main straight north past the
    // pit, a hairpin, esses, a fast right-hander, a chicane and a final corner back onto the straight. It is drawn as a closed polygon of
    // corner points, each rounded with its own radius (Corners), sampled into a polyline (Path). Pure geometry shared by the builder, the
    // server and the tests. Coordinates are island-local metres (x east, z south, like WaitingIsland) unless a name says world.
    public static class RaceTrack
    {
        public const float Width = 12f;            // tarmac
        public const float Runoff = 4f;            // grass/gravel between the tarmac edge and the barrier, both sides
        public const float MainX = 62f;            // the main straight's centre line (north-bound past the pit)
        public const float LineZ = -26f;           // start/finish across the main straight, just north of the pit exit

        // Corner points (x, z) with the radius each is rounded to, in driving order (clockwise seen from above, north up).
        public static readonly (float x, float z, float r)[] Corners =
        {
            (MainX, 60f, 22f),      // final corner: onto the main straight
            (MainX, -125f, 13f),    // T1 \ hairpin
            (92f, -125f, 13f),      // T2 /
            (92f, -75f, 12f),       // T3 \ esses
            (135f, -95f, 15f),      // T4 /
            (180f, -55f, 25f),      // T5: the fast right-hander
            (180f, 20f, 14f),       // T6
            (140f, 20f, 12f),       // T7 \ chicane
            (125f, 58f, 12f),       // T8 /
        };

        // The pit: a box between the island's wall and the main straight's run-off, open to the track on its east side. Cars stand east of the
        // bollard line (people pass between the posts, cars do not), people may walk all of it.
        public const float PitX0 = 26f, PitX1 = 52f, PitZ0 = -23f, PitZ1 = -6f;
        public const float BollardX = 36f;
        // The hall round it all (walls and roof); every vehicle inside is a race car (no damage, no fuel, no roadkill, ADR-508).
        public const float HallX0 = 40f, HallX1 = 196f, HallZ0 = -140f, HallZ1 = 75f;
        public const float HallHeight = 20f;      // as high as the island's wall: the gate is a plain cut in it

        // Where the race cars stand (local x, z, yaw in degrees; 90 = east, out to the track): a front row of three and two behind, in the gaps.
        static readonly (float x, float z, float yaw)[] Grid =
        {
            (47f, -19.5f, 90f), (47f, -14.5f, 90f), (47f, -9.5f, 90f),
            (40.5f, -17f, 90f), (40.5f, -12f, 90f),
        };
        public static int Cars => Grid.Length;     // ADR-506: measured cost per car, see docs/RACE_SPEC.md
        public static IReadOnlyList<(float x, float z, float yaw)> GridSpots() => Grid;

        // ---- the centre line ----

        // A sample of the centre line: place, distance from the line (start/finish), the radius of the corner it is on (0 on a straight) and
        // which way that corner turns (+1 towards the left normal, -1 the right, 0 straight). Left normal of a direction (dx, dz) = (-dz, dx).
        public struct Point { public float X, Z, S, Radius; public int Turn; }

        static Point[] _path;
        static float _lap;
        public static IReadOnlyList<Point> Path { get { Build(); return _path; } }
        public static float LapLength { get { Build(); return _lap; } }

        const float StraightStep = 10f, ArcStepDeg = 4f;

        static void Build()
        {
            if (_path != null) return;
            var raw = new List<Point>();
            var corner = new List<int>();   // which corner each arc sample belongs to
            int n = Corners.Length;
            for (int i = 0; i < n; i++)
            {
                var a = Corners[(i + n - 1) % n]; var p = Corners[i]; var b = Corners[(i + 1) % n];
                float u1x = p.x - a.x, u1z = p.z - a.z, l1 = (float)Math.Sqrt(u1x * u1x + u1z * u1z); u1x /= l1; u1z /= l1;
                float u2x = b.x - p.x, u2z = b.z - p.z, l2 = (float)Math.Sqrt(u2x * u2x + u2z * u2z); u2x /= l2; u2z /= l2;
                float cross = u1x * u2z - u1z * u2x, dot = u1x * u2x + u1z * u2z;
                float th = (float)Math.Atan2(Math.Abs(cross), dot), t = p.r * (float)Math.Tan(th * 0.5f);
                int turn = cross > 0f ? 1 : -1;
                float t1x = p.x - u1x * t, t1z = p.z - u1z * t;
                float cx = t1x - u1z * turn * p.r, cz = t1z + u1x * turn * p.r;      // the centre lies on the inside (left normal x turn)
                float a0 = (float)Math.Atan2(t1z - cz, t1x - cx);
                int steps = Math.Max(2, (int)Math.Ceiling(th * 180f / Math.PI / ArcStepDeg));
                for (int k = 0; k <= steps; k++)
                {
                    float ang = a0 + turn * th * k / steps;
                    raw.Add(new Point { X = cx + p.r * (float)Math.Cos(ang), Z = cz + p.r * (float)Math.Sin(ang), Radius = p.r, Turn = turn });
                    corner.Add(i);
                }
            }
            // The straights between corners, cut into short pieces (the builder lights and tiles them; Project scans pieces).
            var pts = new List<Point>();
            for (int i = 0; i < raw.Count; i++)
            {
                var a = raw[i]; var b = raw[(i + 1) % raw.Count];
                pts.Add(a);
                if (corner[i] == corner[(i + 1) % raw.Count]) continue;   // within an arc
                float len = Dist(a, b);
                int k = (int)Math.Ceiling(len / StraightStep);
                for (int j = 1; j < k; j++) pts.Add(new Point { X = a.X + (b.X - a.X) * j / k, Z = a.Z + (b.Z - a.Z) * j / k });
            }
            float s = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                var q = pts[i]; q.S = s; pts[i] = q;
                s += Dist(pts[i], pts[(i + 1) % pts.Count]);
            }
            _lap = s;
            _path = pts.ToArray();
            // Distances count from the start/finish line.
            float line = RawProject(MainX, LineZ, out _);
            for (int i = 0; i < _path.Length; i++) _path[i].S = Wrap(_path[i].S - line);
        }

        static float Dist(Point a, Point b) { float dx = b.X - a.X, dz = b.Z - a.Z; return (float)Math.Sqrt(dx * dx + dz * dz); }
        static float Wrap(float s) { s %= _lap; return s < 0f ? s + _lap : s; }

        // Distance along the path (as stored) of the nearest centre-line point, and the offset from it (positive = left of the driving direction).
        static float RawProject(float lx, float lz, out float offset)
        {
            float best = float.MaxValue, bestS = 0f; offset = 0f;
            for (int i = 0; i < _path.Length; i++)
            {
                var a = _path[i]; var b = _path[(i + 1) % _path.Length];
                float dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
                float u = len2 > 1e-6f ? Math.Max(0f, Math.Min(1f, ((lx - a.X) * dx + (lz - a.Z) * dz) / len2)) : 0f;
                float px = a.X + dx * u, pz = a.Z + dz * u, ex = lx - px, ez = lz - pz, d2 = ex * ex + ez * ez;
                if (d2 >= best) continue;
                best = d2;
                float len = (float)Math.Sqrt(len2);
                bestS = a.S + len * u;
                offset = len > 1e-6f ? (-dz * ex + dx * ez) / len : 0f;
            }
            return bestS;
        }

        // Distance along the lap from the line (0..LapLength, the race runs north on the main straight) and the offset from the centre line.
        public static (float s, float offset) Project(float lx, float lz)
        {
            Build();
            float s = RawProject(lx, lz, out float off);
            return (Wrap(s), off);
        }

        public static bool OnTrack(float lx, float lz) => Math.Abs(Project(lx, lz).offset) <= Width * 0.5f;

        public static int Sector(float s) => Math.Min(3, Math.Max(0, (int)(s / (LapLength * 0.25f))));

        // ---- the hall ----

        public static bool InArea(float worldX, float worldZ)
        {
            float lx = worldX - WaitingIsland.CentreX, lz = worldZ - WaitingIsland.CentreZ;
            return lx <= HallX1 && lz >= HallZ0 && lz <= HallZ1 && (lx >= HallX0 || (lx >= BollardX && lz >= PitZ0 && lz <= PitZ1));
        }

        public static bool InPit(float lx, float lz, float margin = 0f)
            => lx >= BollardX + margin && lx <= PitX1 - margin && lz >= PitZ0 + margin && lz <= PitZ1 - margin;

        // ---- people on foot (ADR-508): the island's walk circle or the pit, never the track ----
        const float WalkerPitX1 = PitX1 - 0.5f, WalkerPitZ0 = PitZ0 + 0.5f, WalkerPitZ1 = PitZ1 - 0.5f;

        public static bool Walkable(float lx, float lz)
            => lx * lx + lz * lz <= WaitingIsland.WalkRadius * WaitingIsland.WalkRadius + 1e-3f
               || (lx >= PitX0 && lx <= WalkerPitX1 && lz >= WalkerPitZ0 && lz <= WalkerPitZ1);

        // The nearest walkable place (world metres), for the server's clamp of the waiting people.
        public static void ClampWalker(ref float x, ref float z)
        {
            float lx = x - WaitingIsland.CentreX, lz = z - WaitingIsland.CentreZ;
            if (Walkable(lx, lz)) return;
            float cx = x, cz = z;
            WaitingIsland.Clamp(ref cx, ref cz);
            float px = WaitingIsland.CentreX + Math.Min(WalkerPitX1, Math.Max(PitX0, lx));
            float pz = WaitingIsland.CentreZ + Math.Min(WalkerPitZ1, Math.Max(WalkerPitZ0, lz));
            float dc = (cx - x) * (cx - x) + (cz - z) * (cz - z), dp = (px - x) * (px - x) + (pz - z) * (pz - z);
            if (dc <= dp) { x = cx; z = cz; } else { x = px; z = pz; }
        }

        // Shortest distance from the hall to the map box [0, mapSize]^2 (world).
        public static float GapToMap(float mapSize)
        {
            float x0 = WaitingIsland.CentreX + PitX0, x1 = WaitingIsland.CentreX + HallX1, z0 = WaitingIsland.CentreZ + HallZ0, z1 = WaitingIsland.CentreZ + HallZ1;
            float dx = Math.Max(0f, Math.Max(0f - x1, x0 - mapSize)), dz = Math.Max(0f, Math.Max(0f - z1, z0 - mapSize));
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
