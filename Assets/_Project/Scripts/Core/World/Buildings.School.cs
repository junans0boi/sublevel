using System;
using System.Collections.Generic;

namespace Game.Core
{
    // School (#249 T8): a long building of 2-3 storeys. A stair hall at each end, a corridor along the middle and classrooms on both sides of it, each with
    // a door on the corridor. The flights alternate between the two end halls like the other tall kinds; the roof is flat and closed.
    public static partial class Buildings
    {
        static readonly Spec[] ClassroomSet =
        {
            new Spec(PropKind.Desk, 1.4f, 1.3f, 0.75f), new Spec(PropKind.Bookcase, 1.0f, 0.35f, 1.9f),
            new Spec(PropKind.Locker, 1.2f, 0.5f, 1.9f), new Spec(PropKind.Table, 1.4f, 1.4f, 0.8f),
        };
        static readonly Spec[] CorridorSet = { new Spec(PropKind.Locker, 1.2f, 0.5f, 1.9f), new Spec(PropKind.Bookcase, 1.0f, 0.35f, 1.9f) };

        static void School(Draft d)
        {
            const float t = OuterWall, ti = InnerWall, hall = 3.6f, corr = 1.25f;
            var rng = d.Rng;
            var plan = d.Plan;
            float hw = d.Hw, hd = d.Hd, floor = FloorTop;
            float xl = -hw + t, xr = hw - t, zb = -hd + t, zf = hd - t;
            BuildingKinds.Storeys(BuildingKind.School, out int minS, out int maxS);
            int storeys = minS + rng.Int(maxS - minS + 1);
            plan.Storeys = storeys;
            d.Box("Slab", SurfaceKind.Floor, -hw, hw, 0f, floor, -hd, hd);

            float xa = xl + hall, xb = xr - hall;                    // centres of the two walls between the halls and the middle
            float midStart = xa + ti / 2, midEnd = xb - ti / 2;
            int n = Math.Max(2, (int)((midEnd - midStart) / 6.2f));
            float cw = (midEnd - midStart) / n;
            float zTop = zb + 1.3f, zFoot = zTop + StairLength(StoreyHeight);
            int flights = storeys - 1;
            float YOf(int s) => floor + s * StoreyHeight;
            float StairX(int k) => k % 2 == 0 ? xl : xr - StairWidth;

            for (int k = 0; k < flights; k++)
            {
                bool left = k % 2 == 0;
                float x0 = StairX(k), x1 = x0 + StairWidth;
                float yLow = YOf(k), yHigh = YOf(k + 1);
                d.Stairs("Stair" + k, x0, zFoot, yLow, yHigh);
                var keep = left ? new Rect(xl, x1 + PropSpacing, zb, zf) : new Rect(x0 - PropSpacing, xr, zb, zf);
                d.Clear[k].Add(keep);
                d.Clear[k + 1].Add(keep);
                string tag = "S" + (k + 1);
                float yb = yHigh - SlabThickness;
                d.Box(tag + "Floor", SurfaceKind.Floor, left ? x1 : xl, left ? xr : x0, yb, yHigh, zb, zf);
                d.Box(tag + "Landing", SurfaceKind.Floor, x0, x1, yb, yHigh, zb, zTop);
                d.Box(tag + "FloorFront", SurfaceKind.Floor, x0, x1, yb, yHigh, zFoot, zf);
                float railX = left ? x1 : x0 - 0.1f;
                d.Box(tag + "RailSide", SurfaceKind.Trim, railX, railX + 0.1f, yHigh, yHigh + 1f, zTop, zFoot + 0.1f);
                d.Box(tag + "RailEnd", SurfaceKind.Trim, x0, x1, yHigh, yHigh + 1f, zFoot, zFoot + 0.1f);
            }

            var all = new List<BuildingRoom>();
            for (int s = 0; s < storeys; s++)
            {
                float y0 = YOf(s), yCeil = y0 + StoreyClear;
                string tag = "S" + s;
                var hallL = d.Room(tag + "HallL", s, xl, xa - ti / 2, zb, zf, y0, yCeil);
                var hallR = d.Room(tag + "HallR", s, xb + ti / 2, xr, zb, zf, y0, yCeil);
                var corridor = d.Room(tag + "Corridor", s, midStart, midEnd, -corr, corr, y0, yCeil);
                var rooms = new List<BuildingRoom> { hallL, hallR };
                var classrooms = new List<BuildingRoom>();
                float zFrontIn = corr + ti, zBackIn = -corr - ti;
                for (int i = 0; i < n; i++)
                {
                    float cx0 = midStart + i * cw + (i == 0 ? 0f : ti / 2), cx1 = midStart + (i + 1) * cw - (i == n - 1 ? 0f : ti / 2);
                    classrooms.Add(d.Room(tag + "ClassF" + i, s, cx0, cx1, zFrontIn, zf, y0, yCeil));
                    classrooms.Add(d.Room(tag + "ClassB" + i, s, cx0, cx1, zb, zBackIn, y0, yCeil));
                }

                // hall walls with a door to the corridor, the walls along the corridor with a door for every classroom, the dividers between classrooms
                foreach (var (wx, lowRoom, highRoom, name) in new[] { (xa, hallL.Name, corridor.Name, "DoorHallL"), (xb, corridor.Name, hallR.Name, "DoorHallR") })
                {
                    var gaps = new List<Gap>();
                    d.InnerDoor(gaps, false, wx, 0f, y0, lowRoom, highRoom, s, tag + name);
                    d.Wall(tag + "Hall" + name, SurfaceKind.WallInner, false, wx, zb, zf, y0, yCeil, ti, gaps);
                }
                var frontGaps = new List<Gap>(); var backGaps = new List<Gap>();
                for (int i = 0; i < n; i++)
                {
                    float at = midStart + (i + 0.5f) * cw;
                    d.InnerDoor(frontGaps, true, corr + ti / 2, at, y0, corridor.Name, tag + "ClassF" + i, s, tag + "DoorF" + i);
                    d.InnerDoor(backGaps, true, -corr - ti / 2, at, y0, tag + "ClassB" + i, corridor.Name, s, tag + "DoorB" + i);
                }
                d.Wall(tag + "CorrF", SurfaceKind.WallInner, true, corr + ti / 2, midStart, midEnd, y0, yCeil, ti, frontGaps);
                d.Wall(tag + "CorrB", SurfaceKind.WallInner, true, -corr - ti / 2, midStart, midEnd, y0, yCeil, ti, backGaps);
                for (int i = 1; i < n; i++)
                {
                    float wx = midStart + i * cw;
                    d.Wall(tag + "DivF" + i, SurfaceKind.WallInner, false, wx, zFrontIn, zf, y0, yCeil, ti, null);
                    d.Wall(tag + "DivB" + i, SurfaceKind.WallInner, false, wx, zb, zBackIn, y0, yCeil, ti, null);
                }

                // outside: the entrance in the left hall's front, a second door in the right hall's back; windows in a rhythm on every classroom wall
                bool flightLeft = s % 2 == 0, hasFlight = s < flights;
                var outer = NewGaps();
                var touching = new List<BuildingRoom>(rooms); touching.AddRange(classrooms);
                foreach (var room in touching)
                    for (int sd = 0; sd < 4; sd++)
                    {
                        var side = (Side)sd;
                        if (!d.Touches(room, side, t)) continue;
                        bool isHallL = room.Name == hallL.Name, isHallR = room.Name == hallR.Name;
                        if (hasFlight && ((isHallL && flightLeft && side == Side.Left) || (isHallR && !flightLeft && side == Side.Right))) continue;
                        bool door = s == 0 && ((isHallL && side == Side.Front) || (isHallR && side == Side.Back));
                        bool alongX = side == Side.Front || side == Side.Back;
                        float a0 = alongX ? room.MinX : room.MinZ, a1 = alongX ? room.MaxX : room.MaxZ;
                        FacadeOpenings(d, outer, side, a0, a1, door, room.Name, s, y0, t, false);
                    }
                d.Shell(tag, outer, y0, s == storeys - 1 ? yCeil : yCeil + SlabThickness, t);
                all.Add(hallL); all.Add(hallR); all.Add(corridor); all.AddRange(classrooms);
            }

            float roofY = floor + storeys * StoreyHeight - SlabThickness;
            d.FlatRoof(roofY, SlabThickness, RoofOverhang, true);
            Facade(d, BuildingKind.School, storeys, floor, false, 0f, hw, hd);

            foreach (var room in all)
            {
                int s = room.Storey;
                bool isClass = room.Name.Contains("Class");
                bool isCorridor = room.Name.EndsWith("Corridor");
                d.SetRole(room.Name, isClass ? RoomRole.Office : RoomRole.None);
                if (isClass)
                {
                    d.Furnish(room, s, 3, ClassroomSet, 1);
                    d.Centre(room, s, new Spec(PropKind.Table, 1.4f, 1.4f, 0.8f));
                }
                else if (isCorridor) d.Furnish(room, s, 3, CorridorSet, 1);
                if (isCorridor) d.HallLamps(room, 7f, 5);
                else d.Lamp(room, (room.MinX + room.MaxX) * 0.5f, (room.MinZ + room.MaxZ) * 0.5f, false, false, 6f, 5.5f, false);
            }
            foreach (var room in all)
                d.Loot(room.Name, new Rect(room.MinX, room.MaxX, room.MinZ, room.MaxZ), room.FloorY, room.Name.Contains("Class") ? Clamp((int)(room.Area / 9f), 2, 4) : 2, null);
            d.Curtains();
            foreach (var room in all)
                if (room.Name.Contains("Class")) d.Clutter(room, room.Storey, 1);
        }
    }
}
