using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Zone D (factory) blockout, ADR-040. 160 x 120 m, four compartments, each with one convergence point:
    //
    //                 north (+Z)
    //   +-----------------[N]------------------+
    //   |  open strip                          |
    //   |  +--------------+      Loading Yard  |
    //  [W] | Assembly Hall |     (containers)  |
    //   |  | + mezzanine   |                  [E]
    //   |  +--------------+    +-------------+ |
    //   |   Power Plaza        | Warehouse   | |
    //   |   (tanks, pipes)     | Hall        | |
    //   +-----------------[S]--+-------------+-+
    //
    // Everything is deterministic: the same call always returns the same layout.
    public static class ZoneDBlockout
    {
        const float HalfX = 80f, HalfZ = 60f;
        const float PerimeterHeight = 8f;
        const float WallThickness = 0.5f;
        const float LootHeight = 0.3f;

        enum Side { North, South, East, West }

        public static ZoneLayout Create()
        {
            var layout = new ZoneLayout { Id = "D", HalfX = HalfX, HalfZ = HalfZ, WallHeight = PerimeterHeight };
            Shell(layout);
            AssemblyHall(layout);
            WarehouseHall(layout);
            LoadingYard(layout);
            PowerPlaza(layout);
            Connectors(layout);
            Elevators(layout);
            return layout;
        }

        // ---- building blocks ----

        static void Box(ZoneLayout layout, string name, BlockKind kind, float x, float z, float sizeX, float sizeZ, float height, float baseY = 0f)
        {
            layout.Blocks.Add(new ZoneBlock
            {
                Name = name, Kind = kind, X = x, Y = baseY + height * 0.5f, Z = z, SizeX = sizeX, SizeY = height, SizeZ = sizeZ,
            });
        }

        static void Crate(ZoneLayout layout, string name, float x, float z) => Box(layout, name, BlockKind.Cover, x, z, 2f, 2f, 1.2f);

        static void Loot(ZoneLayout layout, float x, float z, float y = LootHeight) =>
            layout.LootPoints.Add(new ZonePoint { Name = "Loot" + layout.LootPoints.Count, X = x, Y = y, Z = z });

        static void Convergence(ZoneLayout layout, string name, float x, float z, float clearRadius) =>
            layout.ConvergencePoints.Add(new ZonePoint { Name = name, X = x, Z = z, ClearRadius = clearRadius });

        // Four walls around a rectangle, with door gaps given as (side, position along the wall, width).
        static void Room(ZoneLayout layout, string name, float x0, float x1, float z0, float z1, float height, params (Side side, float at, float width)[] doors)
        {
            foreach (Side side in Enum.GetValues(typeof(Side)))
            {
                bool alongX = side == Side.North || side == Side.South;
                float from = alongX ? x0 : z0, to = alongX ? x1 : z1;
                float line = side == Side.North ? z1 : side == Side.South ? z0 : side == Side.East ? x1 : x0;

                var gaps = new List<(float start, float end)>();
                foreach (var door in doors)
                    if (door.side == side) gaps.Add((door.at - door.width * 0.5f, door.at + door.width * 0.5f));
                gaps.Sort((a, b) => a.start.CompareTo(b.start));

                float cursor = from;
                int piece = 0;
                void Segment(float a, float b)
                {
                    if (b - a < 0.05f) return;
                    float mid = (a + b) * 0.5f, length = b - a;
                    if (alongX) Box(layout, $"{name}Wall{side}{piece}", BlockKind.Wall, mid, line, length, WallThickness, height);
                    else Box(layout, $"{name}Wall{side}{piece}", BlockKind.Wall, line, mid, WallThickness, length, height);
                    piece++;
                }
                foreach (var gap in gaps)
                {
                    Segment(cursor, gap.start);
                    cursor = Math.Max(cursor, gap.end);
                }
                Segment(cursor, to);
            }
        }

        // A ramp of the given width that climbs `rise` meters while running `run` meters toward +Z, starting at (x, zStart).
        static void RampNorth(ZoneLayout layout, string name, float x, float zStart, float run, float rise, float width)
        {
            const float thickness = 0.3f;
            double angle = Math.Atan2(rise, run);
            float length = (float)Math.Sqrt(run * run + rise * rise);
            layout.Blocks.Add(new ZoneBlock
            {
                Name = name, Kind = BlockKind.Ramp,
                X = x, Z = zStart + run * 0.5f,
                // Lowered so the top face, not the center plane, meets the floor and the platform.
                Y = rise * 0.5f - thickness * 0.5f / (float)Math.Cos(angle),
                SizeX = width, SizeY = thickness, SizeZ = length,
                PitchDeg = -(float)(angle * 180.0 / Math.PI),
            });
        }

        // ---- compartments ----

        static void Shell(ZoneLayout layout)
        {
            Box(layout, "Ground", BlockKind.Floor, 0, 0, HalfX * 2 + 2, HalfZ * 2 + 2, 1f, -1f);
            Box(layout, "OuterWallNorth", BlockKind.PerimeterWall, 0, HalfZ + 0.5f, HalfX * 2 + 2, 1f, PerimeterHeight);
            Box(layout, "OuterWallSouth", BlockKind.PerimeterWall, 0, -HalfZ - 0.5f, HalfX * 2 + 2, 1f, PerimeterHeight);
            Box(layout, "OuterWallEast", BlockKind.PerimeterWall, HalfX + 0.5f, 0, 1f, HalfZ * 2, PerimeterHeight);
            Box(layout, "OuterWallWest", BlockKind.PerimeterWall, -HalfX - 0.5f, 0, 1f, HalfZ * 2, PerimeterHeight);
        }

        // Big machine hall with a mezzanine along its north wall. Convergence point: the open floor in the middle.
        static void AssemblyHall(ZoneLayout layout)
        {
            const float cx = -33f, cz = 7f;
            Room(layout, "Assembly", -58, -8, -18, 32, 7f,
                (Side.South, cx, 6f), (Side.North, cx, 6f), (Side.East, cz, 6f), (Side.West, cz, 6f), (Side.East, 24f, 4f));
            Convergence(layout, "AssemblyHall", cx, cz, 7f);

            // Machines along the walls, low crates in a loose ring around the open middle.
            var machines = new (float x, float z)[]
            {
                (-52, -12), (-45, -12), (-21, -12), (-14, -12), (-52, -3), (-14, -3),
                (-45, 19), (-21, 19), (-52, 8), (-16, 8),
            };
            for (int i = 0; i < machines.Length; i++)
                Box(layout, "Machine" + i, BlockKind.Cover, machines[i].x, machines[i].z, 3f, 2f, 1.6f);
            var crates = new (float x, float z)[] { (-43.5f, 7), (-22.5f, 7), (-33, -4), (-33, 18), (-40, -6), (-26, -6) };
            for (int i = 0; i < crates.Length; i++) Crate(layout, "AssemblyCrate" + i, crates[i].x, crates[i].z);

            // Mezzanine: deck 4 m up along the north wall, two ramps, a parapet with gaps at the ramp heads.
            const float deckTop = 4f, deckThickness = 0.4f;
            Box(layout, "AssemblyMezzanine", BlockKind.Platform, -33, 28.75f, 46f, 5.5f, deckThickness, deckTop - deckThickness);
            RampNorth(layout, "MezzanineRampWest", -52, 15, 11, deckTop, 3f);
            RampNorth(layout, "MezzanineRampEast", -14, 15, 11, deckTop, 3f);
            Box(layout, "MezzanineParapetWest", BlockKind.Cover, -42, 26.3f, 14f, 0.4f, 1f, deckTop);
            Box(layout, "MezzanineParapetEast", BlockKind.Cover, -24, 26.3f, 14f, 0.4f, 1f, deckTop);
            foreach (var x in new[] { -46f, -39f, -27f, -20f })
                Box(layout, "MezzaninePost" + x, BlockKind.Wall, x, 26.3f, 0.5f, 0.5f, deckTop - deckThickness);
            layout.Landmarks.Add(new ZonePoint { Name = "AssemblyMezzanine", X = -33, Y = deckTop, Z = 29 });

            Loot(layout, -50, 0); Loot(layout, -16, 0); Loot(layout, -45, 12); Loot(layout, -21, 12);
            Loot(layout, -33, -12); Loot(layout, -33, 23);
            Loot(layout, -40, 29, deckTop + LootHeight); Loot(layout, -26, 29, deckTop + LootHeight);
        }

        // Storage hall: shelf rows on both sides of a wide cross aisle. Convergence point: the aisle crossing.
        static void WarehouseHall(ZoneLayout layout)
        {
            const float cx = 43f, cz = -28f;
            Room(layout, "Warehouse", 18, 68, -48, -8, 7f,
                (Side.North, cx, 6f), (Side.West, cz, 6f), (Side.East, cz, 4f), (Side.South, 30f, 4f));
            Convergence(layout, "WarehouseHall", cx, cz, 6f);

            int shelf = 0;
            foreach (var x in new[] { 24f, 30f, 36f, 50f, 56f, 62f })
            {
                Box(layout, "Shelf" + shelf++, BlockKind.Cover, x, -40, 1.2f, 10f, 3f);
                Box(layout, "Shelf" + shelf++, BlockKind.Cover, x, -16, 1.2f, 10f, 3f);
            }
            var crates = new (float x, float z)[] { (43, -38.5f), (43, -17.5f), (33, -24), (53, -32), (27, -30), (60, -26) };
            for (int i = 0; i < crates.Length; i++) Crate(layout, "WarehouseCrate" + i, crates[i].x, crates[i].z);

            Loot(layout, 27, -40); Loot(layout, 33, -16); Loot(layout, 59, -40); Loot(layout, 53, -16);
            Loot(layout, 43, -12); Loot(layout, 43, -44); Loot(layout, 64, -28);
        }

        // Open-air container yard. Convergence point: the clearing in the middle of the stacks.
        static void LoadingYard(ZoneLayout layout)
        {
            const float cx = 42f, cz = 30f;
            const float w = 2.4f, h = 2.6f;
            Convergence(layout, "LoadingYard", cx, cz, 7f);

            int n = 0;
            foreach (var z in new[] { 14f, 20f, 40f, 46f })
                foreach (var x in new[] { 20f, 34f, 48f, 62f })
                {
                    // Leave a gap in each row so the lanes connect.
                    if ((z == 20f && x == 34f) || (z == 40f && x == 48f)) continue;
                    Box(layout, "Container" + n++, BlockKind.Container, x, z, 12f, w, h);
                }
            // Second tier on some of them, for height variation and sight blocking.
            foreach (var (x, z) in new[] { (20f, 14f), (62f, 14f), (34f, 46f), (62f, 46f), (48f, 20f) })
                Box(layout, "Container" + n++, BlockKind.Container, x, z, 12f, w, h, h);
            // Cross pieces framing the clearing.
            Box(layout, "Container" + n++, BlockKind.Container, 18, 30, w, 12f, h);
            Box(layout, "Container" + n++, BlockKind.Container, 66, 30, w, 12f, h);
            Box(layout, "Container" + n++, BlockKind.Container, 30, 30, w, 6f, h);
            Box(layout, "Container" + n++, BlockKind.Container, 54, 30, w, 6f, h);
            Crate(layout, "YardCrate0", 42, 20.5f);
            Crate(layout, "YardCrate1", 42, 39.5f);
            Crate(layout, "YardCrate2", 24, 26);
            Crate(layout, "YardCrate3", 60, 34);

            Loot(layout, 27, 17); Loot(layout, 55, 17); Loot(layout, 27, 43); Loot(layout, 55, 43);
            Loot(layout, 36, 24); Loot(layout, 48, 36); Loot(layout, 71, 30);
        }

        // Yard in front of the power house: tanks, pipe runs, low barriers. Convergence point: the paved middle.
        static void PowerPlaza(ZoneLayout layout)
        {
            const float cx = -46f, cz = -42f;
            Convergence(layout, "PowerPlaza", cx, cz, 6f);
            Room(layout, "PowerHouse", -74, -62, -54, -34, 5f, (Side.East, -44f, 3f));

            var tanks = new (float x, float z)[] { (-28, -50), (-22, -44), (-28, -34) };
            for (int i = 0; i < tanks.Length; i++) Box(layout, "Tank" + i, BlockKind.Cover, tanks[i].x, tanks[i].z, 4f, 4f, 5f);
            Box(layout, "PipeRun0", BlockKind.Cover, -53, -30, 10f, 0.8f, 1f);
            Box(layout, "PipeRun1", BlockKind.Cover, -39, -30, 10f, 0.8f, 1f);
            Box(layout, "PipeRun2", BlockKind.Cover, -48, -54, 16f, 0.8f, 1f);
            Box(layout, "BarrierWest", BlockKind.Cover, -55, cz, 0.6f, 3f, 1.1f);
            Box(layout, "BarrierEast", BlockKind.Cover, -37, cz, 0.6f, 3f, 1.1f);
            Box(layout, "BarrierSouth", BlockKind.Cover, cx, -50.5f, 3f, 0.6f, 1.1f);
            Box(layout, "BarrierNorth", BlockKind.Cover, cx, -34, 3f, 0.6f, 1.1f);
            Crate(layout, "PowerCrate0", -58, -50);
            Crate(layout, "PowerCrate1", -34, -52);

            Loot(layout, -68, -44); Loot(layout, -40, -36); Loot(layout, -52, -48); Loot(layout, -25, -39);
        }

        // Cover along the open routes between compartments, so no approach is a bare run.
        static void Connectors(ZoneLayout layout)
        {
            var crates = new (float x, float z)[]
            {
                // central lane between the assembly hall and the east compartments
                (2, -34), (6, -20), (1, -8), (4, 10), (-2, 22), (6, 32), (0, 43), (2, -44),
                // north strip
                (-15, 40), (-30, 45), (-45, 40), (-60, 48), (-70, 36), (-25, 53),
                // west strip
                (-68, -5), (-64, 20), (-72, -18), (-66, 29),
                // south middle
                (-12, -25), (10, -40), (-14, -48), (9, -54), (-2, -19),
                // east strip
                (74, -40), (73, 20), (74, 46), (73, -18),
                // between the assembly hall and the power plaza
                (-40, -22), (-25, -23), (-55, -23),
            };
            for (int i = 0; i < crates.Length; i++) Crate(layout, "RouteCrate" + i, crates[i].x, crates[i].z);

            Loot(layout, 0, 0); Loot(layout, 5, 40); Loot(layout, -66, 42); Loot(layout, -10, -32);
            Loot(layout, 74, -30); Loot(layout, -70, -24);
        }

        // Cars stand against the outer wall at the middle of each side, opening toward the inside.
        static void Elevators(ZoneLayout layout)
        {
            const float inset = 2.8f; // car half-depth plus the shaft's back wall
            Elevator(layout, "ElevatorNorth", 0, HalfZ - inset, 180);
            Elevator(layout, "ElevatorSouth", -5, -HalfZ + inset, 0);
            Elevator(layout, "ElevatorEast", HalfX - inset, -1, 270);
            Elevator(layout, "ElevatorWest", -HalfX + inset, 8, 90);
        }

        static void Elevator(ZoneLayout layout, string name, float x, float z, float facingDeg)
        {
            const float car = 5f, shaftHeight = 4f, shaftThickness = 0.3f, wallOffset = 2.6f; // car center to shaft wall
            var spot = new ElevatorSpot { Name = name, X = x, Z = z, FacingDeg = facingDeg };
            layout.Elevators.Add(spot);
            float fx = spot.ForwardX, fz = spot.ForwardZ; // way out
            float rx = fz, rz = -fx;                      // to the right of the way out
            bool facesAlongZ = Math.Abs(fz) > 0.5f;

            Box(layout, name + "Pad", BlockKind.ElevatorPad, x, z, car, car, 0.05f);
            // Back wall and two side walls; the front stays open.
            Box(layout, name + "ShaftBack", BlockKind.ElevatorShaft, x - fx * wallOffset, z - fz * wallOffset,
                facesAlongZ ? car + 0.5f : shaftThickness, facesAlongZ ? shaftThickness : car + 0.5f, shaftHeight);
            foreach (var sign in new[] { -1f, 1f })
                Box(layout, name + (sign < 0 ? "ShaftLeft" : "ShaftRight"), BlockKind.ElevatorShaft, x + rx * wallOffset * sign, z + rz * wallOffset * sign,
                    facesAlongZ ? shaftThickness : car, facesAlongZ ? car : shaftThickness, shaftHeight);
        }
    }
}
