using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The generated island as the pollution sees it (IPollutionTerrain over a WorldPlan): region raster from the
    // plan's own region lookup, slope from the ground height, river cells from the river centre lines, road
    // classes from the road list (trunk and bridges paved, spurs and paths unpaved; s6_map_data_format D5).
    public sealed class WorldPlanTerrain : IPollutionTerrain
    {
        const float Bucket = 64f;

        readonly WorldPlan _plan;
        readonly List<float[]> _roadSegs = new List<float[]>();  // ax, az, bx, bz, halfWidth, class
        readonly Dictionary<long, List<int>> _roadAt = new Dictionary<long, List<int>>();
        readonly List<float[]> _riverSegs = new List<float[]>(); // ax, az, bx, bz, halfWidthA, halfWidthB

        public WorldPlanTerrain(WorldPlan plan)
        {
            _plan = plan;
            foreach (var road in plan.Roads)
                for (int i = 0; i + 1 < road.X.Length; i++)
                {
                    bool bridge = road.Bridge != null && i < road.Bridge.Length && road.Bridge[i];
                    float cls = road.Kind == RoadKind.Trunk || bridge ? 2f : 1f;
                    AddRoad(road.X[i], road.Z[i], road.X[i + 1], road.Z[i + 1], road.Width * 0.5f, cls);
                }
            foreach (var r in plan.Rivers)
                for (int i = 0; i + 1 < r.X.Length; i++)
                    _riverSegs.Add(new[] { r.X[i], r.Z[i], r.X[i + 1], r.Z[i + 1], r.HalfWidth[i], r.HalfWidth[i + 1] });
        }

        void AddRoad(float ax, float az, float bx, float bz, float half, float cls)
        {
            int id = _roadSegs.Count;
            _roadSegs.Add(new[] { ax, az, bx, bz, half, cls });
            int x0 = (int)Math.Floor((Math.Min(ax, bx) - half) / Bucket), x1 = (int)Math.Floor((Math.Max(ax, bx) + half) / Bucket);
            int z0 = (int)Math.Floor((Math.Min(az, bz) - half) / Bucket), z1 = (int)Math.Floor((Math.Max(az, bz) + half) / Bucket);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    long key = ((long)x << 32) ^ (uint)z;
                    if (!_roadAt.TryGetValue(key, out var list)) _roadAt[key] = list = new List<int>();
                    list.Add(id);
                }
        }

        static float SegDist(float px, float pz, float ax, float az, float bx, float bz, out float t)
        {
            float dx = bx - ax, dz = bz - az, len2 = dx * dx + dz * dz;
            t = len2 < 1e-6f ? 0f : Math.Max(0f, Math.Min(1f, ((px - ax) * dx + (pz - az) * dz) / len2));
            float qx = ax + dx * t - px, qz = az + dz * t - pz;
            return (float)Math.Sqrt(qx * qx + qz * qz);
        }

        public float OriginX => 0f;
        public float OriginZ => 0f;
        public float WidthMeters => _plan.Size;
        public float DepthMeters => _plan.Size;
        public int RegionCount => _plan.Regions.Count;

        public byte RegionAt(float x, float z)
        {
            int r = _plan.RegionAt(x, z);
            return r < 0 || r > 254 ? PollutionGrid.Sea : (byte)r;
        }

        public float SlopeDegAt(float x, float z) => (float)(Math.Atan(_plan.SlopeTan(x, z)) * 180.0 / Math.PI);

        public byte RoadAt(float x, float z)
        {
            long key = ((long)(int)Math.Floor(x / Bucket) << 32) ^ (uint)(int)Math.Floor(z / Bucket);
            if (!_roadAt.TryGetValue(key, out var list)) return 0;
            float best = 0f;
            foreach (int id in list)
            {
                var s = _roadSegs[id];
                if (s[5] > best && SegDist(x, z, s[0], s[1], s[2], s[3], out _) <= s[4]) best = s[5];
            }
            return (byte)best;
        }

        public bool RiverAt(float x, float z)
        {
            foreach (var s in _riverSegs)
            {
                float d = SegDist(x, z, s[0], s[1], s[2], s[3], out float t);
                if (d <= s[4] + (s[5] - s[4]) * t) return true;
            }
            return false;
        }
    }
}
