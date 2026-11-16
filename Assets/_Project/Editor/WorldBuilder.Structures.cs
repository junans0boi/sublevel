using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Net;
using static EditorFields;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

// What stands on the terrain: roads, buildings and elevator stations, rocks (trees: WorldBuilder.Vegetation).
public static partial class WorldBuilder
{
    const string LotPrefix = "Lot", StationPrefix = "Station";

    // Roads: sampled finer than the 4 m of the first sketch so the strip follows the 1 m heightmap; lifted just
    // clear of the ground. Overlapping strips get slightly different lifts so they do not flicker at junctions.
    const float RoadStep = 2f, RoadLift = 0.03f;

    // Buildings: a skirt under the footprint hides gaps where the pad is not perfectly level.
    const float FoundationDepth = 1.2f;
    // LODGroup screen heights: inside walls, stairs and props are dropped first, the shell much later.
    const float InteriorLodHeight = 0.25f, ShellLodHeight = 0.02f;

    // Rocks: ScatterPoint.Scale 1 is a rock this wide; the models are scaled to it whatever their own size.
    const float RockWidth = 2.4f, RockSink = 0.22f;
    // Triangle budget: rocks with LODs cost little far away, so the cap is on the count; the moss set has no LODs
    // (5k-16.5k triangles each), so few of them are placed and they are culled early.
    static int MaxRocks => Cfg.MaxRocks;
    static int MaxSingleLodRocks => Cfg.MaxSingleLodRocks;
    const int SingleLodPercent = 12;
    const float FirstLodHeight = 0.8f, LastLodHeight = 0.035f, SingleLodCullHeight = 0.045f;

    // A building as it stands in the world.
    sealed class Placed
    {
        public string Name;
        public BuildingPlan Plan;
        public Vector3 Origin;                  // centre of the footprint on the ground
        public Quaternion Rotation;
        public readonly List<Vector3> Loot = new List<Vector3>();
        public readonly List<(Light light, bool flicker)> Lamps = new List<(Light light, bool flicker)>();
        public Transform Box;                   // the inside of the building as a scaled unit box (not stations)
        public Vector3 Spawn;                   // stations only (feet)
        public float SpawnYaw;
    }

    static uint Hash(int seed, int index)
    {
        unchecked
        {
            uint h = (uint)seed * 2654435761u + (uint)index * 40503u + 0x9E3779B9u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return h;
        }
    }

    // ---- roads ----

    static bool IsPaved(RoadKind kind)
    {
        // By name so that kinds added later (links are paved, lanes and paths are not) fall on the right side.
        var name = kind.ToString();
        return name != "Path" && name != "Lane";
    }

    static void BuildRoads(WorldPlan plan, WorldMeshes.HeightField ground, Transform parent, WorldMeshes.Store store)
    {
        var root = Child(parent, "Roads");
        var asphalt = WorldMaterials.Asphalt;
        var dirt = WorldMaterials.DirtRoad;
        // One mesh per surface per region (-1: the roads between regions) so far roads can be culled.
        var batches = new SortedDictionary<int, WorldMeshes.StripBatch>();
        var points = new List<Vector2>();
        for (int i = 0; i < plan.Roads.Count; i++)
        {
            var road = plan.Roads[i];
            int count = Mathf.Min(road.X.Length, road.Z.Length);
            if (count < 2) continue;
            bool paved = IsPaved(road.Kind);
            int key = (road.Region + 1) * 2 + (paved ? 1 : 0);
            if (!batches.TryGetValue(key, out var batch))
                batches[key] = batch = new WorldMeshes.StripBatch(paved ? asphalt.Tile : dirt.Tile);

            points.Clear();
            for (int p = 0; p < count - 1; p++)
            {
                var a = new Vector2(road.X[p], road.Z[p]);
                var b = new Vector2(road.X[p + 1], road.Z[p + 1]);
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / RoadStep));
                for (int s = 0; s < steps; s++) points.Add(Vector2.Lerp(a, b, (float)s / steps));
            }
            points.Add(new Vector2(road.X[count - 1], road.Z[count - 1]));
            float lift = RoadLift + (paved ? 0.012f : 0f) + (i % 3) * 0.004f;
            batch.Add(points, road.Width, ground, lift);
        }

        float total = 0f;
        foreach (var pair in batches)
        {
            if (pair.Value.Empty) continue;
            bool paved = (pair.Key & 1) == 1;
            int region = pair.Key / 2 - 1;
            var name = (paved ? "Asphalt" : "Dirt") + (region >= 0 ? "_Region" + region : "_Links");
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = store.AddBlob(pair.Value.ToMesh("Road_" + name));
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = WorldMaterials.Road(paved);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            go.isStatic = true;
            total += pair.Value.Length;
        }
        // No colliders: the ground under a road is the terrain itself, flattened by the generator.
        Debug.Log($"[WorldBuilder] roads: {plan.Roads.Count} roads, {total:F0} m, {batches.Count} meshes");
    }

    // ---- buildings ----

    static void BuildBuildings(WorldPlan plan, WorldMeshes.HeightField ground, Transform parent, WorldMeshes.Store store,
        List<NavMeshBuildSource> nav, List<Placed> lots, List<Placed> stations)
    {
        var root = Child(parent, "Buildings");
        int pieces = 0, renderers = 0;
        ResetGlass();
        for (int i = 0; i < plan.Lots.Count; i++)
        {
            var lot = plan.Lots[i];
            int seed = unchecked((int)Hash(plan.Seed, i));
            var placed = PlaceBuilding(root, $"{LotPrefix}{i:00}_{lot.Kind}", lot.Kind, lot.X, lot.Y, lot.Z, lot.SizeX, lot.SizeZ, lot.YawDeg,
                seed, ground, store, nav, ref renderers);
            pieces += placed.Plan.Pieces.Count;
            lots.Add(placed);
        }
        // Same order as plan.Elevators: FillInfo pairs them by index.
        for (int i = 0; i < plan.Elevators.Count; i++)
        {
            var e = plan.Elevators[i];
            int seed = unchecked((int)Hash(plan.Seed, 100000 + i));
            var placed = PlaceBuilding(root, $"{StationPrefix}{i:00}", BuildingKind.ElevatorStation, e.X, e.Y, e.Z,
                WorldElevator.StationWidth, WorldElevator.StationDepth, e.FacingDeg, seed, ground, store, nav, ref renderers, shaftHole: true);
            var station = placed.Plan;
            if (station.HasSpawn)
            {
                placed.Spawn = placed.Origin + placed.Rotation * new Vector3(station.SpawnX, station.SpawnY, station.SpawnZ);
                placed.SpawnYaw = e.FacingDeg + station.SpawnFacingDeg;
            }
            else
            {
                Debug.LogWarning($"[WorldBuilder] {placed.Name}: the station plan has no spawn point, using its centre");
                placed.Spawn = placed.Origin + Vector3.up * station.FloorY;
                placed.SpawnYaw = e.FacingDeg;
            }
            pieces += station.Pieces.Count;
            AddElevatorDoor(root, i, placed);
            AddElevatorShaft(root, i, placed, store);
            stations.Add(placed);
        }
        var lamps = lots.SelectMany(l => l.Lamps).ToList();
        var pool = Child(parent, "InteriorLights").gameObject.AddComponent<Game.Gameplay.InteriorLights>();
        Set(pool, "_lights", p => { p.arraySize = lamps.Count; for (int i = 0; i < lamps.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = lamps[i].light; });
        var boxes = lots.Where(l => l.Box != null).Select(l => l.Box).ToList();
        Set(pool, "_boxes", p => { p.arraySize = boxes.Count; for (int i = 0; i < boxes.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = boxes[i]; });
        var gradeVolume = Child(pool.transform, "InteriorGrade").gameObject.AddComponent<Volume>();
        gradeVolume.isGlobal = true;
        gradeVolume.priority = 5f;
        gradeVolume.weight = 0f;
        gradeVolume.sharedProfile = InteriorGradeProfile();
        Set(pool, "_grade", p => p.objectReferenceValue = gradeVolume);
        Set(pool, "_flicker", p => { p.arraySize = lamps.Count; for (int i = 0; i < lamps.Count; i++) p.GetArrayElementAtIndex(i).boolValue = lamps[i].flicker; });
        Debug.Log($"[WorldBuilder] buildings: {lots.Count} lots + {stations.Count} stations, {pieces} boxes + {lots.Sum(l => l.Plan.Decor.Count) + stations.Sum(l => l.Plan.Decor.Count)} furniture parts ({DecorFacesHidden} of {DecorFacesTotal} faces hidden and left out) combined into {renderers} renderers, {lamps.Count} lamps");
    }

    // The sliding door across the station's front opening (ElevatorDoors finds it by `Station`): 3.2 x 3.0 x 0.12 m,
    // not static and no network object, it sinks into the floor when the match opens the doors.
    static void AddElevatorDoor(Transform parent, int index, Placed station)
    {
        var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = $"ElevatorDoor{index:00}";
        door.transform.SetParent(parent, false);
        door.transform.SetPositionAndRotation(station.Spawn + station.Rotation * new Vector3(0f, 1.5f, 1.44f), station.Rotation);
        door.transform.localScale = new Vector3(3.2f, 3.0f, 0.12f);
        door.isStatic = false;
        door.GetComponent<MeshRenderer>().sharedMaterial = WorldMaterials.Door();
        // A centre seam and two amber bands on both faces so the shut door reads as a door, not a wall.
        var stripe = WorldMaterials.DoorStripe();
        var seamMaterial = WorldMaterials.DoorSeam();
        foreach (float face in new[] { -0.5f, 0.5f })
        {
            AddDoorDetail(door.transform, "Seam", new Vector3(0f, 0f, face), new Vector3(0.012f, 0.94f, 0.5f), seamMaterial);
            AddDoorDetail(door.transform, "BandLow", new Vector3(0f, -0.38f, face), new Vector3(0.94f, 0.05f, 0.5f), stripe);
            AddDoorDetail(door.transform, "BandHigh", new Vector3(0f, 0.38f, face), new Vector3(0.94f, 0.05f, 0.5f), stripe);
        }
        var component = door.AddComponent<ElevatorDoor>();
        Set(component, "_station", p => p.intValue = index);
    }

    static void AddDoorDetail(Transform door, string name, Vector3 localPosition, Vector3 localScale, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(door, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    // One GameObject per building: the boxes of its plan combined into one mesh per material (inside parts apart
    // from the shell so they can be dropped with distance), one BoxCollider per box, one navmesh source per box.
    // shaftHole (stations, ADR-065 (1)): the floor slab is built around the elevator shaft's opening; the navmesh
    // still gets the whole slab, since the car fills the opening at floor level whenever anyone can walk there.
    static Placed PlaceBuilding(Transform parent, string name, BuildingKind kind, float x, float y, float z, float sizeX, float sizeZ, float yawDeg,
        int seed, WorldMeshes.HeightField ground, WorldMeshes.Store store, List<NavMeshBuildSource> nav, ref int rendererCount, bool shaftHole = false)
    {
        var plan = Buildings.Generate(kind, sizeX, sizeZ, seed);
        bool cutSlab = shaftHole && plan.HasSpawn;
        var hole = cutSlab ? ShaftOpening(plan) : default;
        var rotation = Quaternion.Euler(0f, yawDeg, 0f); // yaw 0 = +Z, 90 = +X, as in the plan
        var origin = new Vector3(x, FootY(ground, x, y, z, plan.Width, plan.Depth, rotation), z);
        var placed = new Placed { Name = name, Plan = plan, Origin = origin, Rotation = rotation };

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(origin, rotation);
        go.isStatic = true;
        var colliders = Child(go.transform, "Colliders").gameObject;
        colliders.isStatic = true;

        uint style = Hash(seed, 7);
        // Inside parts are split into 10 m cells per storey: a mesh only gets the few nearest lamps (URP's per-object
        // light limit), so one mesh over a 40 m hall would be lit by four lamps all over.
        var order = new List<(Material material, bool interior, int cell)>();
        var batches = new Dictionary<(Material material, bool interior, int cell), WorldMeshes.BoxBatch>();
        WorldMeshes.BoxBatch Batch(WorldMaterials.Surface surface, bool interior, Vector3 at = default)
        {
            int cell = interior ? 1 + ((Mathf.FloorToInt(at.x / 10f) + 20) * 64 + Mathf.FloorToInt(at.z / 10f) + 20) * 2 + (at.y > 3.5f ? 1 : 0) : 0;
            var key = (surface.Material, interior, cell);
            if (!batches.TryGetValue(key, out var batch))
            {
                batches[key] = batch = new WorldMeshes.BoxBatch(surface.Tile);
                order.Add(key);
            }
            return batch;
        }

        foreach (var piece in plan.Pieces)
        {
            if (piece.SizeX <= 0f || piece.SizeY <= 0f || piece.SizeZ <= 0f) continue;
            var center = new Vector3(piece.X, piece.Y, piece.Z);
            var size = new Vector3(piece.SizeX, piece.SizeY, piece.SizeZ);
            bool interior = piece.Kind == SurfaceKind.WallInner || piece.Kind == SurfaceKind.Prop || piece.Kind == SurfaceKind.Stairs;
            var parts = cutSlab && piece.Name == "Slab" ? SplitAround(center, size, hole) : new List<(Vector3 center, Vector3 size)> { (center, size) };
            // A prop's box is only its collider: what you see is its decor parts below.
            if (piece.Kind != SurfaceKind.Prop)
                foreach (var part in parts)
                    Batch(SurfaceFor(kind, piece, style), interior, part.center).Add(part.center, part.size, skipBottom: piece.MinY <= 0.001f);
            // A house's rooms get a plastered ceiling: without it the underside of the roof shows roof tiles and
            // the underside of an upper floor shows floor boards.
            if (BuildingKinds.Family(kind) == BuildingKind.House && piece.MinY > 1.5f && (piece.Kind == SurfaceKind.Roof || piece.Kind == SurfaceKind.Floor)
                && size.x > 0.7f && size.z > 0.7f)
                Batch(WorldMaterials.PlasterInner, true, new Vector3(piece.X, piece.MinY, piece.Z)).Add(new Vector3(piece.X, piece.MinY, piece.Z), new Vector3(size.x - 0.5f, 0.03f, size.z - 0.5f));

            foreach (var part in parts)
            {
                var box = colliders.AddComponent<BoxCollider>();
                box.center = part.center;
                box.size = part.size;
            }

            // Floors and stairs are walked on; walls, roofs, trim and props only get in the way (a roof nobody
            // can reach would otherwise become an island of navmesh).
            bool walkable = piece.Kind == SurfaceKind.Floor || piece.Kind == SurfaceKind.Stairs;
            nav.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = size,
                transform = Matrix4x4.TRS(origin + rotation * center, rotation, Vector3.one),
                area = walkable ? 0 : NotWalkableArea,
            });
        }

        int hiddenFaces = 0;
        for (int di = 0; di < plan.Decor.Count; di++)
        {
            var decor = plan.Decor[di];
            var at = new Vector3(decor.X, decor.Y, decor.Z);
            int skip = HiddenFaces(plan, decor) | HiddenByNeighbours(plan.Decor, di);
            for (int f = 0; f < 6; f++) hiddenFaces += skip >> f & 1;
            Batch(WorldMaterials.Decor(decor.Mat), true, at).Add(at, new Vector3(decor.SizeX, decor.SizeY, decor.SizeZ), false, skip);
        }
        DecorFacesHidden += hiddenFaces;
        DecorFacesTotal += plan.Decor.Count * 6;

        // Foundation skirt: its top is inside the floor slab, its sides reach into the ground. Not a collider.
        if (plan.Width > 0.3f && plan.Depth > 0.3f)
        {
            var floor = SurfaceFor(kind, new BuildingPiece { Kind = SurfaceKind.Floor }, style);
            float top = Mathf.Min(0.05f, plan.FloorY * 0.5f);
            var skirtCenter = new Vector3(0f, (top - FoundationDepth) * 0.5f, 0f);
            var skirtSize = new Vector3(plan.Width - 0.1f, top + FoundationDepth, plan.Depth - 0.1f);
            // Around the shaft too: its top would otherwise show as a floor inside the rising car.
            foreach (var part in cutSlab ? SplitAround(skirtCenter, skirtSize, hole) : new List<(Vector3 center, Vector3 size)> { (skirtCenter, skirtSize) })
                Batch(floor, false).Add(part.center, part.size, skipBottom: true);
        }

        var all = new List<Renderer>();
        var shell = new List<Renderer>();
        foreach (var key in order)
        {
            var part = new GameObject((key.interior ? "Inside_" : "Shell_") + key.material.name + (key.interior ? "_" + key.cell : ""));
            part.transform.SetParent(go.transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = store.AddBlob(batches[key].ToMesh($"{name}_{part.name}"));
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = key.material;
            renderer.shadowCastingMode = key.interior ? ShadowCastingMode.Off : ShadowCastingMode.On;
            part.isStatic = true;
            all.Add(renderer);
            if (!key.interior) shell.Add(renderer);
        }
        rendererCount += all.Count;
        if (all.Count > 0)
        {
            var group = go.AddComponent<LODGroup>();
            group.SetLODs(shell.Count > 0 && shell.Count < all.Count
                ? new[] { new LOD(InteriorLodHeight, all.ToArray()), new LOD(ShellLodHeight, shell.ToArray()) }
                : new[] { new LOD(ShellLodHeight, all.ToArray()) });
            group.RecalculateBounds();
        }

        AddWindowGlass(go.transform, plan, seed);
        AddLamps(go.transform, plan, placed);
        if (kind != BuildingKind.ElevatorStation) placed.Box = AddInteriorBox(go.transform, plan);

        foreach (var point in plan.LootPoints)
            placed.Loot.Add(origin + rotation * new Vector3(point.X, point.Y, point.Z) + Vector3.up * LootLift);
        return placed;
    }

    public static int DecorFacesHidden, DecorFacesTotal;

    // Faces of a furniture part that lie flat against a floor, wall or ceiling piece (bit f of the result, in BoxBatch's face
    // order): nobody can see them, so they are not put in the mesh. Only a piece that covers the whole face counts.
    static int HiddenFaces(BuildingPlan plan, BuildingDecor d)
    {
        const float tol = 0.05f;
        float hx = d.SizeX * 0.5f, hy = d.SizeY * 0.5f, hz = d.SizeZ * 0.5f;
        int mask = 0;
        foreach (var p in plan.Pieces)
        {
            if (p.Kind != SurfaceKind.Floor && p.Kind != SurfaceKind.WallInner && p.Kind != SurfaceKind.WallOuter) continue;
            bool coversX = p.MinX <= d.X - hx + tol && p.MaxX >= d.X + hx - tol;
            bool coversY = p.MinY <= d.Y - hy + tol && p.MaxY >= d.Y + hy - tol;
            bool coversZ = p.MinZ <= d.Z - hz + tol && p.MaxZ >= d.Z + hz - tol;
            if (coversX && coversZ)
            {
                if (Mathf.Abs(p.MaxY - (d.Y - hy)) <= tol) mask |= 1 << 1;   // stands on it
                if (Mathf.Abs(p.MinY - (d.Y + hy)) <= tol) mask |= 1 << 0;   // hangs from it
            }
            if (coversX && coversY)
            {
                if (Mathf.Abs(p.MaxZ - (d.Z - hz)) <= tol) mask |= 1 << 3;
                if (Mathf.Abs(p.MinZ - (d.Z + hz)) <= tol) mask |= 1 << 2;
            }
            if (coversY && coversZ)
            {
                if (Mathf.Abs(p.MaxX - (d.X - hx)) <= tol) mask |= 1 << 5;
                if (Mathf.Abs(p.MinX - (d.X + hx)) <= tol) mask |= 1 << 4;
            }
        }
        return mask;
    }

    // Faces of a furniture part that another part covers completely on the same plane (a cushion on its frame, a leg under a seat).
    static int HiddenByNeighbours(System.Collections.Generic.List<BuildingDecor> all, int index)
    {
        const float tol = 0.004f;
        var a = all[index];
        float ax0 = a.X - a.SizeX * 0.5f, ax1 = a.X + a.SizeX * 0.5f, ay0 = a.Y - a.SizeY * 0.5f, ay1 = a.Y + a.SizeY * 0.5f, az0 = a.Z - a.SizeZ * 0.5f, az1 = a.Z + a.SizeZ * 0.5f;
        int mask = 0;
        for (int j = 0; j < all.Count; j++)
        {
            if (j == index) continue;
            var b = all[j];
            float bx0 = b.X - b.SizeX * 0.5f, bx1 = b.X + b.SizeX * 0.5f, by0 = b.Y - b.SizeY * 0.5f, by1 = b.Y + b.SizeY * 0.5f, bz0 = b.Z - b.SizeZ * 0.5f, bz1 = b.Z + b.SizeZ * 0.5f;
            if (bx0 > ax1 + tol || bx1 < ax0 - tol || by0 > ay1 + tol || by1 < ay0 - tol || bz0 > az1 + tol || bz1 < az0 - tol) continue;
            bool cx = bx0 <= ax0 + tol && bx1 >= ax1 - tol, cy = by0 <= ay0 + tol && by1 >= ay1 - tol, cz = bz0 <= az0 + tol && bz1 >= az1 - tol;
            if (cx && cz)
            {
                if (Mathf.Abs(ay1 - by0) <= tol) mask |= 1 << 0;
                if (Mathf.Abs(ay0 - by1) <= tol) mask |= 1 << 1;
            }
            if (cx && cy)
            {
                if (Mathf.Abs(az1 - bz0) <= tol) mask |= 1 << 2;
                if (Mathf.Abs(az0 - bz1) <= tol) mask |= 1 << 3;
            }
            if (cy && cz)
            {
                if (Mathf.Abs(ax1 - bx0) <= tol) mask |= 1 << 4;
                if (Mathf.Abs(ax0 - bx1) <= tol) mask |= 1 << 5;
            }
        }
        return mask;
    }

    // Darker inside than out: a post volume that lowers the exposure of the shared grade while the camera is inside a
    // building, so the sky's flat ambient stops washing out the lamps (InteriorLights drives its weight, ADR-148).
    // The rooms are plain transforms scaled to the building's inner box: no collider, so no ray or bullet meets them.
    const float InteriorExposure = -0.6f;

    static VolumeProfile InteriorGradeProfile()
    {
        string path = WorldMaterials.GeneratedDir + "/World_InteriorGrade.asset";
        var profile = UnityEditor.AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            UnityEditor.AssetDatabase.CreateAsset(profile, path);
        }
        if (!profile.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments grading))
        {
            grading = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>();
            UnityEditor.AssetDatabase.AddObjectToAsset(grading, profile);
        }
        grading.postExposure.Override(InteriorExposure);
        UnityEditor.EditorUtility.SetDirty(profile);
        return profile;
    }

    static Transform AddInteriorBox(Transform building, BuildingPlan plan)
    {
        var go = Child(building, "InteriorBox");
        go.localPosition = new Vector3(0f, plan.FloorY + (plan.Height - plan.FloorY) * 0.5f, 0f);
        go.localScale = new Vector3(plan.Width - 0.6f, plan.Height - plan.FloorY, plan.Depth - 0.6f);
        return go;
    }

    // The lights of a plan's lamps, all off: InteriorLights switches on the few nearest to the camera (ADR-147).
    static void AddLamps(Transform building, BuildingPlan plan, Placed placed)
    {
        if (plan.Lamps.Count == 0) return;
        var group = Child(building, "Lamps");
        foreach (var lamp in plan.Lamps)
        {
            var go = new GameObject(lamp.Name);
            go.transform.SetParent(group, false);
            go.transform.localPosition = new Vector3(lamp.X, lamp.Y, lamp.Z);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = lamp.Range;
            light.intensity = lamp.Intensity;
            light.color = BuildingKinds.Family(plan.Kind) == BuildingKind.House ? new Color(1f, 0.82f, 0.6f) : new Color(0.92f, 0.96f, 1f);
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.enabled = false;
            placed.Lamps.Add((light, lamp.Flicker));
        }
    }

    // The height the building stands at: the lot's, unless the ground under the footprint says otherwise
    // (never let the ground come through the floor, and do not float above the lowest corner).
    static float FootY(WorldMeshes.HeightField ground, float x, float y, float z, float width, float depth, Quaternion rotation)
    {
        float highest = float.MinValue, lowest = float.MaxValue;
        int nx = Mathf.Max(1, Mathf.CeilToInt(width / 2f)), nz = Mathf.Max(1, Mathf.CeilToInt(depth / 2f));
        for (int i = 0; i <= nx; i++)
        {
            for (int j = 0; j <= nz; j++)
            {
                var p = rotation * new Vector3(((float)i / nx - 0.5f) * width, 0f, ((float)j / nz - 0.5f) * depth);
                float h = ground.Height(x + p.x, z + p.z);
                highest = Mathf.Max(highest, h);
                lowest = Mathf.Min(lowest, h);
            }
        }
        return Mathf.Max(highest - 0.08f, Mathf.Min(y, lowest + 0.1f));
    }

    // Material per surface kind and building kind, with tint variants picked per building.
    static WorldMaterials.Surface SurfaceFor(BuildingKind kind, BuildingPiece piece, uint style)
    {
        int wall = (int)((style >> 4) % WorldMaterials.Variants);
        int roof = (int)((style >> 12) % WorldMaterials.Variants);
        bool brick = ((style >> 20) & 1u) == 0u;

        if (piece.Kind == SurfaceKind.Prop)
        {
            switch (piece.Prop)
            {
                case PropKind.Crate: return WorldMaterials.Crate;
                case PropKind.Table:
                case PropKind.Counter: return WorldMaterials.PlanksDark;
                case PropKind.Bed: return WorldMaterials.Bedding;
                case PropKind.Machine: return WorldMaterials.RustyMetal;
                default: return WorldMaterials.Shelving; // shelves and anything new
            }
        }

        switch (BuildingKinds.Family(kind))
        {
            case BuildingKind.House:
                switch (piece.Kind)
                {
                    case SurfaceKind.Floor: return WorldMaterials.Planks;
                    case SurfaceKind.WallOuter: return brick ? WorldMaterials.Brick(wall) : WorldMaterials.Plaster(wall);
                    case SurfaceKind.WallInner: return WorldMaterials.PlasterInner;
                    case SurfaceKind.Roof: return WorldMaterials.RoofTiles(roof);
                    case SurfaceKind.Stairs: return WorldMaterials.PlanksDark;
                    default: return WorldMaterials.Concrete;
                }
            case BuildingKind.Warehouse:
            case BuildingKind.FactoryHall:
            case BuildingKind.Shed:
                switch (piece.Kind)
                {
                    case SurfaceKind.Floor: return WorldMaterials.Concrete;
                    case SurfaceKind.WallOuter: return WorldMaterials.Steel(wall);
                    case SurfaceKind.WallInner: return WorldMaterials.ConcreteDark;
                    case SurfaceKind.Roof: return WorldMaterials.SteelRoof;
                    case SurfaceKind.Stairs: return WorldMaterials.RustyMetal;
                    default: return WorldMaterials.PaintedMetal;
                }
            default: // elevator station: concrete throughout
                switch (piece.Kind)
                {
                    case SurfaceKind.Floor:
                    case SurfaceKind.Roof:
                    case SurfaceKind.Stairs: return WorldMaterials.Concrete;
                    case SurfaceKind.Trim: return WorldMaterials.Concrete;
                    default: return WorldMaterials.ConcreteDark;
                }
        }
    }

    // ---- rocks ----

    // One rock shape: its LOD meshes (one if the model has none) and where each sits relative to the rock's
    // base centre, taken from the model's own hierarchy.
    sealed class RockVariant
    {
        public string Name;
        public Mesh[] Lods;
        public Matrix4x4[] Local;
        public Material Material;
        public Vector3 Size;
        public long Triangles; // of the most detailed mesh
    }

    static int LodIndex(string name)
    {
        int at = name.LastIndexOf("_LOD", StringComparison.OrdinalIgnoreCase);
        if (at < 0 || at + 4 >= name.Length || !char.IsDigit(name[at + 4])) return -1;
        return name[at + 4] - '0';
    }

    // A model whose meshes are named _LOD0.. is one rock with LODs; otherwise every mesh in it is a rock of its own.
    static void LoadRockSet(int set, List<RockVariant> withLods, List<RockVariant> single)
    {
        var model = SceneLook.Load<GameObject>(WorldMaterials.RockModelPath(set));
        var material = WorldMaterials.Rock(set);
        var filters = model.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null)
            .OrderBy(f => f.name, StringComparer.Ordinal).ToArray();
        if (filters.Length == 0) throw new Exception("rock model has no meshes: " + model.name);
        var lodFilters = filters.Where(f => LodIndex(f.name) >= 0).OrderBy(f => LodIndex(f.name)).ToArray();
        if (lodFilters.Length > 1)
        {
            withLods.Add(Variant(model, model.name, lodFilters, material));
            return;
        }
        foreach (var filter in filters) single.Add(Variant(model, filter.name, new[] { filter }, material));
    }

    static RockVariant Variant(GameObject model, string name, MeshFilter[] filters, Material material)
    {
        var toRoot = model.transform.worldToLocalMatrix;
        var local = filters.Select(f => toRoot * f.transform.localToWorldMatrix).ToArray();

        // Bounds of the most detailed mesh in the model's frame; the rock is moved so its base centre is the origin.
        var meshBounds = filters[0].sharedMesh.bounds;
        var min = Vector3.positiveInfinity;
        var max = Vector3.negativeInfinity;
        for (int corner = 0; corner < 8; corner++)
        {
            var p = meshBounds.center + Vector3.Scale(meshBounds.extents,
                new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
            p = local[0].MultiplyPoint3x4(p);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        var shift = Matrix4x4.Translate(new Vector3(-(min.x + max.x) * 0.5f, -min.y, -(min.z + max.z) * 0.5f));
        long triangles = 0;
        var mesh0 = filters[0].sharedMesh;
        for (int s = 0; s < mesh0.subMeshCount; s++) triangles += mesh0.GetIndexCount(s) / 3;
        return new RockVariant
        {
            Name = name,
            Lods = filters.Select(f => f.sharedMesh).ToArray(),
            Local = local.Select(m => shift * m).ToArray(),
            Material = material,
            Size = max - min,
            Triangles = triangles,
        };
    }

    // Returns how many rocks were placed (the plan may ask for more than the budget allows).
    static int BuildRocks(WorldPlan plan, WorldMeshes.HeightField ground, Transform parent, List<NavMeshBuildSource> nav)
    {
        var root = Child(parent, "Rocks");
        var withLods = new List<RockVariant>();
        var single = new List<RockVariant>();
        for (int set = 0; set < WorldMaterials.RockSets.Length; set++) LoadRockSet(set, withLods, single);

        int count = plan.Rocks.Count;
        float stride = Mathf.Max(1f, count / (float)MaxRocks); // thinned evenly, not cut off at the end of the list
        int placed = 0, singles = 0;
        long nearTriangles = 0;
        for (float at = 0f; (int)at < count && placed < MaxRocks; at += stride)
        {
            int i = (int)at;
            var rock = plan.Rocks[i];
            uint h = Hash(plan.Seed, 500000 + i);
            bool useSingle = single.Count > 0 && singles < MaxSingleLodRocks && (withLods.Count == 0 || h % 100 < SingleLodPercent);
            if (!useSingle && withLods.Count == 0) continue;
            var variant = useSingle ? single[(int)((h >> 8) % (uint)single.Count)] : withLods[(int)((h >> 8) % (uint)withLods.Count)];
            if (useSingle) singles++;
            PlaceRock(root, variant, placed, new Vector3(rock.X, ground.Height(rock.X, rock.Z), rock.Z), rock.YawDeg,
                RockWidth * Mathf.Max(rock.Scale, 0.1f), RockSink, nav);
            nearTriangles += variant.Triangles;
            placed++;
        }
        int outcrops = PlaceOutcrops(plan, ground, root, withLods, placed, nav);
        placed += outcrops;
        Debug.Log($"[WorldBuilder] rocks: {outcrops} outcrops on the ridges; placed {placed} of {count} in the plan (cap {MaxRocks}); {singles} are from the set without LODs " +
            $"(cap {MaxSingleLodRocks}, culled below {SingleLodCullHeight:P1} of the screen height); {withLods.Count} shapes with LODs, {single.Count} without; " +
            $"{nearTriangles} triangles if every rock were drawn at full detail");
        return placed;
    }

    static void PlaceRock(Transform parent, RockVariant variant, int index, Vector3 position, float yawDeg, float width, float sink, List<NavMeshBuildSource> nav)
    {
        float scale = width / Mathf.Max(variant.Size.x, variant.Size.z, 0.01f);
        var size = variant.Size * scale;
        var rotation = Quaternion.Euler(0f, yawDeg, 0f);
        var foot = position - Vector3.up * (size.y * sink); // set into the ground a little

        var go = new GameObject($"Rock{index:000}_{variant.Name}");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(foot, rotation);
        go.transform.localScale = Vector3.one * scale;
        go.isStatic = true;

        int n = variant.Lods.Length;
        var lods = new LOD[n];
        for (int i = 0; i < n; i++)
        {
            var child = new GameObject("LOD" + i);
            child.transform.SetParent(go.transform, false);
            var m = variant.Local[i];
            child.transform.localPosition = m.GetColumn(3);
            child.transform.localRotation = m.rotation;
            child.transform.localScale = m.lossyScale;
            child.AddComponent<MeshFilter>().sharedMesh = variant.Lods[i];
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = variant.Material;
            // Only the nearest, most detailed meshes cast shadows: a thousand rocks in the shadow pass cost more
            // than their small shadows are worth under an overcast sky.
            renderer.shadowCastingMode = i == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
            child.isStatic = true;
            float height = n == 1 ? SingleLodCullHeight : FirstLodHeight * Mathf.Pow(LastLodHeight / FirstLodHeight, (float)i / (n - 1));
            lods[i] = new LOD(height, new Renderer[] { renderer });

            // The coarsest mesh is the collision shape; a model without LODs is too dense for that, so its hull is used.
            if (i == n - 1)
            {
                var collider = child.AddComponent<MeshCollider>();
                collider.sharedMesh = variant.Lods[i];
                if (n == 1) collider.convex = true;
            }
        }
        var group = go.AddComponent<LODGroup>();
        group.SetLODs(lods);
        group.RecalculateBounds();

        // The ground under a rock is not walkable. A volume instead of the mesh itself: nobody stands on these.
        nav.Add(new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.ModifierBox,
            size = new Vector3(size.x * 0.8f, size.y + 2f, size.z * 0.8f),
            transform = Matrix4x4.TRS(foot + Vector3.up * (size.y * 0.5f), rotation, Vector3.one),
            area = NotWalkableArea,
        });
    }

    // Outcrops: big rocks set deep into the flanks of the border ridges and the mountain slopes, where the plan
    // scatters none (it keeps to ground a player can stand on). They break the smooth profile of the ridges.
    const float OutcropCell = 13f, OutcropChance = 0.34f, OutcropSink = 0.38f;

    static int PlaceOutcrops(WorldPlan plan, WorldMeshes.HeightField ground, Transform root, List<RockVariant> variants, int firstIndex, List<NavMeshBuildSource> nav)
    {
        if (variants.Count == 0) return 0;
        int cells = Mathf.FloorToInt(plan.Size / OutcropCell), placed = 0;
        for (int cz = 0; cz < cells; cz++)
        {
            for (int cx = 0; cx < cells; cx++)
            {
                uint h = Hash(plan.Seed, 900000 + cz * cells + cx);
                if ((h & 1023u) / 1023f > OutcropChance) continue;
                float x = (cx + ((h >> 10) & 255u) / 255f) * OutcropCell, z = (cz + ((h >> 18) & 255u) / 255f) * OutcropCell;
                ground.Sample(x, z, out float height, out float slopeTan);
                if (height < plan.SeaLevel + 1.5f || slopeTan < 0.5f || slopeTan > 1.9f) continue;
                int region = plan.RegionAt(x, z);
                bool mountain = region >= 0 && plan.Regions[region].Kind == RegionKind.Mountain;
                if (plan.BorderDistance(x, z) > 42f && !mountain) continue;
                if (plan.RoadDistance(x, z) < 6f) continue;
                if (plan.Passes.Any(p => (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < 34f * 34f)) continue;
                var variant = variants[(int)((h >> 26) % (uint)variants.Count)];
                float width = 3.2f + 5.5f * (((h >> 4) & 63u) / 63f) * (((h >> 12) & 63u) / 63f);
                PlaceRock(root, variant, firstIndex + placed, new Vector3(x, height, z), (h >> 7) % 360u, width, OutcropSink, nav);
                placed++;
            }
        }
        return placed;
    }
}
