using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum PlantKind { Broadleaf = 0, Conifer = 1, Bush = 2 }

    // One tree or bush of the outdoor map. Y is the ground under it; Scale multiplies the variant's own size.
    [Serializable]
    public struct Plant
    {
        public PlantKind Kind;
        public int Variant;             // 0 .. Vegetation.Variants - 1
        public int Region;
        public float X, Y, Z;
        public float Scale, YawDeg;

        public bool HasTrunk => Kind != PlantKind.Bush;
    }

    // One grass clump inside a grass cell (ground height is looked up by the renderer).
    public struct GrassClump
    {
        public float X, Z, Scale, YawDeg;
        public bool Tall;               // reeds of the wetland shallows instead of short grass
        public bool Flower;             // a bunch of wild flowers instead of a grass clump (in patches of the meadows)
    }

    // Trees, bushes and grass of the outdoor map (ADR-058 (9), ADR-070). Pure and seeded like the rest of the world:
    // where every plant stands, which species it is, where grass grows and how much of it each graphics level draws.
    // The meshes are built by the editor (WorldVegetation) and drawn instanced by the Gameplay renderers.
    public static class Vegetation
    {
        public const int Variants = 5;

        // ---- graphics levels (0 low, 1 medium, 2 high) ----

        public const int Levels = 3;
        // Grass is drawn this far from the camera; low draws none (alpha-tested cards are the costliest pixels).
        public static readonly float[] GrassRadius = { 0f, 32f, 55f };
        // Share of the clumps of a cell that are drawn.
        public static readonly float[] GrassShare = { 0f, 0.6f, 1f };
        // Tree and bush LOD distances are multiplied by this.
        public static readonly float[] LodDistanceScale = { 0.65f, 1f, 1.5f };

        // Graphics level from the quality preset (0 low, 1 medium, 2 high, 3 custom) and, for custom, the
        // view-distance row (0 low .. 3 ultra).
        public static int Level(int preset, int viewDistance)
        {
            if (preset >= 0 && preset < Levels) return preset;
            return viewDistance <= 0 ? 0 : viewDistance == 1 ? 1 : 2;
        }

        // ---- level of detail ----

        public const float ReferenceFovDeg = 60f;
        // Distances (m) at the reference field of view, medium level, for a plant of Scale 1.
        public const float TreeFullDetail = 38f, TreeCards = 125f;   // LOD0 up to the first, LOD1 up to the second, then the billboard
        public const float BushDistance = 85f;                       // bushes have one LOD and are dropped beyond this

        // How much nearer things look through a camera than through the reference one: 1 at 60 degrees, 6 through a
        // 10-degree scope. Distances are divided by this.
        public static float Zoom(float verticalFovDeg)
        {
            float fov = Math.Max(1f, Math.Min(170f, verticalFovDeg));
            return (float)(Math.Tan(ReferenceFovDeg * 0.5 * Math.PI / 180.0) / Math.Tan(fov * 0.5 * Math.PI / 180.0));
        }

        // 0 full detail, 1 cards, 2 billboard, -1 not drawn (bushes beyond their distance). The far clip and fog are
        // the caller's.
        public static int Lod(PlantKind kind, float distance, float scale, float zoom, int level)
        {
            float d = distance / Math.Max(0.01f, zoom) / (Math.Max(0.2f, scale) * LodDistanceScale[Clamp(level)]);
            if (kind == PlantKind.Bush) return d < BushDistance ? 0 : -1;
            return d < TreeFullDetail ? 0 : d < TreeCards ? 1 : 2;
        }

        static int Clamp(int level) => level < 0 ? 0 : level >= Levels ? Levels - 1 : level;

        // ---- trees and bushes ----

        // Clearances (m) for the plants added here (the plan's own trees already keep 3 m from roads and pads).
        const float YardTreeRoadClear = 4.5f, YardTreeLotClear = 4f, TreeSpacing = 4.5f;
        const float StreetTreeStep = 14f, StreetTreeOffset = 4.6f, StreetTreeChance = 0.7f;
        const float BushCell = 5f, BushRoadClear = 1.5f, BushLotClear = 2f, BushSpacing = 2.2f;
        const float PassClear = 26f, JunctionTreeClear = 9f;
        const float MaxPlantSlopeTan = 0.5f;
        public const int MaxTrees = 2200, MaxBushes = 2600;   // instanced draws make these cheap; colliders are per trunk

        // Every tree and bush of the map: the plan's scattered trees with a species each, trees in the yards of the
        // houses and along the town streets (the town read as bare between buildings), and bushes at the edges of
        // woods, round the houses and through the wetland.
        public static List<Plant> Plan(WorldPlan plan) => Plan(plan, true);

        // forest: false gives the map as it was before the forest belt (ADR-270), for before/after measurements.
        public static List<Plant> Plan(WorldPlan plan, bool forest)
        {
            var plants = new List<Plant>();
            var grid = new SpacingGrid(plan.Size, 8f);
            uint seed = unchecked((uint)plan.Seed + 0x5EEDu);

            for (int i = 0; i < plan.Trees.Count && Count(plants, true) < MaxTrees; i++)
            {
                var t = plan.Trees[i];
                var kind = Species(plan, t.Region, t.Y, WorldNoise.Hash01(i, 1, seed));
                Add(plants, grid, kind, t.Region, t.X, t.Y, t.Z, t.Scale, t.YawDeg, (int)(WorldNoise.Hash01(i, 2, seed) * Variants));
            }

            // Yard trees: one or two behind each house, a little way off the back wall, or beside it when the back is
            // taken.
            for (int l = 0; l < plan.Lots.Count; l++)
            {
                var lot = plan.Lots[l];
                if (lot.Kind != BuildingKind.House) continue;
                int count = WorldNoise.Hash01(l, 10, seed) < 0.85f ? (WorldNoise.Hash01(l, 11, seed) < 0.55f ? 2 : 1) : 0;
                for (int n = 0; n < count; n++)
                {
                    float side = (n == 0) == (WorldNoise.Hash01(l, 12, seed) < 0.5f) ? -1f : 1f;
                    float right = side * (lot.SizeX * 0.5f - 1f + 3f * WorldNoise.Hash01(l, 13 + n, seed));
                    float back = -(lot.SizeZ * 0.5f + 5.5f + 2.5f * WorldNoise.Hash01(l, 15 + n, seed));
                    lot.ToWorld(right, back, out float x, out float z);
                    if (!Free(plan, x, z, YardTreeRoadClear, YardTreeLotClear, 2f, JunctionTreeClear))
                    {
                        lot.ToWorld(side * (lot.SizeX * 0.5f + 5.5f), -lot.SizeZ * 0.25f, out x, out z);
                        if (!Free(plan, x, z, YardTreeRoadClear, YardTreeLotClear, 2f, JunctionTreeClear)) continue;
                    }
                    float y = plan.Height(x, z);
                    var kind = WorldNoise.Hash01(l, 17 + n, seed) < 0.8f ? PlantKind.Broadleaf : PlantKind.Conifer;
                    TryAdd(plants, grid, TreeSpacing, kind, lot.Region, x, y, z, 0.7f + 0.35f * WorldNoise.Hash01(l, 19 + n, seed),
                        WorldNoise.Hash01(l, 21 + n, seed) * 360f, (int)(WorldNoise.Hash01(l, 23 + n, seed) * Variants));
                }
            }

            // Street trees along the paved roads of the town, both sides, not every slot filled.
            for (int r = 0; r < plan.Roads.Count; r++)
            {
                var road = plan.Roads[r];
                if (road.Kind == RoadKind.Path || road.Region < 0 || plan.Regions[road.Region].Kind != RegionKind.Town) continue;
                float carried = StreetTreeStep * 0.5f;
                int count = Math.Min(road.X.Length, road.Z.Length), slot = 0;
                for (int i = 0; i + 1 < count; i++)
                {
                    float ax = road.X[i], az = road.Z[i], bx = road.X[i + 1], bz = road.Z[i + 1];
                    float length = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                    if (length < 0.01f) continue;
                    float dx = (bx - ax) / length, dz = (bz - az) / length;
                    for (; carried < length; carried += StreetTreeStep, slot++)
                    {
                        for (int s = -1; s <= 1; s += 2)
                        {
                            if (WorldNoise.Hash01(r * 1000 + slot, 30 + s, seed) > StreetTreeChance) continue;
                            float off = road.Width * 0.5f + StreetTreeOffset;
                            float x = ax + dx * carried - dz * off * s, z = az + dz * carried + dx * off * s;
                            if (!Free(plan, x, z, YardTreeRoadClear, YardTreeLotClear, 2f, JunctionTreeClear)) continue;
                            TryAdd(plants, grid, TreeSpacing, PlantKind.Broadleaf, road.Region, x, plan.Height(x, z),
                                z, 0.75f + 0.25f * WorldNoise.Hash01(r * 1000 + slot, 33 + s, seed), WorldNoise.Hash01(r * 1000 + slot, 35 + s, seed) * 360f,
                                (int)(WorldNoise.Hash01(r * 1000 + slot, 37 + s, seed) * Variants));
                        }
                    }
                    carried -= length;
                }
            }

            if (forest) AddForest(plan, plants, grid, seed);

            int trees = plants.Count;
            var treeGrid = new SpacingGrid(plan.Size, 12f);
            foreach (var p in plants) treeGrid.Add(p.X, p.Z);

            // Bushes on a jittered grid: more at the edge of a wood, round the houses and in the wetland.
            int cells = (int)(plan.Size / BushCell), bushes = 0;
            for (int gz = 0; gz < cells && bushes < MaxBushes; gz++)
            {
                for (int gx = 0; gx < cells && bushes < MaxBushes; gx++)
                {
                    float x = (gx + WorldNoise.Hash01(gx, gz, seed + 40u)) * BushCell;
                    float z = (gz + WorldNoise.Hash01(gx, gz, seed + 41u)) * BushCell;
                    int region = plan.RegionAt(x, z);
                    if (region < 0) continue;
                    float y = plan.Height(x, z);
                    if (y < plan.SeaLevel + 0.3f || y > plan.SeaLevel + 80f) continue;
                    var regionKind = plan.Regions[region].Kind;
                    float chance = regionKind == RegionKind.Wetland ? 0.13f : regionKind == RegionKind.Factory ? 0.025f : 0.045f;
                    if (treeGrid.Near(x, z, 9f)) chance += 0.22f;
                    float lotDistance = NearestLot(plan, x, z);
                    if (lotDistance > BushLotClear && lotDistance < 5f) chance += 0.2f;
                    if (WorldNoise.Hash01(gx, gz, seed + 42u) >= chance) continue;
                    if (!Free(plan, x, z, BushRoadClear, BushLotClear, 1.5f, 2f)) continue;
                    if (!grid.Clear(x, z, BushSpacing)) continue;
                    float size = WorldNoise.Hash01(gx, gz, seed + 43u);
                    TryAdd(plants, grid, BushSpacing, PlantKind.Bush, region, x, y, z, 0.7f + 0.6f * size,
                        WorldNoise.Hash01(gx, gz, seed + 44u) * 360f, (int)(WorldNoise.Hash01(gx, gz, seed + 45u) * Variants));
                    bushes = plants.Count - trees;
                }
            }
            return plants;
        }

        // ---- forest belt (ADR-270): dense trees on mountain and hill slopes so that cover and concealment exist up there ----

        public const float ForestCell = 3.6f, ForestSpacing = 2.6f, ForestChance = 0.88f;
        public const int MaxForest = 80000;
        public const float TreeLineAbove = 165f;                 // metres above the sea: full forest up to TreeLineAbove - 30, thinning to none at TreeLineAbove
        const float ForestRoadClear = 5f, ForestRoadRamp = 9f;   // bare verge, then the wood thickens over this distance
        const float ForestLotClear = 12f, ForestLotRamp = 12f;
        const float ForestPadClear = 14f, ForestPassClear = 30f, ForestJunctionClear = 10f, ForestRiverClear = 3f;
        const float ForestSlopeFull = 0.7f, ForestSlopeMax = 1.0f;    // tan: dense up to 35 degrees, none past 45

        // How thick the wood is at a point, 0 (none) .. 1 (full). Keeps roads, passes, buildings, elevator pads and
        // junction yards open; the wood grows in on a ramp so the clearings have soft edges, never an empty wall.
        public static float ForestDensity(WorldPlan plan, float x, float z)
        {
            int region = plan.RegionAt(x, z);
            if (region < 0) return 0f;
            float h = plan.Height(x, z), up = h - plan.SeaLevel;
            if (up < 2f || up >= TreeLineAbove) return 0f;
            if (plan.Regions[region].Kind != RegionKind.Mountain && up < 30f) return 0f;      // hills of the low regions only above 30 m
            float slope = plan.SlopeTan(x, z);
            if (slope >= ForestSlopeMax) return 0f;
            float f = 1f - WorldTerrain.Smooth01((slope - ForestSlopeFull) / (ForestSlopeMax - ForestSlopeFull));
            f *= 1f - WorldTerrain.Smooth01((up - (TreeLineAbove - 30f)) / 30f);
            f *= 0.7f + 0.3f * WorldTerrain.Smooth01(0.5f + 1.1f * WorldNoise.Value(x / 45f, z / 45f, unchecked((uint)plan.Seed + 0xF0E5u)));   // glades, never bare
            if (f <= 0f) return 0f;
            f *= WorldTerrain.Smooth01((plan.RoadDistance(x, z) - ForestRoadClear) / ForestRoadRamp);
            if (f <= 0f) return 0f;
            f *= WorldTerrain.Smooth01((NearestLot(plan, x, z, ForestLotClear + ForestLotRamp) - ForestLotClear) / ForestLotRamp);
            if (f <= 0f) return 0f;
            foreach (var e in plan.Elevators)
                if (e.Pad().Distance(x, z) < ForestPadClear) return 0f;
            foreach (var p in plan.ConvergencePoints)
                if (Sq(p.X - x) + Sq(p.Z - z) < Sq(p.ClearRadius + ForestJunctionClear)) return 0f;
            foreach (var p in plan.Passes)
                if (Sq(p.X - x) + Sq(p.Z - z) < ForestPassClear * ForestPassClear) return 0f;
            foreach (var river in plan.Rivers)
                for (int i = 0; i < river.X.Length; i++)
                    if (Sq(river.X[i] - x) + Sq(river.Z[i] - z) < Sq(river.HalfWidth[i] + ForestRiverClear + 4f)) return 0f;
            return f;
        }

        static void AddForest(WorldPlan plan, List<Plant> plants, SpacingGrid grid, uint seed)
        {
            int cells = (int)(plan.Size / ForestCell), added = 0;
            for (int gz = 0; gz < cells && added < MaxForest; gz++)
                for (int gx = 0; gx < cells && added < MaxForest; gx++)
                {
                    float x = (gx + WorldNoise.Hash01(gx, gz, seed + 60u)) * ForestCell;
                    float z = (gz + WorldNoise.Hash01(gx, gz, seed + 61u)) * ForestCell;
                    if (WorldNoise.Hash01(gx, gz, seed + 62u) >= ForestChance) continue;
                    float f = ForestDensity(plan, x, z);
                    if (f <= 0f || WorldNoise.Hash01(gx, gz, seed + 63u) >= f) continue;
                    float y = plan.Height(x, z);
                    int region = plan.RegionAt(x, z);
                    if (!grid.Clear(x, z, ForestSpacing)) continue;
                    Add(plants, grid, Species(plan, region, y, WorldNoise.Hash01(gx, gz, seed + 64u)), region, x, y, z,
                        1f + 0.6f * WorldNoise.Hash01(gx, gz, seed + 65u), WorldNoise.Hash01(gx, gz, seed + 66u) * 360f,
                        (int)(WorldNoise.Hash01(gx, gz, seed + 67u) * Variants));
                    added++;
                }
        }

        // Conifers climb the mountain and mix into its foothills; the low regions are broadleaf with a few conifers.
        public static PlantKind Species(WorldPlan plan, int region, float height, float roll)
        {
            var kind = region >= 0 && region < plan.Regions.Count ? plan.Regions[region].Kind : RegionKind.Town;
            float conifer;
            switch (kind)
            {
                case RegionKind.Mountain: conifer = 0.6f + 0.35f * WorldTerrain.Smooth01((height - plan.SeaLevel - 18f) / 30f); break;
                case RegionKind.Wetland: conifer = 0.1f; break;
                case RegionKind.Factory: conifer = 0.35f; break;
                default: conifer = 0.2f; break;
            }
            return roll < conifer ? PlantKind.Conifer : PlantKind.Broadleaf;
        }

        static int Count(List<Plant> plants, bool trees)
        {
            int n = 0;
            foreach (var p in plants) if (p.HasTrunk == trees) n++;
            return n;
        }

        static void Add(List<Plant> plants, SpacingGrid grid, PlantKind kind, int region, float x, float y, float z, float scale, float yaw, int variant)
        {
            plants.Add(new Plant { Kind = kind, Variant = Math.Min(Variants - 1, Math.Max(0, variant)), Region = region, X = x, Y = y, Z = z, Scale = scale, YawDeg = yaw });
            grid.Add(x, z);
        }

        static void TryAdd(List<Plant> plants, SpacingGrid grid, float spacing, PlantKind kind, int region, float x, float y, float z, float scale, float yaw, int variant)
        {
            if (!grid.Clear(x, z, spacing)) return;
            Add(plants, grid, kind, region, x, y, z, scale, yaw, variant);
        }

        static float NearestLot(WorldPlan plan, float x, float z, float within = 8f)
        {
            float best = float.MaxValue;
            foreach (var lot in plan.Lots)
            {
                float reach = (lot.SizeX + lot.SizeZ) * 0.5f + within;
                if (Math.Abs(lot.X - x) > reach || Math.Abs(lot.Z - z) > reach) continue;
                best = Math.Min(best, lot.Distance(x, z));
            }
            return best;
        }

        // Ground a plant may stand on: dry, not steep, off the roads, clear of buildings, elevator pads, the open yard
        // of every compound junction (the dressing rings it with barriers) and the passes (closure barriers stand there).
        public static bool Free(WorldPlan plan, float x, float z, float roadClear, float lotClear, float padClear, float junctionClear)
        {
            if (plan.RegionAt(x, z) < 0) return false;
            if (plan.Height(x, z) < plan.SeaLevel + 0.3f || plan.SlopeTan(x, z) > MaxPlantSlopeTan) return false;
            if (plan.RoadDistance(x, z) < roadClear) return false;
            if (NearestLot(plan, x, z) < lotClear) return false;
            foreach (var e in plan.Elevators)
                if (e.Pad().Distance(x, z) < lotClear + padClear) return false;
            foreach (var p in plan.ConvergencePoints)
                if (Sq(p.X - x) + Sq(p.Z - z) < Sq(p.ClearRadius + junctionClear)) return false;
            foreach (var p in plan.Passes)
                if (Sq(p.X - x) + Sq(p.Z - z) < PassClear * PassClear) return false;
            return true;
        }

        static float Sq(float v) => v * v;

        // Points on a coarse grid for "anything within r?" questions.
        sealed class SpacingGrid
        {
            readonly float _cell;
            readonly int _cells;
            readonly List<float>[] _points;

            public SpacingGrid(float size, float cell)
            {
                _cell = cell;
                _cells = Math.Max(1, (int)Math.Ceiling(size / cell));
                _points = new List<float>[_cells * _cells];
            }

            int Index(int cx, int cz) => cz * _cells + cx;
            int CellOf(float v) => Math.Max(0, Math.Min(_cells - 1, (int)Math.Floor(v / _cell)));

            public void Add(float x, float z)
            {
                int i = Index(CellOf(x), CellOf(z));
                (_points[i] ?? (_points[i] = new List<float>())).Add(x);
                _points[i].Add(z);
            }

            public bool Near(float x, float z, float r) => !Clear(x, z, r);

            public bool Clear(float x, float z, float r)
            {
                int x0 = CellOf(x - r), x1 = CellOf(x + r), z0 = CellOf(z - r), z1 = CellOf(z + r);
                for (int cz = z0; cz <= z1; cz++)
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        var list = _points[Index(cx, cz)];
                        if (list == null) continue;
                        for (int k = 0; k < list.Count; k += 2)
                            if (Sq(list[k] - x) + Sq(list[k + 1] - z) < r * r) return false;
                    }
                return true;
            }
        }

        // ---- grass ----

        // The grass map is one byte per GrassMapCell metres: 0 none, 1..127 short grass (density v / 127),
        // 128..255 reeds (density (v - 128) / 127).
        public const float GrassMapCell = 2f;
        public const float GrassCell = 8f;                // the renderer builds and culls grass in squares this size
        public const float GrassPerSquareMetre = 4.5f;    // clumps at full density, high level (#193: the meadow was bare tufts)
        public const float MinGrassDensity = 0.12f;

        public static int GrassMapResolution(float mapSize) => (int)Math.Ceiling(mapSize / GrassMapCell);

        public static byte EncodeGrass(float density, bool tall)
        {
            if (density < MinGrassDensity) return 0;
            int v = Math.Max(1, Math.Min(127, (int)Math.Round(density * 127f)));
            return (byte)(tall ? 128 + v : v);
        }

        public static float DecodeGrass(byte value, out bool tall)
        {
            tall = value >= 128;
            return (tall ? value - 128 : value) / 127f;
        }

        // How much of the grass the ground would carry may grow here (0..1): none on roads and their verges, on or
        // next to building and elevator pads, or in water deeper than reeds stand.
        public static float GrassAllowed(WorldPlan plan, float x, float z, float height)
        {
            if (plan.RegionAt(x, z) < 0) return 0f;
            bool wetland = WetlandAt(plan, x, z);
            if (height < plan.SeaLevel - (wetland ? 0.35f : 0f)) return 0f;
            float road = plan.RoadDistance(x, z);
            if (road < 1.2f) return 0f;
            if (NearestLot(plan, x, z) < 1.2f) return 0f;
            foreach (var e in plan.Elevators)
                if (e.Pad().Distance(x, z) < 1.2f) return 0f;
            // Verges thin out toward the road.
            return WorldTerrain.Smooth01((road - 1.2f) / 2.5f);
        }

        // Reeds: the wetland shallows and the wet ground just above them.
        public static bool TallGrass(WorldPlan plan, float x, float z, float height) =>
            WetlandAt(plan, x, z) && height < plan.SeaLevel + 1.1f;

        static bool WetlandAt(WorldPlan plan, float x, float z)
        {
            int region = plan.RegionAt(x, z);
            return region >= 0 && plan.Regions[region].Kind == RegionKind.Wetland;
        }

        // The clumps of one grass cell, in random order (drawing the first share of them thins the cell evenly).
        // Same cell, same map, same seed: same clumps.
        public static void ScatterGrass(byte[] map, int resolution, float mapCell, int cellX, int cellZ, int seed, List<GrassClump> into)
        {
            into.Clear();
            if (map == null || resolution <= 0) return;
            uint salt = unchecked((uint)seed + 0x6A55u);
            int candidates = (int)Math.Round(GrassPerSquareMetre * GrassCell * GrassCell);
            for (int i = 0; i < candidates; i++)
            {
                int key = cellX * 131 + i;
                float x = (cellX + WorldNoise.Hash01(key, cellZ, salt)) * GrassCell;
                float z = (cellZ + WorldNoise.Hash01(key, cellZ, salt + 1u)) * GrassCell;
                int mx = (int)(x / mapCell), mz = (int)(z / mapCell);
                if (mx < 0 || mz < 0 || mx >= resolution || mz >= resolution) continue;
                float density = DecodeGrass(map[mz * resolution + mx], out bool tall);
                if (WorldNoise.Hash01(key, cellZ, salt + 2u) >= density) continue;
                // Flowers grow in patches: a clump in a flowery patch is one 25 % of the time, elsewhere rarely.
                bool flower = !tall && density > 0.4f && WorldNoise.Hash01(key, cellZ, salt + 5u) < (WorldNoise.Value(x / 9f, z / 9f, salt + 9u) > 0.25f ? 0.25f : 0.012f);
                into.Add(new GrassClump
                {
                    X = x, Z = z, Tall = tall, Flower = flower,
                    Scale = 0.8f + 0.5f * WorldNoise.Hash01(key, cellZ, salt + 3u),
                    YawDeg = WorldNoise.Hash01(key, cellZ, salt + 4u) * 360f,
                });
            }
        }
    }
}
