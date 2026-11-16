using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Shop, Villa and Apartment (#249 T8, G2): 2 to 5 storeys. Every floor is a left and a right room split by one wall. The
    // stairs are straight flights along an outer wall, left on even storeys and right on odd ones, so two flights never stand
    // above each other and every flight needs only a floor opening of its own. Shops, villas and apartments may have a flight
    // on to the roof, with a stair house over the opening and a parapet around the deck.
    public static partial class Buildings
    {
        const float RoofParapet = 1.0f, StairHouseHeight = 2.6f;

        static void Block(Draft d, BuildingKind kind)
        {
            const float t = OuterWall, ti = InnerWall;
            var rng = d.Rng;
            var plan = d.Plan;
            float hw = d.Hw, hd = d.Hd, floor = FloorTop;
            float xl = -hw + t, xr = hw - t, zb = -hd + t, zf = hd - t;
            BuildingKinds.Storeys(kind, out int minStoreys, out int maxStoreys);
            int storeys = minStoreys + rng.Int(maxStoreys - minStoreys + 1);
            bool roofAccess = BuildingKinds.RoofAccess(kind);
            plan.Storeys = storeys;
            d.Box("Slab", SurfaceKind.Floor, -hw, hw, 0f, floor, -hd, hd);

            float zoneMin = StairWidth + PropSpacing + 0.9f;        // a stair hall wide enough for the flight and its clear strip
            float xs = rng.Pick(xl + zoneMin + ti / 2, xr - zoneMin - ti / 2);
            float zTop = zb + 1.3f;
            float zFoot = zTop + StairLength(StoreyHeight);
            int flights = roofAccess ? storeys : storeys - 1;

            float StairX(int k) => k % 2 == 0 ? xl : xr - StairWidth;
            float YOf(int s) => floor + s * StoreyHeight;

            // Flights, the floors they climb to (a slab with an opening over the flight) and the clear strips beside them.
            for (int k = 0; k < flights; k++)
            {
                bool left = k % 2 == 0;
                float x0 = StairX(k), x1 = x0 + StairWidth;
                float yLow = YOf(k), yHigh = YOf(k + 1);
                d.Stairs("Stair" + k, x0, zFoot, yLow, yHigh);
                var keep = left ? new Rect(xl, x1 + PropSpacing, zb, zf) : new Rect(x0 - PropSpacing, xr, zb, zf);
                d.Clear[k].Add(keep);
                d.Clear[k + 1].Add(keep);

                string tag = k + 1 == storeys ? "Roof" : "S" + (k + 1);
                var slab = k + 1 == storeys ? SurfaceKind.Roof : SurfaceKind.Floor;   // the deck is roof: it is outdoors
                float yb = yHigh - SlabThickness;
                // the slab around the opening; the landing beyond the head of the flight and the strip in front of its foot
                d.Box(tag + "Floor", slab, left ? x1 : xl, left ? xr : x0, yb, yHigh, zb, zf);
                d.Box(tag + "Landing", slab, x0, x1, yb, yHigh, zb, zTop);
                d.Box(tag + "FloorFront", slab, x0, x1, yb, yHigh, zFoot, zf);
                float railX = left ? x1 : x0 - 0.1f;
                d.Box(tag + "RailSide", SurfaceKind.Trim, railX, railX + 0.1f, yHigh, yHigh + 1f, zTop, zFoot + 0.1f);
                d.Box(tag + "RailEnd", SurfaceKind.Trim, x0, x1, yHigh, yHigh + 1f, zFoot, zFoot + 0.1f);
            }

            var all = new List<BuildingRoom>();
            var levels = new List<int>();
            for (int s = 0; s < storeys; s++)
            {
                float y0 = YOf(s), yCeil = y0 + StoreyClear;
                string tag = "S" + s;
                var left = d.Room(tag + "Left", s, xl, xs - ti / 2, zb, zf, y0, yCeil);
                var right = d.Room(tag + "Right", s, xs + ti / 2, xr, zb, zf, y0, yCeil);
                var rooms = new List<BuildingRoom> { left, right };

                // the door between the two rooms, away from the walls
                var mainGaps = new List<Gap>();
                float doorZ = rng.Pick(zb + 0.3f + DoorWidth / 2 + 0.4f, zf - 0.3f - DoorWidth / 2 - 0.4f);
                d.InnerDoor(mainGaps, false, xs, doorZ, y0, left.Name, right.Name, s, tag + "DoorMain");
                d.Wall(tag + "Main", SurfaceKind.WallInner, false, xs, zb, zf, y0, yCeil, ti, mainGaps);

                // the room that holds the flight up from this floor, and the wall it runs along
                bool stairLeft = s % 2 == 0;
                bool hasFlightUp = s < flights;
                var outer = NewGaps();
                for (int r = 0; r < 2; r++)
                    for (int sd = 0; sd < 4; sd++)
                    {
                        var side = (Side)sd;
                        if (!d.Touches(rooms[r], side, t)) continue;
                        if (hasFlightUp && ((r == 0) == stairLeft) && (side == (stairLeft ? Side.Left : Side.Right))) continue;
                        bool door = s == 0 && ((r == 1 && side == Side.Front) || (r == 1 && side == Side.Right));
                        bool alongX = side == Side.Front || side == Side.Back;
                        float a0 = alongX ? rooms[r].MinX : rooms[r].MinZ, a1 = alongX ? rooms[r].MaxX : rooms[r].MaxZ;
                        d.FillSegment(outer, side, a0, a1, door, rooms[r].Name, s, y0, t);
                    }
                bool top = s == storeys - 1;
                d.Shell(tag, outer, y0, top ? yCeil : yCeil + SlabThickness, t);
                foreach (var room in rooms) { all.Add(room); levels.Add(s); }
            }

            float roofY = floor + storeys * StoreyHeight - SlabThickness;
            if (!roofAccess) d.FlatRoof(roofY, SlabThickness, RoofOverhang, true);
            else RoofDeck(d, flights - 1, StairX(flights - 1), xl, xr, zb, zTop, zFoot, zf, YOf(storeys), hw, hd);

            // Roles and furniture: a shop floor below, homes above; one bath on the top floor.
            for (int i = 0; i < all.Count; i++)
            {
                var room = all[i];
                int s = levels[i];
                bool second = i % 2 == 1;
                RoomRole role = s == 0 ? (kind == BuildingKind.Shop ? (second ? RoomRole.Office : RoomRole.Storage) : (second ? RoomRole.Kitchen : RoomRole.Living))
                    : (second && s == storeys - 1 ? RoomRole.Bath : second && s == 1 ? RoomRole.Kitchen : s == 1 && !second ? RoomRole.Living : RoomRole.Bedroom);
                d.SetRole(room.Name, role);
                int n = room.Area >= 30f ? 6 : room.Area >= 18f ? 5 : room.Area >= 10f ? 4 : 3;
                if (s > 0) n = Math.Min(n, 4);
                switch (role)
                {
                    case RoomRole.Living:
                        d.Furnish(room, s, n, LivingSet, 2);
                        if (room.Area >= 12f) d.Rug(room, DecorMat.Fabric2);
                        break;
                    case RoomRole.Kitchen: d.Furnish(room, s, n, KitchenSet, 1); break;
                    case RoomRole.Bath: d.Furnish(room, s, 2, BathSet, 2); break;
                    case RoomRole.Office: d.Furnish(room, s, n, OfficeSet, 2); break;
                    case RoomRole.Storage: d.Furnish(room, s, n + 1, StorageSet, 2); break;
                    default:
                        d.Furnish(room, s, n, BedroomSet, 2);
                        if (room.Area >= 12f) d.Rug(room, DecorMat.Fabric);
                        break;
                }
                d.Lamp(room, (room.MinX + room.MaxX) * 0.5f, (room.MinZ + room.MaxZ) * 0.5f, false, false, 6f, 5.5f, false);
            }
            foreach (var room in all)
                d.Loot(room.Name, new Rect(room.MinX, room.MaxX, room.MinZ, room.MaxZ), room.FloorY, Clamp((int)(room.Area / 6f), 2, room.Storey == 0 ? 8 : 5), null);
            d.Curtains();
            for (int i = 0; i < all.Count; i++)
                if (all[i].Role != RoomRole.Bath) d.Clutter(all[i], levels[i], Clamp((int)(all[i].Area / 9f), 1, 4));
        }

        // The roof deck of a block with roof access: parapet on the outer edge, a stair house over the head of the last flight
        // with a doorway out on to the deck.
        static void RoofDeck(Draft d, int k, float x0, float xl, float xr, float zb, float zTop, float zFoot, float zf, float deckY, float hw, float hd)
        {
            bool left = k % 2 == 0;
            float x1 = x0 + StairWidth;
            float top = deckY;
            d.Box("ParapetFront", SurfaceKind.Trim, -hw, hw, top, top + RoofParapet, hd - 0.2f, hd);
            d.Box("ParapetBack", SurfaceKind.Trim, -hw, hw, top, top + RoofParapet, -hd, -hd + 0.2f);
            d.Box("ParapetRight", SurfaceKind.Trim, hw - 0.2f, hw, top, top + RoofParapet, -hd + 0.2f, hd - 0.2f);
            d.Box("ParapetLeft", SurfaceKind.Trim, -hw, -hw + 0.2f, top, top + RoofParapet, -hd + 0.2f, hd - 0.2f);

            // walls of the stair house on the deck: the side facing the roof (with the doorway beside the landing) and the front
            var gaps = new List<Gap> { new Gap { At = (zb + zTop) * 0.5f, Width = DoorWidth, Bottom = top, Top = top + DoorHeight } };
            float wallX = left ? x1 + 0.1f : x0 - 0.1f;
            d.Wall("StairHouseSide", SurfaceKind.WallOuter, false, wallX, zb, zFoot + 0.2f, top, top + StairHouseHeight, 0.2f, gaps);
            d.Wall("StairHouseFront", SurfaceKind.WallOuter, true, zFoot + 0.1f, left ? xl : x0, left ? x1 : xr, top, top + StairHouseHeight, 0.2f, null);
            float rx0 = left ? xl : x0 - 0.2f, rx1 = left ? x1 + 0.2f : xr;
            d.Box("StairHouseRoof", SurfaceKind.Roof, rx0, rx1, top + StairHouseHeight, top + StairHouseHeight + SlabThickness, zb, zFoot + 0.2f);
        }
    }
}
