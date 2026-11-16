using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The map as the pollution sees it: one cell grid (pollution_sim.html uses 12 m cells). Built by the map
    // generator (S1) or by a test. Arrays are row-major, index = y * Width + x.
    public sealed class PollutionGrid
    {
        public const byte Sea = 255;

        public int Width, Height;
        public float CellMeters = 12f;
        public float OriginX, OriginZ; // world position of the grid corner (the terrain box corner); 0 when the world origin is the corner
        public byte[] Region;    // region index 0..n-1 on land and river, Sea (255) in the sea
        public float[] SlopeDeg; // terrain slope in degrees (0 on rivers)
        public byte[] Road;      // 0 none, 1 unpaved, 2 paved or bridge
        public bool[] River;     // river cell: slows the front (RiverFactor)

        public int Count => Width * Height;
        public bool IsSea(int i) => Region[i] == Sea;
        // Land counts for area; river cells carry a region but are not land.
        public bool IsLand(int i) => Region[i] != Sea && !River[i];
        public int CellAt(float xMeters, float zMeters)
        {
            int x = Math.Max(0, Math.Min(Width - 1, (int)Math.Floor((xMeters - OriginX) / CellMeters)));
            int y = Math.Max(0, Math.Min(Height - 1, (int)Math.Floor((zMeters - OriginZ) / CellMeters)));
            return y * Width + x;
        }

        public PollutionGrid(int width, int height, float cellMeters = 12f)
        {
            Width = width; Height = height; CellMeters = cellMeters;
            int n = width * height;
            Region = new byte[n]; SlopeDeg = new float[n]; Road = new byte[n]; River = new bool[n];
        }
    }

    // When the front reaches every cell, and what that means for a combatant (ADR-169): a cost-based Dijkstra
    // flood on the grid, ported from pollution_sim.html `computeFront`. Deterministic: the same grid, order and
    // rules give the same field on every machine. Times are match minutes.
    public sealed class PollutionTimeline
    {
        public readonly PollutionRules Rules;
        public readonly PollutionGrid Grid;
        public readonly int[] Order;             // regions in confirmation order; the last is the final region
        public readonly float[] ConfirmSeconds;  // by closing index
        public readonly float[] ArriveMinutes;   // by cell; 1e9 where the front never comes (sea)
        readonly int[] _closingOf;

        public int RegionCount => Order.Length;
        public int FinalRegion => Order.Length > 0 ? Order[Order.Length - 1] : -1;

        public PollutionTimeline(PollutionGrid grid, int[] order, PollutionRules rules = null)
        {
            Rules = rules ?? PollutionRules.Current;
            Grid = grid; Order = order;
            ConfirmSeconds = Rules.ConfirmSeconds(order.Length);
            _closingOf = new int[256];
            for (int i = 0; i < _closingOf.Length; i++) _closingOf[i] = -1;
            for (int k = 0; k < order.Length; k++) _closingOf[order[k]] = k;
            ArriveMinutes = Flood();
        }

        // ---- Region level (what the server replicates) ----

        public int ClosingIndexOf(int region) => region >= 0 && region < _closingOf.Length ? _closingOf[region] : -1;
        public float ConfirmAt(int region) { int k = ClosingIndexOf(region); return k < 0 ? float.PositiveInfinity : ConfirmSeconds[k]; }
        public float WarningAt(int region) => ConfirmAt(region) - Rules.WarningLeadSeconds;

        public PollutionState StateOf(int region, double seconds)
        {
            int k = ClosingIndexOf(region);
            if (k < 0) return PollutionState.Safe;
            if (seconds >= ConfirmSeconds[k]) return PollutionState.Contaminated;
            return seconds >= ConfirmSeconds[k] - Rules.WarningLeadSeconds ? PollutionState.Warning : PollutionState.Safe;
        }

        public void States(double seconds, PollutionState[] result)
        {
            for (int r = 0; r < result.Length; r++) result[r] = StateOf(r, seconds);
        }

        // Regions confirmed by this time (0..RegionCount).
        public int Confirmed(double seconds)
        {
            int n = 0;
            while (n < ConfirmSeconds.Length && ConfirmSeconds[n] <= seconds) n++;
            return n;
        }

        // The next region that goes into warning or confirmation: its closing index, or -1 when all are confirmed.
        public int NextConfirmIndex(double seconds)
        {
            int n = Confirmed(seconds);
            return n < ConfirmSeconds.Length ? n : -1;
        }

        // ---- Cell level ----

        public bool Contaminated(int cell, double seconds) => ArriveMinutes[cell] * 60.0 <= seconds;

        // Health per second a combatant on this cell loses with no protection.
        public float DamagePerSecond(int cell, double seconds) =>
            Contaminated(cell, seconds) ? Rules.DamagePerSecond(Confirmed(seconds)) : 0f;

        // Same, with a gas cover (never in the final region).
        public float DamagePerSecond(int cell, double seconds, GasCover cover) =>
            DamagePerSecond(cell, seconds) * cover.Multiplier(seconds, Grid.Region[cell] == FinalRegion);

        // The match ends when the land the front has not reached falls to this area; the match length in minutes.
        public float EndMinutes(float endAreaKm2 = 0.03f)
        {
            var land = new List<float>();
            for (int i = 0; i < Grid.Count; i++) if (Grid.IsLand(i) && ArriveMinutes[i] < 1e8f) land.Add(ArriveMinutes[i]);
            if (land.Count == 0) return 0f;
            land.Sort();
            float cellKm2 = Grid.CellMeters * Grid.CellMeters / 1e6f;
            int endIdx = Math.Min(land.Count - 1, Math.Max(0, land.Count - (int)Math.Ceiling(endAreaKm2 / cellKm2)));
            return land[endIdx];
        }

        // Area in km2 of land the front has not reached by this time.
        public float SafeAreaKm2(double seconds)
        {
            int n = 0;
            for (int i = 0; i < Grid.Count; i++) if (Grid.IsLand(i) && ArriveMinutes[i] * 60.0 > seconds) n++;
            return n * Grid.CellMeters * Grid.CellMeters / 1e6f;
        }

        // ---- The flood ----

        float[] Speeds()
        {
            var g = Grid; var r = Rules;
            var sp = new float[g.Count];
            for (int i = 0; i < sp.Length; i++)
            {
                if (g.IsSea(i)) continue;
                float t = Math.Max(0f, Math.Min(1f, (g.SlopeDeg[i] - r.FlatMaxDeg) / (r.SteepMinDeg - r.FlatMaxDeg)));
                float s = r.FlatSpeed + (r.SteepSpeed - r.FlatSpeed) * t;
                if (g.River[i]) s *= r.RiverFactor;
                int rc = g.Road[i];
                if (rc > 0) { float rs = rc >= 2 ? r.RoadSpeed : (r.RoadSpeed + r.FlatSpeed) * 0.5f; if (rs > s) s = rs; }
                sp[i] = Math.Max(s, 1f);
            }
            return sp;
        }

        static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        float[] Flood()
        {
            var g = Grid; var r = Rules;
            int w = g.Width, h = g.Height, n = g.Count;
            var confirmMin = new double[256];
            var mu = new double[256];
            for (int k = 0; k < Order.Length; k++)
            {
                confirmMin[Order[k]] = ConfirmSeconds[k] / 60.0;
                mu[Order[k]] = (1.0 + r.StageAccel * k) * (k == Order.Length - 1 ? r.FinalFactor : 1.0);
            }
            var sp = Speeds();
            var inv = new double[n];
            for (int i = 0; i < n; i++) inv[i] = g.IsSea(i) ? 0.0 : 1.0 / sp[i];
            var arrive = new double[n];
            for (int i = 0; i < n; i++) arrive[i] = 1e9;
            var heap = new MinHeap(n * 2);
            // The front starts on every coast cell (the grid border counts as coast) at its region's confirmation.
            for (int i = 0; i < n; i++)
            {
                if (g.IsSea(i) || ClosingIndexOf(g.Region[i]) < 0) continue;
                int x = i % w, y = i / w;
                bool coast = false;
                for (int k = 0; k < 8 && !coast; k++)
                {
                    int nx = x + Dx[k], ny = y + Dy[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h || g.IsSea(ny * w + nx)) coast = true;
                }
                if (!coast) continue;
                arrive[i] = confirmMin[g.Region[i]];
                heap.Push(arrive[i], i);
            }
            double cell = g.CellMeters, diag = cell * 1.4142;
            while (heap.Count > 0)
            {
                int u = heap.Pop(out double t);
                if (t > arrive[u]) continue;
                int ux = u % w, uy = u / w;
                for (int k = 0; k < 8; k++)
                {
                    int nx = ux + Dx[k], ny = uy + Dy[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int v = ny * w + nx;
                    if (g.IsSea(v)) continue;
                    int rv = g.Region[v];
                    if (ClosingIndexOf(rv) < 0) continue;
                    double nt = t + (k < 4 ? cell : diag) * 0.5 * (inv[u] + inv[v]) / mu[rv];
                    if (nt < confirmMin[rv]) nt = confirmMin[rv]; // never spreads into a region before it is confirmed
                    if (nt < arrive[v]) { arrive[v] = nt; heap.Push(nt, v); }
                }
            }
            var result = new float[n];
            for (int i = 0; i < n; i++) result[i] = (float)arrive[i];
            return result;
        }

        sealed class MinHeap
        {
            double[] _k; int[] _v; int _n;
            public int Count => _n;
            public MinHeap(int cap) { _k = new double[Math.Max(16, cap)]; _v = new int[_k.Length]; }
            public void Push(double key, int val)
            {
                if (_n == _k.Length) { Array.Resize(ref _k, _n * 2); Array.Resize(ref _v, _n * 2); }
                int i = _n++;
                while (i > 0) { int p = (i - 1) >> 1; if (_k[p] <= key) break; _k[i] = _k[p]; _v[i] = _v[p]; i = p; }
                _k[i] = key; _v[i] = val;
            }
            public int Pop(out double key)
            {
                int rv = _v[0]; key = _k[0];
                int n = --_n;
                if (n > 0)
                {
                    double k = _k[n]; int v = _v[n]; int i = 0;
                    for (;;)
                    {
                        int c = 2 * i + 1;
                        if (c >= n) break;
                        if (c + 1 < n && _k[c + 1] < _k[c]) c++;
                        if (_k[c] >= k) break;
                        _k[i] = _k[c]; _v[i] = _v[c]; i = c;
                    }
                    _k[i] = k; _v[i] = v;
                }
                return rv;
            }
        }
    }
}
