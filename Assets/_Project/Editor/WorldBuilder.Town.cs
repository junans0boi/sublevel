using System.Linq;
using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Town detail around and on every building (#207, ADR-152): roofs with a shape, window frames and shutters, doorsteps and
// awnings, signs, rooftop equipment, fences, mailboxes, street lamps, benches, bins, a water tank, a billboard, rubble.
// All of it is generated boxes, prisms and cylinders merged per region and material by Dressing; no text and no brand
// anywhere (signs are plain painted plates). Parts you can bump into get a collider, the rest is show only.
public static partial class WorldBuilder
{
    static void DressTown(Dressing d, WorldPlan plan, BuildingLot lot, Placed placed, uint h, int seed)
    {
        var b = placed.Plan;
        int region = lot.Region;
        float hx = b.Width * 0.5f, hz = b.Depth * 0.5f;
        var family = BuildingKinds.Family(lot.Kind);
        bool house = family == BuildingKind.House, shed = family == BuildingKind.Shed;
        uint style = Hash(seed, 7);
        int accent = (int)(Hash((int)h, 91) % 5u);
        bool lightTrim = R(2) < 0.65f;
        var trim = WorldMaterials.Trim(lightTrim);
        var accentMat = WorldMaterials.Accent(accent);
        var q = placed.Rotation;
        Vector3 L(float x, float y, float z) => placed.Origin + q * new Vector3(x, y, z);
        Vector3 Ground(float lx, float lz) { var p = placed.Origin + q * new Vector3(lx, 0f, lz); return d.OnGround(p.x, p.z); }
        float R(int k) => Unit(Hash((int)h, 1000 + k), 0);
        void PartV(WorldMaterials.Surface s, Vector3 c, Vector3 size, int skip = 0, bool shadow = false) => d.Visual(region, s, L(c.x, c.y, c.z), size, q, skip, shadow);
        // the face of a part on a wall that looks into that wall (its side toward -facing), left out of the mesh
        int AgainstWall(BuildingOpening o) => 1 << (o.ForwardZ > 0.5f ? 3 : o.ForwardZ < -0.5f ? 2 : o.ForwardX > 0.5f ? 5 : 4);
        void Part(WorldMaterials.Surface s, float cx, float cy, float cz, float sx, float sy, float sz, bool shadow = false) => d.Visual(region, s, L(cx, cy, cz), new Vector3(sx, sy, sz), q, 0, shadow);

        // ---- roof ----
        var roof = b.Pieces.FirstOrDefault(p => p.Name == "Roof");
        float roofTop = roof.SizeY > 0f ? roof.MaxY : b.Height;
        float parapet = house ? 0.4f : 0f;
        bool alongX = b.Width >= b.Depth;
        float across = (alongX ? b.Depth : b.Width) + 2f * RoofOverhangLocal + 0.3f, ridgeLen = (alongX ? b.Width : b.Depth) + 2f * RoofOverhangLocal + 0.3f;
        var ridgeRot = q * Quaternion.Euler(0f, alongX ? 90f : 0f, 0f);
        bool gable = (house && R(4) < 0.62f) || shed;
        float ridgeTop = roofTop + parapet;
        if (gable)
        {
            float ridgeH = Mathf.Min(shed ? 1.3f : 2.8f, across * (shed ? 0.2f : 0.27f));
            float baseY = roofTop + (house ? parapet - 0.05f : 0.02f);
            var roofSurface = SurfaceFor(lot.Kind, new BuildingPiece { Kind = SurfaceKind.Roof }, style);
            var wallSurface = SurfaceFor(lot.Kind, new BuildingPiece { Kind = SurfaceKind.WallOuter }, style);
            d.VisualPrism(region, roofSurface, L(0f, baseY, 0f), new Vector3(across, ridgeH, ridgeLen), ridgeRot, true, false);
            d.VisualPrism(region, wallSurface, L(0f, baseY, 0f), new Vector3(across, ridgeH, ridgeLen), ridgeRot, false, true);
            // fascia boards along both eaves and a ridge cap
            float eave = across * 0.5f;
            for (int e = -1; e <= 1; e += 2)
                d.Visual(region, trim, L(alongX ? 0f : e * eave, baseY + 0.04f, alongX ? e * eave : 0f), alongX ? new Vector3(ridgeLen, 0.12f, 0.1f) : new Vector3(0.1f, 0.12f, ridgeLen), q);
            d.Visual(region, trim, L(0f, baseY + ridgeH + 0.02f, 0f), alongX ? new Vector3(ridgeLen, 0.1f, 0.22f) : new Vector3(0.22f, 0.1f, ridgeLen), q);
            ridgeTop = baseY + ridgeH;
            if (house && R(6) < 0.7f)
            {
                // a brick chimney through the slope near one gable
                float along = (alongX ? hx : hz) * (R(8) < 0.5f ? -0.6f : 0.6f);
                float cx = alongX ? along : 0f, cz = alongX ? 0f : along;
                Part(WorldMaterials.Brick((int)(h % 5u)), cx, (baseY + ridgeTop) * 0.5f + 0.3f, cz, 0.6f, ridgeTop - baseY + 0.7f, 0.6f, true);
                Part(WorldMaterials.ConcreteDark, cx, ridgeTop + 0.68f, cz, 0.78f, 0.1f, 0.78f);
            }
            if (R(10) < 0.35f)
            {
                // an antenna on the ridge
                var foot = L(alongX ? hx * 0.4f : 0f, ridgeTop, alongX ? 0f : hz * 0.4f);
                d.VisualCylinder(region, WorldMaterials.Wire, foot, 0.025f, 2.6f, 5);
                for (int k = 0; k < 3; k++)
                    d.Visual(region, WorldMaterials.Wire, foot + Vector3.up * (1.5f + k * 0.4f), new Vector3(0.9f - k * 0.2f, 0.02f, 0.02f), ridgeRot * Quaternion.Euler(0f, 90f, 0f));
            }
        }
        else if (!shed)
        {
            // Flat roof with equipment: air-conditioning units, vent pipes, a skylight, sometimes an antenna mast or a tank.
            float x0 = -hx + 1.2f, x1 = hx - 1.2f, z0 = -hz + 1.2f, z1 = hz - 1.2f;
            int units = house ? 1 + (int)(R(11) * 2.2f) : 2 + (int)(R(11) * 3.5f);
            for (int u = 0; u < units && x1 > x0 && z1 > z0; u++)
            {
                uint uh = Hash((int)h, 200 + u);
                float ux = Mathf.Lerp(x0, x1, Unit(uh, 0)), uz = Mathf.Lerp(z0, z1, Unit(uh, 10));
                bool box = Unit(uh, 20) < 0.7f;
                if (box)
                {
                    Part(WorldMaterials.PaintedMetal, ux, ridgeTop + 0.35f, uz, 1.0f, 0.7f, 0.7f);
                    Part(WorldMaterials.ConcreteDark, ux, ridgeTop + 0.72f, uz, 0.8f, 0.05f, 0.5f);
                    Part(WorldMaterials.Wire, ux, ridgeTop + 0.36f, uz + 0.355f, 0.7f, 0.5f, 0.02f);
                }
                else
                {
                    d.VisualCylinder(region, WorldMaterials.RustyMetal, L(ux, ridgeTop, uz), 0.14f, 0.8f, 8);
                    d.VisualCylinder(region, WorldMaterials.RustyMetal, L(ux, ridgeTop + 0.8f, uz), 0.22f, 0.08f, 8);
                }
            }
            if (!house || R(13) < 0.4f)
                Part(WorldMaterials.Decor(DecorMat.Screen), Mathf.Lerp(x0, x1, R(14)), ridgeTop + 0.15f, Mathf.Lerp(z0, z1, R(16)), 1.6f, 0.3f, 1.2f);
            if (R(18) < 0.4f)
            {
                var foot = L(Mathf.Lerp(x0, x1, R(20)), ridgeTop, Mathf.Lerp(z0, z1, R(22)));
                d.VisualCylinder(region, WorldMaterials.Wire, foot, 0.03f, 4.2f, 5);
                d.Visual(region, WorldMaterials.Wire, foot + Vector3.up * 3.6f, new Vector3(1.2f, 0.025f, 0.025f), q);
                d.Visual(region, WorldMaterials.Wire, foot + Vector3.up * 3.9f, new Vector3(0.8f, 0.025f, 0.025f), q);
            }
            else if (!house && R(24) < 0.4f && x1 - x0 > 3f)
            {
                // a roof tank on four legs
                var foot = L(Mathf.Lerp(x0, x1, R(26)), ridgeTop, Mathf.Lerp(z0, z1, R(28)));
                for (int k = 0; k < 4; k++)
                    d.Visual(region, WorldMaterials.RustyMetal, foot + q * new Vector3((k % 2 - 0.5f) * 1.2f, 0.5f, (k / 2 - 0.5f) * 1.2f), new Vector3(0.1f, 1f, 0.1f), q);
                d.VisualCylinder(region, WorldMaterials.PaintedMetal, foot + Vector3.up * 1f, 0.95f, 1.6f, 12, true);
                d.VisualCylinder(region, WorldMaterials.RustyMetal, foot + Vector3.up * 2.6f, 0.4f, 0.12f, 8);
            }
        }

        // ---- windows ----
        foreach (var o in b.Windows)
        {
            if (!o.Exterior) continue;
            float fx = o.ForwardX, fz = o.ForwardZ;
            bool onX = Mathf.Abs(fz) > 0.5f;
            // size of a part by its length along the wall, height and thickness out of the wall
            Vector3 Sz(float along, float height, float thick) => onX ? new Vector3(along, height, thick) : new Vector3(thick, height, along);
            Vector3 At(float along, float outward, float y) =>
                new Vector3(o.X + fx * (o.Depth * 0.5f + outward) + (onX ? along : 0f), y, o.Z + fz * (o.Depth * 0.5f + outward) + (onX ? 0f : along));
            float w = o.Width, hh = o.Height, y0 = o.Y, y1 = o.Y + o.Height;
            PartV(trim, At(0f, 0.03f, y1 + 0.07f), Sz(w + 0.28f, 0.14f, 0.1f), AgainstWall(o));
            for (int s = -1; s <= 1; s += 2)
            {
                PartV(trim, At(s * (w * 0.5f + 0.06f), 0.03f, y0 + hh * 0.5f), Sz(0.12f, hh, 0.1f), AgainstWall(o));
            }
            PartV(trim, At(0f, 0.07f, y0 - 0.035f), Sz(w + 0.3f, 0.07f, 0.2f), AgainstWall(o));
            uint wh = Hash((int)h, 300 + o.Name.Length * 7 + (int)(o.X * 13f + o.Z * 29f));
            if (house && Unit(wh, 0) < 0.45f)
                for (int s = -1; s <= 1; s += 2)
                {
                    PartV(accentMat, At(s * (w * 0.5f + 0.34f), 0.05f, y0 + hh * 0.5f), Sz(0.55f, hh, 0.05f), AgainstWall(o));
                }
        }

        // ---- doors: step, frame, awning or sign ----
        string mainDoor = b.Doors.Where(x => x.Exterior && x.Depth > 0f).Select(x => x.Name).FirstOrDefault();
        foreach (var o in b.Doors)
        {
            if (!o.Exterior || o.Depth <= 0f) continue;
            float fx = o.ForwardX, fz = o.ForwardZ;
            bool onX = Mathf.Abs(fz) > 0.5f;
            Vector3 Sz(float along, float height, float thick) => onX ? new Vector3(along, height, thick) : new Vector3(thick, height, along);
            Vector3 At(float along, float outward, float y) =>
                new Vector3(o.X + fx * (o.Depth * 0.5f + outward) + (onX ? along : 0f), y, o.Z + fz * (o.Depth * 0.5f + outward) + (onX ? 0f : along));
            float w = o.Width;
            bool big = w > 2.5f;
            PartV(WorldMaterials.Concrete, At(0f, 0.45f, o.Y * 0.275f), Sz(w + 0.5f, o.Y * 0.55f, 0.9f), AgainstWall(o) | 2);
            if (!big)
            {
                // frame: two jambs and a lintel
                for (int s = -1; s <= 1; s += 2)
                {
                    PartV(trim, At(s * (w * 0.5f + 0.05f), 0.03f, o.Y + o.Height * 0.5f), Sz(0.1f, o.Height, 0.1f), AgainstWall(o) | 2);
                }
                PartV(trim, At(0f, 0.03f, o.Y + o.Height + 0.06f), Sz(w + 0.2f, 0.12f, 0.1f), AgainstWall(o));
            }
            uint dh = Hash((int)h, 400 + (int)(o.X * 7f + o.Z * 13f));
            if (house && Unit(dh, 0) < 0.6f)
            {
                // an awning on two brackets over the door, striped by an inner band
                PartV(accentMat, At(0f, 0.7f, o.Y + o.Height + 0.38f), Sz(w + 1.0f, 0.08f, 1.4f));
                PartV(WorldMaterials.Trim(true), At(0f, 1.38f, o.Y + o.Height + 0.3f), Sz(w + 1.0f, 0.2f, 0.05f));
                for (int s = -1; s <= 1; s += 2)
                    PartV(WorldMaterials.Wire, At(s * (w * 0.5f + 0.4f), 0.55f, o.Y + o.Height + 0.24f), Sz(0.06f, 0.06f, 1.0f));
            }
            else if (!house && o.Name == mainDoor)
            {
                // a sign plate over the main door: painted panel, a bar and two small marks, no lettering
                float y = o.Y + o.Height + 0.55f, len = Mathf.Max(2.6f, w);
                PartV(accentMat, At(0f, 0.12f, y), Sz(len, 0.7f, 0.12f), AgainstWall(o));
                PartV(WorldMaterials.Trim(true), At(0f, 0.2f, y - 0.12f), Sz(len - 0.4f, 0.12f, 0.06f));
                for (int k = -1; k <= 1; k += 2)
                    PartV(WorldMaterials.Trim(true), At(k * 0.7f, 0.2f, y + 0.1f), Sz(0.5f, 0.28f, 0.06f));
            }
        }

        // ---- yard ----
        float yaw = lot.YawDeg;
        var doors = b.Doors.Where(o => o.Exterior && o.Depth > 0f).ToList();
        bool NearDoor(float lx, float lz, float reach) => doors.Any(o => Mathf.Abs(o.X - lx) < reach && Mathf.Abs(o.Z - lz) < reach);
        bool FreeAt(Vector3 p, float clear) => d.Free(p.x, p.z, 1.2f, clear, lot);

        // Picket fence across the front with a gate gap, beyond the low walls.
        if (house && R(30) < 0.4f)
        {
            float fz = hz + 3.6f;
            var front = doors.FirstOrDefault(o => Mathf.Abs(o.FacingDeg) < 1f);
            float gateAt = front.Width > 0f ? front.X : 0f;
            int segments = Mathf.Max(2, Mathf.RoundToInt((hx * 2f + 5f) / 2.5f));
            float length = (hx * 2f + 5f) / segments;
            for (int s = 0; s < segments; s++)
            {
                float lx = -hx - 2.5f + (s + 0.5f) * length;
                if (Mathf.Abs(lx - gateAt) < 1.8f) continue;
                var at = Ground(lx, fz);
                if (!FreeAt(at, 1.0f)) continue;
                d.Box(region, WorldMaterials.FenceWhite, at, new Vector3(length - 0.04f, 0.9f, 0.05f), yaw, 0.3f);
                d.Visual(region, trim, at + q * new Vector3(0f, 0.92f, 0f), new Vector3(length, 0.07f, 0.1f), q);
                d.Visual(region, trim, at + q * new Vector3(length * 0.5f, 0.5f, 0f), new Vector3(0.1f, 1.0f, 0.1f), q);
            }
        }

        // Mailbox at the front corner of a house.
        if (house && R(32) < 0.55f)
        {
            var at = Ground(hx + 0.8f, hz + 3.1f);
            if (FreeAt(at, 1.0f))
            {
                d.VisualCylinder(region, WorldMaterials.PlanksDark, at - Vector3.up * 0.1f, 0.04f, 1.2f, 5);
                d.Visual(region, accentMat, at + Vector3.up * 1.2f, new Vector3(0.42f, 0.22f, 0.2f), Quaternion.Euler(0f, yaw, 0f));
                d.Visual(region, WorldMaterials.Wire, at + q * new Vector3(0f, 1.25f, 0.1f), new Vector3(0.1f, 0.14f, 0.02f), q);
            }
        }

        // Street lamp on the front side: pole, arm, an emissive head.
        if (!shed && R(34) < 0.7f)
        {
            float side = R(36) < 0.5f ? -1f : 1f;
            float lx = side * (hx + 1.8f), lz = hz + 2.2f;
            var at = Ground(lx, lz);
            if (!NearDoor(lx, lz, 2.5f) && FreeAt(at, 1.2f))
            {
                d.Cylinder(region, WorldMaterials.Wire, at, 0.07f, 4.8f, 0.3f);
                var arm = q * new Vector3(-side * 0.55f, 0f, 0f);
                d.Visual(region, WorldMaterials.Wire, at + Vector3.up * 4.8f + arm, new Vector3(1.1f, 0.07f, 0.07f), q);
                d.Visual(region, WorldMaterials.Decor(DecorMat.Lamp), at + Vector3.up * 4.74f + arm * 2f, new Vector3(0.5f, 0.1f, 0.26f), q);
                d.Visual(region, WorldMaterials.Wire, at + Vector3.up * 4.84f + arm * 2f, new Vector3(0.56f, 0.06f, 0.32f), q);
            }
        }

        // A bench by the front wall, away from the door.
        if (house && R(38) < 0.4f)
        {
            float lx = (R(39) < 0.5f ? -1f : 1f) * (hx * 0.6f);
            var at = Ground(lx, hz + 0.6f);
            if (!NearDoor(lx, hz, 2.6f) && FreeAt(at, 0.8f))
            {
                d.Visual(region, WorldMaterials.PlanksDark, at + Vector3.up * 0.46f, new Vector3(1.6f, 0.06f, 0.45f), q);
                d.Visual(region, WorldMaterials.PlanksDark, at + q * new Vector3(0f, 0.78f, 0.2f), new Vector3(1.6f, 0.4f, 0.05f), q);
                for (int s = -1; s <= 1; s += 2)
                    d.Visual(region, WorldMaterials.Wire, at + q * new Vector3(s * 0.7f, 0.22f, 0f), new Vector3(0.06f, 0.44f, 0.4f), q);
                d.Block(region, at + Vector3.up * 0.35f, new Vector3(1.6f, 0.7f, 0.45f), q);
            }
        }

        // Bins beside a house door, or a dumpster by an industrial building.
        if (R(40) < (house ? 0.5f : 0.6f))
        {
            float lx = (R(41) < 0.5f ? -1f : 1f) * (hx - 1.6f), lz = -hz - 0.7f;
            var at = Ground(lx, lz);
            if (FreeAt(at, 0.8f))
            {
                if (house)
                {
                    d.Cylinder(region, WorldMaterials.BinGreen, at, 0.28f, 0.9f, 0.2f);
                    d.Visual(region, WorldMaterials.Wire, at + Vector3.up * 0.93f, new Vector3(0.6f, 0.06f, 0.6f), q);
                    var at2 = at + q * new Vector3(0.7f, 0f, 0f);
                    d.Cylinder(region, WorldMaterials.PaintedMetal, at2, 0.26f, 0.8f, 0.2f);
                }
                else
                {
                    d.Box(region, WorldMaterials.BinGreen, at, new Vector3(1.9f, 1.2f, 1.1f), yaw, 0.2f);
                    d.Visual(region, WorldMaterials.Wire, at + Vector3.up * 1.22f, new Vector3(2.0f, 0.08f, 0.5f), q);
                    d.Visual(region, accentMat, at + q * new Vector3(0f, 0.6f, 0.56f), new Vector3(1.5f, 0.3f, 0.02f), q);
                }
            }
        }

        // A water tank on a tower beside an industrial building.
        if (!house && !shed && R(44) < 0.4f)
        {
            float lx = (R(45) < 0.5f ? -1f : 1f) * (hx + 2.4f), lz = -hz * 0.3f;
            var at = Ground(lx, lz);
            if (FreeAt(at, 2.0f))
            {
                for (int k = 0; k < 4; k++)
                    d.Visual(region, WorldMaterials.RustyMetal, at + q * new Vector3((k % 2 - 0.5f) * 1.6f, 2f, (k / 2 - 0.5f) * 1.6f), new Vector3(0.14f, 4f, 0.14f), q);
                d.Visual(region, WorldMaterials.RustyMetal, at + Vector3.up * 2.4f, new Vector3(2.0f, 0.08f, 0.08f), q * Quaternion.Euler(0f, 45f, 0f));
                d.Visual(region, WorldMaterials.RustyMetal, at + Vector3.up * 2.4f, new Vector3(2.0f, 0.08f, 0.08f), q * Quaternion.Euler(0f, -45f, 0f));
                d.VisualCylinder(region, WorldMaterials.PaintedMetal, at + Vector3.up * 4f, 1.3f, 2.2f, 14, true);
                d.VisualCylinder(region, WorldMaterials.RustyMetal, at + Vector3.up * 6.2f, 0.5f, 0.15f, 8);
                d.Block(region, at + Vector3.up * 2.5f, new Vector3(1.9f, 5f, 1.9f), q);
            }
        }

        // A billboard on two posts beside a warehouse or hall: a painted face with bars, no lettering.
        if (!house && !shed && R(46) < 0.35f)
        {
            float lx = (R(47) < 0.5f ? -1f : 1f) * (hx * 0.5f), lz = hz + 4.5f;
            var at = Ground(lx, lz);
            if (FreeAt(at, 2.5f))
            {
                for (int s = -1; s <= 1; s += 2)
                    d.Cylinder(region, WorldMaterials.PlanksDark, at + q * new Vector3(s * 2.2f, 0f, 0f), 0.12f, 4.2f, 0.4f);
                d.Visual(region, trim, at + q * new Vector3(0f, 4.4f, 0f), new Vector3(6.2f, 3.2f, 0.18f), q, 0, true);
                d.Visual(region, accentMat, at + q * new Vector3(0f, 4.4f, 0.1f), new Vector3(5.9f, 2.9f, 0.04f), q);
                d.Visual(region, WorldMaterials.Trim(true), at + q * new Vector3(-1f, 4.9f, 0.13f), new Vector3(3.2f, 0.5f, 0.03f), q);
                d.Visual(region, WorldMaterials.Trim(true), at + q * new Vector3(0.5f, 4.1f, 0.13f), new Vector3(4.2f, 0.25f, 0.03f), q);
                d.Visual(region, WorldMaterials.Trim(true), at + q * new Vector3(-1.5f, 3.65f, 0.13f), new Vector3(2.0f, 0.25f, 0.03f), q);
            }
        }

        // Rubble: broken blocks and boards in a corner, as if part of a wall had come down.
        if (R(48) < 0.22f)
        {
            float sx = R(49) < 0.5f ? -1f : 1f, sz = R(50) < 0.5f ? -1f : 1f;
            float cx = sx * (hx + 1.4f), cz = sz * (hz + 1.4f);
            if (!NearDoor(cx, cz, 3f))
                for (int k = 0; k < 6; k++)
                {
                    uint rh = Hash((int)h, 500 + k);
                    float lx = cx + (Unit(rh, 0) - 0.5f) * 2.4f, lz = cz + (Unit(rh, 10) - 0.5f) * 2.4f;
                    var at = Ground(lx, lz);
                    if (!FreeAt(at, 0.8f)) continue;
                    var mat = k % 3 == 0 ? WorldMaterials.Brick((int)(h % 5u)) : k % 3 == 1 ? WorldMaterials.Concrete : WorldMaterials.PlanksDark;
                    float size = 0.3f + 0.4f * Unit(rh, 20);
                    d.Box(region, mat, at, new Vector3(size * (k % 3 == 2 ? 2.4f : 1f), size * 0.7f, size), yaw + 360f * Unit(rh, 4), 0.1f);
                }
        }
    }

    const float RoofOverhangLocal = 0.3f;

    // ---- pictures for review (#204, #207, #190) ----

    // PNGs in Logs/town: houses, halls and sheds from the street at eye height, and the biggest rooms of a few houses from a
    // corner and back. Does not change the scene on disk. tools/unity.sh run-gfx WorldBuilder.RenderTown
    public static void RenderTown()
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            var info = Object.FindFirstObjectByType<WorldInfo>();
            var terrain = Object.FindFirstObjectByType<Terrain>();
            System.IO.Directory.CreateDirectory("Logs/town");
            foreach (var old in System.IO.Directory.GetFiles("Logs/town", "*.png")) System.IO.File.Delete(old);
            DynamicGI.UpdateEnvironment();
            var cam = new GameObject("TownCamera").AddComponent<Camera>();
            SceneLook.EnablePost(cam);
            UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam).SetRenderer(info.RendererIndex);
            cam.fieldOfView = 65f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = FarClip;
            var plan = WorldGenerator.Generate(LoadSpec());
            var buildings = GameObject.Find("World/Buildings").transform;
            Vector3 Ground(Vector3 p) => new Vector3(p.x, terrain.SampleHeight(new Vector3(p.x, 0f, p.z)), p.z);

            var wanted = new (string kind, int count)[] { ("House", 8), ("Warehouse", 2), ("FactoryHall", 2), ("Shed", 2) };
            foreach (var (kind, count) in wanted)
            {
                var list = new System.Collections.Generic.List<Transform>();
                foreach (Transform b in buildings) if (b.name.EndsWith("_" + kind)) list.Add(b);
                int step = Mathf.Max(1, list.Count / count);
                for (int n = 0; n < count && n * step < list.Count; n++)
                {
                    var b = list[n * step];
                    var lot = plan.Lots[int.Parse(b.name.Substring(LotPrefix.Length, 2))];
                    var local = new Vector3(lot.SizeX * 0.5f + 9f, 0f, lot.SizeZ * 0.5f + 10f) * (n % 2 == 0 ? 1f : -1f);
                    if (n % 2 == 1) local.x = -local.x;
                    var eye = Ground(b.position + b.rotation * local) + Vector3.up * 1.7f;
                    SceneLook.Shot(cam, $"Logs/town/ext_{kind}_{n}_{b.name}.png", eye, b.position + Vector3.up * 3f);
                    // and from the front, nearer: the door side
                    var front = Ground(b.position + b.rotation * new Vector3(lot.SizeX * 0.15f, 0f, lot.SizeZ * 0.5f + 7f)) + Vector3.up * 1.7f;
                    SceneLook.Shot(cam, $"Logs/town/front_{kind}_{n}_{b.name}.png", front, b.position + Vector3.up * 2.2f);
                }
            }
            cam.fieldOfView = 75f;
            int houses = 0;
            foreach (Transform b in buildings)
            {
                if (!b.name.EndsWith("_House") || houses >= 5) continue;
                if ((int.Parse(b.name.Substring(LotPrefix.Length, 2)) % 4) != 1) continue;
                var lot = plan.Lots[int.Parse(b.name.Substring(LotPrefix.Length, 2))];
                var bp = Buildings.Generate(lot.Kind, lot.SizeX, lot.SizeZ, unchecked((int)Hash(plan.Seed, int.Parse(b.name.Substring(LotPrefix.Length, 2)))));
                Vector3 At(float x, float y, float z) => b.position + b.rotation * new Vector3(x, y, z);
                int shots = 0;
                foreach (var room in bp.Rooms.OrderByDescending(r => r.Area))
                {
                    if (shots >= 4 || room.Area < 8f) continue;
                    SceneLook.Shot(cam, $"Logs/town/room_{b.name}_{shots}a_{room.Role}.png", At(room.MinX + 0.4f, room.FloorY + 1.65f, room.MinZ + 0.4f), At(room.MaxX - 0.6f, room.FloorY + 0.9f, room.MaxZ - 0.6f));
                    SceneLook.Shot(cam, $"Logs/town/room_{b.name}_{shots}b_{room.Role}.png", At(room.MaxX - 0.4f, room.FloorY + 1.65f, room.MaxZ - 0.4f), At(room.MinX + 0.6f, room.FloorY + 0.9f, room.MinZ + 0.6f));
                    shots++;
                }
                houses++;
            }
            Object.DestroyImmediate(cam.gameObject);
            Debug.Log("[WorldBuilder] town pictures written to Logs/town");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.Exit(0);
    }
}
