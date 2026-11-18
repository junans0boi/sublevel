using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The waiting island (ADR-381, #414): a sealed 80 m platform far out in the sea of the World scene where matched people (and
    // waiting bots) gather before the elevator ride. Pure data and geometry, shared by the editor builder (what to build where),
    // the server (clamp, spawn) and the tests. Coordinates are world metres like the map (x east, z south; the map box is
    // 0..WorldSize in both).
    public static class WaitingIsland
    {
        public const float CentreX = 1200f;          // beside the map's middle...
        public const float CentreZ = -1800f;         // ...1.8 km north of its edge: past the far clip + fog, well inside float precision
        public const float Radius = 40f;             // 80 m across
        public const float WalkRadius = 36f;         // people stay inside this circle (server clamp)
        public const float FloorOffset = 0.4f;       // the deck stands this high above the sea plane

        // Local (to the centre) positions of the features. x east, z south.
        public static readonly (float x, float z) Tower = (22f, 4f);          // bungee tower
        public const float TowerRadius = 5f;
        public static readonly (float x, float z) Board = (0f, -33f);         // the big board, on the north rim facing the middle
        public const float BoardWidth = 22f, BoardHeight = 9f;
        public static readonly (float x, float z)[] Booths =                  // shooting booths (2nd stage), west side
        {
            (-27f, -12f), (-27f, 0f), (-27f, 12f),
        };
        public const float BoothRadius = 3.5f;
        public static readonly (float x, float z) Elevator = (0f, 31f);       // the lift doors the countdown ends at (south rim)
        public const float ElevatorRadius = 6f;

        public const int SpawnCount = 96;            // a match has at most 80 participants

        public static float WorldX(float localX) => CentreX + localX;
        public static float WorldZ(float localZ) => CentreZ + localZ;

        public static bool Inside(float x, float z, float margin = 0f)
        {
            float dx = x - CentreX, dz = z - CentreZ, r = Radius - margin;
            return dx * dx + dz * dz <= r * r;
        }

        // The nearest point inside the walkable circle (server authority: the waiting people cannot leave the island).
        public static void Clamp(ref float x, ref float z)
        {
            float dx = x - CentreX, dz = z - CentreZ, d2 = dx * dx + dz * dz;
            if (d2 <= WalkRadius * WalkRadius) return;
            float k = WalkRadius / (float)Math.Sqrt(d2);
            x = CentreX + dx * k;
            z = CentreZ + dz * k;
        }

        // Shortest distance from the island's rim to the map box [0, mapSize]^2 (0 when overlapping).
        public static float GapToMap(float mapSize)
        {
            float dx = Math.Max(0f, Math.Max(0f - CentreX, CentreX - mapSize));
            float dz = Math.Max(0f, Math.Max(0f - CentreZ, CentreZ - mapSize));
            return Math.Max(0f, (float)Math.Sqrt(dx * dx + dz * dz) - Radius);
        }

        // Spread of standing places: a sunflower spiral, minus the spots the features take. Deterministic.
        public static List<(float x, float z)> SpawnPoints()
        {
            var list = new List<(float, float)>();
            const float golden = 2.39996323f;
            for (int i = 0; list.Count < SpawnCount && i < SpawnCount * 4; i++)
            {
                float r = 6f + 26f * (float)Math.Sqrt((i + 0.5f) / (SpawnCount * 1.6f));
                float a = i * golden;
                float lx = r * (float)Math.Cos(a), lz = r * (float)Math.Sin(a);
                if (Blocked(lx, lz, 1.8f)) continue;
                list.Add((WorldX(lx), WorldZ(lz)));
            }
            return list;
        }

        static bool Blocked(float lx, float lz, float pad)
        {
            if (Near(lx, lz, Tower.x, Tower.z, TowerRadius + pad)) return true;
            if (Near(lx, lz, Elevator.x, Elevator.z, ElevatorRadius + pad)) return true;
            foreach (var b in Booths) if (Near(lx, lz, b.x, b.z, BoothRadius + pad)) return true;
            return Math.Abs(lx - Board.x) < BoardWidth * 0.5f + pad && lz < Board.z + 6f;
        }

        static bool Near(float x, float z, float cx, float cz, float r) { float dx = x - cx, dz = z - cz; return dx * dx + dz * dz < r * r; }
    }
}
