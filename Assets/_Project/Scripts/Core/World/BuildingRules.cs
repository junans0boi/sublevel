using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum BuildingRule
    {
        ExteriorDoorCount, ExteriorDoorSides, DoorSize, DoorBlocked, RoomUnreachable, RoomWithoutWindow, WindowSill,
        StairMissing, StairStep, StairTop, LootOutside, LootUnsupported, LootInsidePiece, LootUnreachable,
        PieceOutsideFootprint, CeilingClearance, WallThickness, FloorStep, SpawnPoint, PieceBudget,
        DecorBudget, DecorOutside, LampMissing, LampBudget, DecorBlocksDoor, RoofUnreachable,
    }

    public struct BuildingViolation
    {
        public BuildingRule Rule;
        public string Detail;
    }

    // What makes a box building playable (ADR-052, open-world-tech section 7) as checks over a plan.
    // Reachability is a flood fill over a 0.25 m grid of standing places, with the pieces as obstacles.
    public static class BuildingRules
    {
        public const float CapsuleRadius = 0.35f, CapsuleHeight = 1.8f;
        public const float StepHeight = 0.3f;                  // the character controller's step offset
        public const float MinDoorWidth = 1.2f, MinDoorHeight = 2.2f;
        public const float MinCeiling = 3.0f;
        public const float MaxFloorStep = 0.15f;
        public const float MinWall = 0.2f, MaxWall = 0.3f;
        public const float MinSill = 0.9f, MaxSill = 1.1f;
        public const float MaxRise = 0.2f, MinRun = 0.28f, MinStairWidth = 1.1f;
        public const float MaxRoofOverhang = 0.5f;
        public const float MaxAttachment = 1.5f;               // balconies, canopies, awnings and sign panels stand out from the wall this far
        static bool IsAttachment(string name) => name.StartsWith("Balcony", StringComparison.Ordinal) || name.StartsWith("Canopy", StringComparison.Ordinal)
            || name.StartsWith("Awning", StringComparison.Ordinal) || name.StartsWith("Sign", StringComparison.Ordinal);
        public const float Cell = 0.25f;
        const float GridMargin = 2f;
        // The rounded foot of the capsule rides over anything this low beside it (the next two steps of a flight).
        const float Lip = 0.45f;
        const float Eps = 0.005f;

        // Boxes per building, so a building stays a handful of draw calls after combining by material.
        public static int MaxPieces(BuildingKind kind)
        {
            if (kind == BuildingKind.School) return 900;
            if (kind == BuildingKind.Shop) return 380;
            if (kind == BuildingKind.Villa) return 450;
            if (kind == BuildingKind.Apartment) return 620;
            kind = BuildingKinds.Family(kind);
            switch (kind)
            {
                case BuildingKind.House: return 140;
                case BuildingKind.Warehouse: return 120;
                case BuildingKind.FactoryHall: return 140;
                case BuildingKind.Shed: return 30;
                default: return 12;
            }
        }

        // Visual furniture boxes and lamps per building: interior meshes are batched, but the triangles still count.
        public static int MaxDecor(BuildingKind kind)
        {
            if (kind == BuildingKind.School) return 5000;
            if (kind == BuildingKind.Shop) return 2600;
            if (kind == BuildingKind.Villa) return 3000;
            if (kind == BuildingKind.Apartment) return 3600;
            kind = BuildingKinds.Family(kind);
            switch (kind)
            {
                case BuildingKind.House: return 2200;
                case BuildingKind.Warehouse: return 2000;
                case BuildingKind.FactoryHall: return 1500;
                case BuildingKind.Shed: return 220;
                default: return 0;
            }
        }

        public const int MaxLamps = 12;

        public static int MinExteriorDoors(BuildingKind kind) =>
            BuildingKinds.Family(kind) == BuildingKind.Shed || kind == BuildingKind.ElevatorStation ? 1 : 2;

        public static List<BuildingViolation> Validate(BuildingPlan plan)
        {
            var found = new List<BuildingViolation>();
            void Add(BuildingRule rule, string detail) => found.Add(new BuildingViolation { Rule = rule, Detail = detail });
            float hw = plan.Width * 0.5f, hd = plan.Depth * 0.5f;
            var grid = new WalkGrid(plan);

            // ---- pieces ----
            if (plan.Pieces.Count > MaxPieces(plan.Kind)) Add(BuildingRule.PieceBudget, $"{plan.Pieces.Count} pieces > {MaxPieces(plan.Kind)}");
            float lowestFloor = float.MaxValue;
            foreach (var p in plan.Pieces)
            {
                float allowed = p.Kind == SurfaceKind.Trim && IsAttachment(p.Name) ? MaxAttachment : p.Kind == SurfaceKind.Roof || p.Kind == SurfaceKind.Trim ? MaxRoofOverhang : 0f;
                if (p.MinX < -hw - allowed - Eps || p.MaxX > hw + allowed + Eps || p.MinZ < -hd - allowed - Eps || p.MaxZ > hd + allowed + Eps || p.MinY < -Eps)
                    Add(BuildingRule.PieceOutsideFootprint, p.Name);
                if (p.Kind == SurfaceKind.WallOuter || p.Kind == SurfaceKind.WallInner)
                {
                    float thickness = Math.Min(p.SizeX, p.SizeZ);
                    if (thickness < MinWall - Eps || thickness > MaxWall + Eps) Add(BuildingRule.WallThickness, $"{p.Name} is {thickness:F2} m thick");
                }
                if (p.Kind == SurfaceKind.Floor) lowestFloor = Math.Min(lowestFloor, p.MaxY);
            }
            if (lowestFloor > MaxFloorStep + Eps || Math.Abs(lowestFloor - plan.FloorY) > Eps)
                Add(BuildingRule.FloorStep, $"ground floor at {lowestFloor:F2} m, expected {plan.FloorY:F2} and at most {MaxFloorStep}");

            // ---- furniture and lamps ----
            if (plan.Decor.Count > MaxDecor(plan.Kind)) Add(BuildingRule.DecorBudget, $"{plan.Decor.Count} decor boxes > {MaxDecor(plan.Kind)}");
            foreach (var dc in plan.Decor)
            {
                // Inside the footprint, above the floor, and no higher than a tall cabinet's top above any ceiling.
                if (dc.X - dc.SizeX * 0.5f < -hw - Eps || dc.X + dc.SizeX * 0.5f > hw + Eps || dc.Z - dc.SizeZ * 0.5f < -hd - Eps
                    || dc.Z + dc.SizeZ * 0.5f > hd + Eps || dc.Y - dc.SizeY * 0.5f < -Eps || dc.SizeX <= 0f || dc.SizeY <= 0f || dc.SizeZ <= 0f)
                    Add(BuildingRule.DecorOutside, dc.Name);
                // Furniture must not stand in a doorway: only the thin rugs lie on the floor there.
                if (dc.SizeY < 0.05f || dc.Name.StartsWith("Lamp", StringComparison.Ordinal)) continue;
                foreach (var door in plan.Doors)
                {
                    bool wallAlongX = Math.Abs(door.ForwardZ) > 0.5f;
                    float halfAlong = door.Width * 0.5f, reach = door.Exterior ? 0.6f : 0.4f;
                    float hx = wallAlongX ? halfAlong : reach, hz = wallAlongX ? reach : halfAlong;
                    if (dc.X - dc.SizeX * 0.5f < door.X + hx && dc.X + dc.SizeX * 0.5f > door.X - hx && dc.Z - dc.SizeZ * 0.5f < door.Z + hz
                        && dc.Z + dc.SizeZ * 0.5f > door.Z - hz && dc.Y - dc.SizeY * 0.5f < door.Y + 2f && dc.Y + dc.SizeY * 0.5f > door.Y + 0.05f)
                        Add(BuildingRule.DecorBlocksDoor, $"{dc.Name} at {door.Name}");
                }
            }
            int lampLimit = plan.Kind == BuildingKind.School ? 60 : MaxLamps;
            if (plan.Lamps.Count > lampLimit) Add(BuildingRule.LampBudget, $"{plan.Lamps.Count} lamps > {lampLimit}");
            if (plan.Kind != BuildingKind.ElevatorStation)
                foreach (var room in plan.Rooms)
                {
                    bool lit = false;
                    foreach (var lamp in plan.Lamps)
                        if (lamp.Room == room.Name && room.Contains(lamp.X, lamp.Z) && lamp.Y > room.FloorY + 2f && lamp.Y < room.CeilingY) lit = true;
                    if (!lit) Add(BuildingRule.LampMissing, room.Name);
                }

            // ---- doors ----
            int exterior = 0;
            var sides = new List<float>();
            foreach (var door in plan.Doors)
            {
                if (door.Exterior)
                {
                    exterior++;
                    if (!sides.Contains(door.FacingDeg)) sides.Add(door.FacingDeg);
                }
                if (door.Width < MinDoorWidth - Eps || door.Height < MinDoorHeight - Eps)
                    Add(BuildingRule.DoorSize, $"{door.Name} is {door.Width:F2} x {door.Height:F2} m");
                string plug = PieceInOpening(plan, door);
                if (plug != null) { Add(BuildingRule.DoorBlocked, $"{plug} stands in {door.Name}"); continue; }
                // Both sides of the doorway must be standing places the flood fill got to.
                float reach = door.Depth * 0.5f + 0.6f;
                bool inside = grid.ReachedNear(door.X - door.ForwardX * reach, door.Z - door.ForwardZ * reach, Cell, door.Y - 0.35f, door.Y + 0.35f);
                bool outside = grid.ReachedNear(door.X + door.ForwardX * reach, door.Z + door.ForwardZ * reach, Cell, door.Y - 0.35f, door.Y + 0.35f);
                if (!inside || !outside) Add(BuildingRule.DoorBlocked, $"{door.Name} cannot be walked through");
            }
            int minDoors = MinExteriorDoors(plan.Kind);
            if (exterior < minDoors) Add(BuildingRule.ExteriorDoorCount, $"{exterior} exterior doors, expected {minDoors}+");
            if (minDoors >= 2 && sides.Count < 2) Add(BuildingRule.ExteriorDoorSides, "all exterior doors are on one side");

            // ---- rooms ----
            var family = BuildingKinds.Family(plan.Kind);
            bool needsWindows = family == BuildingKind.House || family == BuildingKind.Shed;
            foreach (var room in plan.Rooms)
            {
                if (!grid.ReachedInRect(room.MinX, room.MaxX, room.MinZ, room.MaxZ, room.FloorY))
                    Add(BuildingRule.RoomUnreachable, $"{room.Name} cannot be reached from outside");
                if (needsWindows)
                {
                    bool window = false;
                    foreach (var w in plan.Windows)
                        if (w.Room == room.Name) window = true;
                    if (!window) Add(BuildingRule.RoomWithoutWindow, room.Name);
                }
                string low = CeilingProblem(plan, room);
                if (low != null) Add(BuildingRule.CeilingClearance, $"{room.Name}: {low}");
            }
            foreach (var w in plan.Windows)
            {
                bool known = false;
                foreach (var room in plan.Rooms)
                {
                    if (room.Name != w.Room) continue;
                    known = true;
                    float sill = w.Y - room.FloorY;
                    if (sill < MinSill - Eps || sill > MaxSill + Eps) Add(BuildingRule.WindowSill, $"{w.Name} sill at {sill:F2} m");
                }
                if (!known) Add(BuildingRule.WindowSill, $"{w.Name} belongs to no room");
            }

            // ---- stairs ----
            bool raised = plan.Storeys > 1;
            foreach (var room in plan.Rooms)
                if (room.FloorY > plan.FloorY + 1f) raised = true;
            if (raised && plan.Stairs.Count == 0) Add(BuildingRule.StairMissing, "an upper floor without stairs");
            foreach (var stair in plan.Stairs) CheckStair(plan, stair, Add);
            // A flight that climbs above the top room leads to a roof deck: its middle must be a standing place that can be walked to.
            float topRoom = plan.FloorY;
            foreach (var room in plan.Rooms) topRoom = Math.Max(topRoom, room.FloorY);
            foreach (var stair in plan.Stairs)
                if (stair.TopY > topRoom + 0.5f && !grid.ReachedInRect(-0.6f, 0.6f, -0.6f, 0.6f, stair.TopY))
                    Add(BuildingRule.RoofUnreachable, $"{stair.Name} leads to a roof that cannot be walked on");

            // ---- loot ----
            foreach (var loot in plan.LootPoints)
            {
                if (Math.Abs(loot.X) > hw || Math.Abs(loot.Z) > hd) { Add(BuildingRule.LootOutside, loot.Name); continue; }
                bool carried = false;
                string inside = null;
                foreach (var p in plan.Pieces)
                {
                    if (!p.FootprintContains(loot.X, loot.Z)) continue;
                    if (Math.Abs(p.MaxY - loot.Y) <= 0.02f) carried = true;
                    if (p.MinY < loot.Y + 0.4f && p.MaxY > loot.Y + 0.02f) inside = p.Name;
                }
                if (!carried) Add(BuildingRule.LootUnsupported, $"{loot.Name} lies on nothing");
                if (inside != null) Add(BuildingRule.LootInsidePiece, $"{loot.Name} is inside {inside}");
                // Someone standing within arm's reach: on the same floor, or beside the furniture it lies on.
                // It does not count through a wall: the place must be in the loot point's own room.
                float x0 = -hw, x1 = hw, z0 = -hd, z1 = hd;
                foreach (var room in plan.Rooms)
                    if (room.Name == loot.Room) { x0 = room.MinX; x1 = room.MaxX; z0 = room.MinZ; z1 = room.MaxZ; }
                if (!grid.ReachedNear(loot.X, loot.Z, 1.5f, loot.Y - 1.5f, loot.Y + 0.3f, x0, x1, z0, z1))
                    Add(BuildingRule.LootUnreachable, loot.Name);
            }

            // ---- spawn ----
            if (plan.Kind == BuildingKind.ElevatorStation)
            {
                if (!plan.HasSpawn) Add(BuildingRule.SpawnPoint, "no spawn point");
                else
                {
                    // Walk straight ahead from the car to a metre past the footprint and meet nothing.
                    float fx = (float)Math.Sin(plan.SpawnFacingDeg * Math.PI / 180.0), fz = (float)Math.Cos(plan.SpawnFacingDeg * Math.PI / 180.0);
                    float length = Math.Abs(fx) > 0.5f ? hw - plan.SpawnX * fx + 1f : hd - plan.SpawnZ * fz + 1f;
                    for (float step = 0f; step <= length; step += Cell)
                    {
                        if (grid.ReachedNear(plan.SpawnX + fx * step, plan.SpawnZ + fz * step, 0f, plan.SpawnY - 0.35f, plan.SpawnY + 0.35f)) continue;
                        Add(BuildingRule.SpawnPoint, $"the way out is blocked {step:F1} m from the spawn point");
                        break;
                    }
                }
            }
            return found;
        }

        // Furniture can leave a pocket between a prop and a wall that nobody fits into: loot on a prop in such a pocket cannot be picked up.
        // The generators of the tall kinds drop those points instead of leaving them for the validator. Returns how many were dropped.
        public static int DropUnreachableLoot(BuildingPlan plan)
        {
            var grid = new WalkGrid(plan);
            float hw = plan.Width * 0.5f, hd = plan.Depth * 0.5f;
            int dropped = plan.LootPoints.RemoveAll(loot =>
            {
                float x0 = -hw, x1 = hw, z0 = -hd, z1 = hd;
                foreach (var room in plan.Rooms)
                    if (room.Name == loot.Room) { x0 = room.MinX; x1 = room.MaxX; z0 = room.MinZ; z1 = room.MaxZ; }
                return !grid.ReachedNear(loot.X, loot.Z, 1.5f, loot.Y - 1.5f, loot.Y + 0.3f, x0, x1, z0, z1);
            });
            return dropped;
        }

        // Name of a piece that reaches into a door's opening, or null.
        static string PieceInOpening(BuildingPlan plan, BuildingOpening door)
        {
            bool wallAlongX = Math.Abs(door.ForwardZ) > 0.5f;
            float halfAlong = door.Width * 0.5f - 0.03f, halfAcross = Math.Max(door.Depth * 0.5f - 0.03f, 0.05f);
            float halfX = wallAlongX ? halfAlong : halfAcross, halfZ = wallAlongX ? halfAcross : halfAlong;
            foreach (var p in plan.Pieces)
                if (p.MinX < door.X + halfX && p.MaxX > door.X - halfX && p.MinZ < door.Z + halfZ && p.MaxZ > door.Z - halfZ
                    && p.MinY < door.Y + door.Height - 0.03f && p.MaxY > door.Y + 0.03f)
                    return p.Name;
            return null;
        }

        // A room needs a slab or roof over all of it, nowhere lower than the minimum ceiling.
        static string CeilingProblem(BuildingPlan plan, BuildingRoom room)
        {
            if (room.CeilingY - room.FloorY < MinCeiling - Eps) return $"ceiling at {room.CeilingY - room.FloorY:F2} m";
            for (float x = room.MinX + 0.25f; x < room.MaxX; x += 0.5f)
                for (float z = room.MinZ + 0.25f; z < room.MaxZ; z += 0.5f)
                {
                    float lowest = float.MaxValue;
                    foreach (var p in plan.Pieces)
                        if ((p.Kind == SurfaceKind.Floor || p.Kind == SurfaceKind.Roof) && p.MinY > room.FloorY + 0.5f && p.FootprintContains(x, z))
                            lowest = Math.Min(lowest, p.MinY);
                    if (lowest == float.MaxValue) return $"open to the sky at ({x:F1}, {z:F1})";
                    if (lowest - room.FloorY < MinCeiling - Eps) return $"{lowest - room.FloorY:F2} m of head room at ({x:F1}, {z:F1})";
                }
            return null;
        }

        static void CheckStair(BuildingPlan plan, BuildingStair stair, Action<BuildingRule, string> add)
        {
            var steps = new List<BuildingPiece>();
            foreach (var p in plan.Pieces)
                if (p.Kind == SurfaceKind.Stairs && p.Name.StartsWith(stair.Name + "Step", StringComparison.Ordinal)) steps.Add(p);
            if (steps.Count == 0) { add(BuildingRule.StairStep, $"{stair.Name} has no steps"); return; }
            steps.Sort((a, b) => a.MaxY.CompareTo(b.MaxY));
            bool climbsAlongZ = Math.Abs(Math.Cos(stair.FacingDeg * Math.PI / 180.0)) > 0.5;
            float below = stair.BaseY;
            foreach (var step in steps)
            {
                float rise = step.MaxY - below, run = climbsAlongZ ? step.SizeZ : step.SizeX, width = climbsAlongZ ? step.SizeX : step.SizeZ;
                if (rise > MaxRise + Eps) add(BuildingRule.StairStep, $"{step.Name} rises {rise:F2} m");
                if (run < MinRun - Eps) add(BuildingRule.StairStep, $"{step.Name} is {run:F2} m deep");
                if (width < MinStairWidth - Eps) add(BuildingRule.StairStep, $"{step.Name} is {width:F2} m wide");
                below = step.MaxY;
            }
            if (stair.TopY - below > MaxRise + Eps || stair.TopY < below - Eps)
                add(BuildingRule.StairTop, $"{stair.Name} ends {stair.TopY - below:F2} m below its upper floor");
            bool lands = false;
            foreach (var p in plan.Pieces)
                if ((p.Kind == SurfaceKind.Floor || p.Kind == SurfaceKind.Roof) && Math.Abs(p.MaxY - stair.TopY) <= 0.01f) lands = true;
            if (!lands) add(BuildingRule.StairTop, $"{stair.Name} leads to no floor");
        }

        // Standing places on a grid: per cell, the heights at which a capsule fits, and which of them can be
        // walked to from the open ground around the building without a step higher than the controller takes.
        sealed class WalkGrid
        {
            static readonly float[] None = new float[0];
            readonly float _x0, _z0;
            readonly int _nx, _nz;
            readonly float[][] _levels;
            readonly bool[][] _reached;

            public WalkGrid(BuildingPlan plan)
            {
                float halfX = plan.Width * 0.5f + GridMargin, halfZ = plan.Depth * 0.5f + GridMargin;
                _x0 = -halfX;
                _z0 = -halfZ;
                _nx = Math.Max(1, (int)Math.Ceiling(2f * halfX / Cell));
                _nz = Math.Max(1, (int)Math.Ceiling(2f * halfZ / Cell));
                _levels = new float[_nx * _nz][];
                _reached = new bool[_nx * _nz][];
                var pieces = plan.Pieces;
                var near = new List<int>();
                var candidates = new List<float>();
                var standable = new List<float>();
                for (int i = 0; i < _nx; i++)
                    for (int j = 0; j < _nz; j++)
                    {
                        float x = _x0 + (i + 0.5f) * Cell, z = _z0 + (j + 0.5f) * Cell;
                        near.Clear();
                        candidates.Clear();
                        standable.Clear();
                        candidates.Add(0f);   // the ground
                        for (int k = 0; k < pieces.Count; k++)
                        {
                            var p = pieces[k];
                            float distance = p.FootprintDistance(x, z);
                            if (distance >= CapsuleRadius) continue;
                            near.Add(k);
                            if (distance <= 0f && (p.Kind == SurfaceKind.Floor || p.Kind == SurfaceKind.Stairs || p.Kind == SurfaceKind.Roof)) candidates.Add(p.MaxY);
                        }
                        foreach (float h in candidates)
                        {
                            bool seen = false;
                            foreach (float other in standable)
                                if (Math.Abs(other - h) < 0.01f) seen = true;
                            if (seen) continue;
                            bool free = true;
                            foreach (int k in near)
                            {
                                var p = pieces[k];
                                // Under the feet nothing may stick up; beside them only what the capsule rides over.
                                float lip = p.FootprintContains(x, z) ? 0.01f : Lip;
                                if (p.MinY < h + CapsuleHeight - 0.01f && p.MaxY > h + lip) { free = false; break; }
                            }
                            if (free) standable.Add(h);
                        }
                        int index = i * _nz + j;
                        _levels[index] = standable.Count == 0 ? None : standable.ToArray();
                        _reached[index] = new bool[standable.Count];
                    }
                Flood();
            }

            void Flood()
            {
                var open = new Stack<long>();
                void Visit(int index, int level)
                {
                    if (_reached[index][level]) return;
                    _reached[index][level] = true;
                    open.Push((long)index * 64 + level);
                }
                for (int i = 0; i < _nx; i++)
                    for (int j = 0; j < _nz; j++)
                    {
                        if (i != 0 && j != 0 && i != _nx - 1 && j != _nz - 1) continue;
                        int index = i * _nz + j;
                        for (int level = 0; level < _levels[index].Length; level++)
                            if (Math.Abs(_levels[index][level]) < 0.01f) Visit(index, level);
                    }
                while (open.Count > 0)
                {
                    long node = open.Pop();
                    int index = (int)(node / 64), level = (int)(node % 64);
                    int i = index / _nz, j = index % _nz;
                    float h = _levels[index][level];
                    for (int side = 0; side < 4; side++)
                    {
                        int ni = i + (side == 0 ? 1 : side == 1 ? -1 : 0), nj = j + (side == 2 ? 1 : side == 3 ? -1 : 0);
                        if (ni < 0 || nj < 0 || ni >= _nx || nj >= _nz) continue;
                        int next = ni * _nz + nj;
                        for (int k = 0; k < _levels[next].Length; k++)
                            if (Math.Abs(_levels[next][k] - h) <= StepHeight + 0.001f) Visit(next, k);
                    }
                }
            }

            int Clamp(int v, int max) => v < 0 ? 0 : v >= max ? max - 1 : v;

            // True when a reached standing place between yLo and yHi lies within radius of the point (in the plane).
            public bool ReachedNear(float x, float z, float radius, float yLo, float yHi) =>
                ReachedNear(x, z, radius, yLo, yHi, float.MinValue, float.MaxValue, float.MinValue, float.MaxValue);

            // The same, counting only standing places inside the rectangle x0..x1, z0..z1.
            public bool ReachedNear(float x, float z, float radius, float yLo, float yHi, float x0, float x1, float z0, float z1)
            {
                int i0 = Clamp((int)Math.Floor((x - radius - _x0) / Cell), _nx), i1 = Clamp((int)Math.Floor((x + radius - _x0) / Cell), _nx);
                int j0 = Clamp((int)Math.Floor((z - radius - _z0) / Cell), _nz), j1 = Clamp((int)Math.Floor((z + radius - _z0) / Cell), _nz);
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                    {
                        float cx = _x0 + (i + 0.5f) * Cell, cz = _z0 + (j + 0.5f) * Cell;
                        if (cx < x0 || cx > x1 || cz < z0 || cz > z1) continue;
                        int index = i * _nz + j;
                        for (int k = 0; k < _levels[index].Length; k++)
                            if (_reached[index][k] && _levels[index][k] >= yLo && _levels[index][k] <= yHi) return true;
                    }
                return false;
            }

            // True when any reached standing place at floor height lies inside the rectangle.
            public bool ReachedInRect(float x0, float x1, float z0, float z1, float y)
            {
                for (int i = 0; i < _nx; i++)
                {
                    float x = _x0 + (i + 0.5f) * Cell;
                    if (x < x0 || x > x1) continue;
                    for (int j = 0; j < _nz; j++)
                    {
                        float z = _z0 + (j + 0.5f) * Cell;
                        if (z < z0 || z > z1) continue;
                        int index = i * _nz + j;
                        for (int k = 0; k < _levels[index].Length; k++)
                            if (_reached[index][k] && Math.Abs(_levels[index][k] - y) <= 0.05f) return true;
                    }
                }
                return false;
            }
        }
    }
}
