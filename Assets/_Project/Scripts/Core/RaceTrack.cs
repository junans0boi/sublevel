using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The race circuit of the waiting island (ADR-505, #438): a sealed hall east of the island, reached through a gate in the island's wall
    // into the pit, where the race cars stand. The circuit is an original layout (no real track copied): a long main straight north past the
    // pit, a hairpin, esses, a fast right-hander, a chicane and a final corner back onto the straight (Corners, drawn by RaceCourse). Pure
    // geometry shared by the builder, the server and the tests. Coordinates are island-local metres (x east, z south, like WaitingIsland)
    // unless a name says world.
    public static class RaceTrack
    {
        public const float Width = 12f;            // tarmac
        public const float Runoff = 4f;            // grass/gravel between the tarmac edge and the barrier, both sides
        public const float MainX = 62f;            // the main straight's centre line (north-bound past the pit)
        public const float LineZ = -26f;           // start/finish across the main straight, just north of the pit exit

        // Corner points (x, z) with the radius each is rounded to, in driving order (clockwise seen from above, north up).
        public static readonly (float x, float z, float r)[] Corners =
        {
            (MainX, 60f, 22f),      // final corner: onto the main straight
            (MainX, -125f, 13f),    // T1 \ hairpin
            (92f, -125f, 13f),      // T2 /
            (92f, -75f, 12f),       // T3 \ esses
            (135f, -95f, 15f),      // T4 /
            (180f, -55f, 25f),      // T5: the fast right-hander
            (180f, 20f, 14f),       // T6
            (140f, 20f, 12f),       // T7 \ chicane
            (125f, 58f, 12f),       // T8 /
        };

        // The pit: a box between the island's wall and the main straight's run-off, open to the track on its east side. Cars stand east of the
        // bollard line (people pass between the posts, cars do not), people may walk all of it.
        public const float PitX0 = 26f, PitX1 = 52f, PitZ0 = -23f, PitZ1 = -6f;
        public const float BollardX = 36f;
        // #483: on the island in front of the gate or in the pit: where the HUD names the race hall and its key.
        public static bool NearGate(float lx, float lz) => lx >= PitX0 - 12f && lx <= PitX1 && lz >= PitZ0 - 4f && lz <= PitZ1 + 4f;
        // The hall round it all (walls and roof); every vehicle inside is a race car (no damage, no fuel, no roadkill, ADR-508).
        public const float HallX0 = 40f, HallX1 = 196f, HallZ0 = -140f, HallZ1 = 75f;
        public const float HallHeight = 20f;      // as high as the island's wall: the gate is a plain cut in it

        // Where the race cars stand (local x, z, yaw in degrees; 90 = east, out to the track): a front row of three and two behind, in the gaps.
        static readonly (float x, float z, float yaw)[] Grid =
        {
            (47f, -19.5f, 90f), (47f, -14.5f, 90f), (47f, -9.5f, 90f),
            (40.5f, -17f, 90f), (40.5f, -12f, 90f),
        };
        public static int Cars => Grid.Length;     // ADR-506: measured cost per car, see docs/RACE_SPEC.md
        public static IReadOnlyList<(float x, float z, float yaw)> GridSpots() => Grid;

        // ---- the centre line (RaceCourse, ADR-513) ----

        public static readonly RaceCourse Course = new RaceCourse(Corners, Width, Runoff, MainX, LineZ, 4, RaceLap.MinLapSeconds);
        public static IReadOnlyList<RaceCourse.Point> Path => Course.Path;
        public static float LapLength => Course.LapLength;

        // Distance along the lap from the line (0..LapLength, the race runs north on the main straight) and the offset from the centre line.
        public static (float s, float offset) Project(float lx, float lz) => Course.Project(lx, lz);
        public static bool OnTrack(float lx, float lz) => Course.OnTrack(lx, lz);
        public static int Sector(float s) => Course.Sector(s);

        // ---- the hall ----

        public static bool InArea(float worldX, float worldZ)
        {
            float lx = worldX - WaitingIsland.CentreX, lz = worldZ - WaitingIsland.CentreZ;
            return lx <= HallX1 && lz >= HallZ0 && lz <= HallZ1 && (lx >= HallX0 || (lx >= BollardX && lz >= PitZ0 && lz <= PitZ1));
        }

        // Drawing the hall (perf, ADR-505): without occlusion culling a camera on the island would draw the whole hall behind the island's
        // wall. From the island only the pit and the hall's first stretch can be seen through the gate: pieces that start at GateViewX or
        // further east, or further than GateViewHalfZ north or south of the gate (and the lamps there), are drawn only for a camera in the pit
        // or the hall (lx >= BollardX - 1).
        public const float GateViewX = 80f;
        public const float GateViewHalfZ = 40f;   // ...and only within this far north or south of the gate (the island's wall hides the rest)
        public static bool SeenFromIsland(float minLx, float minLz, float maxLz)
            => minLx < GateViewX && maxLz > (PitZ0 + PitZ1) * 0.5f - GateViewHalfZ && minLz < (PitZ0 + PitZ1) * 0.5f + GateViewHalfZ;
        public static bool SeesWholeHall(float worldX, float worldZ) => worldX - WaitingIsland.CentreX >= BollardX - 1f || InArea(worldX, worldZ);

        public static bool InPit(float lx, float lz, float margin = 0f)
            => lx >= BollardX + margin && lx <= PitX1 - margin && lz >= PitZ0 + margin && lz <= PitZ1 - margin;

        // ---- people on foot (ADR-508): the island's walk circle or the pit, never the track ----
        const float WalkerPitX1 = PitX1 - 0.5f, WalkerPitZ0 = PitZ0 + 0.5f, WalkerPitZ1 = PitZ1 - 0.5f;

        public static bool Walkable(float lx, float lz)
            => lx * lx + lz * lz <= WaitingIsland.WalkRadius * WaitingIsland.WalkRadius + 1e-3f
               || (lx >= PitX0 && lx <= WalkerPitX1 && lz >= WalkerPitZ0 && lz <= WalkerPitZ1);

        // The nearest walkable place (world metres), for the server's clamp of the waiting people.
        public static void ClampWalker(ref float x, ref float z)
        {
            float lx = x - WaitingIsland.CentreX, lz = z - WaitingIsland.CentreZ;
            if (Walkable(lx, lz)) return;
            float cx = x, cz = z;
            WaitingIsland.Clamp(ref cx, ref cz);
            float px = WaitingIsland.CentreX + Math.Min(WalkerPitX1, Math.Max(PitX0, lx));
            float pz = WaitingIsland.CentreZ + Math.Min(WalkerPitZ1, Math.Max(WalkerPitZ0, lz));
            float dc = (cx - x) * (cx - x) + (cz - z) * (cz - z), dp = (px - x) * (px - x) + (pz - z) * (pz - z);
            if (dc <= dp) { x = cx; z = cz; } else { x = px; z = pz; }
        }

        // Shortest distance from the hall to the map box [0, mapSize]^2 (world).
        public static float GapToMap(float mapSize)
        {
            float x0 = WaitingIsland.CentreX + PitX0, x1 = WaitingIsland.CentreX + HallX1, z0 = WaitingIsland.CentreZ + HallZ0, z1 = WaitingIsland.CentreZ + HallZ1;
            float dx = Math.Max(0f, Math.Max(0f - x1, x0 - mapSize)), dz = Math.Max(0f, Math.Max(0f - z1, z0 - mapSize));
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
