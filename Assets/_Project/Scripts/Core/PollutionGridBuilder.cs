using System;
using System.Collections.Generic;

namespace Game.Core
{
    // What the map must answer for the pollution (s6_map_data_format.md). The map track implements it over its own
    // data (region raster, terrain height, road classes, river mask); the pollution never sees the map types.
    public interface IPollutionTerrain
    {
        float OriginX { get; }      // world position of the box corner
        float OriginZ { get; }
        float WidthMeters { get; }
        float DepthMeters { get; }
        int RegionCount { get; }
        byte RegionAt(float x, float z);     // 0..RegionCount-1 on land and river, PollutionGrid.Sea elsewhere
        float SlopeDegAt(float x, float z);  // degrees, 0 on rivers
        byte RoadAt(float x, float z);       // 0 none, 1 unpaved, 2 paved or bridge
        bool RiverAt(float x, float z);
    }

    // Terrain -> PollutionGrid, region adjacency, and a hash two machines can compare (s6 spec 6: float determinism).
    public static class PollutionGridBuilder
    {
        public static PollutionGrid Build(IPollutionTerrain t, float cellMeters = 12f)
        {
            int w = Math.Max(1, (int)Math.Ceiling(t.WidthMeters / cellMeters));
            int h = Math.Max(1, (int)Math.Ceiling(t.DepthMeters / cellMeters));
            var g = new PollutionGrid(w, h, cellMeters) { OriginX = t.OriginX, OriginZ = t.OriginZ };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float wx = t.OriginX + (x + 0.5f) * cellMeters, wz = t.OriginZ + (y + 0.5f) * cellMeters;
                    int i = y * w + x;
                    byte r = t.RegionAt(wx, wz);
                    g.Region[i] = r;
                    if (r == PollutionGrid.Sea) continue;
                    g.River[i] = t.RiverAt(wx, wz);
                    g.SlopeDeg[i] = g.River[i] ? 0f : Math.Max(0f, t.SlopeDegAt(wx, wz));
                    g.Road[i] = (byte)Math.Min(2, (int)t.RoadAt(wx, wz));
                }
            return g;
        }

        // Regions that touch by land: some cell of one has a 4-neighbour in the other. A river is crossed (bridges
        // and fords), as the simulator does. Sorted lists, so the result is stable.
        public static int[][] Adjacency(PollutionGrid g, int regionCount)
        {
            var sets = new SortedSet<int>[regionCount];
            for (int r = 0; r < regionCount; r++) sets[r] = new SortedSet<int>();
            for (int y = 0; y < g.Height; y++)
                for (int x = 0; x < g.Width; x++)
                {
                    int a = g.Region[y * g.Width + x];
                    if (a == PollutionGrid.Sea || a >= regionCount) continue;
                    if (x + 1 < g.Width) Link(sets, a, g.Region[y * g.Width + x + 1], regionCount);
                    if (y + 1 < g.Height) Link(sets, a, g.Region[(y + 1) * g.Width + x], regionCount);
                }
            var result = new int[regionCount][];
            for (int r = 0; r < regionCount; r++) { result[r] = new int[sets[r].Count]; sets[r].CopyTo(result[r]); }
            return result;
        }

        static void Link(SortedSet<int>[] sets, int a, int b, int n)
        {
            if (b == PollutionGrid.Sea || b >= n || a == b) return;
            sets[a].Add(b); sets[b].Add(a);
        }

        // Land cells per region (a loot weight stand-in when the map has no loot plan yet).
        public static float[] LandAreaWeights(PollutionGrid g, int regionCount)
        {
            var w = new float[regionCount];
            for (int i = 0; i < g.Count; i++) if (g.IsLand(i) && g.Region[i] < regionCount) w[g.Region[i]] += 1f;
            return w;
        }

        // FNV-1a over the confirmation order and the arrival seconds of every land cell (whole seconds, so float
        // noise below a second does not matter; a value on a rounding edge could still differ, see the DONE note).
        public static uint Hash(PollutionTimeline tl)
        {
            unchecked
            {
                uint h = 2166136261u;
                for (int k = 0; k < tl.Order.Length; k++) h = (h ^ (uint)tl.Order[k]) * 16777619u;
                var a = tl.ArriveMinutes;
                for (int i = 0; i < a.Length; i++)
                {
                    int s = a[i] >= 1e8f ? -1 : (int)Math.Round(a[i] * 60.0);
                    h = (h ^ (uint)s) * 16777619u;
                }
                return h;
            }
        }
    }
}
