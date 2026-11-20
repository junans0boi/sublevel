using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Trees, bushes, grass and the sea stacks off the coast (#71, ADR-070). Where every plant stands is Vegetation.Plan
// (Game.Core); the meshes and materials are WorldVegetation's. Plants are drawn instanced by one VegetationRenderer,
// grass by one GrassRenderer from a baked map; only the trunks are GameObjects (a capsule each, for bodies, shots and
// the navmesh).
public static partial class WorldBuilder
{
    const string GrassMapPath = WorldMaterials.GeneratedDir + "/World_Grass.bytes";
    const float PlantSink = 0.08f;          // feet a little into the ground
    const float TrunkCell = 32f;            // trunk colliders of one square share a GameObject
    const float MaxTrunkCollider = 7f;      // capsule height cap: the crown above it does not stop anything

    // Sea stacks: rocks standing in the sea beyond the wading line, so the view out from the shore has something in
    // it and the outline reads as a coast rather than the edge of a plate.
    const int OffshoreRays = 44;
    const float OffshoreChance = 0.45f, OffshoreDepth = 3f;

    static void BuildVegetation(WorldPlan plan, WorldMeshes.HeightField ground, Terrain terrain, Transform parent, WorldMeshes.Store store,
        List<NavMeshBuildSource> nav)
    {
        var watch = Stopwatch.StartNew();
        var kit = WorldVegetation.Build(store);
        var plants = Vegetation.Plan(plan, System.Environment.GetEnvironmentVariable("FOREST") != "0"); // FOREST=0: the map before the forest belt, for before/after captures
        var root = Child(parent, "Vegetation");
        var trunks = Child(root, "Trunks");
        trunks.gameObject.isStatic = true;

        var prototype = new int[plants.Count];
        var placement = new Vector4[plants.Count];
        var yaw = new float[plants.Count];
        int colliders = 0;
        var cellObjects = new Dictionary<long, GameObject>();
        for (int i = 0; i < plants.Count; i++)
        {
            var p = plants[i];
            int proto = kit.Prototype(p.Kind, p.Variant);
            float y = ground.Height(p.X, p.Z) - PlantSink;
            prototype[i] = proto;
            placement[i] = new Vector4(p.X, y, p.Z, p.Scale);
            yaw[i] = p.YawDeg;
            if (!p.HasTrunk) continue;

            // Bushes stop nothing; trunks stop bodies and shots and are holes in the navmesh.
            float radius = kit.TrunkRadius[proto] * p.Scale + 0.05f;
            float height = Mathf.Min(kit.TrunkHeight[proto] * p.Scale, MaxTrunkCollider);
            // One GameObject per TrunkCell square carries the capsules of all its trunks (ADR-270: ~30 000 trunks, a GameObject each
            // would bloat the scene and the load), each capsule offset by its own centre.
            int cx = Mathf.FloorToInt(p.X / TrunkCell), cz = Mathf.FloorToInt(p.Z / TrunkCell);
            long key = ((long)cx << 32) ^ (uint)cz;
            if (!cellObjects.TryGetValue(key, out var cell))
            {
                var cellGo = new GameObject($"TrunkCell_{cx}_{cz}");
                cellGo.transform.SetParent(trunks, false);
                cellGo.transform.position = new Vector3(cx * TrunkCell, 0f, cz * TrunkCell);
                cellGo.isStatic = true;
                cellObjects[key] = cell = cellGo;
            }
            colliders++;
            var capsule = cell.AddComponent<CapsuleCollider>();
            capsule.radius = radius;
            capsule.height = height;
            capsule.center = new Vector3(p.X - cx * TrunkCell, y + height * 0.5f, p.Z - cz * TrunkCell);
            float side = radius * 2f + 0.3f;
            nav.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.ModifierBox,
                size = new Vector3(side, height + 2f, side),
                transform = Matrix4x4.TRS(new Vector3(p.X, y + height * 0.5f, p.Z), Quaternion.identity, Vector3.one),
                area = NotWalkableArea,
            });
        }
        root.gameObject.AddComponent<VegetationRenderer>().Fill(kit.Prototypes, prototype, placement, yaw);

        var map = BakeGrass(plan, ground, terrain.terrainData, out int grassy, out int reeds);
        var grass = Child(root, "Grass");
        grass.gameObject.AddComponent<GrassRenderer>().Fill(map, Vegetation.GrassMapResolution(plan.Size), Vegetation.GrassMapCell, plan.Seed,
            terrain, kit.GrassShort, kit.GrassTall, kit.Grass, kit.FlowerClump, kit.Flowers);

        long nearTriangles = 0;
        foreach (var p in plants) nearTriangles += Triangles(kit.Prototypes[kit.Prototype(p.Kind, p.Variant)].Lods[0].Mesh);
        Debug.Log($"[WorldBuilder] vegetation in {watch.Elapsed.TotalSeconds:F1} s: {plants.Count(p => p.Kind == PlantKind.Broadleaf)} broadleaf, " +
            $"{plants.Count(p => p.Kind == PlantKind.Conifer)} conifer ({colliders} trunk colliders, plan {plan.Trees.Count}), " +
            $"{plants.Count(p => p.Kind == PlantKind.Bush)} bushes; {nearTriangles} triangles if all were at full detail; " +
            $"grass map {grassy} grass and {reeds} reed cells of {Vegetation.GrassMapCell} m");
    }

    static long Triangles(Mesh mesh)
    {
        long n = 0;
        for (int s = 0; s < mesh.subMeshCount; s++) n += mesh.GetIndexCount(s) / 3;
        return n;
    }

    // Where grass grows, one byte per GrassMapCell (Vegetation.EncodeGrass): the terrain's own grass layer as it was
    // painted (so grass stands where the ground looks grassy), thinned in patches, kept off roads and pads; reeds in
    // the wetland shallows.
    static TextAsset BakeGrass(WorldPlan plan, WorldMeshes.HeightField ground, TerrainData data, out int grassy, out int reeds)
    {
        int res = Vegetation.GrassMapResolution(plan.Size);
        float cell = Vegetation.GrassMapCell;
        int ares = data.alphamapResolution;
        var alpha = data.GetAlphamaps(0, 0, ares, ares);
        var bytes = new byte[res * res];
        grassy = 0;
        reeds = 0;
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float wx = (x + 0.5f) * cell, wz = (z + 0.5f) * cell;
                float h = ground.Height(wx, wz);
                float allowed = Vegetation.GrassAllowed(plan, wx, wz, h);
                if (allowed <= 0f) continue;
                float patch = Mathf.Clamp01(0.45f + 0.75f * Mathf.PerlinNoise(wx / 19f + 3.3f, wz / 19f + 8.1f) - 0.2f * Mathf.PerlinNoise(wx / 5f + 1.7f, wz / 5f + 6.2f));
                bool tall = Vegetation.TallGrass(plan, wx, wz, h);
                float meadow = tall ? 0f : Vegetation.TallMeadow(plan, wx, wz, h);
                if (meadow > 0.25f) tall = true;
                float density;
                if (tall && meadow <= 0f) density = allowed * (0.5f + 0.5f * patch);
                else
                {
                    int ax = Mathf.Clamp((int)(wx / plan.Size * ares), 0, ares - 1), az = Mathf.Clamp((int)(wz / plan.Size * ares), 0, ares - 1);
                    int region = plan.RegionAt(wx, wz);
                    var kind = region >= 0 ? plan.Regions[region].Kind : RegionKind.Town;
                    float regionShare = kind == RegionKind.Factory ? 0.6f : kind == RegionKind.Mountain ? 0.85f : 1f;
                    density = allowed * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.55f, alpha[az, ax, 0])) * regionShare * patch;
                    if (tall) density = Mathf.Max(density, allowed * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.55f, alpha[az, ax, 0])) * regionShare * (0.65f + 0.35f * patch)); // tall patches are denser than short grass
                }
                byte v = Vegetation.EncodeGrass(density, tall);
                bytes[z * res + x] = v;
                if (v >= 128) reeds++;
                else if (v > 0) grassy++;
            }
        }
        File.WriteAllBytes(GrassMapPath, bytes);
        AssetDatabase.ImportAsset(GrassMapPath);
        return SceneLook.Load<TextAsset>(GrassMapPath);
    }

    // Big rocks set in the sea a little way out, beyond where anyone can wade: stretched upward into stacks.
    static int BuildOffshore(WorldPlan plan, WorldMeshes.HeightField ground, Transform parent)
    {
        var withLods = new List<RockVariant>();
        var single = new List<RockVariant>();
        for (int set = 0; set < WorldMaterials.RockSets.Length; set++) LoadRockSet(set, withLods, single);
        if (withLods.Count == 0) return 0;
        var root = Child(parent, "Offshore");
        var unused = new List<NavMeshBuildSource>(); // out of reach: nothing to cut out of the navmesh
        var centre = new Vector2(plan.Size * 0.5f, plan.Size * 0.5f);
        int placed = 0;
        for (int i = 0; i < OffshoreRays; i++)
        {
            uint h = Hash(plan.Seed, 950000 + i);
            if (Unit(h, 0) > OffshoreChance) continue;
            float angle = (i + 0.8f * Unit(h, 10)) / OffshoreRays * Mathf.PI * 2f;
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            // Out from the middle to where the sea is OffshoreDepth deep, then a little further.
            float r = 200f;
            bool found = false;
            for (; r < plan.Size * 0.72f; r += 2f)
            {
                var p = centre + dir * r;
                if (ground.Height(p.x, p.y) < plan.SeaLevel - OffshoreDepth) { found = true; break; }
            }
            if (!found) continue;
            r += 6f + 50f * Unit(h, 20);
            int count = 1 + (int)(Unit(h, 5) * 2.99f);
            for (int k = 0; k < count; k++)
            {
                uint kh = Hash((int)h, 17 + k);
                var side = new Vector2(-dir.y, dir.x);
                var at = centre + dir * (r + 8f * k) + side * (Unit(kh, 0) - 0.5f) * 18f;
                if (at.x < 6f || at.y < 6f || at.x > plan.Size - 6f || at.y > plan.Size - 6f) continue;
                float floor = ground.Height(at.x, at.y);
                if (floor > plan.SeaLevel - OffshoreDepth + 0.5f) continue;
                var variant = withLods[(int)((kh >> 8) % (uint)withLods.Count)];
                float width = (k == 0 ? 9f : 5f) + 12f * Unit(kh, 10) * Unit(kh, 20);
                PlaceRock(root, variant, placed, new Vector3(at.x, floor, at.y), (kh >> 3) % 360u, width, 0.05f, unused);
                // Tall enough to stand 2..9 m out of the water whatever the depth.
                var rock = root.GetChild(root.childCount - 1);
                float standing = rock.localScale.y * variant.Size.y;
                float wanted = plan.SeaLevel - floor + 2f + 7f * Unit(kh, 25);
                float stretch = Mathf.Clamp(wanted / Mathf.Max(0.5f, standing), 1f, 3.2f);
                var scale = rock.localScale;
                scale.y *= stretch;
                rock.localScale = scale;
                rock.GetComponent<LODGroup>().RecalculateBounds();
                placed++;
            }
        }
        Debug.Log($"[WorldBuilder] offshore: {placed} sea stacks");
        return placed;
    }

    // Forest belt check on the built World scene (ADR-270; tools/unity.sh run-gfx WorldBuilder.ForestCheck, FOREST_TAG names the outputs): mean sight distance over
    // 8 horizontal physics rays at eye height from slope points, a shot-blocking check, eye-level and aerial pictures, and render stats of the densest view.
    public static void ForestCheck()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        Physics.SyncTransforms();
        string tag = System.Environment.GetEnvironmentVariable("FOREST_TAG") ?? "now";
        Directory.CreateDirectory(OverviewDir);
        DynamicGI.UpdateEnvironment();
        var plan = WorldGenerator.Generate(LoadSpec());
        var terrain = Object.FindFirstObjectByType<Terrain>();
        var info = Object.FindFirstObjectByType<WorldInfo>();
        Vector3 Ground(float x, float z) => new Vector3(x, terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.GetPosition().y, z);

        var pts = new List<Vector2>();
        for (float z = 20f; z < plan.Size; z += 40f)
            for (float x = 20f; x < plan.Size; x += 40f)
                if (Vegetation.ForestDensity(plan, x, z) >= 0.5f) pts.Add(new Vector2(x, z));
        double sum = 0; int n = 0, under30 = 0;
        foreach (var pt in pts)
        {
            var eye = Ground(pt.x, pt.y) + Vector3.up * 1.7f;
            for (int d = 0; d < 8; d++)
            {
                float a = d * Mathf.PI / 4f + 0.2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float seen = Physics.Raycast(eye, dir, out var hit, 150f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.distance : 150f;
                sum += seen; n++;
                if (seen < 30f) under30++;
            }
        }
        string line = $"[ForestCheck:{tag}] {pts.Count} slope points, 8 rays each: mean sight {sum / System.Math.Max(1, n):F1} m, {100.0 * under30 / System.Math.Max(1, n):F0}% of rays blocked within 30 m";
        Debug.Log(line);
        File.AppendAllText($"Logs/forest_{tag}.txt", line + "\n");

        // A shot at a trunk from 4 m away stops at it.
        var trunks = Object.FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None).Where(c => c.transform.parent != null && c.transform.parent.name == "Trunks").Take(400).ToList();
        int stopped = 0, tried = 0;
        foreach (var c in trunks)
        {
            if (tried >= 40) break;
            var centre = c.transform.TransformPoint(c.center);
            var from = new Vector3(centre.x + 4f, centre.y - c.height * 0.5f + 1.3f, centre.z);
            tried++;
            if (Physics.Raycast(from, (new Vector3(centre.x, from.y, centre.z) - from).normalized, out var hit, 4.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.distance < 4f) stopped++;
        }
        Debug.Log($"[ForestCheck:{tag}] shot at {tried} trunks from 4 m at chest height: {stopped} stopped short of the centre");
        File.AppendAllText($"Logs/forest_{tag}.txt", $"shots at trunks: {stopped}/{tried}\n");

        var cam = new GameObject("ForestCamera").AddComponent<Camera>();
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 1500f; cam.fieldOfView = 60f;
        SceneLook.EnablePost(cam);
        UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam).SetRenderer(info.RendererIndex);
        // Eye-level views at a few slope points (every 25th), looking along the contour.
        int shots = 0;
        for (int i = 3; i < pts.Count && shots < 4; i += System.Math.Max(1, pts.Count / 5))
        {
            var e = Ground(pts[i].x, pts[i].y) + Vector3.up * 1.7f;
            SceneLook.Shot(cam, $"{OverviewDir}/forest_{tag}_eye{shots}.png", e, e + new Vector3(0.7f, 0f, 0.7f) * 40f);
            shots++;
        }
        // Aerial: the mountain regions from the south at an angle.
        foreach (var region in info.Regions.Where(r => r.Kind == RegionKind.Mountain).Take(2))
        {
            var c = Ground(region.Hub.x, region.Hub.z);
            cam.fieldOfView = 50f;
            SceneLook.Shot(cam, $"{OverviewDir}/forest_{tag}_air_{region.Id}.png", c + new Vector3(0f, 230f, -380f), c + Vector3.up * 40f);
        }
        CarInTheWood(cam, plan, pts, Ground, tag);
        var statAt = Ground(pts[pts.Count / 2].x, pts[pts.Count / 2].y) + Vector3.up * 1.7f;
        string stats = ViewStats(cam, statAt, statAt + new Vector3(0.7f, 0f, 0.7f) * 40f, tag);
        Debug.Log(stats);
        File.AppendAllText($"Logs/forest_{tag}.txt", stats + "\n");
        cam.targetTexture = null;
        EditorApplication.Exit(0);
    }

    // ADR-487: a car parked in the wood, seen from the chase camera and from above, at the first slope points that are drivable (tan < 0.35),
    // have trunks round them (at least 6 within 15 m: in the wood, not a glade) and room for the car (no trunk within 2.6 m).
    static void CarInTheWood(Camera cam, WorldPlan plan, List<Vector2> pts, System.Func<float, float, Vector3> ground, string tag)
    {
        var prefab = AssetDatabase.FindAssets("t:Prefab", new[] { VehicleModels.OutDir })
            .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
            .FirstOrDefault(p => p != null && p.GetComponent<Game.Net.Vehicle>() != null && !p.name.ToLowerInvariant().Contains("moto") && !p.name.ToLowerInvariant().Contains("bike"));
        if (prefab == null) { Debug.LogWarning("[ForestCheck] no car prefab"); return; }
        bool Trunk(Collider c) => c.transform.parent != null && c.transform.parent.name == "Trunks";
        int shots = 0;
        for (int i = 0; i < pts.Count && shots < 3; i += 7)
        {
            var at = ground(pts[i].x, pts[i].y);
            if (plan.SlopeTan(pts[i].x, pts[i].y) > 0.35f) continue;
            var near = Physics.OverlapSphere(at + Vector3.up, 15f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Count(Trunk);
            if (near < 6 || Physics.OverlapSphere(at + Vector3.up, 2.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Any(Trunk)) continue;
            float e = 1f, gx = plan.Height(pts[i].x + e, pts[i].y) - plan.Height(pts[i].x - e, pts[i].y), gz = plan.Height(pts[i].x, pts[i].y + e) - plan.Height(pts[i].x, pts[i].y - e);
            var uphill = new Vector3(gx, 0f, gz).sqrMagnitude > 1e-4f ? new Vector3(gx, 0f, gz).normalized : Vector3.forward;
            var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            car.transform.SetPositionAndRotation(at + Vector3.up * 0.05f, Quaternion.LookRotation(uphill));
            foreach (var body in car.GetComponentsInChildren<Rigidbody>()) body.isKinematic = true;
            cam.fieldOfView = 60f;
            VegetationRenderer.ClearAround = cam;   // as the car camera does in play
            VegetationRenderer.ClearPivot = at;
            SceneLook.Shot(cam, $"{OverviewDir}/forest_{tag}_car{shots}_chase.png", at - uphill * 6.5f + Vector3.up * 2.6f, at + uphill * 12f + Vector3.up * 1.2f);
            VegetationRenderer.ClearAround = null;
            SceneLook.Shot(cam, $"{OverviewDir}/forest_{tag}_car{shots}_top.png", at + Vector3.up * 55f - uphill * 0.01f, at);
            Object.DestroyImmediate(car);
            Debug.Log($"[ForestCheck:{tag}] car view {shots} at ({at.x:F0},{at.z:F0}): {near} trunks within 15 m");
            shots++;
        }
    }

    // Render stats of one view over 120 frames, plus the instanced vegetation estimated from the renderer's own LOD rule.
    static string ViewStats(Camera cam, Vector3 stat, Vector3 look, string tag)
    {
        cam.fieldOfView = 60f;
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.transform.position = stat; cam.transform.LookAt(look);
        var times = new List<double>();
        for (int i = 0; i < 125; i++)
        {
            var w = Stopwatch.StartNew(); cam.Render(); w.Stop();
            if (i >= 5) times.Add(w.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        // Instanced draws are not in the editor's stats in batch mode: estimate them from the renderer's own LOD rule (frustum + reach, high level).
        var veg = Object.FindFirstObjectByType<VegetationRenderer>();
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        var avg = new double[3, 3]; var subs = new double[3, 3]; var kinds = new int[3]; // kind, lod
        foreach (var proto in veg.Prototypes)
        {
            kinds[(int)proto.Kind]++;
            for (int l = 0; l < proto.Lods.Length && l < 3; l++) { avg[(int)proto.Kind, l] += Triangles(proto.Lods[l].Mesh); subs[(int)proto.Kind, l] += proto.Lods[l].Mesh.subMeshCount; }
        }
        var inView = new int[3, 3]; double estTris = 0; int visible = 0;
        float zoom = Vegetation.Zoom(cam.fieldOfView);
        for (int i = 0; i < veg.Count; i++)
        {
            var foot = veg.Foot(i); var at = new Vector3(foot.x, foot.y, foot.z);
            float dist = Vector3.Distance(stat, at);
            if (dist > 400f || !GeometryUtility.TestPlanesAABB(planes, new Bounds(at + Vector3.up * 5f, new Vector3(10f, 10f, 10f)))) continue;
            var kind = veg.KindOf(i);
            int lod = Vegetation.Lod(kind, dist, 1.3f, zoom, 2);
            if (lod < 0) continue;
            int k = (int)kind; lod = Mathf.Min(lod, kind == PlantKind.Bush ? 0 : 2);
            inView[k, lod]++; visible++;
            estTris += avg[k, lod] / System.Math.Max(1, kinds[k]);
        }
        double estDraws = 0;
        for (int k = 0; k < 3; k++) for (int l = 0; l < 3; l++)
            if (inView[k, l] > 0) estDraws += (subs[k, l] / System.Math.Max(1, kinds[k])) * 5 * System.Math.Ceiling(inView[k, l] / 5.0 / 1023.0);
        times.Sort();
        string stats = $"[ForestCheck:{tag}] eye view: {visible} plants drawn (conifer LOD0/1/2 {inView[1, 0]}/{inView[1, 1]}/{inView[1, 2]}, broadleaf {inView[0, 0]}/{inView[0, 1]}/{inView[0, 2]}, bush {inView[2, 0]}), " +
            $"vegetation estimate ~{estTris / 1000.0:F0}k triangles ~{estDraws:F0} draws; cpu render avg {times.Average():F2} ms p99 {times[(int)(times.Count * 0.99)]:F2} ms (editor, whole scene, CPU submission only)";
        cam.targetTexture = null;
        return stats;
    }

    // ---- pictures for review (called from RenderOverview) ----

    // In a mountain wood, a town street with its trees, the wetland reeds, a meadow at grass height, and out to sea
    // from the shore past the stacks.
    static void RenderVegetationViews(Camera cam, WorldInfo info, Terrain terrain)
    {
        Vector3 Ground(float x, float z) => new Vector3(x, terrain.SampleHeight(new Vector3(x, 0f, z)), z);
        var plan = WorldGenerator.Generate(LoadSpec());
        var plants = Vegetation.Plan(plan);
        var trees = plants.Where(p => p.HasTrunk).ToList();
        int Around(Plant at, float r) => trees.Count(t => (t.X - at.X) * (t.X - at.X) + (t.Z - at.Z) * (t.Z - at.Z) < r * r);

        int mountain = plan.Regions.First(r => r.Kind == RegionKind.Mountain).Index;
        var wood = trees.Where(t => t.Region == mountain).OrderByDescending(t => Around(t, 18f)).FirstOrDefault();
        if (wood.Scale > 0f)
        {
            // A step off the densest tree, looking across the wood.
            var eye = Ground(wood.X + 2.2f, wood.Z + 1.4f) + Vector3.up * 1.7f;
            var dense = trees.Where(t => t.Region == mountain && Around(t, 14f) > 3).OrderBy(t => (t.X - wood.X) * (t.X - wood.X) + (t.Z - wood.Z) * (t.Z - wood.Z)).Skip(6).FirstOrDefault();
            var look = dense.Scale > 0f ? Ground(dense.X, dense.Z) + Vector3.up * 3f : eye + Vector3.forward * 20f;
            SceneLook.Shot(cam, OverviewDir + "/world_forest.png", eye, look);
            SceneLook.Shot(cam, OverviewDir + "/world_forest_far.png", Ground(wood.X, wood.Z - 90f) + Vector3.up * 1.7f, Ground(wood.X, wood.Z) + Vector3.up * 6f);
        }

        int town = plan.Regions.First(r => r.Kind == RegionKind.Town).Index;
        var street = plants.Skip(plan.Trees.Count).Where(p => p.HasTrunk && p.Region == town).FirstOrDefault();
        if (street.Scale > 0f)
            SceneLook.Shot(cam, OverviewDir + "/world_town_trees.png", Ground(street.X + 14f, street.Z - 10f) + Vector3.up * 1.7f, Ground(street.X, street.Z) + Vector3.up * 3f);

        var wetland = info.Regions.First(r => r.Kind == RegionKind.Wetland);
        Vector3? reeds = null;
        for (int i = 0; i < 4000 && reeds == null; i++)
        {
            float a = i * 0.618f * Mathf.PI * 2f, r = 6f + i * 0.06f;
            float x = wetland.Hub.x + Mathf.Cos(a) * r, z = wetland.Hub.z + Mathf.Sin(a) * r;
            float h = terrain.SampleHeight(new Vector3(x, 0f, z));
            if (h > plan.SeaLevel + 0.15f && Vegetation.TallGrass(plan, x, z, h) && Vegetation.GrassAllowed(plan, x, z, h) > 0.8f) reeds = new Vector3(x, h, z);
        }
        if (reeds != null)
            SceneLook.Shot(cam, OverviewDir + "/world_reeds.png", reeds.Value + new Vector3(-6f, 1.6f, -6f), reeds.Value + new Vector3(4f, 0.4f, 4f));

        var townHub = info.Regions.First(r => r.Kind == RegionKind.Town).Hub;
        Vector3? meadow = null;
        for (int i = 0; i < 4000 && meadow == null; i++)
        {
            float a = i * 0.618f * Mathf.PI * 2f, r = 30f + i * 0.08f;
            float x = townHub.x + Mathf.Cos(a) * r, z = townHub.z + Mathf.Sin(a) * r;
            float h = terrain.SampleHeight(new Vector3(x, 0f, z));
            if (Vegetation.GrassAllowed(plan, x, z, h) > 0.95f && plan.RoadDistance(x, z) > 12f && plan.SlopeTan(x, z) < 0.15f) meadow = new Vector3(x, h, z);
        }
        if (meadow != null)
        {
            SceneLook.Shot(cam, OverviewDir + "/world_grass.png", meadow.Value + Vector3.up * 1.7f, meadow.Value + new Vector3(12f, 0.2f, 9f));
            SceneLook.Shot(cam, OverviewDir + "/world_grass_prone.png", meadow.Value + Vector3.up * 0.4f, meadow.Value + new Vector3(12f, 0.3f, 9f));
        }

        // The cell with the most wild flowers, seen from a step away at eye height.
        {
            var bytes = File.ReadAllBytes(GrassMapPath);
            int res = Vegetation.GrassMapResolution(plan.Size);
            var scratch = new List<GrassClump>();
            Vector3? flowers = null;
            int most = 0;
            for (int cz = 10; cz < (int)(plan.Size / Vegetation.GrassCell) - 10; cz += 2)
                for (int cx = 10; cx < (int)(plan.Size / Vegetation.GrassCell) - 10; cx += 2)
                {
                    Vegetation.ScatterGrass(bytes, res, Vegetation.GrassMapCell, cx, cz, plan.Seed, scratch);
                    int n = scratch.Count(c => c.Flower);
                    if (n <= most) continue;
                    var mid = scratch.Where(c => c.Flower).Aggregate(Vector2.zero, (sum, c) => sum + new Vector2(c.X, c.Z)) / n;
                    most = n;
                    flowers = Ground(mid.x, mid.y);
                }
            Debug.Log("[WorldBuilder] flower view: most flowers in one cell " + most);
            if (flowers != null)
            {
                SceneLook.Shot(cam, OverviewDir + "/world_flowers.png", flowers.Value + new Vector3(-3.5f, 1.5f, -3.5f), flowers.Value + new Vector3(0f, 0.2f, 0f));
                SceneLook.Shot(cam, OverviewDir + "/world_flowers_wide.png", flowers.Value + new Vector3(-9f, 1.7f, -9f), flowers.Value + new Vector3(4f, 0.6f, 4f));
            }
        }

        var offshore = GameObject.Find("World/Offshore");
        if (offshore != null && offshore.transform.childCount > 0)
        {
            var stack = offshore.transform.GetChild(0).position;
            var centre = new Vector3(info.Size * 0.5f, 0f, info.Size * 0.5f);
            var inward = new Vector3(centre.x - stack.x, 0f, centre.z - stack.z).normalized;
            var p = stack;
            for (int i = 0; i < 400 && terrain.SampleHeight(p) < plan.SeaLevel + 0.6f; i++) p += inward * 2f;
            p += inward * 6f;
            SceneLook.Shot(cam, OverviewDir + "/world_offshore.png", Ground(p.x, p.z) + Vector3.up * 1.7f, new Vector3(stack.x, plan.SeaLevel + 3f, stack.z));
        }
    }
}
