using System;
using System.Collections.Generic;

namespace Game.Core
{
    // A race circuit (ADR-505, ADR-513/515): a closed polygon of corner points, each rounded with its own radius, sampled into a polyline
    // (Path), with an optional fork (Branch): a second route that leaves the main one and joins it again further on. Distances along the
    // lap (s) count from the start/finish line; on the branch they are the main route's distances between the fork and the join, shared out
    // in proportion. Laps are judged by checkpoints (sector boundaries, all outside the fork). Pure: the builder, the server and the tests
    // use the same numbers. Coordinates are the course's own metres (x east, z south).
    public sealed class RaceCourse
    {
        // A centre-line sample: place, distance from the line, the radius of the corner it is on (0 on a straight) and which way that
        // corner turns (+1 towards the left normal, -1 the right, 0 straight). Left normal of a direction (dx, dz) = (-dz, dx).
        public struct Point { public float X, Z, S, Radius, Y; public int Turn; }   // Y: the road's height (0 on a flat circuit)

        public readonly float Width, Runoff, LineX, LineZ, MinLapSeconds;
        // An open course (#451): a road from one end to the other, raced once from the start line (s = 0) to the finish line
        // (s = LapLength); s is negative on the grid behind the start and runs past LapLength beyond the finish. No laps, no fork.
        public readonly bool Open;
        public readonly (float x, float z, float r)[] Corners;
        public readonly (float x, float z, float r)[] Branch;     // null = no fork; first and last points lie on the main route (radius 0)

        readonly Point[] _path, _branch;
        readonly float[] _checkpoints;
        public IReadOnlyList<Point> Path => _path;
        public IReadOnlyList<Point> BranchPath => _branch ?? Array.Empty<Point>();
        public float LapLength { get; }
        public float ForkS { get; } = -1f;        // main-route distances where the branch leaves and joins (-1 without a fork)
        public float JoinS { get; } = -1f;
        public IReadOnlyList<float> Checkpoints => _checkpoints;   // ascending, the first is the line (0)
        public int Sectors => _checkpoints.Length;

        const float StraightStep = 10f, ArcStepDeg = 4f, ForkMargin = 25f;

        public RaceCourse((float x, float z, float r)[] corners, float width, float runoff, float lineX, float lineZ, int checkpoints, float minLapSeconds,
            (float x, float z, float r)[] branch = null, RaceCourse heightsFrom = null)
        {
            Corners = corners; Width = width; Runoff = runoff; LineX = lineX; LineZ = lineZ; MinLapSeconds = minLapSeconds; Branch = branch;
            _path = Sample(corners, true, out float lap);
            LapLength = lap;
            float line = RawProject(_path, true, lap, lineX, lineZ, out _, out _);
            for (int i = 0; i < _path.Length; i++) _path[i].S = Wrap(_path[i].S - line);
            if (branch != null)
            {
                _branch = Sample(branch, false, out float blen);
                ForkS = Wrap(RawProject(_path, true, LapLength, branch[0].x, branch[0].z, out _, out _));
                JoinS = Wrap(RawProject(_path, true, LapLength, branch[branch.Length - 1].x, branch[branch.Length - 1].z, out _, out _));
                float span = JoinS - ForkS;
                for (int i = 0; i < _branch.Length; i++) _branch[i].S = ForkS + span * (_branch[i].S / blen);
            }
            // Evenly spaced checkpoints, the ones inside the fork (and a margin round it) left out: both routes pass all the others.
            var cps = new List<float>();
            for (int k = 0; k < checkpoints; k++)
            {
                float s = LapLength * k / checkpoints;
                if (k > 0 && branch != null && s > ForkS - ForkMargin && s < JoinS + ForkMargin) continue;
                cps.Add(s);
            }
            _checkpoints = cps.ToArray();
            if (heightsFrom != null) ProfileAlong(heightsFrom);
        }

        // A circuit that runs along an open hill road for part of the lap (#451, the pass loop): the road's own heights where the lap is on
        // it, and on the rest of the lap (one stretch) a grade shaped like the open road's (tight corners half, eased in from both joins)
        // scaled to go from one join's height to the other's.
        void ProfileAlong(RaceCourse road)
        {
            int n = _path.Length;
            var on = new bool[n];
            float a = road._path[0].S, b = road._path[road._path.Length - 1].S;
            for (int i = 0; i < n; i++)
            {
                var (s, off) = road.Project(_path[i].X, _path[i].Z);
                on[i] = Math.Abs(off) < 0.5f && s > a + 1f && s < b - 1f;
                if (on[i]) _path[i].Y = road.HeightAt(s);
            }
            int start = -1;   // the first sample off the road, after one on it
            for (int i = 0; i < n; i++) if (on[i] && !on[(i + 1) % n]) { if (start >= 0) throw new ArgumentException("the lap leaves the road more than once"); start = (i + 1) % n; }
            if (start < 0) throw new ArgumentException("the lap never leaves the road");
            int count = 0; while (!on[(start + count) % n]) count++;
            int from = (start + n - 1) % n, to = (start + count) % n;   // the last sample on the road before, the first after
            var g = new float[count + 1]; var len = new float[count + 1]; var mid = new float[count + 1];
            float along = 0f;
            for (int k = 0; k <= count; k++)
            {
                var p = _path[(from + k) % n]; var q = _path[(from + k + 1) % n];
                len[k] = (float)Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z)); mid[k] = along + len[k] * 0.5f; along += len[k];
                bool tight = p.Radius > 0f && q.Radius > 0f && p.Radius < TightRadius;
                g[k] = tight ? 0.5f : 1f;
            }
            for (int k = 0; k <= count; k++) g[k] *= Math.Min(1f, Math.Min(mid[k], along - mid[k]) / GradeRamp);
            var gs = new float[count + 1]; float total = 0f;
            for (int k = 0; k <= count; k++)
            {
                float sum = 0f, w = 0f;
                for (int j = 0; j <= count; j++) if (Math.Abs(mid[j] - mid[k]) <= GradeSmoothing) { sum += g[j] * len[j]; w += len[j]; }
                gs[k] = sum / w; total += gs[k] * len[k];
            }
            float h0 = _path[from].Y, scale = (_path[to].Y - h0) / total, y = h0;
            for (int k = 0; k < count; k++) { y += gs[k] * scale * len[k]; _path[(from + k + 1) % n].Y = y; }
            Hilly = true;
        }

        public bool Hilly { get; private set; }   // the road has heights (an open hill road, or a circuit along one)


        // An open hill road (#451): the corners from one end of the road to the other in driving order, the start and finish lines on it, and
        // the road's heights: it climbs (`climbs`) or falls along the driving order at `grade` on the open road, half that round tight
        // hairpins (flatter where the inside is short), level for FlatEnds metres at both ends (the grids and the run-outs). The heights
        // start from the low end, so the same road raced the other way (corners reversed, `climbs` flipped) has the same heights.
        public RaceCourse((float x, float z, float r)[] corners, float width, float runoff, (float x, float z) start, (float x, float z) finish,
            int checkpoints, float minSeconds, float grade, bool climbs)
        {
            Open = true; Corners = corners; Width = width; Runoff = runoff; LineX = start.x; LineZ = start.z; MinLapSeconds = minSeconds;
            _path = Sample(corners, false, out _);
            float line = RawProject(_path, false, 0f, start.x, start.z, out _, out _);
            LapLength = RawProject(_path, false, 0f, finish.x, finish.z, out _, out _) - line;
            for (int i = 0; i < _path.Length; i++) _path[i].S -= line;
            Profile(grade, climbs);
            Hilly = grade != 0f;
            _checkpoints = new float[checkpoints];
            for (int k = 0; k < checkpoints; k++) _checkpoints[k] = LapLength * k / checkpoints;
        }

        public const float FlatEnds = 110f, GradeRamp = 60f, TightRadius = 20f, GradeSmoothing = 20f;

        void Profile(float grade, bool climbs)
        {
            int n = _path.Length;
            float s0 = _path[0].S, s1 = _path[n - 1].S;
            var g = new float[n - 1]; var len = new float[n - 1]; var mid = new float[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                var a = _path[i]; var b = _path[i + 1];
                len[i] = b.S - a.S; mid[i] = (a.S + b.S) * 0.5f;
                float ease = Math.Max(0f, Math.Min(1f, (Math.Min(mid[i] - s0, s1 - mid[i]) - FlatEnds) / GradeRamp));
                bool tight = a.Radius > 0f && b.Radius > 0f && a.Radius < TightRadius;
                g[i] = grade * ease * (tight ? 0.5f : 1f);
            }
            // Eased between the open road and the hairpins (a vertical curve, not a kink): the grade averaged over +-GradeSmoothing metres.
            var y = new float[n];
            for (int k = 0; k < n - 1; k++)
            {
                int i = climbs ? k : n - 2 - k;
                float sum = 0f, w = 0f;
                for (int j = 0; j < n - 1; j++) if (Math.Abs(mid[j] - mid[i]) <= GradeSmoothing) { sum += g[j] * len[j]; w += len[j]; }
                float gs = w > 0f ? sum / w : 0f;
                if (climbs) y[i + 1] = y[i] + gs * len[i]; else y[i] = y[i + 1] + gs * len[i];
            }
            for (int i = 0; i < n; i++) _path[i].Y = y[i];
        }

        // The road's height at distance s (0 on a flat circuit).
        public float HeightAt(float s)
        {
            int i = Locate(s, out float u);
            return _path[i].Y + (_path[(i + 1) % _path.Length].Y - _path[i].Y) * u;
        }

        // The road's height at the point of the course nearest (x, z).
        public float RoadHeight(float x, float z) => HeightAt(Project(x, z).s);

        public const float FallDepth = 5f;      // this far under the road where it was last on it = fallen off (#451)
        const float FollowAbove = 2.5f;

        // Where a car on a hill road was last on it (#451), for putting it back: s when it is on the road's surface (within the run-off,
        // not far above or below it) — but not when it has fallen (well under where it last was): landing on the leg below is not the way
        // on. NaN = not yet known (the first place on the road is taken). A car back on the road after a while off it is followed again.
        public float Follow(float goodS, float x, float y, float z)
        {
            var (s, off) = Project(x, z);
            if (Math.Abs(off) > Width * 0.5f + Runoff || Math.Abs(y - HeightAt(s)) > FollowAbove) return goodS;
            return Fell(goodS, y) ? goodS : s;
        }

        // Fallen off the road: well under it where the car was last on it.
        public bool Fell(float goodS, float y) => Hilly && !float.IsNaN(goodS) && y < HeightAt(goodS) - FallDepth;

        // Where a car goes back to (ADR-516, #451): on a circuit the nearest point of the course; on a hill road the last place it was on
        // the road (Follow) — never the leg below that it fell to. Place on the road, heading the way of the race.
        public (float x, float y, float z, float yaw) ResetTo(float x, float z, float goodS)
        {
            if (Hilly && !float.IsNaN(goodS)) { var q = PointAt(goodS); return (q.x, HeightAt(goodS), q.z, q.yaw); }
            var p = ResetPose(x, z);
            return (p.x, RoadHeight(p.x, p.z), p.z, p.yaw);
        }

        // Which sector (between checkpoints) a distance is in.
        public int Sector(float s)
        {
            int k = 0;
            for (int i = 1; i < _checkpoints.Length; i++) if (s >= _checkpoints[i]) k = i;
            return k;
        }

        // Distance along the lap from the line and the offset from the nearer route's centre line (positive = left of the driving direction).
        public (float s, float offset) Project(float x, float z)
        {
            var r = ProjectRoute(x, z);
            return (r.s, r.offset);
        }

        public (float s, float offset, bool onBranch) ProjectRoute(float x, float z)
        {
            float s = Wrap(RawProject(_path, !Open, LapLength, x, z, out float off, out float d2));
            if (_branch != null)
            {
                float bs = RawProject(_branch, false, 0f, x, z, out float boff, out float bd2);
                if (bd2 < d2) return (bs, boff, true);
            }
            return (s, off, false);
        }

        public bool OnTrack(float x, float z) => Math.Abs(Project(x, z).offset) <= Width * 0.5f;

        // The main route's centre line at distance s from the line: place and heading (yaw in degrees, Unity's: 0 = +z, 90 = +x).
        public (float x, float z, float yaw) PointAt(float s)
        {
            int i = Locate(s, out float u);
            var a = _path[i]; var b = _path[(i + 1) % _path.Length];
            return (a.X + (b.X - a.X) * u, a.Z + (b.Z - a.Z) * u, Yaw(b.X - a.X, b.Z - a.Z));
        }

        // The piece of the centre line distance s lies on (i to i + 1) and how far along it (0..1); an open road's ends hold beyond them.
        int Locate(float s, out float u)
        {
            u = 0f;
            int n = _path.Length;
            if (Open)
            {
                if (s <= _path[0].S) return 0;
                for (int i = 0; i < n - 1; i++)
                    if (s <= _path[i + 1].S) { float d = _path[i + 1].S - _path[i].S; u = d > 1e-4f ? (s - _path[i].S) / d : 0f; return i; }
                u = 1f; return n - 2;
            }
            s = Wrap(s);
            for (int i = 0; i < n; i++)
            {
                var a = _path[i]; var b = _path[(i + 1) % n];
                float sa = a.S, sb = b.S < a.S ? b.S + LapLength : b.S, t = s < sa ? s + LapLength : s;
                if (t < sa || t > sb || sb - sa < 1e-4f) continue;
                u = (t - sa) / (sb - sa);
                return i;
            }
            return 0;
        }

        // Start slots behind the line, two staggered columns 3.5 m either side of the centre line (less on a narrow road), 8 m a row, the right column 4 m back
        // (ADR-514). Each faces the way of the race.
        public List<(float x, float z, float yaw)> GridSpots(int count)
        {
            var list = new List<(float, float, float)>();
            for (int i = 0; i < count; i++)
            {
                bool right = (i & 1) == 1;
                var (x, z, yaw) = PointAt(-(8f + (i / 2) * 8f + (right ? 4f : 0f)));
                float col = Math.Min(3.5f, Width * 0.5f - 1.3f);                         // a narrow road (the pass) keeps the cars on the tarmac
                float rad = yaw * (float)Math.PI / 180f, side = right ? col : -col;      // right of the heading = (cos, -sin) in (x, z)
                list.Add((x + (float)Math.Cos(rad) * side, z - (float)Math.Sin(rad) * side, yaw));
            }
            return list;
        }

        // Distance from (x, z) to one route's centre line (the main one, or the branch; +inf without a branch). The builder leaves out a
        // route's kerbs, run-off and barriers where they would lie on the other route (where the fork leaves and joins).
        public float RouteDistance(bool branch, float x, float z)
        {
            if (branch && _branch == null) return float.PositiveInfinity;
            RawProject(branch ? _branch : _path, !branch && !Open, LapLength, x, z, out _, out float d2);
            return (float)Math.Sqrt(d2);
        }

        // Where R puts a car back (ADR-516): the nearest point of the nearer route's centre line, facing the way of the race.
        public (float x, float z, float yaw) ResetPose(float x, float z)
        {
            Nearest(_path, !Open, x, z, out float px, out float pz, out float yaw, out float d2);
            if (_branch != null)
            {
                Nearest(_branch, false, x, z, out float bx, out float bz, out float byaw, out float bd2);
                if (bd2 < d2) return (bx, bz, byaw);
            }
            return (px, pz, yaw);
        }

        static void Nearest(Point[] pts, bool closed, float x, float z, out float px, out float pz, out float yaw, out float best)
        {
            best = float.MaxValue; px = pz = yaw = 0f;
            int n = closed ? pts.Length : pts.Length - 1;
            for (int i = 0; i < n; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Length];
                float dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
                float u = len2 > 1e-6f ? Math.Max(0f, Math.Min(1f, ((x - a.X) * dx + (z - a.Z) * dz) / len2)) : 0f;
                float qx = a.X + dx * u, qz = a.Z + dz * u, d2 = (x - qx) * (x - qx) + (z - qz) * (z - qz);
                if (d2 >= best) continue;
                best = d2; px = qx; pz = qz; yaw = Yaw(dx, dz);
            }
        }

        static float Yaw(float dx, float dz) => (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);

        float Wrap(float s) { if (Open) return s; s %= LapLength; return s < 0f ? s + LapLength : s; }

        // The polyline's distance (as stored, interpolated; may run past `lap` over the line) at the point nearest (x, z), its offset and
        // squared distance.
        static float RawProject(Point[] pts, bool closed, float lap, float x, float z, out float offset, out float best)
        {
            best = float.MaxValue; offset = 0f;
            float bestS = 0f;
            int n = closed ? pts.Length : pts.Length - 1;
            for (int i = 0; i < n; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Length];
                float dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
                float u = len2 > 1e-6f ? Math.Max(0f, Math.Min(1f, ((x - a.X) * dx + (z - a.Z) * dz) / len2)) : 0f;
                float px = a.X + dx * u, pz = a.Z + dz * u, ex = x - px, ez = z - pz, d2 = ex * ex + ez * ez;
                if (d2 >= best) continue;
                best = d2;
                float len = (float)Math.Sqrt(len2);
                float sb = b.S < a.S ? b.S + lap : b.S;   // the piece over the line (or the closing piece) runs past the end of the lap
                bestS = a.S + (sb - a.S) * u;
                offset = len > 1e-6f ? (-dz * ex + dx * ez) / len : 0f;
            }
            return bestS;
        }

        // Corner points rounded into arcs, straights cut into short pieces; S = distance along from the first sample.
        static Point[] Sample((float x, float z, float r)[] c, bool closed, out float length)
        {
            var raw = new List<Point>(); var corner = new List<int>();
            int n = c.Length;
            for (int i = 0; i < n; i++)
            {
                var p = c[i];
                if (!closed && (i == 0 || i == n - 1) || p.r <= 0f) { raw.Add(new Point { X = p.x, Z = p.z }); corner.Add(-1 - i); continue; }
                var a = c[(i + n - 1) % n]; var b = c[(i + 1) % n];
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
            var pts = new List<Point>();
            int m = closed ? raw.Count : raw.Count - 1;
            for (int i = 0; i < m; i++)
            {
                var a = raw[i]; var b = raw[(i + 1) % raw.Count];
                pts.Add(a);
                if (corner[i] == corner[(i + 1) % raw.Count]) continue;   // within an arc
                float len = Dist(a, b);
                int k = (int)Math.Ceiling(len / StraightStep);
                for (int j = 1; j < k; j++) pts.Add(new Point { X = a.X + (b.X - a.X) * j / k, Z = a.Z + (b.Z - a.Z) * j / k });
            }
            if (!closed) pts.Add(raw[raw.Count - 1]);
            float s = 0f;
            int last = closed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < pts.Count; i++)
            {
                var q = pts[i]; q.S = s; pts[i] = q;
                if (i < last) s += Dist(pts[i], pts[(i + 1) % pts.Count]);
            }
            length = s;
            return pts.ToArray();
        }

        static float Dist(Point a, Point b) { float dx = b.X - a.X, dz = b.Z - a.Z; return (float)Math.Sqrt(dx * dx + dz * dz); }
    }
}
