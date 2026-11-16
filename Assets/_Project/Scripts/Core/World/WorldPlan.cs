using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum RoadKind { Trunk, Spur, Path }

    // All yaws in a plan: degrees, 0 = +Z, 90 = +X (same as ElevatorSpot.FacingDeg).

    [Serializable]
    public class WorldRegion
    {
        public int Index;
        public string Id, Name;
        public RegionKind Kind;
        public float CentreX, CentreZ;      // Voronoi seed from the spec
        public float HubX, HubY, HubZ;      // centre compound: every trunk road of the region starts here
        public int Lots, ConvergencePoints; // fewest lots / exact convergence points the spec asks for
    }

    // A gap in a border ridge: the only way between two regions. The closure barrier of a region stands here.
    [Serializable]
    public class WorldPass
    {
        public const float Shoulder = 14f;  // the ridge returns to full height this far beyond Width / 2

        public string Name;
        public int RegionA, RegionB;
        public float X, Y, Z;
        public float DirX, DirZ;            // unit vector from RegionA into RegionB
        public float YawDeg;
        public float Width;
    }

    // A polyline with a width. Y is the road surface; the terrain is flattened to it.
    [Serializable]
    public class WorldRoad
    {
        public string Name;
        public RoadKind Kind;
        public float Width;
        public int Region = -1;             // -1: links two regions
        public int Pass = -1;               // trunk roads: the pass they go through
        public float[] X = new float[0], Y = new float[0], Z = new float[0];
        public bool[] Bridge;               // per segment (point i to i + 1): a deck over water; the terrain is not shaped to it (null: none)
        public bool[] Ford;                 // per segment: a shallow crossing, the road runs through the water
    }

    // A compound (cluster of lots around a yard) and the part of its region nearest to it: the unit of partial closure.
    [Serializable]
    public class WorldSubZone
    {
        public string Name;
        public int Region;
        public float X, Y, Z;
        public float Radius;                // flattened yard
    }

    // A building footprint (Buildings.Generate(Kind, SizeX, SizeZ, seed)): SizeX is the width along the front,
    // SizeZ the depth; the front (+Z of the building) faces YawDeg, toward the road. The ground is level at Y
    // under the footprint and PadMargin beyond it.
    [Serializable]
    public class BuildingLot
    {
        public const float PadMargin = 2f;

        public string Name;
        public BuildingKind Kind;
        public int Region, SubZone;
        public float X, Y, Z;
        public float SizeX, SizeZ;
        public float YawDeg;
        public float GroundSlopeDeg;        // natural slope before the pad was flattened

        // Footprint coordinates (right, forward) of a world point.
        public void ToLocal(float x, float z, out float right, out float forward)
        {
            double yaw = YawDeg * Math.PI / 180.0;
            float cos = (float)Math.Cos(yaw), sin = (float)Math.Sin(yaw);
            float dx = x - X, dz = z - Z;
            right = dx * cos - dz * sin;
            forward = dx * sin + dz * cos;
        }

        public void ToWorld(float right, float forward, out float x, out float z)
        {
            double yaw = YawDeg * Math.PI / 180.0;
            float cos = (float)Math.Cos(yaw), sin = (float)Math.Sin(yaw);
            x = X + right * cos + forward * sin;
            z = Z - right * sin + forward * cos;
        }

        // Distance in the ground plane to the footprint (0 inside).
        public float Distance(float x, float z)
        {
            ToLocal(x, z, out float right, out float forward);
            float dx = Math.Max(0f, Math.Abs(right) - SizeX * 0.5f);
            float dz = Math.Max(0f, Math.Abs(forward) - SizeZ * 0.5f);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // Do two footprints come within margin of each other? (separating axes; slightly generous at corners)
        public static bool Overlap(BuildingLot a, BuildingLot b, float margin)
        {
            double ya = a.YawDeg * Math.PI / 180.0, yb = b.YawDeg * Math.PI / 180.0;
            float[] ax = { (float)Math.Cos(ya), (float)Math.Sin(ya), (float)Math.Cos(yb), (float)Math.Sin(yb) };
            float[] az = { -(float)Math.Sin(ya), (float)Math.Cos(ya), -(float)Math.Sin(yb), (float)Math.Cos(yb) };
            float dx = b.X - a.X, dz = b.Z - a.Z;
            for (int i = 0; i < 4; i++)
            {
                float ra = a.SizeX * 0.5f * Math.Abs(ax[i] * ax[0] + az[i] * az[0]) + a.SizeZ * 0.5f * Math.Abs(ax[i] * ax[1] + az[i] * az[1]);
                float rb = b.SizeX * 0.5f * Math.Abs(ax[i] * ax[2] + az[i] * az[2]) + b.SizeZ * 0.5f * Math.Abs(ax[i] * ax[3] + az[i] * az[3]);
                if (Math.Abs(dx * ax[i] + dz * az[i]) > ra + rb + margin) return false;
            }
            return true;
        }
    }

    // Where players come up at match start (ADR-050). FacingDeg is the way out, toward the compound's road junction.
    [Serializable]
    public class WorldElevator
    {
        public const float StationWidth = 5f, StationDepth = 7.5f;   // BuildingKind.ElevatorStation footprint
        public const float PadWidth = StationWidth + 2f * BuildingLot.PadMargin, PadDepth = StationDepth + 2f * BuildingLot.PadMargin;

        public string Name;
        public int Region, SubZone;
        public float X, Y, Z;
        public float FacingDeg;

        // The levelled ground the station stands on, as a footprint.
        public BuildingLot Pad() => new BuildingLot { Name = Name, Kind = BuildingKind.ElevatorStation, X = X, Y = Y, Z = Z, SizeX = PadWidth, SizeZ = PadDepth, YawDeg = FacingDeg };
    }

    // Convergence point (ClearRadius is its open ground) or outdoor loot point (Lot stays -1).
    [Serializable]
    public class WorldPoint
    {
        public string Name;
        public int Region;
        public int Lot = -1;
        public float X, Y, Z;
        public float ClearRadius;
    }

    [Serializable]
    public struct ScatterPoint
    {
        public float X, Y, Z;
        public float Scale, YawDeg;
        public int Region;
    }

    // Everything the editor builder needs to make the island. Produced by WorldGenerator, checked by WorldRules.
    public class WorldPlan
    {
        public float Size, SeaLevel, MaxHeight, MaxRoadSlopeDeg;
        public int Seed;
        public WorldRuleSpec Rules = new WorldRuleSpec();   // the limits WorldRules checks (null = defaults)
        public WorldBuildSpec Build = new WorldBuildSpec();
        public List<WorldRegion> Regions = new List<WorldRegion>();
        public List<WorldPass> Passes = new List<WorldPass>();
        public List<WorldRoad> Roads = new List<WorldRoad>();
        public List<WorldSubZone> SubZones = new List<WorldSubZone>();
        public List<BuildingLot> Lots = new List<BuildingLot>();
        public List<WorldElevator> Elevators = new List<WorldElevator>();
        public List<WorldPoint> ConvergencePoints = new List<WorldPoint>();
        public List<WorldPoint> LootPoints = new List<WorldPoint>(); // outdoor only (none yet): indoor loot comes with each BuildingPlan
        public List<ScatterPoint> Rocks = new List<ScatterPoint>();
        public List<ScatterPoint> Trees = new List<ScatterPoint>();
        public List<WorldRiver> Rivers = new List<WorldRiver>();      // the island's rivers (none in the original world)
        public List<WorldBridge> Bridges = new List<WorldBridge>();   // decks over water, in road order

        // The ground is fixed when the plan is generated: editing the lists afterwards does not move it.
        internal WorldTerrain Terrain;

        // Ground height in metres (0..MaxHeight). Water stands at SeaLevel.
        public float Height(float x, float z) => Terrain != null ? Terrain.Height(x, z) : 0f;

        // Fills grid[z, x] (the order TerrainData.SetHeights wants) over the whole map, corners included.
        // scale = 1 / MaxHeight gives the 0..1 values a heightmap takes.
        public void FillHeights(float[,] grid, float scale = 1f)
        {
            int rows = grid.GetLength(0), cols = grid.GetLength(1);
            float stepZ = rows > 1 ? Size / (rows - 1) : 0f, stepX = cols > 1 ? Size / (cols - 1) : 0f;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    grid[r, c] = Height(c * stepX, r * stepZ) * scale;
        }

        // Steepness (rise over run) of the final ground.
        public float SlopeTan(float x, float z)
        {
            float gx = (Height(x + 1f, z) - Height(x - 1f, z)) * 0.5f;
            float gz = (Height(x, z + 1f) - Height(x, z - 1f)) * 0.5f;
            return (float)Math.Sqrt(gx * gx + gz * gz);
        }

        public bool IsWater(float x, float z) => Height(x, z) < SeaLevel;

        // Index into Regions, or -1 for the sea and anything off the island.
        public int RegionAt(float x, float z)
        {
            if (Terrain == null || x < 0f || z < 0f || x > Size || z > Size || Terrain.Coast(x, z) < 0f) return -1;
            Terrain.Locate(x, z, out int region, out _);
            return region;
        }

        // Distance to the nearest region border (the ridge line); 0 at sea.
        public float BorderDistance(float x, float z)
        {
            if (RegionAt(x, z) < 0) return 0f;
            Terrain.Locate(x, z, out _, out float border);
            return border;
        }

        // Index into SubZones (the nearest compound of the region the point is in), or -1.
        public int SubZoneAt(float x, float z)
        {
            int region = RegionAt(x, z);
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; region >= 0 && i < SubZones.Count; i++)
            {
                if (SubZones[i].Region != region) continue;
                float dx = x - SubZones[i].X, dz = z - SubZones[i].Z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // Distance to the nearest road edge (negative on a road); float.MaxValue when none is within 10 m.
        public float RoadDistance(float x, float z) => Terrain != null ? Terrain.RoadEdge(x, z) : float.MaxValue;

        // Terrain layer weights (sum 1): grass, dirt/gravel, rock, sand/mud.
        public void Splat(float x, float z, out float grass, out float dirt, out float rock, out float sand) =>
            Splat(x, z, Height(x, z), SlopeTan(x, z), out grass, out dirt, out rock, out sand);

        // Same, for callers that already have the height and steepness (a builder walking its heightmap).
        public void Splat(float x, float z, float height, float slopeTan, out float grass, out float dirt, out float rock, out float sand)
        {
            grass = 1f; dirt = 0f; rock = 0f; sand = 0f;
            if (Terrain == null) return;
            Terrain.Locate(x, z, out int region, out float border);
            var kind = region >= 0 && region < Regions.Count ? Regions[region].Kind : RegionKind.Town;
            float inRegion = WorldTerrain.Smooth01(border / 22f); // region tints fade out toward the ridge
            uint seed = unchecked((uint)Seed);

            // Worn ground: gravel yards in the factory, bare patches elsewhere.
            float patch = 0.5f + 0.5f * WorldNoise.Value(x / 23f, z / 23f, unchecked(seed + 61u));
            float worn = kind == RegionKind.Factory ? 0.25f + 0.35f * patch
                : kind == RegionKind.Mountain ? 0.3f * patch
                : kind == RegionKind.Town ? 0.15f * patch : 0f;
            Blend(ref grass, ref dirt, ref rock, ref sand, 1, worn * inRegion);

            // Shoreline sand; the wetland is mud all over, wettest in the pools.
            float low = 1f - WorldTerrain.Smooth01((height - (SeaLevel + 0.3f)) / 1.5f);
            if (kind == RegionKind.Wetland) low = Math.Max(low, 0.4f * inRegion);
            Blend(ref grass, ref dirt, ref rock, ref sand, 3, low);

            // Rock on anything steep, and on the high mountain.
            float steep = WorldTerrain.Smooth01((slopeTan - 0.58f) / 0.38f); // 30 deg .. 44 deg
            if (kind == RegionKind.Mountain)
                steep = Math.Max(steep, 0.6f * inRegion * WorldTerrain.Smooth01((height - (SeaLevel + 60f)) / 30f));
            Blend(ref grass, ref dirt, ref rock, ref sand, 2, steep);

            // Roads are packed dirt with a shoulder; building pads sit in a dirt apron.
            // The shoulder is ragged: noise moves the edge in and out by a metre or two, so it is never a straight line.
            float edge = Terrain.RoadEdge(x, z);
            if (edge < 6f) edge += 1.5f * WorldNoise.Value(x / 3.7f, z / 3.7f, unchecked(seed + 71u)) + 1.2f * WorldNoise.Value(x / 13f, z / 13f, unchecked(seed + 72u));
            if (edge < 3f) Blend(ref grass, ref dirt, ref rock, ref sand, 1, 1f - WorldTerrain.Smooth01((edge + 0.5f) / 3.5f));
            float pad = Terrain.PadDistance(x, z);
            if (pad < 3f) Blend(ref grass, ref dirt, ref rock, ref sand, 1, 0.85f * (1f - WorldTerrain.Smooth01(pad / 3f)));
        }

        // Moves the weights toward one layer (1 dirt, 2 rock, 3 sand) by w; the sum stays 1.
        static void Blend(ref float grass, ref float dirt, ref float rock, ref float sand, int layer, float w)
        {
            if (w <= 0f) return;
            if (w > 1f) w = 1f;
            float keep = 1f - w;
            grass *= keep; dirt *= keep; rock *= keep; sand *= keep;
            if (layer == 1) dirt += w; else if (layer == 2) rock += w; else sand += w;
        }
    }
}
