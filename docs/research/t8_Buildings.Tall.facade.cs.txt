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
                        FacadeOpenings(d, outer, side, a0, a1, door, rooms[r].Name, s, y0, t, kind == BuildingKind.Shop && s == 0 && side == Side.Front);
                    }
                bool top = s == storeys - 1;
                d.Shell(tag, outer, y0, top ? yCeil : yCeil + SlabThickness, t);
                foreach (var room in rooms) { all.Add(room); levels.Add(s); }
            }

            float roofY = floor + storeys * StoreyHeight - SlabThickness;
            if (!roofAccess) d.FlatRoof(roofY, SlabThickness, RoofOverhang, true);
            else RoofDeck(d, flights - 1, StairX(flights - 1), xl, xr, zb, zTop, zFoot, zf, YOf(storeys), hw, hd);

            Facade(d, kind, storeys, floor, roofAccess, StairX(flights - 1), hw, hd);

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

        // Windows in a regular rhythm: one every 3 m along the wall, the same on every floor, so the facade reads as bays. A door takes the place
        // of the windows near it; a shopfront has large windows (sill 0.9, 1.45 high) in wider bays.
        static void FacadeOpenings(Draft d, List<Gap>[] gaps, Side side, float a0, float a1, bool door, string room, int storey, float floorY, float t, bool shopfront)
        {
            float length = a1 - a0;
            float doorAt = float.NaN;
            if (door)
            {
                doorAt = d.Rng.Pick(a0 + 0.4f + DoorWidth / 2, a1 - 0.4f - DoorWidth / 2);
                d.Door(gaps, side, doorAt, DoorWidth, DoorHeight, floorY, room, storey, t, "Door" + side);
            }
            float pitch = shopfront ? 3.4f : 3.0f, width = shopfront ? 2.2f : WindowWidth, height = shopfront ? 1.45f : WindowHeight;
            int bays = (int)(length / pitch);
            for (int i = 0; i < bays; i++)
            {
                float at = a0 + (i + 0.5f) * length / bays;
                if (!float.IsNaN(doorAt) && Math.Abs(at - doorAt) < (width + DoorWidth) / 2 + 1.0f) continue;
                d.Window(gaps, side, at, width, floorY + WindowSill - (shopfront ? 0.1f : 0f), height, room, storey, t);
            }
        }

        // Everything on the outside that is not a wall: bands at every floor, a cornice under the roof deck and a cap on the parapet, balconies,
        // an entrance canopy (an awning and a blank sign panel on a shop), and a water tank and an antenna on the roof. Pieces named
        // Balcony/Canopy/Awning/Sign may stand out 1.5 m from the wall (BuildingRules.MaxAttachment).
        static void Facade(Draft d, BuildingKind kind, int storeys, float floor, bool roofAccess, float roofStairX, float hw, float hd)
        {
            float deck = floor + storeys * StoreyHeight;
            const float bandOut = 0.08f;
            // string courses under every upper floor and the cornice under the deck
            for (int s = 1; s <= storeys; s++)
            {
                float y = floor + s * StoreyHeight;
                bool cornice = s == storeys;
                float out_ = cornice ? 0.3f : bandOut, h = cornice ? 0.3f : 0.22f;
                float x = hw + out_, z = hd + out_;
                d.Box("BandFront" + s, SurfaceKind.Trim, -x, x, y - SlabThickness - h + 0.2f, y - SlabThickness + 0.2f, hd, z);
                d.Box("BandBack" + s, SurfaceKind.Trim, -x, x, y - SlabThickness - h + 0.2f, y - SlabThickness + 0.2f, -z, -hd);
                d.Box("BandRight" + s, SurfaceKind.Trim, hw, x, y - SlabThickness - h + 0.2f, y - SlabThickness + 0.2f, -hd, hd);
                d.Box("BandLeft" + s, SurfaceKind.Trim, -x, -hw, y - SlabThickness - h + 0.2f, y - SlabThickness + 0.2f, -hd, hd);
            }
            // a cap on the parapet
            float cap = deck + (roofAccess ? RoofParapet : 0.4f);
            d.Box("CapFront", SurfaceKind.Trim, -hw - 0.1f, hw + 0.1f, cap, cap + 0.1f, hd - 0.3f, hd + 0.1f);
            d.Box("CapBack", SurfaceKind.Trim, -hw - 0.1f, hw + 0.1f, cap, cap + 0.1f, -hd - 0.1f, -hd + 0.3f);
            d.Box("CapRight", SurfaceKind.Trim, hw - 0.3f, hw + 0.1f, cap, cap + 0.1f, -hd + 0.3f, hd - 0.3f);
            d.Box("CapLeft", SurfaceKind.Trim, -hw - 0.1f, -hw + 0.3f, cap, cap + 0.1f, -hd + 0.3f, hd - 0.3f);

            // balconies on the upper floors of villas and apartments: one under every second window on the front and the back
            if (kind == BuildingKind.Villa || kind == BuildingKind.Apartment)
                for (int s = 1; s < storeys; s++)
                {
                    float y0 = floor + s * StoreyHeight;
                    foreach (var side in new[] { 1f, -1f })
                    {
                        var windows = new List<float>();
                        foreach (var w in d.Plan.Windows)
                            if (w.Storey == s && Math.Abs(w.Z - side * (hd - OuterWall / 2)) < 0.05f) windows.Add(w.X);
                        windows.Sort();
                        for (int i = 0; i < windows.Count; i += 2)
                        {
                            float cx = windows[i];
                            float z0 = side > 0 ? hd : -hd - 1.1f, z1 = side > 0 ? hd + 1.1f : -hd;
                            d.Box("Balcony" + s + (side > 0 ? "F" : "B") + i, SurfaceKind.Trim, cx - 1.2f, cx + 1.2f, y0 - 0.2f, y0 - 0.02f, z0, z1);
                            // railing: front bar and the two ends, 0.95 m high
                            float zr0 = side > 0 ? z1 - 0.06f : z0, zr1 = side > 0 ? z1 : z0 + 0.06f;
                            d.Box("BalconyRail" + s + (side > 0 ? "F" : "B") + i, SurfaceKind.Trim, cx - 1.2f, cx + 1.2f, y0 - 0.02f, y0 + 0.93f, zr0, zr1);
                            d.Box("BalconyRailL" + s + (side > 0 ? "F" : "B") + i, SurfaceKind.Trim, cx - 1.2f, cx - 1.14f, y0 - 0.02f, y0 + 0.93f, z0, z1);
                            d.Box("BalconyRailR" + s + (side > 0 ? "F" : "B") + i, SurfaceKind.Trim, cx + 1.14f, cx + 1.2f, y0 - 0.02f, y0 + 0.93f, z0, z1);
                        }
                    }
                }

            // the entrance: a canopy over the front door, on a shop a wide awning with a blank sign panel above the windows
            foreach (var door in d.Plan.Doors)
            {
                if (!door.Exterior || door.Name != "DoorFront") continue;
                float y = door.Y + DoorHeight + 0.15f;
                if (kind == BuildingKind.Shop)
                {
                    d.Box("AwningFront", SurfaceKind.Trim, -hw + 0.8f, hw - 0.8f, y - 0.05f, y + 0.07f, hd, hd + 1.1f);
                    d.Box("SignPanel", SurfaceKind.Trim, -hw + 1.6f, hw - 1.6f, y + 0.12f, y + 0.62f, hd, hd + 0.1f);
                }
                else d.Box("CanopyFront", SurfaceKind.Trim, door.X - 1.5f, door.X + 1.5f, y, y + 0.12f, hd, hd + 1.2f);
            }

            if (!roofAccess) return;
            // on the roof: a water tank on legs in the corner away from the stair house, a short antenna in the front corner on the same side
            float tankSide = roofStairX < 0f ? 1f : -1f;
            float tx = tankSide * (hw - 1.4f), tz = -(hd - 1.4f);
            for (int i = 0; i < 4; i++)
                d.Box("TankLeg" + i, SurfaceKind.Trim, tx + (i % 2 == 0 ? -0.55f : 0.45f), tx + (i % 2 == 0 ? -0.45f : 0.55f), deck, deck + 0.7f, tz + (i / 2 == 0 ? -0.55f : 0.45f), tz + (i / 2 == 0 ? -0.45f : 0.55f));
            d.Box("TankBody", SurfaceKind.Trim, tx - 0.7f, tx + 0.7f, deck + 0.7f, deck + 1.9f, tz - 0.7f, tz + 0.7f);
            d.Box("TankLid", SurfaceKind.Trim, tx - 0.75f, tx + 0.75f, deck + 1.9f, deck + 1.98f, tz - 0.75f, tz + 0.75f);
            float ax = tankSide * (hw - 1.0f), az = hd - 1.0f;
            d.Box("AntennaPole", SurfaceKind.Trim, ax - 0.04f, ax + 0.04f, deck, deck + 4.2f, az - 0.04f, az + 0.04f);
            d.Box("AntennaBarLow", SurfaceKind.Trim, ax - 0.5f, ax + 0.5f, deck + 3.2f, deck + 3.26f, az - 0.03f, az + 0.03f);
            d.Box("AntennaBarHigh", SurfaceKind.Trim, ax - 0.35f, ax + 0.35f, deck + 3.7f, deck + 3.76f, az - 0.03f, az + 0.03f);
        }
    }
}