using System;
using System.Collections.Generic;

namespace Game.Core
{
    // A flat spatial hash over points in the ground plane (ADR-290): which of N points lie within a radius, without
    // looking at all N. Query answers come back in ascending index order, so a caller that picks "the first best" gets
    // the same one as with a plain scan of the list. Pure numbers; the bots' item sense and door check use it.
    public sealed class PointGrid
    {
        readonly float _cell;
        readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        float[] _xs = new float[0], _zs = new float[0];
        int _count;

        public PointGrid(float cellMeters) { _cell = Math.Max(0.5f, cellMeters); }

        public int Count => _count;

        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
        int CellOf(float v) => (int)Math.Floor(v / _cell);

        // Rebuilds from the first `count` entries. Cell lists are reused between rebuilds.
        public void Rebuild(float[] xs, float[] zs, int count)
        {
            foreach (var list in _cells.Values) list.Clear();
            if (_xs.Length < count) { _xs = new float[count]; _zs = new float[count]; }
            _count = count;
            for (int i = 0; i < count; i++)
            {
                _xs[i] = xs[i];
                _zs[i] = zs[i];
                long key = Key(CellOf(xs[i]), CellOf(zs[i]));
                if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>(4);
                list.Add(i);
            }
        }

        // Indices of the points within `radius` of (x, z), ascending, appended to `result` (cleared first).
        public void Query(float x, float z, float radius, List<int> result)
        {
            result.Clear();
            float rr = radius * radius;
            int x0 = CellOf(x - radius), x1 = CellOf(x + radius), z0 = CellOf(z - radius), z1 = CellOf(z + radius);
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    if (!_cells.TryGetValue(Key(cx, cz), out var list)) continue;
                    for (int k = 0; k < list.Count; k++)
                    {
                        int i = list[k];
                        float dx = _xs[i] - x, dz = _zs[i] - z;
                        if (dx * dx + dz * dz <= rr) result.Add(i);
                    }
                }
            result.Sort(); // cells are visited in space order; sorting restores the scan order (lists are a handful long)
        }
    }
}
