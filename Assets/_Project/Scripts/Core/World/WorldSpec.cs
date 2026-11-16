using System;

namespace Game.Core
{
    public enum RegionKind { Mountain, Wetland, Town, Factory }

    // One region of the island (ADR-052). Heights are metres above sea level.
    [Serializable]
    public class RegionSpec
    {
        public string Id;                 // "A".."D"
        public string Name;               // display name (Korean)
        public string Kind;               // RegionKind as text so the JSON stays readable
        public string Style = "";        // Mountain only: "" = the original radial relief, "Ridge" = MountainTerrain (ridge lines, saddles, benches; #247)
        public float CentreX, CentreZ;    // regions are the Voronoi cells of these points
        public float Radius;              // rough reach: compounds prefer to stay this close to the centre
        public float BaseHeight;
        public float Relief;              // noise amplitude (mountain: peak height above the foot)
        public int Elevators;             // one compound per elevator
        public int Lots;                  // fewest building lots the region may end up with
        public int CompoundLots;          // lots the generator aims for around each compound
        public int Hamlets;               // outlying clusters on their own lane, no elevator
        public int HamletLots;            // lots per hamlet
        public int ConvergencePoints;
        public float CompoundRadius;      // flattened yard around each compound
        public float RocksPerHectare;
        public float TreesPerHectare;

        public RegionKind KindValue => Enum.TryParse(Kind, true, out RegionKind kind) ? kind : RegionKind.Town;
    }

    // The limits WorldRules checks a plan against (#244: they were constants).
    [Serializable]
    public class WorldRuleSpec
    {
        public int MinElevators = 3, MaxElevators = 4;   // per region
        public float MinElevatorSpacing = 80f;
        public int MinLotsNearElevator = 2;
        public float LotRange = 60f;                     // buildings to run to straight out of the elevator
        public float DoorstepRange = 35f;                // ...and at least one of them practically at the door
        public float MaxLotGroundSlopeDeg = 20f;
        public float WalkSlopeDeg = 40f;
        public float MinLandKm2 = 0.5f, MaxLandKm2 = 0.8f;
        public bool RegionsAreWalled = true;             // ridges and sea separate the regions (original island); false on the C2 island, where rivers are crossed by wading, swimming or bridges
    }

    // What the world builder (editor) used to hard-code: caps, far clip and fog, and the terrain map resolutions.
    [Serializable]
    public class WorldBuildSpec
    {
        public int MaxLootSpawns = 250;       // hard ceiling on loot points (network objects); LootPerBuilding decides how many are wanted
        public float LootPerBuilding = 3.4f;  // points a building of weight 1 asks for (today: 250 points / 73 buildings)
        public int MaxRocks = 900, MaxSingleLodRocks = 80;
        public float FarClip = 700f, FogStart = 90f, FogEnd = 640f;
        public float AlphamapMetersPerPixel = 1f;
        public float HeightmapMetersPerSample = 1f;

        // Unity wants a power of two (16..4096) for the alphamap and 2^n + 1 (33..4097) for the heightmap.
        public int AlphamapResolution(float size) => NearestPowerOfTwo(size / AlphamapMetersPerPixel, 16, 4096);
        public int HeightmapResolution(float size) => NearestPowerOfTwo(size / HeightmapMetersPerSample, 32, 4096) + 1;

        static int NearestPowerOfTwo(float value, int min, int max)
        {
            int p = min;
            while (p < max && value > p * 1.4142135f) p *= 2;
            return p;
        }
    }

    // The world as data: world.json is this class (JsonUtility). The generator adds no randomness beyond Seed.
    [Serializable]
    public class WorldSpec
    {
        public float Size = 1024f;
        public int Seed = 1;
        public float SeaLevel = 10f;
        public float MaxHeight = 120f;
        public float IslandRadius = 420f;     // rounded-square coast, centred on the map
        public float CoastNoise = 14f;
        public float CoastBays = 45f;         // bays and headlands: how far the outline swings in and out (ADR-070)
        public float RidgeHeight = 34f;       // border ridge crest above sea level
        public float RidgeHalfWidth = 22f;
        public int PassesPerBorder = 2;
        public float PassWidth = 18f;
        public float RoadWidth = 6f;
        public float SpurWidth = 4.5f;
        public float PathWidth = 2.5f;
        public float MaxRoadSlopeDeg = 12f;
        public WorldRuleSpec Rules = new WorldRuleSpec();
        public WorldBuildSpec Build = new WorldBuildSpec();
        public RegionSpec[] Regions = new RegionSpec[0];
        public IslandSpec Island;             // the C2 island 'Cheongnam-do' (ADR-204); null = the original generator

        // The C2 island (ADR-168, ADR-204): a 2400 m box with the 2.4 x 2.0 km island at its north, so every map and builder keeps its square assumption.
        // Same values as Resources/GameData/world.json (WorldTests checks).
        public static WorldSpec CreateIsland()
        {
            var isl = IslandData.Create();
            var regions = new RegionSpec[isl.Regions.Length];
            for (int i = 0; i < regions.Length; i++)
            {
                var r = isl.Regions[i];
                regions[i] = new RegionSpec
                {
                    Id = r.Id, Name = r.Name, Kind = r.Kind, CentreX = r.Seeds[0], CentreZ = r.Seeds[1], Radius = 300f, Elevators = r.Elevators,
                    Lots = 8, CompoundLots = 3, ConvergencePoints = 3, CompoundRadius = 40f, RocksPerHectare = r.RocksPerHectare, TreesPerHectare = r.TreesPerHectare,
                };
            }
            return new WorldSpec
            {
                Size = 2400f, Seed = 20261116, SeaLevel = 10f, MaxHeight = 240f, IslandRadius = 0f, CoastNoise = 0f, CoastBays = 0f, MaxRoadSlopeDeg = 12f,
                Island = isl, Regions = regions,
                Rules = new WorldRuleSpec { MinElevators = 3, MaxElevators = 4, MinElevatorSpacing = 80f, MinLotsNearElevator = 2, LotRange = 60f, DoorstepRange = 35f, MaxLotGroundSlopeDeg = 20f, WalkSlopeDeg = 40f, MinLandKm2 = 2.8f, MaxLandKm2 = 3.8f, RegionsAreWalled = false },
                Build = new WorldBuildSpec { MaxLootSpawns = 900, LootPerBuilding = 3.4f, MaxRocks = 1400, MaxSingleLodRocks = 120, FarClip = 700f, FogStart = 90f, FogEnd = 640f },
            };
        }

        // Same values as Resources/GameData/world.json (Game.Core cannot read Resources).
        public static WorldSpec CreateDefault() => new WorldSpec
        {
            Size = 1024f, Seed = 20261113, SeaLevel = 10f, MaxHeight = 120f, IslandRadius = 420f, CoastNoise = 14f, CoastBays = 45f,
            RidgeHeight = 34f, RidgeHalfWidth = 22f, PassesPerBorder = 2, PassWidth = 18f,
            RoadWidth = 6f, SpurWidth = 4.5f, PathWidth = 2.5f, MaxRoadSlopeDeg = 12f,
            Regions = new[]
            {
                new RegionSpec
                {
                    Id = "A", Name = "산악", Kind = "Mountain", Style = "Ridge", CentreX = 520f, CentreZ = 800f, Radius = 320f,
                    BaseHeight = 18f, Relief = 84f, Elevators = 3, Lots = 8, CompoundLots = 3, Hamlets = 0, HamletLots = 0, ConvergencePoints = 3, CompoundRadius = 40f,
                    RocksPerHectare = 60f, TreesPerHectare = 40f,
                },
                new RegionSpec
                {
                    Id = "B", Name = "습지", Kind = "Wetland", CentreX = 200f, CentreZ = 400f, Radius = 300f,
                    BaseHeight = 0.05f, Relief = 0.6f, Elevators = 3, Lots = 8, CompoundLots = 3, Hamlets = 2, HamletLots = 2, ConvergencePoints = 3, CompoundRadius = 40f,
                    RocksPerHectare = 5f, TreesPerHectare = 30f,
                },
                new RegionSpec
                {
                    Id = "C", Name = "마을", Kind = "Town", CentreX = 500f, CentreZ = 400f, Radius = 280f,
                    BaseHeight = 8f, Relief = 4.5f, Elevators = 4, Lots = 30, CompoundLots = 7, Hamlets = 3, HamletLots = 3, ConvergencePoints = 3, CompoundRadius = 50f,
                    RocksPerHectare = 8f, TreesPerHectare = 25f,
                },
                new RegionSpec
                {
                    Id = "D", Name = "공장", Kind = "Factory", CentreX = 824f, CentreZ = 400f, Radius = 300f,
                    BaseHeight = 9f, Relief = 3.2f, Elevators = 3, Lots = 14, CompoundLots = 5, Hamlets = 2, HamletLots = 2, ConvergencePoints = 3, CompoundRadius = 60f,
                    RocksPerHectare = 6f, TreesPerHectare = 8f,
                },
            },
        };
    }
}
