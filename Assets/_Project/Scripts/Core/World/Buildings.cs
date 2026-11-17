using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Enterable buildings assembled from boxes (ADR-052 point 7). Pure and deterministic: the same kind, size and
    // seed always give the same plan. Walls are built around their door and window holes, so nothing has to be
    // cut out of a mesh afterwards. The plan is in the building's local frame; BuildingRules.Validate checks it.
    public static partial class Buildings
    {
        public const float FloorTop = 0.15f;                 // the step up from the ground into every building
        public const float StoreyClear = 3.0f;               // third-person camera head room
        public const float SlabThickness = 0.2f;
        public const float StoreyHeight = StoreyClear + SlabThickness;
        public const float OuterWall = 0.25f, HallWall = 0.3f, InnerWall = 0.2f;
        public const float DoorWidth = 1.3f, DoorHeight = 2.3f;
        public const float RollDoorWidth = 3.6f, RollDoorHeight = 3.6f;
        public const float WindowSill = 1.0f, WindowWidth = 1.4f, WindowHeight = 1.2f;
        public const float StairWidth = 1.3f, StairRun = 0.28f, StairRiseTarget = 0.18f;
        public const float RoofOverhang = 0.3f;
        public const float TwoStoreyMinDepth = 7.8f;         // room for a straight flight plus both landings
        public const float ElevatorBay = 3f;
        const float PropSpacing = 1.2f;                      // free gap kept around stairs and offices: a capsule always fits
        // Between two props the gap is either closed (a run of furniture, up to 10 cm) or wide enough to walk (80 cm):
        // never a slot a capsule cannot enter yet looks like a way through, and never a 1.2 m margin that left rooms empty.
        const float PropWalkGap = 0.8f, PropClosedGap = 0.1f;
        const float LootTopMax = 1.3f;                       // furniture higher than this holds no loot

        public static void FootprintLimits(BuildingKind kind, out float minWidth, out float minDepth, out float maxWidth, out float maxDepth)
        {
            switch (kind)
            {
                case BuildingKind.Shop: minWidth = 10f; minDepth = 9f; maxWidth = 16f; maxDepth = 13f; return;
                case BuildingKind.Villa: minWidth = 11f; minDepth = 10f; maxWidth = 16f; maxDepth = 14f; return;
                case BuildingKind.Apartment: minWidth = 14f; minDepth = 11f; maxWidth = 22f; maxDepth = 15f; return;
                case BuildingKind.School: minWidth = 24f; minDepth = 11f; maxWidth = 34f; maxDepth = 13f; return;
            }
            kind = BuildingKinds.Family(kind);
            switch (kind)
            {
                case BuildingKind.House: minWidth = 8f; minDepth = 7f; maxWidth = 18f; maxDepth = 14f; break;
                case BuildingKind.Warehouse: minWidth = 14f; minDepth = 10f; maxWidth = 40f; maxDepth = 28f; break;
                case BuildingKind.FactoryHall: minWidth = 20f; minDepth = 14f; maxWidth = 48f; maxDepth = 32f; break;
                case BuildingKind.Shed: minWidth = 3.5f; minDepth = 3.5f; maxWidth = 7f; maxDepth = 7f; break;
                default: minWidth = 3.6f; minDepth = 5.5f; maxWidth = 6f; maxDepth = 9f; break;
            }
        }

        // The requested footprint is clamped to what the kind can be built in; plan.Width/Depth hold the result.
        public static BuildingPlan Generate(BuildingKind kind, float width, float depth, int seed)
        {
            var d = new Draft(kind, width, depth, seed);
            if (kind == BuildingKind.School) { School(d); d.Finish(); BuildingRules.DropUnreachableLoot(d.Plan); return d.Plan; }
            if (kind == BuildingKind.Shop || kind == BuildingKind.Villa || kind == BuildingKind.Apartment) { Block(d, kind); d.Finish(); BuildingRules.DropUnreachableLoot(d.Plan); return d.Plan; }
            switch (BuildingKinds.Family(kind))
            {
                case BuildingKind.House: House(d); break;
                case BuildingKind.Warehouse: Warehouse(d); break;
                case BuildingKind.FactoryHall: FactoryHall(d); break;
                case BuildingKind.Shed: Shed(d); break;
                default: ElevatorStation(d); break;
            }
            d.Finish();
            BuildingRules.DropUnreachableLoot(d.Plan); // door keep-out moved the random picks: drop any that now sit out of reach
            return d.Plan;
        }

        static int Risers(float height) => (int)Math.Ceiling(height / StairRiseTarget - 1e-3f);
        static float StairLength(float height) => (Risers(height) - 1) * StairRun;
        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        // ---- kinds ----

        // One or two storeys. A wall across the depth splits every floor in two; either half may be split again.
        // In a two-storey house the left half is the stair hall and stays whole.
        static void House(Draft d)
        {
            const float t = OuterWall, ti = InnerWall;
            var rng = d.Rng;
            var plan = d.Plan;
            float hw = d.Hw, hd = d.Hd, floor = FloorTop;
            float xl = -hw + t, xr = hw - t, zb = -hd + t, zf = hd - t;
            int storeys = plan.Depth >= TwoStoreyMinDepth && rng.Chance(0.5f) ? 2 : 1;
            bool stairs = storeys == 2;
            plan.Storeys = storeys;
            d.Box("Slab", SurfaceKind.Floor, -hw, hw, 0f, floor, -hd, hd);

            float xs = rng.Pick(xl + 3.2f, xr - 3.2f);
            float stairX1 = xl + StairWidth;
            float zTop = zb + 1.3f;                              // upper landing between the back wall and the last step
            float zFoot = zTop + StairLength(StoreyHeight);
            if (stairs)
            {
                d.Stairs("Stair", xl, zFoot, floor, floor + StoreyHeight);
                var keep = new Rect(xl, stairX1 + PropSpacing, zb, zf);
                d.Clear[0].Add(keep);
                d.Clear[1].Add(keep);
            }

            var all = new List<BuildingRoom>();
            var levels = new List<int>();
            for (int s = 0; s < storeys; s++)
            {
                float y0 = floor + s * StoreyHeight, yCeil = y0 + StoreyClear;
                bool splitRight = rng.Chance(0.65f), splitLeft = !stairs && rng.Chance(0.5f);
                float zsR = rng.Pick(zb + 3.1f, zf - 3.1f), zsL = rng.Pick(zb + 3.1f, zf - 3.1f);
                string tag = "S" + s;

                var rooms = new List<BuildingRoom>();
                if (splitLeft)
                {
                    rooms.Add(d.Room(tag + "LeftBack", s, xl, xs - ti / 2, zb, zsL - ti / 2, y0, yCeil));
                    rooms.Add(d.Room(tag + "LeftFront", s, xl, xs - ti / 2, zsL + ti / 2, zf, y0, yCeil));
                }
                else rooms.Add(d.Room(tag + "Left", s, xl, xs - ti / 2, zb, zf, y0, yCeil));
                if (splitRight)
                {
                    rooms.Add(d.Room(tag + "RightBack", s, xs + ti / 2, xr, zb, zsR - ti / 2, y0, yCeil));
                    rooms.Add(d.Room(tag + "RightFront", s, xs + ti / 2, xr, zsR + ti / 2, zf, y0, yCeil));
                }
                else rooms.Add(d.Room(tag + "Right", s, xs + ti / 2, xr, zb, zf, y0, yCeil));

                // The door in the main wall keeps clear of the places where the split walls meet it.
                float keepOff = DoorWidth / 2 + 0.3f + ti / 2;
                var cuts = new List<float>();
                if (splitLeft) cuts.Add(zsL);
                if (splitRight) cuts.Add(zsR);
                cuts.Sort();
                var spans = new List<float>();
                float start = zb + 0.3f + DoorWidth / 2, end = zf - 0.3f - DoorWidth / 2;
                foreach (float cut in cuts)
                {
                    if (cut - keepOff >= start) { spans.Add(start); spans.Add(cut - keepOff); }
                    start = Math.Max(start, cut + keepOff);
                }
                if (end >= start) { spans.Add(start); spans.Add(end); }
                int span = rng.Int(spans.Count / 2);
                float doorZ = rng.Pick(spans[span * 2], spans[span * 2 + 1]);
                var mainGaps = new List<Gap>();
                d.InnerDoor(mainGaps, false, xs, doorZ, y0, RoomAt(rooms, xs - 1f, doorZ), RoomAt(rooms, xs + 1f, doorZ), s, tag + "DoorMain");
                d.Wall(tag + "Main", SurfaceKind.WallInner, false, xs, zb, zf, y0, yCeil, ti, mainGaps);
                if (splitLeft)
                {
                    var gaps = new List<Gap>();
                    d.InnerDoor(gaps, true, zsL, rng.Pick(xl + 0.3f + DoorWidth / 2, xs - ti / 2 - 0.3f - DoorWidth / 2), y0, tag + "LeftBack", tag + "LeftFront", s, tag + "DoorLeft");
                    d.Wall(tag + "SplitLeft", SurfaceKind.WallInner, true, zsL, xl, xs - ti / 2, y0, yCeil, ti, gaps);
                }
                if (splitRight)
                {
                    var gaps = new List<Gap>();
                    d.InnerDoor(gaps, true, zsR, rng.Pick(xs + ti / 2 + 0.3f + DoorWidth / 2, xr - 0.3f - DoorWidth / 2), y0, tag + "RightBack", tag + "RightFront", s, tag + "DoorRight");
                    d.Wall(tag + "SplitRight", SurfaceKind.WallInner, true, zsR, xs + ti / 2, xr, y0, yCeil, ti, gaps);
                }

                // Two exterior doors on the ground floor: one in front, one on another side. The second goes to
                // another room, or to a room that still has a third outside wall left for its window.
                int frontRoom = -1, otherRoom = -1;
                Side otherSide = Side.Back;
                if (s == 0)
                {
                    var front = new List<int>();
                    for (int r = 0; r < rooms.Count; r++)
                        if (d.Touches(rooms[r], Side.Front, t)) front.Add(r);
                    frontRoom = front[rng.Int(front.Count)];
                    var options = new List<int>();
                    for (int r = 0; r < rooms.Count; r++)
                        for (int sd = 1; sd < 4; sd++)
                        {
                            var side = (Side)sd;
                            if (!d.Touches(rooms[r], side, t)) continue;
                            bool stairHall = stairs && rooms[r].MinX <= xl + 0.01f;
                            if (stairHall && side != Side.Right) continue;
                            if (stairs && side == Side.Left) continue;
                            if (r == frontRoom && d.TouchCount(rooms[r], t) < 3) continue;
                            options.Add(r * 4 + sd);
                        }
                    int option = options[rng.Int(options.Count)];
                    otherRoom = option / 4;
                    otherSide = (Side)(option % 4);
                }

                var outer = NewGaps();
                for (int r = 0; r < rooms.Count; r++)
                    for (int sd = 0; sd < 4; sd++)
                    {
                        var side = (Side)sd;
                        if (!d.Touches(rooms[r], side, t)) continue;
                        if (stairs && side == Side.Left) continue;   // the flight runs along this wall
                        bool door = s == 0 && ((side == Side.Front && r == frontRoom) || (side == otherSide && r == otherRoom));
                        bool alongX = side == Side.Front || side == Side.Back;
                        float a0 = alongX ? rooms[r].MinX : rooms[r].MinZ, a1 = alongX ? rooms[r].MaxX : rooms[r].MaxZ;
                        d.FillSegment(outer, side, a0, a1, door, rooms[r].Name, s, y0, t);
                    }
                bool top = s == storeys - 1;
                d.Shell(tag, outer, y0, top ? yCeil : yCeil + SlabThickness, t);

                if (s > 0)
                {
                    // Upper floor around the stairwell, with a rail on its two open edges.
                    d.Box(tag + "Floor", SurfaceKind.Floor, stairX1, xr, y0 - SlabThickness, y0, zb, zf);
                    d.Box(tag + "Landing", SurfaceKind.Floor, xl, stairX1, y0 - SlabThickness, y0, zb, zTop);
                    d.Box(tag + "FloorFront", SurfaceKind.Floor, xl, stairX1, y0 - SlabThickness, y0, zFoot, zf);
                    d.Box(tag + "RailSide", SurfaceKind.Trim, stairX1, stairX1 + 0.1f, y0, y0 + 1f, zTop, zFoot + 0.1f);
                    d.Box(tag + "RailEnd", SurfaceKind.Trim, xl, stairX1, y0, y0 + 1f, zFoot, zFoot + 0.1f);
                }
                foreach (var room in rooms) { all.Add(room); levels.Add(s); }
            }

            float roofY = floor + storeys * StoreyHeight - SlabThickness;
            d.FlatRoof(roofY, SlabThickness, RoofOverhang, true);

            // Roles: downstairs the biggest room is the living room, then kitchen, the smallest a bathroom (three
            // rooms or more); upstairs bedrooms around one bathroom. A lone room is a studio: living plus kitchen.
            var roles = new RoomRole[all.Count];
            for (int s = 0; s < storeys; s++)
            {
                var ids = new List<int>();
                for (int i = 0; i < all.Count; i++) if (levels[i] == s) ids.Add(i);
                ids.Sort((p, q) => all[q].Area.CompareTo(all[p].Area));
                for (int k = 0; k < ids.Count; k++)
                {
                    RoomRole role = RoomRole.Bedroom;
                    if (s == 0) role = k == 0 ? RoomRole.Living : k == 1 ? RoomRole.Kitchen : k == ids.Count - 1 && ids.Count >= 3 ? RoomRole.Bath : RoomRole.Bedroom;
                    else if (k == ids.Count - 1 && ids.Count >= 2) role = RoomRole.Bath;
                    roles[ids[k]] = role;
                }
            }
            for (int i = 0; i < all.Count; i++)
            {
                var room = all[i];
                d.SetRole(room.Name, roles[i]);
                bool studio = roles[i] == RoomRole.Living && levels[i] == 0 && all.FindAll(r => r.Storey == 0).Count == 1;
                int n = room.Area >= 30f ? 6 : room.Area >= 18f ? 5 : room.Area >= 10f ? 4 : 3;
                switch (roles[i])
                {
                    case RoomRole.Living:
                        d.Furnish(room, levels[i], studio ? n + 1 : n, studio ? StudioSet : LivingSet, 2);
                        if (room.Area >= 12f) d.Rug(room, DecorMat.Fabric2);
                        d.Centre(room, levels[i], new Spec(PropKind.Table, 1.4f, 1.4f, 0.8f));
                        break;
                    case RoomRole.Kitchen:
                        d.Furnish(room, levels[i], n, KitchenSet, 1);
                        d.Centre(room, levels[i], new Spec(PropKind.Table, 1.4f, 1.4f, 0.8f));
                        break;
                    case RoomRole.Bath: d.Furnish(room, levels[i], 2, BathSet, 2); break;
                    default:
                        d.Furnish(room, levels[i], n, BedroomSet, 2);
                        if (room.Area >= 12f) d.Rug(room, DecorMat.Fabric);
                        break;
                }
                d.RoomLamps(room);
            }
            foreach (var room in all)
                d.Loot(room.Name, new Rect(room.MinX, room.MaxX, room.MinZ, room.MaxZ), room.FloorY, Clamp((int)(room.Area / 5f), 2, 8), null);
            d.Curtains();
            for (int i = 0; i < all.Count; i++)
                if (roles[i] != RoomRole.Bath) d.Clutter(all[i], Math.Min(levels[i], 1), Clamp((int)(all[i].Area / 7f), 2, 6));
        }

        static string RoomAt(List<BuildingRoom> rooms, float x, float z)
        {
            foreach (var room in rooms)
                if (room.Contains(x, z)) return room.Name;
            // On a split line: take the nearest room on that side of the main wall.
            string best = rooms[0].Name;
            float bestDistance = float.MaxValue;
            foreach (var room in rooms)
            {
                if (x < room.MinX || x > room.MaxX) continue;
                float distance = Math.Min(Math.Abs(z - room.MinZ), Math.Abs(z - room.MaxZ));
                if (distance < bestDistance) { bestDistance = distance; best = room.Name; }
            }
            return best;
        }

        // One open hall 6 m high with a roll door in front and at the back, a side door, shelf rows and crates,
        // and a small office in the back-left corner.
        static void Warehouse(Draft d)
        {
            const float t = HallWall, ti = InnerWall, clearHeight = 6f;
            var rng = d.Rng;
            var plan = d.Plan;
            float hw = d.Hw, hd = d.Hd, floor = FloorTop;
            float xl = -hw + t, xr = hw - t, zb = -hd + t, zf = hd - t;
            plan.Storeys = 1;
            d.Box("Slab", SurfaceKind.Floor, -hw, hw, 0f, floor, -hd, hd);
            var hall = d.Room("Hall", 0, xl, xr, zb, zf, floor, floor + clearHeight);

            float ox1 = xl + (plan.Width >= 20f ? 5f : 4f), oz1 = zb + 3.5f;
            var office = d.Room("Office", 0, xl, ox1, zb, oz1, floor, floor + StoreyClear);
            var east = new List<Gap>();
            var north = new List<Gap>();
            if (rng.Chance(0.5f))
                d.InnerDoor(east, false, ox1 + ti / 2, rng.Pick(zb + 0.3f + DoorWidth / 2, oz1 - 0.3f - DoorWidth / 2), floor, "Office", "Hall", 0, "DoorOffice");
            else
                d.InnerDoor(north, true, oz1 + ti / 2, rng.Pick(xl + 0.3f + DoorWidth / 2, ox1 - 0.3f - DoorWidth / 2), floor, "Office", "Hall", 0, "DoorOffice");
            d.Wall("OfficeEast", SurfaceKind.WallInner, false, ox1 + ti / 2, zb, oz1 + ti, floor, floor + StoreyClear, ti, east);
            d.Wall("OfficeNorth", SurfaceKind.WallInner, true, oz1 + ti / 2, xl, ox1, floor, floor + StoreyClear, ti, north);
            d.Box("OfficeCeiling", SurfaceKind.Floor, xl, ox1 + ti, floor + StoreyClear, floor + StoreyClear + 0.15f, zb, oz1 + ti);

            var outer = NewGaps();
            d.Door(outer, Side.Front, rng.Pick(xl + 0.5f + RollDoorWidth / 2, xr - 0.5f - RollDoorWidth / 2), RollDoorWidth, RollDoorHeight, floor, "Hall", 0, t, "RollFront");
            d.Door(outer, Side.Back, rng.Pick(ox1 + ti + 0.5f + RollDoorWidth / 2, xr - 0.5f - RollDoorWidth / 2), RollDoorWidth, RollDoorHeight, floor, "Hall", 0, t, "RollBack");
            if (rng.Chance(0.5f))
                d.Door(outer, Side.Right, rng.Pick(zb + 0.5f + DoorWidth / 2, zf - 0.5f - DoorWidth / 2), DoorWidth, DoorHeight, floor, "Hall", 0, t, "DoorSide");
            else
                d.Door(outer, Side.Left, rng.Pick(oz1 + ti + 0.5f + DoorWidth / 2, zf - 0.5f - DoorWidth / 2), DoorWidth, DoorHeight, floor, "Hall", 0, t, "DoorSide");
            if (rng.Chance(0.5f)) d.Window(outer, Side.Back, (xl + ox1) / 2, WindowWidth, floor + WindowSill, WindowHeight, "Office", 0, t);
            else d.Window(outer, Side.Left, (zb + oz1) / 2, WindowWidth, floor + WindowSill, WindowHeight, "Office", 0, t);
            d.WindowRow(outer, Side.Right, zb + 0.5f, zf - 0.5f, 6f, 2f, floor + WindowSill, 1.4f, "Hall", 0, t);
            d.WindowRow(outer, Side.Left, oz1 + ti + 0.5f, zf - 0.5f, 6f, 2f, floor + WindowSill, 1.4f, "Hall", 0, t);
            d.Shell("", outer, floor, floor + clearHeight, t);
            d.FlatRoof(floor + clearHeight, 0.25f, RoofOverhang, false);

            d.SetRole("Office", RoomRole.Office);
            d.SetRole("Hall", RoomRole.Storage);
            d.Furnish(office, 0, 3, OfficeSet, 2);
            d.RoomLamps(office);
            var officeZone = new Rect(xl, ox1 + ti + PropSpacing, zb, oz1 + ti + PropSpacing);
            d.Clear[0].Add(officeZone);

            // Shelf rows run front to back, in line with the roll doors; a missing row leaves a bay for crates.
            for (float x = xl + 1.6f + rng.Range(0f, 1f); x + 0.8f <= xr - 1.6f; x += 3.4f)
            {
                if (rng.Chance(0.25f)) continue;
                for (float z = zb + 1.6f; z + 3f <= zf - 1.6f;)
                {
                    float length = Math.Min(rng.Range(3f, 4.5f), zf - 1.6f - z);
                    d.TryProp("Shelf", PropKind.Shelf, new Rect(x, x + 0.8f, z, z + length), floor, 2.4f, 0, "Hall", 1 + 2 * rng.Int(2));
                    z += length + 1.6f;
                }
            }
            d.Scatter("Crate", PropKind.Crate, Clamp((int)(hall.Area / 25f), 4, 12), new Rect(xl + 1.3f, xr - 1.3f, zb + 1.3f, zf - 1.3f), 1f, 1.4f, 1f, 1.4f, 1f, 1.3f, floor, 0, "Hall");

            d.Furnish(hall, 0, Clamp((int)(hall.Area / 40f), 3, 8), StorageSet, 0);
            d.HallLamps(hall, 12f, 6);

            d.Loot("Office", new Rect(office.MinX, office.MaxX, office.MinZ, office.MaxZ), floor, 3, null);
            d.Loot("Hall", new Rect(xl, xr, zb, zf), floor, Clamp((int)(hall.Area / 10f), 4, 24), new Rect(xl, ox1 + ti + 0.4f, zb, oz1 + ti + 0.4f));
        }

        // A hall 7 m high with a mezzanine along the back wall, reached by a straight flight. Doors on all four
        // sides, machine blocks on the floor, crates under and on the mezzanine.
        static void FactoryHall(Draft d)
        {
            const float t = HallWall, clearHeight = 7f, mezzDepth = 5f;
            var rng = d.Rng;
            var plan = d.Plan;
            float hw = d.Hw, hd = d.Hd, floor = FloorTop;
            float xl = -hw + t, xr = hw - t, zb = -hd + t, zf = hd - t;
            float zm = zb + mezzDepth, deck = floor + StoreyHeight;
            plan.Storeys = 1;
            d.Box("Slab", SurfaceKind.Floor, -hw, hw, 0f, floor, -hd, hd);
            var hall = d.Room("Hall", 0, xl, xr, zb, zf, floor, floor + clearHeight);
            var mezz = d.Room("Mezzanine", 1, xl, xr, zb, zm, deck, floor + clearHeight);
            d.Box("Mezzanine", SurfaceKind.Floor, xl, xr, deck - SlabThickness, deck, zb, zm);

            float sx = rng.Pick(xl + 2f, xr - 3.5f - StairWidth);
            float zFoot = zm + StairLength(StoreyHeight);
            d.Stairs("Stair", sx, zFoot, floor, deck);
            d.Box("RailLeft", SurfaceKind.Trim, xl, sx, deck, deck + 1f, zm - 0.1f, zm);
            d.Box("RailRight", SurfaceKind.Trim, sx + StairWidth, xr, deck, deck + 1f, zm - 0.1f, zm);
            int posts = 0;
            for (float x = xl + 2.5f; x + 0.3f <= xr - 2.5f; x += 5f)
            {
                if (x + 0.3f > sx - 0.5f && x < sx + StairWidth + 0.5f) continue;
                d.Box("Post" + posts++, SurfaceKind.Trim, x, x + 0.3f, floor, deck - SlabThickness, zm - 0.3f, zm);
                d.Blockers[0].Add(new Rect(x, x + 0.3f, zm - 0.3f, zm));
            }
            d.Clear[0].Add(new Rect(sx - 0.6f, sx + StairWidth + 0.6f, zm, zFoot + 1.5f));
            d.Clear[1].Add(new Rect(sx - 0.6f, sx + StairWidth + 0.6f, zm - 2f, zm));

            var outer = NewGaps();
            d.Door(outer, Side.Front, rng.Pick(xl + 0.5f + 2f, xr - 0.5f - 2f), 4f, 4f, floor, "Hall", 0, t, "RollFront");
            d.Door(outer, Side.Right, rng.Pick(zm + 0.5f + RollDoorWidth / 2, zf - 0.5f - RollDoorWidth / 2), RollDoorWidth, RollDoorHeight, floor, "Hall", 0, t, "RollSide");
            d.Door(outer, Side.Left, rng.Pick(zb + 0.5f + DoorWidth / 2, zf - 0.5f - DoorWidth / 2), DoorWidth, DoorHeight, floor, "Hall", 0, t, "DoorLeft");
            d.Door(outer, Side.Back, rng.Pick(xl + 0.5f + DoorWidth / 2, xr - 0.5f - DoorWidth / 2), DoorWidth, DoorHeight, floor, "Hall", 0, t, "DoorBack");
            d.WindowRow(outer, Side.Front, xl + 0.5f, xr - 0.5f, 6f, 2f, floor + WindowSill, 1.4f, "Hall", 0, t);
            d.WindowRow(outer, Side.Left, zb + 0.5f, zf - 0.5f, 6f, 2f, floor + WindowSill, 1.4f, "Hall", 0, t);
            d.WindowRow(outer, Side.Right, zb + 0.5f, zf - 0.5f, 6f, 2f, floor + WindowSill, 1.4f, "Hall", 0, t);
            d.WindowRow(outer, Side.Back, xl + 0.5f, xr - 0.5f, 6f, 2f, deck + WindowSill, 1.2f, "Mezzanine", 1, t);
            d.Shell("", outer, floor, floor + clearHeight, t);
            d.FlatRoof(floor + clearHeight, 0.25f, RoofOverhang, false);

            float openArea = (xr - xl) * (zf - zm);
            d.Scatter("Machine", PropKind.Machine, Clamp((int)(openArea / 30f), 3, 10), new Rect(xl + 1.3f, xr - 1.3f, zm + 1.3f, zf - 1.3f), 2f, 3.5f, 1.5f, 2.5f, 1.5f, 2.8f, floor, 0, "Hall");
            d.Scatter("Crate", PropKind.Crate, Clamp((int)(plan.Width / 5f), 2, 6), new Rect(xl + 1.3f, xr - 1.3f, zb + 1.3f, zm - 1.6f), 1f, 1.4f, 1f, 1.4f, 1f, 1.3f, floor, 0, "Hall");
            d.Scatter("DeckCrate", PropKind.Crate, Clamp((int)(plan.Width / 8f), 1, 4), new Rect(xl + 1.3f, xr - 1.3f, zb + 1.3f, zm - 1.5f), 1f, 1.4f, 1f, 1.4f, 1f, 1.3f, deck, 1, "Mezzanine");

            d.SetRole("Hall", RoomRole.Workshop);
            d.SetRole("Mezzanine", RoomRole.Storage);
            d.Furnish(hall, 0, Clamp((int)(hall.Area / 50f), 4, 9), WorkshopSet, 0);
            d.HallLamps(hall, 12f, 6);
            d.HallLamps(mezz, 12f, 3);

            d.Loot("Hall", new Rect(xl, xr, zb, zf), floor, Clamp((int)(hall.Area / 12f), 6, 24), null);
            d.Loot("Mezzanine", new Rect(xl, xr, zb, zm), deck, Clamp((int)(mezz.Area / 10f), 2, 8), null);
        }

        // One small room: a door in front, a window on another side.
        static void Shed(Draft d)
        {
            const float t = InnerWall;
            var rng = d.Rng;
            float hw = d.Hw, hd = d.Hd, floor = FloorTop;
            float xl = -hw + t, xr = hw - t, zb = -hd + t, zf = hd - t;
            d.Plan.Storeys = 1;
            d.Box("Slab", SurfaceKind.Floor, -hw, hw, 0f, floor, -hd, hd);
            var room = d.Room("Room", 0, xl, xr, zb, zf, floor, floor + StoreyClear);
            var outer = NewGaps();
            d.Door(outer, Side.Front, rng.Pick(xl + 0.4f + DoorWidth / 2, xr - 0.4f - DoorWidth / 2), DoorWidth, DoorHeight, floor, "Room", 0, t, "Door");
            var side = (Side)(1 + rng.Int(3));
            bool alongX = side == Side.Back;
            float a0 = alongX ? xl : zb, a1 = alongX ? xr : zf;
            float width = Math.Min(WindowWidth, a1 - a0 - 1f);
            d.Window(outer, side, rng.Pick(a0 + 0.5f + width / 2, a1 - 0.5f - width / 2), width, floor + WindowSill, WindowHeight, "Room", 0, t);
            d.Shell("", outer, floor, floor + StoreyClear, t);
            d.FlatRoof(floor + StoreyClear, SlabThickness, RoofOverhang, false);
            d.SetRole("Room", RoomRole.Workshop);
            d.Furnish(room, 0, room.Area >= 14f ? 3 : 2, WorkshopSet, 1);
            d.RoomLamps(room);
            d.Loot("Room", new Rect(xl, xr, zb, zf), floor, Clamp((int)(room.Area / 5f), 2, 8), null);
        }

        // A concrete car bay, 3 x 3 m inside, open at the front under a short porch roof on two posts.
        // The spawn point is the centre of the bay, facing the way out (+Z).
        static void ElevatorStation(Draft d)
        {
            const float t = HallWall, half = ElevatorBay / 2;
            var plan = d.Plan;
            float hw = d.Hw, hd = d.Hd, floor = FloorTop, top = FloorTop + StoreyClear;
            float zb = -hd + t, zFront = zb + ElevatorBay;
            plan.Storeys = 1;
            d.Box("Slab", SurfaceKind.Floor, -hw, hw, 0f, floor, -hd, hd);
            d.Box("WallBack", SurfaceKind.WallOuter, -half - t, half + t, floor, top, -hd, zb);
            d.Box("WallLeft", SurfaceKind.WallOuter, -half - t, -half, floor, top, zb, zFront);
            d.Box("WallRight", SurfaceKind.WallOuter, half, half + t, floor, top, zb, zFront);
            d.Box("PostLeft", SurfaceKind.Trim, -hw, -hw + 0.3f, floor, top, hd - 0.3f, hd);
            d.Box("PostRight", SurfaceKind.Trim, hw - 0.3f, hw, floor, top, hd - 0.3f, hd);
            d.Box("Roof", SurfaceKind.Roof, -hw, hw, top, top + 0.25f, -hd, hd);
            d.Room("CarBay", 0, -half, half, zb, zFront, floor, top);
            plan.Doors.Add(new BuildingOpening
            {
                Name = "BayFront", X = 0f, Y = floor, Z = zFront, Width = ElevatorBay, Height = StoreyClear, Depth = 0f,
                FacingDeg = 0f, Exterior = true, Storey = 0, Room = "CarBay",
            });
            plan.HasSpawn = true;
            plan.SpawnX = 0f;
            plan.SpawnY = floor;
            plan.SpawnZ = zb + half;
            plan.SpawnFacingDeg = 0f;
        }

        // ---- building blocks ----

        enum Side { Front, Right, Back, Left }   // +Z, +X, -Z, -X

        struct Gap { public float At, Width, Bottom, Top; }

        static List<Gap>[] NewGaps() => new[] { new List<Gap>(), new List<Gap>(), new List<Gap>(), new List<Gap>() };

        struct Rect
        {
            public float X0, X1, Z0, Z1;
            public Rect(float x0, float x1, float z0, float z1) { X0 = x0; X1 = x1; Z0 = z0; Z1 = z1; }
            public bool Intersects(Rect o, float margin) =>
                X0 - margin < o.X1 && X1 + margin > o.X0 && Z0 - margin < o.Z1 && Z1 + margin > o.Z0;
            public bool Contains(float x, float z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;
        }

        // One piece of furniture a room may get: its footprint (W along the wall, D into the room) and height.
        struct Spec
        {
            public PropKind Prop; public float W, D, H;
            public Spec(PropKind prop, float w, float d, float h) { Prop = prop; W = w; D = d; H = h; }
        }

        static readonly Spec[] LivingSet =
        {
            new Spec(PropKind.Sofa, 2.0f, 0.9f, 0.85f), new Spec(PropKind.TvStand, 1.2f, 0.45f, 0.5f),
            new Spec(PropKind.Table, 1.4f, 1.4f, 0.8f), new Spec(PropKind.Bookcase, 1.0f, 0.35f, 1.9f),
            new Spec(PropKind.Wardrobe, 1.2f, 0.55f, 2.0f), new Spec(PropKind.Counter, 1.4f, 0.5f, 0.85f),
            new Spec(PropKind.Armchair, 0.85f, 0.85f, 0.85f), new Spec(PropKind.Plant, 0.5f, 0.5f, 1.15f),
        };
        static readonly Spec[] StudioSet =
        {
            new Spec(PropKind.Bed, 1.55f, 2.0f, 0.5f), new Spec(PropKind.Kitchen, 2.6f, 0.6f, 0.9f),
            new Spec(PropKind.Sofa, 2.0f, 0.9f, 0.85f), new Spec(PropKind.TvStand, 1.2f, 0.45f, 0.5f),
            new Spec(PropKind.Table, 1.4f, 1.4f, 0.8f), new Spec(PropKind.Wardrobe, 1.2f, 0.55f, 2.0f),
        };
        static readonly Spec[] KitchenSet =
        {
            new Spec(PropKind.Kitchen, 2.6f, 0.6f, 0.9f), new Spec(PropKind.Table, 1.4f, 1.4f, 0.8f),
            new Spec(PropKind.Shelf, 1.2f, 0.4f, 1.9f), new Spec(PropKind.Counter, 1.4f, 0.6f, 0.9f),
            new Spec(PropKind.Plant, 0.5f, 0.5f, 1.15f),
        };
        static readonly Spec[] BedroomSet =
        {
            new Spec(PropKind.Bed, 1.55f, 2.0f, 0.5f), new Spec(PropKind.Wardrobe, 1.2f, 0.55f, 2.0f),
            new Spec(PropKind.Desk, 1.4f, 1.3f, 0.75f), new Spec(PropKind.Bookcase, 1.0f, 0.35f, 1.9f),
            new Spec(PropKind.Table, 1.4f, 1.4f, 0.8f), new Spec(PropKind.Dresser, 1.1f, 0.5f, 0.9f),
            new Spec(PropKind.Plant, 0.5f, 0.5f, 1.15f), new Spec(PropKind.Armchair, 0.85f, 0.85f, 0.85f),
        };
        static readonly Spec[] BathSet =
        {
            new Spec(PropKind.BathSet, 1.7f, 0.75f, 0.75f), new Spec(PropKind.Bathtub, 1.7f, 0.75f, 0.55f),
        };
        static readonly Spec[] OfficeSet =
        {
            new Spec(PropKind.Desk, 1.4f, 1.3f, 0.75f), new Spec(PropKind.Locker, 1.2f, 0.5f, 1.9f),
            new Spec(PropKind.Bookcase, 1.0f, 0.35f, 1.9f), new Spec(PropKind.Counter, 1.8f, 0.6f, 0.9f),
            new Spec(PropKind.Table, 1.4f, 1.4f, 0.8f), new Spec(PropKind.Plant, 0.5f, 0.5f, 1.15f),
        };
        static readonly Spec[] WorkshopSet =
        {
            new Spec(PropKind.Workbench, 1.8f, 0.7f, 0.95f), new Spec(PropKind.Shelf, 1.2f, 0.45f, 1.9f),
            new Spec(PropKind.Locker, 1.2f, 0.5f, 1.9f), new Spec(PropKind.Barrel, 0.6f, 0.6f, 0.9f),
            new Spec(PropKind.Pallet, 1.2f, 1.0f, 1.0f), new Spec(PropKind.Crate, 1.0f, 1.0f, 1.0f),
        };
        static readonly Spec[] StorageSet =
        {
            new Spec(PropKind.Pallet, 1.2f, 1.0f, 1.0f), new Spec(PropKind.Barrel, 0.6f, 0.6f, 0.9f),
            new Spec(PropKind.Crate, 1.1f, 1.1f, 1.1f), new Spec(PropKind.Locker, 1.2f, 0.5f, 1.9f),
            new Spec(PropKind.Pallet, 1.2f, 1.0f, 1.0f), new Spec(PropKind.Barrel, 0.6f, 0.6f, 0.9f),
        };

        struct LootTop { public float X, Y, Z; public string Room; }

        // xorshift32 over a mixed seed: the same on every runtime, unlike System.Random.
        sealed class Rng
        {
            uint _state;

            public Rng(int seed, int salt)
            {
                unchecked
                {
                    uint x = (uint)seed * 2654435761u + (uint)salt * 40503u + 0x9E3779B9u;
                    x ^= x >> 16; x *= 0x85EBCA6Bu; x ^= x >> 13; x *= 0xC2B2AE35u; x ^= x >> 16;
                    _state = x == 0u ? 1u : x;
                }
            }

            uint Next()
            {
                unchecked
                {
                    _state ^= _state << 13; _state ^= _state >> 17; _state ^= _state << 5;
                    return _state;
                }
            }

            public float Value() => (Next() >> 8) / 16777216f;   // [0, 1)
            public float Range(float lo, float hi) => hi <= lo ? (lo + hi) * 0.5f : lo + (hi - lo) * Value();
            public int Int(int count) => count <= 1 ? 0 : (int)(Value() * count) % count;
            public bool Chance(float p) => Value() < p;

            // A position on a 5 cm grid inside [lo, hi]; the middle when the range is empty.
            public float Pick(float lo, float hi)
            {
                if (hi <= lo) { Value(); return (lo + hi) * 0.5f; }
                float v = (float)Math.Round(Range(lo, hi) * 20f) / 20f;
                return v < lo ? lo : v > hi ? hi : v;
            }
        }

        sealed class Draft
        {
            public readonly BuildingPlan Plan;
            public readonly Rng Rng;
            public readonly float Hw, Hd;
            // Per level (0 = ground, 1 = upper floor or mezzanine): where props may not stand, and what they keep away from.
            public readonly List<Rect>[] Clear = NewLevels();
            public readonly List<Rect>[] Blockers = NewLevels();
            static List<Rect>[] NewLevels() { var l = new List<Rect>[6]; for (int i = 0; i < l.Length; i++) l[i] = new List<Rect>(); return l; }
            readonly List<LootTop> _tops = new List<LootTop>();
            readonly bool _mirror;
            int _props;

            public Draft(BuildingKind kind, float width, float depth, int seed)
            {
                FootprintLimits(kind, out float minW, out float minD, out float maxW, out float maxD);
                float w = (float)Math.Round(Math.Min(maxW, Math.Max(minW, width)) * 10f) / 10f;
                float dp = (float)Math.Round(Math.Min(maxD, Math.Max(minD, depth)) * 10f) / 10f;
                Plan = new BuildingPlan { Kind = kind, Seed = seed, Width = w, Depth = dp, FloorY = FloorTop, Storeys = 1 };
                Rng = new Rng(seed, (int)kind);
                Hw = w * 0.5f;
                Hd = dp * 0.5f;
                _mirror = kind != BuildingKind.ElevatorStation && Rng.Chance(0.5f);
            }

            public void Box(string name, SurfaceKind kind, float x0, float x1, float y0, float y1, float z0, float z1, PropKind prop = PropKind.None)
            {
                if (x1 - x0 < 0.02f || y1 - y0 < 0.02f || z1 - z0 < 0.02f) return;
                Plan.Pieces.Add(new BuildingPiece
                {
                    Name = name, Kind = kind, Prop = prop,
                    X = (x0 + x1) * 0.5f, Y = (y0 + y1) * 0.5f, Z = (z0 + z1) * 0.5f,
                    SizeX = x1 - x0, SizeY = y1 - y0, SizeZ = z1 - z0,
                });
            }

            public BuildingRoom Room(string name, int storey, float x0, float x1, float z0, float z1, float floorY, float ceilingY)
            {
                var room = new BuildingRoom { Name = name, Storey = storey, MinX = x0, MaxX = x1, MinZ = z0, MaxZ = z1, FloorY = floorY, CeilingY = ceilingY };
                Plan.Rooms.Add(room);
                return room;
            }

            // A straight wall from a0 to a1 (along X at z = c, or along Z at x = c), split into the solid parts
            // beside, below and above each gap. Gaps must not overlap along the wall.
            public void Wall(string name, SurfaceKind kind, bool alongX, float c, float a0, float a1, float y0, float y1, float t, List<Gap> gaps)
            {
                int n = 0;
                void Part(float p0, float p1, float q0, float q1)
                {
                    if (alongX) Box(name + n, kind, p0, p1, q0, q1, c - t / 2, c + t / 2);
                    else Box(name + n, kind, c - t / 2, c + t / 2, q0, q1, p0, p1);
                    n++;
                }
                float cursor = a0;
                if (gaps != null)
                {
                    gaps.Sort((p, q) => p.At.CompareTo(q.At));
                    foreach (var gap in gaps)
                    {
                        float g0 = gap.At - gap.Width / 2, g1 = gap.At + gap.Width / 2;
                        Part(cursor, g0, y0, y1);
                        Part(g0, g1, y0, gap.Bottom);
                        Part(g0, g1, gap.Top, y1);
                        cursor = g1;
                    }
                }
                Part(cursor, a1, y0, y1);
            }

            // The four outer walls. Front and back run the full width; the side walls fit between them.
            public void Shell(string tag, List<Gap>[] gaps, float y0, float y1, float t)
            {
                Wall(tag + "Front", SurfaceKind.WallOuter, true, Hd - t / 2, -Hw, Hw, y0, y1, t, gaps[(int)Side.Front]);
                Wall(tag + "Back", SurfaceKind.WallOuter, true, -Hd + t / 2, -Hw, Hw, y0, y1, t, gaps[(int)Side.Back]);
                Wall(tag + "Right", SurfaceKind.WallOuter, false, Hw - t / 2, -Hd + t, Hd - t, y0, y1, t, gaps[(int)Side.Right]);
                Wall(tag + "Left", SurfaceKind.WallOuter, false, -Hw + t / 2, -Hd + t, Hd - t, y0, y1, t, gaps[(int)Side.Left]);
            }

            public void FlatRoof(float y, float thickness, float overhang, bool parapet)
            {
                float x = Hw + overhang, z = Hd + overhang, top = y + thickness;
                Box("Roof", SurfaceKind.Roof, -x, x, y, top, -z, z);
                if (!parapet) return;
                Box("ParapetFront", SurfaceKind.Trim, -x, x, top, top + 0.4f, z - 0.2f, z);
                Box("ParapetBack", SurfaceKind.Trim, -x, x, top, top + 0.4f, -z, -z + 0.2f);
                Box("ParapetRight", SurfaceKind.Trim, x - 0.2f, x, top, top + 0.4f, -z + 0.2f, z - 0.2f);
                Box("ParapetLeft", SurfaceKind.Trim, -x, -x + 0.2f, top, top + 0.4f, -z + 0.2f, z - 0.2f);
            }

            public bool Touches(BuildingRoom room, Side side, float t)
            {
                switch (side)
                {
                    case Side.Front: return room.MaxZ >= Hd - t - 0.01f;
                    case Side.Back: return room.MinZ <= -Hd + t + 0.01f;
                    case Side.Right: return room.MaxX >= Hw - t - 0.01f;
                    default: return room.MinX <= -Hw + t + 0.01f;
                }
            }

            public int TouchCount(BuildingRoom room, float t)
            {
                int count = 0;
                for (int sd = 0; sd < 4; sd++)
                    if (Touches(room, (Side)sd, t)) count++;
                return count;
            }

            Rect InsideZone(Side side, float at, float halfWidth, float depth, float t)
            {
                switch (side)
                {
                    case Side.Front: return new Rect(at - halfWidth, at + halfWidth, Hd - t - depth, Hd - t);
                    case Side.Back: return new Rect(at - halfWidth, at + halfWidth, -Hd + t, -Hd + t + depth);
                    case Side.Right: return new Rect(Hw - t - depth, Hw - t, at - halfWidth, at + halfWidth);
                    default: return new Rect(-Hw + t, -Hw + t + depth, at - halfWidth, at + halfWidth);
                }
            }

            BuildingOpening Outer(string name, Side side, float at, float width, float bottom, float height, string room, int storey, float t)
            {
                float x, z, facing;
                switch (side)
                {
                    case Side.Front: x = at; z = Hd - t / 2; facing = 0f; break;
                    case Side.Back: x = at; z = -Hd + t / 2; facing = 180f; break;
                    case Side.Right: x = Hw - t / 2; z = at; facing = 90f; break;
                    default: x = -Hw + t / 2; z = at; facing = 270f; break;
                }
                return new BuildingOpening
                {
                    Name = name, X = x, Y = bottom, Z = z, Width = width, Height = height, Depth = t,
                    FacingDeg = facing, Exterior = true, Storey = storey, Room = room,
                };
            }

            public void Door(List<Gap>[] gaps, Side side, float at, float width, float height, float floorY, string room, int storey, float t, string name)
            {
                gaps[(int)side].Add(new Gap { At = at, Width = width, Bottom = floorY, Top = floorY + height });
                Plan.Doors.Add(Outer(name, side, at, width, floorY, height, room, storey, t));
                Clear[storey].Add(InsideZone(side, at, width / 2 + 0.3f, width >= 3f ? 3f : 1.4f, t));
            }

            public void Window(List<Gap>[] gaps, Side side, float at, float width, float bottom, float height, string room, int storey, float t)
            {
                gaps[(int)side].Add(new Gap { At = at, Width = width, Bottom = bottom, Top = bottom + height });
                Plan.Windows.Add(Outer("Window" + Plan.Windows.Count, side, at, width, bottom, height, room, storey, t));
                Clear[storey].Add(InsideZone(side, at, width / 2 + 0.3f, 0.5f, t));   // keeps the firing position free
            }

            // Evenly spaced windows along a stretch of wall, skipping places already taken by another opening.
            public void WindowRow(List<Gap>[] gaps, Side side, float a0, float a1, float spacing, float width, float bottom, float height, string room, int storey, float t)
            {
                float length = a1 - a0;
                if (length < width + 1f) return;
                int count = Math.Max(1, (int)(length / spacing));
                for (int i = 0; i < count; i++)
                {
                    float at = a0 + (i + 0.5f) * length / count;
                    bool taken = false;
                    foreach (var gap in gaps[(int)side])
                        if (Math.Abs(at - gap.At) < (width + gap.Width) / 2 + 0.5f) taken = true;
                    if (!taken) Window(gaps, side, at, width, bottom, height, room, storey, t);
                }
            }

            // Openings for one room along one outer wall: a door (with a window beside it when there is room),
            // or one or two windows.
            public void FillSegment(List<Gap>[] gaps, Side side, float a0, float a1, bool door, string room, int storey, float floorY, float t)
            {
                float length = a1 - a0, sill = floorY + WindowSill;
                if (door)
                {
                    float d0 = a0, d1 = a1;
                    if (length >= 4.8f)
                    {
                        float mid = (a0 + a1) * 0.5f;
                        bool doorFirst = Rng.Chance(0.5f);
                        d0 = doorFirst ? a0 : mid;
                        d1 = doorFirst ? mid : a1;
                        float w0 = doorFirst ? mid : a0, w1 = doorFirst ? a1 : mid;
                        Window(gaps, side, Rng.Pick(w0 + 0.4f + WindowWidth / 2, w1 - 0.4f - WindowWidth / 2), WindowWidth, sill, WindowHeight, room, storey, t);
                    }
                    Door(gaps, side, Rng.Pick(d0 + 0.4f + DoorWidth / 2, d1 - 0.4f - DoorWidth / 2), DoorWidth, DoorHeight, floorY, room, storey, t, "Door" + side);
                    return;
                }
                int count = length >= 6.4f ? 2 : length >= 2.4f ? 1 : 0;
                for (int i = 0; i < count; i++)
                {
                    float p0 = a0 + length * i / count, p1 = a0 + length * (i + 1) / count;
                    Window(gaps, side, Rng.Pick(p0 + 0.4f + WindowWidth / 2, p1 - 0.4f - WindowWidth / 2), WindowWidth, sill, WindowHeight, room, storey, t);
                }
            }

            // A door in an interior wall (along X at z = c, or along Z at x = c). Room is on the low side.
            public void InnerDoor(List<Gap> gaps, bool wallAlongX, float c, float at, float floorY, string room, string other, int storey, string name)
            {
                gaps.Add(new Gap { At = at, Width = DoorWidth, Bottom = floorY, Top = floorY + DoorHeight });
                Plan.Doors.Add(new BuildingOpening
                {
                    Name = name, X = wallAlongX ? at : c, Y = floorY, Z = wallAlongX ? c : at, Width = DoorWidth, Height = DoorHeight,
                    Depth = InnerWall, FacingDeg = wallAlongX ? 0f : 90f, Exterior = false, Storey = storey, Room = room, OtherRoom = other,
                });
                float half = DoorWidth / 2 + 0.3f, reach = InnerWall / 2 + 1.4f;
                Clear[storey].Add(wallAlongX ? new Rect(at - half, at + half, c - reach, c + reach) : new Rect(c - reach, c + reach, at - half, at + half));
            }

            // A straight flight of solid box steps climbing toward -Z from the foot edge at z = zFoot.
            // The last riser lands on the floor at topY, which the caller provides beyond the flight.
            public void Stairs(string name, float x0, float zFoot, float baseY, float topY)
            {
                int risers = Risers(topY - baseY);
                float rise = (topY - baseY) / risers;
                for (int i = 1; i < risers; i++)
                    Box(name + "Step" + i, SurfaceKind.Stairs, x0, x0 + StairWidth, baseY, baseY + i * rise, zFoot - i * StairRun, zFoot - (i - 1) * StairRun);
                Plan.Stairs.Add(new BuildingStair
                {
                    Name = name, X = x0 + StairWidth / 2, Z = zFoot, BaseY = baseY, TopY = topY, Rise = rise, Run = StairRun,
                    Width = StairWidth, Length = (risers - 1) * StairRun, Steps = risers - 1, FacingDeg = 180f,
                });
            }

            // Places a prop unless it would stand in a kept-clear zone or closer than a walkable gap to another prop.
            // The prop's box is the collider; its decor boxes are what it looks like (Furniture).
            public bool TryProp(string name, PropKind prop, Rect r, float y0, float height, int level, string room, int facing = 0)
            {
                foreach (var zone in Clear[level])
                    if (r.Intersects(zone, 0f)) return false;
                foreach (var other in Blockers[level])
                    if (TooTight(r, other)) return false;
                Box(name + _props, SurfaceKind.Prop, r.X0, r.X1, y0, y0 + height, r.Z0, r.Z1, prop);
                var piece = Plan.Pieces[Plan.Pieces.Count - 1];
                piece.Facing = facing;
                Plan.Pieces[Plan.Pieces.Count - 1] = piece;
                Furniture.Build(prop, name + _props, r.X0, r.X1, r.Z0, r.Z1, y0, height, facing, _props, BuildingKinds.Family(Plan.Kind) != BuildingKind.House,
                    Plan.Decor, out float lootX, out float lootZ, BuildingKinds.Family(Plan.Kind) == BuildingKind.House && !WindowNear(r));
                _props++;
                Blockers[level].Add(r);
                if (height <= LootTopMax && Furniture.HoldsLoot(prop))
                    _tops.Add(new LootTop { X = lootX, Y = y0 + height, Z = lootZ, Room = room });
                return true;
            }

            static bool TooTight(Rect a, Rect b)
            {
                float sx = Math.Max(b.X0 - a.X1, a.X0 - b.X1), sz = Math.Max(b.Z0 - a.Z1, a.Z0 - b.Z1);
                if (sx < 0f && sz < 0f) return true;   // overlap
                float dist = (float)Math.Sqrt(Math.Max(sx, 0f) * Math.Max(sx, 0f) + Math.Max(sz, 0f) * Math.Max(sz, 0f));
                return dist > PropClosedGap && dist < PropWalkGap;
            }

            // A window in the wall next to this rectangle: no picture goes on that wall.
            bool WindowNear(Rect r)
            {
                foreach (var w in Plan.Windows)
                    if (w.X > r.X0 - 0.7f && w.X < r.X1 + 0.7f && w.Z > r.Z0 - 0.7f && w.Z < r.Z1 + 0.7f) return true;
                return false;
            }

            public void SetRole(string room, RoomRole role)
            {
                for (int i = 0; i < Plan.Rooms.Count; i++)
                    if (Plan.Rooms[i].Name == room) { var r = Plan.Rooms[i]; r.Role = role; Plan.Rooms[i] = r; }
            }

            // Furniture against the walls of a room: the specs in order (the first `essential` ones first, the rest
            // shuffled), at most `count` pieces, each facing into the room.
            public void Furnish(BuildingRoom room, int level, int count, Spec[] specs, int essential)
            {
                const float gap = 0.03f;
                var order = new List<Spec>();
                for (int i = 0; i < Math.Min(essential, specs.Length); i++) order.Add(specs[i]);
                var rest = new List<Spec>();
                for (int i = essential; i < specs.Length; i++) rest.Add(specs[i]);
                while (rest.Count > 0) { int k = Rng.Int(rest.Count); order.Add(rest[k]); rest.RemoveAt(k); }
                int placed = 0;
                for (int pass = 0; pass < 2 && placed < count; pass++)
                    foreach (var spec in order)
                    {
                        if (placed >= count) break;
                        for (int attempt = 0; attempt < 14; attempt++)
                        {
                            int wall = Rng.Int(4);
                            float along = Rng.Value();
                            bool alongX = wall < 2;
                            float span = alongX ? room.MaxX - room.MinX : room.MaxZ - room.MinZ;
                            float other = alongX ? room.MaxZ - room.MinZ : room.MaxX - room.MinX;
                            if (span < spec.W + 0.4f || other < spec.D + 1.3f) continue;
                            float lo = (alongX ? room.MinX : room.MinZ) + 0.2f;
                            float p0 = lo + (span - 0.4f - spec.W) * along;
                            Rect r;
                            int facing;
                            if (wall == 0) { r = new Rect(p0, p0 + spec.W, room.MinZ + gap, room.MinZ + gap + spec.D); facing = 0; }
                            else if (wall == 1) { r = new Rect(p0, p0 + spec.W, room.MaxZ - gap - spec.D, room.MaxZ - gap); facing = 2; }
                            else if (wall == 2) { r = new Rect(room.MinX + gap, room.MinX + gap + spec.D, p0, p0 + spec.W); facing = 1; }
                            else { r = new Rect(room.MaxX - gap - spec.D, room.MaxX - gap, p0, p0 + spec.W); facing = 3; }
                            if (TryProp(spec.Prop.ToString(), spec.Prop, r, room.FloorY, spec.H, level, room.Name, facing)) { placed++; break; }
                        }
                    }
            }

            // One free-standing piece in the middle of a big room (a table on the rug), if the walkways around it stay open.
            public void Centre(BuildingRoom room, int level, Spec spec)
            {
                float w = room.MaxX - room.MinX, l = room.MaxZ - room.MinZ;
                if (w < spec.W + 2.4f || l < spec.D + 2.4f) return;
                float cx = (room.MinX + room.MaxX) * 0.5f, cz = (room.MinZ + room.MaxZ) * 0.5f;
                TryProp(spec.Prop.ToString(), spec.Prop, new Rect(cx - spec.W * 0.5f, cx + spec.W * 0.5f, cz - spec.D * 0.5f, cz + spec.D * 0.5f), room.FloorY, spec.H, level, room.Name, Rng.Int(2) * 2);
            }

            // Curtains on both sides of every window of a house, hung on a rod: decor only, on the inner wall face.
            public void Curtains()
            {
                int n = 0;
                foreach (var w in Plan.Windows)
                {
                    if (!Rng.Chance(0.75f)) continue;
                    float fx = w.ForwardX, fz = w.ForwardZ;
                    bool wallAlongX = Math.Abs(fz) > 0.5f;
                    float inX = w.X - fx * (w.Depth * 0.5f), inZ = w.Z - fz * (w.Depth * 0.5f);   // inner face of the wall
                    var mat = n % 2 == 0 ? DecorMat.Fabric : DecorMat.Fabric2;
                    float y0 = w.Y - 0.2f, y1 = w.Y + w.Height + 0.3f, half = w.Width * 0.5f;
                    void Panel(string name, DecorMat m, float a0, float a1, float ya, float yb, float d0, float d1)
                    {
                        // a0..a1 along the wall from the window centre; d0..d1 into the room from the inner face.
                        float p0 = (wallAlongX ? inZ : inX) - (wallAlongX ? fz : fx) * d0, p1 = (wallAlongX ? inZ : inX) - (wallAlongX ? fz : fx) * d1;
                        float lo = Math.Min(p0, p1), hi = Math.Max(p0, p1);
                        if (wallAlongX) Furniture.Add(Plan.Decor, name, m, w.X + a0, w.X + a1, ya, yb, lo, hi);
                        else Furniture.Add(Plan.Decor, name, m, lo, hi, ya, yb, w.Z + a0, w.Z + a1);
                    }
                    string tag = "Curtain" + n++;
                    Panel(tag + "L", mat, -half - 0.45f, -half + 0.05f, y0, y1, 0.01f, 0.07f);
                    Panel(tag + "R", mat, half - 0.05f, half + 0.45f, y0, y1, 0.01f, 0.07f);
                    Panel(tag + "Rod", DecorMat.Metal, -half - 0.5f, half + 0.5f, y1, y1 + 0.03f, 0.02f, 0.1f);
                }
            }

            // Small things on the floor of a room: boxes, bottles, clothes, papers. Decor only (no collider), so they stay
            // out of doorways, away from loot points and off the furniture.
            public void Clutter(BuildingRoom room, int level, int count)
            {
                for (int i = 0; i < count; i++)
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        int kind = Rng.Int(5);
                        float sx = kind == 0 ? Rng.Range(0.3f, 0.45f) : kind == 1 ? 0.12f : kind == 2 ? Rng.Range(0.4f, 0.7f) : 0.32f;
                        float sz = kind == 0 ? Rng.Range(0.3f, 0.45f) : kind == 1 ? 0.12f : kind == 2 ? Rng.Range(0.3f, 0.5f) : 0.22f;
                        float x = Rng.Range(room.MinX + 0.2f, room.MaxX - 0.2f - sx), z = Rng.Range(room.MinZ + 0.2f, room.MaxZ - 0.2f - sz);
                        var r = new Rect(x, x + sx, z, z + sz);
                        bool bad = false;
                        foreach (var zone in Clear[level]) if (r.Intersects(zone, 0f)) bad = true;
                        foreach (var other in Blockers[level]) if (r.Intersects(other, 0.02f)) bad = true;
                        foreach (var loot in Plan.LootPoints)
                            if (Math.Abs(loot.Y - room.FloorY) < 0.5f && loot.X > r.X0 - 0.6f && loot.X < r.X1 + 0.6f && loot.Z > r.Z0 - 0.6f && loot.Z < r.Z1 + 0.6f) bad = true;
                        if (bad) continue;
                        float y = room.FloorY;
                        string name = "Clutter" + Plan.Decor.Count;
                        switch (kind)
                        {
                            case 0: // a cardboard box, sometimes a smaller one on top
                                Furniture.Add(Plan.Decor, name, DecorMat.Crate, r.X0, r.X1, y, y + 0.3f, r.Z0, r.Z1);
                                Furniture.Add(Plan.Decor, name + "Lid", DecorMat.Wood, r.X0 + 0.02f, r.X1 - 0.02f, y + 0.3f, y + 0.32f, r.Z0 + 0.02f, r.Z1 - 0.02f);
                                if (Rng.Chance(0.4f)) Furniture.Add(Plan.Decor, name + "Top", DecorMat.Crate, r.X0 + 0.04f, r.X0 + 0.2f, y + 0.32f, y + 0.5f, r.Z0 + 0.04f, r.Z0 + 0.2f);
                                break;
                            case 1: // two bottles
                                Furniture.Add(Plan.Decor, name, DecorMat.Paint, r.X0, r.X0 + 0.07f, y, y + 0.26f, r.Z0, r.Z0 + 0.07f);
                                Furniture.Add(Plan.Decor, name + "B", DecorMat.Warn, r.X0 + 0.1f, r.X0 + 0.17f, y, y + 0.2f, r.Z0 + 0.04f, r.Z0 + 0.11f);
                                break;
                            case 2: // clothes dropped on the floor
                                Furniture.Add(Plan.Decor, name, Rng.Chance(0.5f) ? DecorMat.Fabric : DecorMat.Fabric2, r.X0, r.X1, y, y + 0.07f, r.Z0, r.Z1);
                                Furniture.Add(Plan.Decor, name + "B", DecorMat.Fabric2, r.X0 + 0.1f, r.X0 + 0.3f, y + 0.07f, y + 0.12f, r.Z0 + 0.05f, r.Z1 - 0.05f);
                                break;
                            case 3: // papers and a book
                                Furniture.Add(Plan.Decor, name, DecorMat.Ceramic, r.X0, r.X1, y, y + 0.012f, r.Z0, r.Z1);
                                Furniture.Add(Plan.Decor, name + "B", DecorMat.Fabric, r.X0 + 0.04f, r.X0 + 0.2f, y + 0.012f, y + 0.05f, r.Z0 + 0.03f, r.Z0 + 0.19f);
                                break;
                            default: // a low stool
                                Furniture.Add(Plan.Decor, name, DecorMat.Wood, r.X0, r.X0 + 0.32f, y + 0.3f, y + 0.34f, r.Z0, r.Z0 + 0.22f);
                                Furniture.Add(Plan.Decor, name + "L", DecorMat.WoodDark, r.X0 + 0.02f, r.X0 + 0.06f, y, y + 0.3f, r.Z0 + 0.02f, r.Z0 + 0.06f);
                                Furniture.Add(Plan.Decor, name + "R", DecorMat.WoodDark, r.X0 + 0.26f, r.X0 + 0.3f, y, y + 0.3f, r.Z0 + 0.16f, r.Z0 + 0.2f);
                                break;
                        }
                        break;
                    }
            }

            // A rug in the middle of the room, clear of the walls: decor only.
            public void Rug(BuildingRoom room, DecorMat mat)
            {
                float cx = (room.MinX + room.MaxX) * 0.5f, cz = (room.MinZ + room.MaxZ) * 0.5f;
                bool wide = room.MaxX - room.MinX >= room.MaxZ - room.MinZ;
                float hx = wide ? 1.1f : 0.8f, hz = wide ? 0.8f : 1.1f;
                if (room.MaxX - room.MinX < 2 * hx + 1.2f || room.MaxZ - room.MinZ < 2 * hz + 1.2f) return;
                Furniture.Rug("Rug" + Plan.Decor.Count, cx - hx, cx + hx, cz - hz, cz + hz, room.FloorY, mat, Plan.Decor);
            }

            // A house lamp (a shade on the ceiling) or, with tube, a hung fluorescent tube, plus the light that goes with it.
            public void Lamp(BuildingRoom room, float x, float z, bool tube, bool alongX, float range, float intensity, bool flicker)
            {
                float hang = tube ? Math.Min(room.CeilingY - room.FloorY - 3.4f, 1.6f) : 0.02f;
                if (tube && hang < 0.1f) hang = 0.1f;
                string name = "Lamp" + Plan.Lamps.Count;
                Furniture.LampFixture(name, x, z, room.CeilingY, hang, tube, alongX, Plan.Decor);
                Plan.Lamps.Add(new BuildingLamp
                {
                    Name = name, X = x, Y = room.CeilingY - hang - (tube ? 0.2f : 0.45f), Z = z, Room = room.Name,
                    Range = range, Intensity = intensity, Flicker = flicker,
                });
            }

            // One shade in the middle of a room, two when it is long.
            public void RoomLamps(BuildingRoom room)
            {
                float w = room.MaxX - room.MinX, l = room.MaxZ - room.MinZ;
                float cx = (room.MinX + room.MaxX) * 0.5f, cz = (room.MinZ + room.MaxZ) * 0.5f;
                float range = Math.Min(7f, Math.Max(4.5f, Math.Max(w, l) * 0.8f));
                if (Math.Max(w, l) > 7.5f)
                {
                    bool alongX = w >= l;
                    float off = Math.Max(w, l) / 4f;
                    Lamp(room, alongX ? cx - off : cx, alongX ? cz : cz - off, false, false, range, 5.5f, false);
                    Lamp(room, alongX ? cx + off : cx, alongX ? cz : cz + off, false, false, range, 5.5f, false);
                }
                else Lamp(room, cx, cz, false, false, range, 5.5f, false);
            }

            // Hung tubes on a grid over a big hall, a few of them tired.
            public void HallLamps(BuildingRoom room, float spacing, int max)
            {
                float w = room.MaxX - room.MinX, l = room.MaxZ - room.MinZ;
                int nx = Math.Max(1, (int)Math.Round(w / spacing)), nz = Math.Max(1, (int)Math.Round(l / spacing));
                while (nx * nz > max) { if (nx >= nz) nx--; else nz--; }
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < nz; j++)
                        Lamp(room, room.MinX + (i + 0.5f) * w / nx, room.MinZ + (j + 0.5f) * l / nz, true, (i + j) % 2 == 0, 16f, 18f, Rng.Chance(0.25f));
            }

            // Free-standing props at random places inside an area that already keeps its distance from the walls.
            public void Scatter(string name, PropKind prop, int count, Rect area, float minX, float maxX, float minZ, float maxZ, float minH, float maxH, float y0, int level, string room)
            {
                for (int i = 0; i < count; i++)
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        float sx = Rng.Range(minX, maxX), sz = Rng.Range(minZ, maxZ), h = Rng.Range(minH, maxH);
                        float x = Rng.Range(area.X0, area.X1 - sx), z = Rng.Range(area.Z0, area.Z1 - sz);
                        if (area.X1 - area.X0 < sx || area.Z1 - area.Z0 < sz) continue;
                        if (TryProp(name, prop, new Rect(x, x + sx, z, z + sz), y0, h, level, room, Rng.Int(4))) break;
                    }
            }

            // Loot for one room: on its low furniture first, then on free floor. Call after all pieces exist.
            public void Loot(string room, Rect area, float y, int count, Rect? exclude)
            {
                int first = Plan.LootPoints.Count, placed = 0;
                foreach (var top in _tops)
                {
                    if (top.Room != room || placed >= (count + 1) / 2 || LootPlacement.NearDoor(Plan, top.X, top.Y, top.Z)) continue;
                    AddLoot(top.X, top.Y, top.Z, room, true);
                    placed++;
                }
                for (int attempt = 0; placed < count && attempt < count * 40; attempt++)
                {
                    float x = Rng.Range(area.X0 + 0.5f, area.X1 - 0.5f), z = Rng.Range(area.Z0 + 0.5f, area.Z1 - 0.5f);
                    if (exclude.HasValue && exclude.Value.Contains(x, z)) continue;
                    if (!FloorIsFree(x, y, z) || LootPlacement.NearDoor(Plan, x, y, z)) continue;
                    float spacing = attempt < count * 20 ? 1.2f : 0.6f;
                    bool crowded = false;
                    for (int i = first; i < Plan.LootPoints.Count; i++)
                    {
                        var p = Plan.LootPoints[i];
                        if (Math.Abs(p.X - x) < spacing && Math.Abs(p.Z - z) < spacing) crowded = true;
                    }
                    if (crowded) continue;
                    AddLoot(x, y, z, room, false);
                    placed++;
                }
            }

            // Loot on a roof deck (ADR-256): free deck, a body's width from the parapet and from the stair house, one point every 2 m or more.
            public void RoofLoot(float y, float x0, float x1, float z0, float z1, int count, Rect exclude)
            {
                int placed = 0;
                for (int attempt = 0; placed < count && attempt < 200; attempt++)
                {
                    float x = Rng.Range(x0 + 1.2f, x1 - 1.2f), z = Rng.Range(z0 + 1.2f, z1 - 1.2f);
                    if (exclude.X0 - 1f < x && x < exclude.X1 + 1f && exclude.Z0 - 1f < z && z < exclude.Z1 + 1f) continue;
                    if (LootPlacement.NearDoor(Plan, x, y, z)) continue;
                    bool crowded = false;
                    foreach (var p in Plan.LootPoints) if (p.Room == "Roof" && Math.Abs(p.X - x) < 2f && Math.Abs(p.Z - z) < 2f) crowded = true;
                    if (crowded) continue;
                    AddLoot(x, y, z, "Roof", false);
                    placed++;
                }
            }

            void AddLoot(float x, float y, float z, string room, bool onProp) =>
                Plan.LootPoints.Add(new BuildingPoint { Name = "Loot" + Plan.LootPoints.Count, X = x, Y = y, Z = z, Room = room, OnProp = onProp });

            // A floor slab carries the point and nothing stands within a capsule radius of it.
            bool FloorIsFree(float x, float y, float z)
            {
                bool carried = false;
                foreach (var p in Plan.Pieces)
                {
                    if (p.Kind == SurfaceKind.Floor && Math.Abs(p.MaxY - y) <= 0.01f && p.FootprintContains(x, z, -0.3f)) carried = true;
                    if (p.MinY < y + 1f && p.MaxY > y + 0.02f && p.FootprintDistance(x, z) < 0.4f) return false;
                }
                return carried;
            }

            public void Finish()
            {
                var plan = Plan;
                if (_mirror) MirrorX(plan);
                float height = 0f;
                foreach (var p in plan.Pieces) height = Math.Max(height, p.MaxY);
                plan.Height = height;
                plan.LootPoints.RemoveAll(p => LootPlacement.NearDoor(plan, p.X, p.Y, p.Z)); // backstop: the placers above already skip these

                foreach (var door in plan.Doors)
                {
                    float reach = door.Depth / 2 + 0.8f;
                    plan.NavHints.Add(new BuildingPoint { Name = door.Name + "In", X = door.X - door.ForwardX * reach, Y = door.Y, Z = door.Z - door.ForwardZ * reach, Room = door.Room });
                    plan.NavHints.Add(new BuildingPoint
                    {
                        Name = door.Name + "Out", X = door.X + door.ForwardX * reach, Y = door.Exterior && door.Depth > 0f ? 0f : door.Y,
                        Z = door.Z + door.ForwardZ * reach, Room = door.OtherRoom,
                    });
                }
                foreach (var stair in plan.Stairs)
                {
                    plan.NavHints.Add(new BuildingPoint { Name = stair.Name + "Foot", X = stair.X, Y = stair.BaseY, Z = stair.Z + 0.6f });
                    plan.NavHints.Add(new BuildingPoint { Name = stair.Name + "Head", X = stair.X, Y = stair.TopY, Z = stair.Z - stair.Length - 0.6f });
                }
            }

            // Flips the plan left to right: free variation without a second code path.
            static void MirrorX(BuildingPlan plan)
            {
                for (int i = 0; i < plan.Pieces.Count; i++) { var p = plan.Pieces[i]; p.X = -p.X; plan.Pieces[i] = p; }
                for (int i = 0; i < plan.Doors.Count; i++) plan.Doors[i] = Mirrored(plan.Doors[i]);
                for (int i = 0; i < plan.Windows.Count; i++) plan.Windows[i] = Mirrored(plan.Windows[i]);
                for (int i = 0; i < plan.Rooms.Count; i++)
                {
                    var r = plan.Rooms[i];
                    float min = -r.MaxX, max = -r.MinX;
                    r.MinX = min; r.MaxX = max;
                    plan.Rooms[i] = r;
                }
                for (int i = 0; i < plan.Stairs.Count; i++) { var s = plan.Stairs[i]; s.X = -s.X; plan.Stairs[i] = s; }
                for (int i = 0; i < plan.LootPoints.Count; i++) { var p = plan.LootPoints[i]; p.X = -p.X; plan.LootPoints[i] = p; }
                for (int i = 0; i < plan.Decor.Count; i++) { var p = plan.Decor[i]; p.X = -p.X; plan.Decor[i] = p; }
                for (int i = 0; i < plan.Lamps.Count; i++) { var p = plan.Lamps[i]; p.X = -p.X; plan.Lamps[i] = p; }
                plan.SpawnX = -plan.SpawnX;
            }

            static BuildingOpening Mirrored(BuildingOpening o)
            {
                o.X = -o.X;
                o.FacingDeg = (360f - o.FacingDeg) % 360f;
                return o;
            }
        }
    }
}
