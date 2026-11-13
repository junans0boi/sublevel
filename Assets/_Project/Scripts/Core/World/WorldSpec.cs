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
        public RegionSpec[] Regions = new RegionSpec[0];

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
                    Id = "A", Name = "산악", Kind = "Mountain", CentreX = 520f, CentreZ = 800f, Radius = 320f,
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
