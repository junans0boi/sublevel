using System.Collections.Generic;
using System.Linq;
using Game.Core;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

// Set dressing made from boxes and cylinders in the CC0 materials: low garden and yard walls, crates, barrels and
// pallet stacks against buildings, concrete barriers around the open junctions and utility poles along the roads.
// Cover for a fight and something for the eye between the buildings. Everything is combined into one mesh per
// material per region; every piece has a box collider and blanks out the navmesh under it.
public static partial class WorldBuilder
{
    const float LowWallHeight = 0.65f, LowWallThickness = 0.24f, LowWallOffset = 2.6f; // low enough to jump (jump apex is higher)
    const float PoleSpacing = 44f, PoleHeight = 7.4f, PoleRadius = 0.13f;
    const float PassKeepClear = 32f;

    sealed class Dressing
    {
        readonly WorldPlan _plan;
        readonly WorldMeshes.HeightField _ground;
        readonly List<NavMeshBuildSource> _nav;
        readonly Transform _root;
        readonly Dictionary<int, Transform> _regions = new Dictionary<int, Transform>();
        readonly Dictionary<(int region, Material material), WorldMeshes.BoxBatch> _batches = new Dictionary<(int, Material), WorldMeshes.BoxBatch>();
        public int Pieces, Details;

        public Dressing(WorldPlan plan, WorldMeshes.HeightField ground, Transform root, List<NavMeshBuildSource> nav)
        {
            _plan = plan; _ground = ground; _root = root; _nav = nav;
        }

        Transform Region(int region)
        {
            if (!_regions.TryGetValue(region, out var t))
            {
                t = Child(_root, region >= 0 ? "Region" + region : "Links");
                t.gameObject.isStatic = true;
                Child(t, "Colliders").gameObject.isStatic = true;
                _regions[region] = t;
            }
            return t;
        }

        WorldMeshes.BoxBatch Batch(int region, WorldMaterials.Surface surface)
        {
            var key = (region, surface.Material);
            if (!_batches.TryGetValue(key, out var batch)) _batches[key] = batch = new WorldMeshes.BoxBatch(surface.Tile);
            return batch;
        }

        // A box whose bottom centre is `foot`; `bury` extends it into the ground so it never shows a gap on a slope.
        public void Box(int region, WorldMaterials.Surface surface, Vector3 foot, Vector3 size, float yawDeg, float bury = 0.25f)
        {
            var rotation = Quaternion.Euler(0f, yawDeg, 0f);
            var full = new Vector3(size.x, size.y + bury, size.z);
            var center = foot + Vector3.up * ((size.y - bury) * 0.5f);
            Batch(region, surface).Add(center, full, rotation, skipBottom: true);
            Solid(region, center, full, rotation);
        }

        public void Cylinder(int region, WorldMaterials.Surface surface, Vector3 foot, float radius, float height, float bury = 0.25f)
        {
            Batch(region, surface).AddCylinder(foot - Vector3.up * bury, radius, height + bury);
            var size = new Vector3(radius * 1.8f, height + bury, radius * 1.8f);
            Solid(region, foot + Vector3.up * ((height - bury) * 0.5f), size, Quaternion.identity);
        }

        // Show-only parts (no collider, no navmesh obstacle): trim, roofs, signs, wires and other detail you cannot bump into.
        // Their meshes are cut into 96 m cells (so the camera only draws the ones in view) and only the big ones cast shadows:
        // one mesh per region would be drawn whole, and into every shadow cascade, from anywhere in the region (#207 perf).
        const float DetailCell = 96f;
        readonly Dictionary<(int region, int cx, int cz, Material material, bool shadow), WorldMeshes.BoxBatch> _details =
            new Dictionary<(int, int, int, Material, bool), WorldMeshes.BoxBatch>();

        WorldMeshes.BoxBatch Detail(int region, WorldMaterials.Surface surface, Vector3 at, bool shadow)
        {
            var key = (region, Mathf.FloorToInt(at.x / DetailCell), Mathf.FloorToInt(at.z / DetailCell), surface.Material, shadow);
            if (!_details.TryGetValue(key, out var batch)) _details[key] = batch = new WorldMeshes.BoxBatch(surface.Tile);
            Details++;
            return batch;
        }

        public void Visual(int region, WorldMaterials.Surface surface, Vector3 center, Vector3 size, Quaternion rotation, int skipFaces = 0, bool shadow = false) =>
            Detail(region, surface, center, shadow).Add(center, size, rotation, false, skipFaces);

        public void VisualCylinder(int region, WorldMaterials.Surface surface, Vector3 foot, float radius, float height, int sides = 8, bool shadow = false) =>
            Detail(region, surface, foot, shadow).AddCylinder(foot, radius, height, sides);

        public void VisualPrism(int region, WorldMaterials.Surface surface, Vector3 baseCenter, Vector3 size, Quaternion rotation, bool slopes, bool ends) =>
            Detail(region, surface, baseCenter, true).AddPrism(baseCenter, size, rotation, slopes, ends);

        // A collider and navmesh obstacle with no mesh of its own (the parts around it are Visual).
        public void Block(int region, Vector3 center, Vector3 size, Quaternion rotation) => Solid(region, center, size, rotation);

        void Solid(int region, Vector3 center, Vector3 size, Quaternion rotation)
        {
            var go = new GameObject("Piece" + Pieces++);
            go.transform.SetParent(Region(region).Find("Colliders"), false);
            go.transform.SetPositionAndRotation(center, rotation);
            go.AddComponent<BoxCollider>().size = size;
            go.isStatic = true;
            _nav.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box, size = size,
                transform = Matrix4x4.TRS(center, rotation, Vector3.one), area = NotWalkableArea,
            });
        }

        public Vector3 OnGround(float x, float z) => new Vector3(x, _ground.Height(x, z), z);

        // Free ground: off the roads, clear of every building pad and elevator, and not in a pass.
        public bool Free(float x, float z, float roadClear, float lotClear, BuildingLot own = null)
        {
            if (_plan.RoadDistance(x, z) < roadClear) return false;
            if (_ground.Height(x, z) < _plan.SeaLevel + 0.3f) return false;
            foreach (var lot in _plan.Lots)
                if (lot != own && lot.Distance(x, z) < lotClear) return false;
            foreach (var e in _plan.Elevators)
                if (e.Pad().Distance(x, z) < lotClear) return false;
            foreach (var p in _plan.Passes)
                if ((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < PassKeepClear * PassKeepClear) return false;
            return true;
        }

        public int Finish(WorldMeshes.Store store)
        {
            int renderers = 0;
            foreach (var pair in _batches.OrderBy(p => p.Key.region).ThenBy(p => p.Key.material.name))
            {
                if (pair.Value.Empty) continue;
                var parent = Region(pair.Key.region);
                var go = new GameObject("Dressing_" + pair.Key.material.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = store.AddBlob(pair.Value.ToMesh($"Dressing_{parent.name}_{pair.Key.material.name}"));
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = pair.Key.material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                go.isStatic = true;
                renderers++;
            }
            foreach (var pair in _details.OrderBy(p => p.Key.region).ThenBy(p => p.Key.cx).ThenBy(p => p.Key.cz).ThenBy(p => p.Key.material.name))
            {
                if (pair.Value.Empty) continue;
                var parent = Region(pair.Key.region);
                var name = $"Detail_{pair.Key.cx}_{pair.Key.cz}_{pair.Key.material.name}{(pair.Key.shadow ? "" : "_NoShadow")}";
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = store.AddBlob(pair.Value.ToMesh($"{parent.name}_{name}"));
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = pair.Key.material;
                renderer.shadowCastingMode = pair.Key.shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
                go.isStatic = true;
                renderers++;
            }
            return renderers;
        }
    }

    static float Unit(uint h, int shift) => ((h >> shift) & 1023u) / 1023f;

    static void BuildDressing(WorldPlan plan, WorldMeshes.HeightField ground, Transform parent, WorldMeshes.Store store,
        List<NavMeshBuildSource> nav, List<Placed> lots)
    {
        var dressing = new Dressing(plan, ground, Child(parent, "Dressing"), nav);
        for (int i = 0; i < lots.Count && i < plan.Lots.Count; i++)
        {
            DressLot(dressing, plan, plan.Lots[i], lots[i], Hash(plan.Seed, 700000 + i));
            DressTown(dressing, plan, plan.Lots[i], lots[i], Hash(plan.Seed, 900000 + i), unchecked((int)Hash(plan.Seed, i)));
        }
        DressJunctions(dressing, plan);
        DressRoads(dressing, plan);
        int renderers = dressing.Finish(store);
        Debug.Log($"[WorldBuilder] dressing: {dressing.Pieces} solid pieces + {dressing.Details} show-only parts combined into {renderers} renderers");
    }

    // Around one building: a low wall behind and beside a house, and clutter against the outer walls away from doors.
    static void DressLot(Dressing d, WorldPlan plan, BuildingLot lot, Placed placed, uint h)
    {
        var building = placed.Plan;
        float hx = building.Width * 0.5f, hz = building.Depth * 0.5f;
        int region = lot.Region;
        bool house = BuildingKinds.Family(lot.Kind) == BuildingKind.House;
        Vector3 World(float lx, float lz)
        {
            var p = placed.Origin + placed.Rotation * new Vector3(lx, 0f, lz);
            return d.OnGround(p.x, p.z);
        }
        float yaw = lot.YawDeg;

        // Low wall: the back, and one side as far as the middle of the house, with a gap to walk through.
        if (BuildingKinds.Family(lot.Kind) != BuildingKind.Shed && Unit(h, 0) < (house ? 0.7f : 0.45f))
        {
            var surface = house ? (Unit(h, 3) < 0.5f ? WorldMaterials.Brick((int)(h % 3u)) : WorldMaterials.Concrete) : WorldMaterials.ConcreteDark;
            float bx = hx + LowWallOffset, bz = hz + LowWallOffset;
            float gapAt = Mathf.Lerp(-bx + 2.5f, bx - 2.5f, Unit(h, 5));
            void Run(float ax, float az, float cx, float cz)
            {
                float length = Mathf.Sqrt((cx - ax) * (cx - ax) + (cz - az) * (cz - az));
                int segments = Mathf.Max(1, Mathf.RoundToInt(length / 3f));
                bool alongX = Mathf.Abs(cx - ax) > Mathf.Abs(cz - az);
                for (int s = 0; s < segments; s++)
                {
                    float t = (s + 0.5f) / segments;
                    float lx = Mathf.Lerp(ax, cx, t), lz = Mathf.Lerp(az, cz, t);
                    if (alongX && Mathf.Abs(lx - gapAt) < 1.6f) continue;
                    var at = World(lx, lz);
                    if (!d.Free(at.x, at.z, 1.2f, 1.2f, lot)) continue;
                    float piece = length / segments + 0.02f;
                    d.Box(region, surface, at, alongX ? new Vector3(piece, LowWallHeight, LowWallThickness) : new Vector3(LowWallThickness, LowWallHeight, piece), yaw, 0.6f);
                }
            }
            Run(-bx, -bz, bx, -bz);
            float side = Unit(h, 7) < 0.5f ? -bx : bx;
            Run(side, -bz, side, hz * 0.2f);
        }

        // Clutter against the walls: a few groups, each on a side of the building, never in front of a door.
        int groups = house ? 1 + (int)(Unit(h, 9) * 2.2f) : 2 + (int)(Unit(h, 9) * 3.2f);
        for (int g = 0; g < groups; g++)
        {
            uint gh = Hash((int)h, 31 + g);
            int side = (int)(gh % 4u); // 0 front (+z), 1 back, 2 left (-x), 3 right
            bool alongX = side < 2;
            float span = (alongX ? hx : hz) - 1.2f;
            if (span < 0.5f) continue;
            float along = Mathf.Lerp(-span, span, Unit(gh, 4));
            float off = (alongX ? hz : hx) + 0.75f;
            float lx = alongX ? along : (side == 2 ? -off : off);
            float lz = alongX ? (side == 0 ? off : -off) : along;
            bool blocked = false;
            foreach (var door in building.Doors)
            {
                if (!door.Exterior) continue;
                if (Mathf.Abs(door.X - lx) < door.Width * 0.5f + 2.2f && Mathf.Abs(door.Z - lz) < door.Width * 0.5f + 2.2f) blocked = true;
            }
            if (blocked) continue;
            // Along the wall: +x for front/back, +z for the sides.
            Vector3 Step(float a, float outward) =>
                World(lx + (alongX ? a : (side == 2 ? -outward : outward)), lz + (alongX ? (side == 0 ? outward : -outward) : a));
            int type = (int)((gh >> 14) % (house ? 3u : 5u));
            switch (type)
            {
                case 0: // crates, one stacked
                {
                    float size = 0.9f + 0.3f * Unit(gh, 16);
                    d.Box(region, WorldMaterials.Crate, Step(0f, 0f), new Vector3(size, size, size), yaw + 8f * (Unit(gh, 18) - 0.5f));
                    d.Box(region, WorldMaterials.Crate, Step(size + 0.15f, 0.05f), new Vector3(size * 0.85f, size * 0.85f, size * 0.85f), yaw + 20f * (Unit(gh, 20) - 0.5f));
                    if (Unit(gh, 22) < 0.5f)
                        d.Box(region, WorldMaterials.Crate, Step(0f, 0f) + Vector3.up * size, new Vector3(size * 0.8f, size * 0.8f, size * 0.8f), yaw + 25f, 0f);
                    break;
                }
                case 1: // barrels
                {
                    int count = 2 + (int)(Unit(gh, 16) * 2.5f);
                    for (int b = 0; b < count; b++)
                        d.Cylinder(region, b % 2 == 0 ? WorldMaterials.PaintedMetal : WorldMaterials.RustyMetal, Step(b * 0.68f, (b % 2) * 0.12f), 0.3f, 0.9f);
                    break;
                }
                case 2: // pallets stacked flat
                {
                    int count = 3 + (int)(Unit(gh, 16) * 5f);
                    var at = Step(0f, 0.2f);
                    for (int p = 0; p < count; p++)
                        d.Box(region, WorldMaterials.PlanksDark, at + Vector3.up * (p * 0.15f), new Vector3(1.2f, 0.13f, 1.0f), yaw + 6f * (Unit(Hash((int)gh, p), 3) - 0.5f), p == 0 ? 0.25f : 0f);
                    break;
                }
                case 3: // concrete barriers end to end, a little way off the wall
                {
                    for (int b = 0; b < 2; b++)
                        d.Box(region, WorldMaterials.Concrete, Step(b * 2.5f - 1.25f, 1.3f), alongX ? new Vector3(2.3f, 0.85f, 0.45f) : new Vector3(0.45f, 0.85f, 2.3f), yaw);
                    break;
                }
                default: // a big crate and barrels
                {
                    d.Box(region, WorldMaterials.Crate, Step(0f, 0.15f), new Vector3(1.4f, 1.3f, 1.4f), yaw + 5f);
                    d.Cylinder(region, WorldMaterials.RustyMetal, Step(1.25f, 0f), 0.3f, 0.9f);
                    d.Cylinder(region, WorldMaterials.PaintedMetal, Step(1.9f, 0.1f), 0.3f, 0.9f);
                    break;
                }
            }
        }
    }

    // The open junction of every compound: concrete barriers in a loose ring, so the middle is not a bare disc.
    static void DressJunctions(Dressing d, WorldPlan plan)
    {
        for (int i = 0; i < plan.ConvergencePoints.Count; i++)
        {
            var point = plan.ConvergencePoints[i];
            uint h = Hash(plan.Seed, 800000 + i);
            int count = 3 + (int)(Unit(h, 0) * 2.5f);
            float start = Unit(h, 10) * 360f;
            for (int b = 0; b < count; b++)
            {
                uint bh = Hash((int)h, b);
                float angle = (start + b * 360f / count + 30f * (Unit(bh, 0) - 0.5f)) * Mathf.Deg2Rad;
                float radius = Mathf.Max(point.ClearRadius, 9f) + 2.5f + 4f * Unit(bh, 10);
                float x = point.X + Mathf.Sin(angle) * radius, z = point.Z + Mathf.Cos(angle) * radius;
                if (!d.Free(x, z, 1.6f, 3f)) continue;
                float yaw = angle * Mathf.Rad2Deg + 90f + 30f * (Unit(bh, 20) - 0.5f); // across the line to the centre
                bool crates = Unit(bh, 5) < 0.35f;
                if (crates)
                {
                    d.Box(point.Region, WorldMaterials.Crate, d.OnGround(x, z), new Vector3(1.3f, 1.2f, 1.3f), yaw);
                    var beside = Quaternion.Euler(0f, yaw, 0f) * new Vector3(1.45f, 0f, 0.1f);
                    d.Box(point.Region, WorldMaterials.Crate, d.OnGround(x + beside.x, z + beside.z), new Vector3(1.0f, 1.0f, 1.0f), yaw + 12f);
                }
                else
                {
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var along = Quaternion.Euler(0f, yaw, 0f) * new Vector3(k * 1.22f, 0f, 0f);
                        d.Box(point.Region, WorldMaterials.Concrete, d.OnGround(x + along.x, z + along.z), new Vector3(2.3f, 0.85f, 0.45f), yaw);
                    }
                }
            }
        }
    }

    // Utility poles down one side of the paved roads in the built-up regions.
    static void DressRoads(Dressing d, WorldPlan plan)
    {
        for (int r = 0; r < plan.Roads.Count; r++)
        {
            var road = plan.Roads[r];
            if (!IsPaved(road.Kind)) continue;
            if (road.Region >= 0 && plan.Regions[road.Region].Kind == RegionKind.Wetland && road.Kind != RoadKind.Trunk) continue;
            float carried = PoleSpacing * 0.5f;
            bool havePrev = false;
            Vector3 prevFoot = default;
            int count = Mathf.Min(road.X.Length, road.Z.Length);
            for (int i = 0; i + 1 < count; i++)
            {
                var a = new Vector2(road.X[i], road.Z[i]);
                var b = new Vector2(road.X[i + 1], road.Z[i + 1]);
                float length = Vector2.Distance(a, b);
                if (length < 0.01f) continue;
                var dir = (b - a) / length;
                var left = new Vector2(-dir.y, dir.x);
                while (carried < length)
                {
                    var p = a + dir * carried + left * (road.Width * 0.5f + 1.7f);
                    carried += PoleSpacing;
                    if (!d.Free(p.x, p.y, 1.3f, 2.5f)) { havePrev = false; continue; }
                    int region = plan.RegionAt(p.x, p.y);
                    var foot = d.OnGround(p.x, p.y);
                    float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                    d.Cylinder(region, WorldMaterials.PlanksDark, foot, PoleRadius, PoleHeight, 0.4f);
                    d.Box(region, WorldMaterials.PlanksDark, foot + Vector3.up * (PoleHeight - 0.7f), new Vector3(1.7f, 0.12f, 0.12f), yaw, 0f);
                    if (havePrev && Vector3.Distance(prevFoot, foot) < PoleSpacing * 1.6f) StringWires(d, region, prevFoot, foot, yaw);
                    prevFoot = foot; havePrev = true;
                }
                carried -= length;
            }
        }
    }

    // Three wires from the crossarm of one pole to the next, each in two straight runs that sag in the middle.
    static void StringWires(Dressing d, int region, Vector3 from, Vector3 to, float yaw)
    {
        var side = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
        float top = PoleHeight - 0.64f;
        for (int k = -1; k <= 1; k++)
        {
            var a = from + Vector3.up * top + side * (k * 0.7f);
            var b = to + Vector3.up * top + side * (k * 0.7f);
            var mid = (a + b) * 0.5f + Vector3.down * 0.55f;
            foreach (var (p, q) in new[] { (a, mid), (mid, b) })
            {
                var along = q - p;
                d.Visual(region, WorldMaterials.Wire, (p + q) * 0.5f, new Vector3(0.03f, 0.03f, along.magnitude), Quaternion.LookRotation(along));
            }
        }
    }
}
