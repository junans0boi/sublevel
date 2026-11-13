using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using static EditorFields;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Generates the outdoor map scene "World" (ADR-050/052, issues #49 and #50) from the world plan: terrain, sea,
// roads, enterable buildings, elevator stations, rocks, trees, bushes and grass (#71), navmesh and a host-run match.
// Run headless: tools/unity.sh run WorldBuilder.Build (or run-gfx; nothing here needs a window).
// Pictures for review: tools/unity.sh run-gfx WorldBuilder.RenderOverview (needs a graphics device).
public static partial class WorldBuilder
{
    public const string ScenePath = "Assets/_Project/Scenes/World.unity";
    const string TerrainDataPath = WorldMaterials.GeneratedDir + "/World_Terrain.asset";
    const string NavMeshPath = WorldMaterials.GeneratedDir + "/World_NavMesh.asset";
    const string MeshPath = WorldMaterials.GeneratedDir + "/World_Meshes.asset";
    const string OverviewDir = "Logs/world";

    // Terrain (docs/research/open-world-tech.md 2-2, 4): one sample per metre; the pixel error and base map
    // distance trade distant detail for draw cost.
    const int HeightmapResolution = 1025;
    const int AlphamapResolution = 1024;
    const int BaseMapResolution = 2048;
    const float TerrainPixelError = 8f;
    const float TerrainBaseMapDistance = 110f;

    const int WaterDepthResolution = 1024;
    const float WaterDepthRange = 4f;       // metres of depth the water shader can tell apart
    const float SeaMargin = 3000f;          // the sea plane reaches this far beyond the map, past the far clip
    // Players wade to this depth and no deeper: a ring of invisible walls follows that depth contour.
    const float BarrierDepth = 1.2f;
    const float BarrierCell = 8f, BarrierHeight = 8f, BarrierThickness = 0.6f;
    const int IgnoreRaycastLayer = 2;       // built-in layer: shots, sight lines and the camera pass through

    const float FarClip = 700f;
    const float FogStart = 90f, FogEnd = 640f;
    static readonly Color FogColor = new Color(0.60f, 0.62f, 0.64f); // the overcast HDRI's horizon

    const float OutdoorAmbient = 0.8f, SunIntensity = 1.25f, SunShadowStrength = 0.8f;

    // The player's CharacterController (radius, height, step); the slope is WorldRules.WalkSlopeDeg.
    const float AgentRadius = 0.35f, AgentHeight = 1.8f, AgentStepHeight = 0.3f;
    // A third of the radius (Unity's default). The research note suggests radius / 2 (0.175 m) to bake faster, but
    // a 1.3 m door then keeps as little as one voxel of walkable width; switch only if the bake time hurts.
    const float NavVoxelSize = AgentRadius / 3f;
    const int NavTileSize = 256;
    const int NotWalkableArea = 1;          // built-in navmesh area

    const int MaxLootSpawns = 250;
    const float LootLift = 0.3f;            // loot points give the surface an item lies on
    const float SpawnLift = 0.1f;

    public static void Build()
    {
        try
        {
            Generate();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Debug.LogError("[WorldBuilder] FAILED: " + e.Message);
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.Exit(0);
    }

    static void Generate()
    {
        var total = Stopwatch.StartNew();
        var spec = LoadSpec();
        var plan = WorldGenerator.Generate(spec);
        var violations = WorldRules.Validate(plan);
        if (violations.Count > 0)
            throw new Exception("world plan breaks design rules: " + string.Join("; ", violations.Select(v => v.Rule + " " + v.Detail)));
        Debug.Log($"[WorldBuilder] plan (seed {plan.Seed}) generated and validated in {total.Elapsed.TotalSeconds:F1} s: {plan.Regions.Count} regions, " +
            $"{plan.Lots.Count} lots, {plan.Elevators.Count} elevators, {plan.Roads.Count} roads, {plan.Rocks.Count} rocks, {plan.Trees.Count} trees");

        Directory.CreateDirectory(WorldMaterials.GeneratedDir);
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        Directory.CreateDirectory(SceneLook.MaterialDir); // SceneLook keeps its sky material there
        var postProfile = RenderSetup.Apply();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        WorldMaterials.Prepare();
        var store = new WorldMeshes.Store(MeshPath);

        var heights01 = new float[HeightmapResolution, HeightmapResolution];
        plan.FillHeights(heights01, 1f / plan.MaxHeight);
        for (int z = 0; z < HeightmapResolution; z++)
            for (int x = 0; x < HeightmapResolution; x++)
                heights01[z, x] = Mathf.Clamp01(heights01[z, x]);
        var ground = new WorldMeshes.HeightField(heights01, plan.Size, plan.MaxHeight);
        Debug.Log($"[WorldBuilder] heights sampled at {total.Elapsed.TotalSeconds:F1} s");

        Lighting(postProfile);
        var world = new GameObject("World");
        var terrain = BuildTerrain(plan, heights01, ground, world.transform);
        Debug.Log($"[WorldBuilder] terrain built at {total.Elapsed.TotalSeconds:F1} s");
        BuildSea(plan, ground, world.transform, store);
        int barriers = BuildSeaBarrier(plan, ground, world.transform);
        BuildRoads(plan, ground, world.transform, store);

        var navSources = new List<NavMeshBuildSource>();
        var lots = new List<Placed>();
        var stations = new List<Placed>();
        BuildBuildings(plan, ground, world.transform, store, navSources, lots, stations);
        int holes = CutShaftHoles(terrain.terrainData, plan, stations);
        EditorUtility.SetDirty(terrain.terrainData);
        Debug.Log($"[WorldBuilder] elevator shafts: {stations.Count} shafts and cars, {holes} terrain cells opened over them");
        int rocks = BuildRocks(plan, ground, world.transform, navSources);
        BuildOffshore(plan, ground, world.transform);
        BuildVegetation(plan, ground, terrain, world.transform, store, navSources);
        BuildDressing(plan, ground, world.transform, store, navSources, lots);
        Debug.Log($"[WorldBuilder] structures built at {total.Elapsed.TotalSeconds:F1} s: {store.Count} generated meshes, {store.Triangles} triangles in them");

        var navMesh = BakeNavMesh(plan, terrain, navSources);
        Set(world.AddComponent<ZoneNavMesh>(), "_data", p => p.objectReferenceValue = navMesh);

        var info = FillInfo(world, plan, ground, stations);
        int rendererIndex = OutdoorRenderer();
        info.SetRenderer(rendererIndex);
        var loot = PickLoot(lots, MaxLootSpawns);
        AddMatch(plan, ground, info, loot);
        AddCamera(plan, rendererIndex);

        // No static batching: it would copy every rock's mesh into the build once per rock (a first build came to
        // 500 MB of combined meshes). The building and road meshes are already one mesh per material.
        foreach (var t in world.GetComponentsInChildren<Transform>(true))
        {
            var flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
            if ((flags & StaticEditorFlags.BatchingStatic) != 0)
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        RegisterScene();
        AssetDatabase.SaveAssets();
        Debug.Log($"[WorldBuilder] Saved {ScenePath} in {total.Elapsed.TotalSeconds:F0} s: {lots.Count} buildings, {stations.Count} elevator stations, " +
            $"{rocks} rocks (plan {plan.Rocks.Count}), {barriers} sea barrier segments, {loot.Count} loot spawns");
    }

    static WorldSpec LoadSpec()
    {
        var text = Resources.Load<TextAsset>("GameData/world");
        if (text != null)
        {
            var spec = JsonUtility.FromJson<WorldSpec>(text.text);
            if (spec != null && spec.Regions != null && spec.Regions.Length > 0) return spec;
            Debug.LogWarning("[WorldBuilder] GameData/world.json has no regions, using the built-in default spec");
        }
        else Debug.LogWarning("[WorldBuilder] GameData/world.json not found, using the built-in default spec");
        return WorldSpec.CreateDefault();
    }

    // SceneBuilder owns the build list; until World is in its BuildOrder this keeps the scene loadable.
    static void RegisterScene()
    {
        SceneBuilder.RegisterBuildScenes();
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == ScenePath)) return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static Transform Child(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    // ---- sky, light, fog ----

    // The approved look (HDRI sky and ambient, key + fill, post volume) plus fog that ends before the far clip,
    // so distant ground fades out instead of being cut off.
    static void Lighting(VolumeProfile postProfile)
    {
        SceneLook.SetupSky();
        SceneLook.AddLights();
        SceneLook.AddPostVolume(postProfile);
        // Outdoors under an overcast sky: more sky light and softer sun shadows than the indoor test scenes.
        RenderSettings.ambientIntensity = OutdoorAmbient;
        var sun = GameObject.Find("Sun");
        if (sun != null)
        {
            var light = sun.GetComponent<Light>();
            light.intensity = SunIntensity;
            light.shadowStrength = SunShadowStrength;
        }
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogStartDistance = FogStart;
        RenderSettings.fogEndDistance = FogEnd;
        // Shadow distance stays the project's 50 m (Game_URP.asset): overcast light hides the missing far shadows.
    }

    // ---- terrain ----

    static Terrain BuildTerrain(WorldPlan plan, float[,] heights01, WorldMeshes.HeightField ground, Transform parent)
    {
        // The data must be an asset before layers and alphamaps are set, or the splat textures are not saved with it.
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        if (data == null)
        {
            data = new TerrainData();
            AssetDatabase.CreateAsset(data, TerrainDataPath);
        }
        data.heightmapResolution = HeightmapResolution; // before size: changing the resolution rescales it
        data.size = new Vector3(plan.Size, plan.MaxHeight, plan.Size);
        data.SetHeights(0, 0, heights01);

        data.terrainLayers = WorldMaterials.TerrainLayers();
        data.alphamapResolution = AlphamapResolution;
        data.baseMapResolution = BaseMapResolution;
        int res = data.alphamapResolution;
        var alpha = new float[res, res, 4];
        float cell = plan.Size / res;
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float wx = (x + 0.5f) * cell, wz = (z + 0.5f) * cell;
                ground.Sample(wx, wz, out float height, out float slopeTan);
                plan.Splat(wx, wz, height, slopeTan, out float grass, out float dirt, out float rock, out float sand);
                // Broad patches so the plains are not one colour: dry, worn ground and darker damp ground take
                // over some of the grass (the plan only knows roads, yards, slopes and the shore).
                float fine = Mathf.PerlinNoise(wx / 23f + 5.1f, wz / 23f + 9.4f);
                float dry = Mathf.SmoothStep(0.5f, 0.78f, Mathf.PerlinNoise(wx / 85f + 13.7f, wz / 85f + 71.3f) * 0.72f + fine * 0.28f) * 0.5f;
                float damp = Mathf.SmoothStep(0.52f, 0.8f, Mathf.PerlinNoise(wx / 120f + 31.9f, wz / 120f + 47.2f) * 0.75f + (1f - fine) * 0.25f) * 0.4f;
                // Hollows are damp and dark, crowns are dry: the hills then read from above.
                const float around = 32f;
                float mean = (ground.Height(wx + around, wz) + ground.Height(wx - around, wz) + ground.Height(wx, wz + around) + ground.Height(wx, wz - around)
                    + ground.Height(wx + around * 0.7f, wz + around * 0.7f) + ground.Height(wx - around * 0.7f, wz + around * 0.7f)
                    + ground.Height(wx + around * 0.7f, wz - around * 0.7f) + ground.Height(wx - around * 0.7f, wz - around * 0.7f)) / 8f;
                float hollow = Mathf.Clamp((mean - height) / 3f, -1f, 1f);
                damp = Mathf.Clamp01(damp + 0.45f * Mathf.Max(0f, hollow));
                dry = Mathf.Clamp01(dry + 0.3f * Mathf.Max(0f, -hollow));
                dirt += grass * dry;
                sand += grass * (1f - dry) * damp;
                grass *= (1f - dry) * (1f - damp);
                float sum = grass + dirt + rock + sand;
                if (sum < 1e-4f) { grass = 1f; sum = 1f; }
                alpha[z, x, 0] = grass / sum;
                alpha[z, x, 1] = dirt / sum;
                alpha[z, x, 2] = rock / sum;
                alpha[z, x, 3] = sand / sum;
            }
        }
        data.SetAlphamaps(0, 0, alpha);
        EditorUtility.SetDirty(data);

        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.transform.SetParent(parent, false);
        go.transform.position = Vector3.zero; // world coordinates are the plan's: the map is 0..Size
        go.isStatic = true;
        var terrain = go.GetComponent<Terrain>();
        terrain.materialTemplate = WorldMaterials.Terrain();
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = TerrainPixelError;
        terrain.basemapDistance = TerrainBaseMapDistance;
        terrain.shadowCastingMode = ShadowCastingMode.Off; // a second pass over the ground for shadows nobody misses
        terrain.allowAutoConnect = false;
        terrain.drawTreesAndFoliage = false;
        return terrain;
    }

    // ---- sea ----

    // One plane at sea level. Wetland pools are simply ground below it. The shader gets the depth from a map baked here.
    static void BuildSea(WorldPlan plan, WorldMeshes.HeightField ground, Transform parent, WorldMeshes.Store store)
    {
        int res = WaterDepthResolution;
        var pixels = new Color32[res * res];
        float cell = plan.Size / res;
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float depth = plan.SeaLevel - ground.Height((x + 0.5f) * cell, (z + 0.5f) * cell);
                byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(depth / WaterDepthRange * 255f), 0, 255);
                pixels[z * res + x] = new Color32(v, v, v, 255);
            }
        }
        var depthMap = WorldMaterials.WaterDepth(pixels, res);
        var normals = WorldMaterials.WaterNormals(plan.Seed);

        var go = new GameObject("Sea");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(plan.Size * 0.5f, plan.SeaLevel, plan.Size * 0.5f);
        float side = plan.Size + 2f * SeaMargin;
        go.AddComponent<MeshFilter>().sharedMesh = store.Add(WorldMeshes.Quad("Sea", side, side));
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = WorldMaterials.Water(normals, depthMap, plan.Size, WaterDepthRange);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // Keeps players from walking out to sea without touching the character controller or adding a server rule:
    // invisible walls along the contour where the water is BarrierDepth deep (marching squares over a coarse grid).
    // They sit on the Ignore Raycast layer, so they stop bodies but not shots, sight lines or the camera.
    static int BuildSeaBarrier(WorldPlan plan, WorldMeshes.HeightField ground, Transform parent)
    {
        var root = Child(parent, "SeaBarrier");
        float level = plan.SeaLevel - BarrierDepth;
        int cells = Mathf.FloorToInt(plan.Size / BarrierCell);
        var crossings = new List<Vector2>(4);
        int count = 0;
        for (int cz = 0; cz < cells; cz++)
        {
            for (int cx = 0; cx < cells; cx++)
            {
                float x0 = cx * BarrierCell, x1 = x0 + BarrierCell, z0 = cz * BarrierCell, z1 = z0 + BarrierCell;
                float a = ground.Height(x0, z0) - level, b = ground.Height(x1, z0) - level;
                float c = ground.Height(x1, z1) - level, d = ground.Height(x0, z1) - level;
                crossings.Clear();
                Crossing(crossings, new Vector2(x0, z0), a, new Vector2(x1, z0), b);
                Crossing(crossings, new Vector2(x1, z0), b, new Vector2(x1, z1), c);
                Crossing(crossings, new Vector2(x1, z1), c, new Vector2(x0, z1), d);
                Crossing(crossings, new Vector2(x0, z1), d, new Vector2(x0, z0), a);
                for (int i = 0; i + 1 < crossings.Count; i += 2)
                {
                    var from = crossings[i];
                    var to = crossings[i + 1];
                    float length = Vector2.Distance(from, to);
                    if (length < 0.05f) continue;
                    var wall = new GameObject("Wall" + count++);
                    wall.layer = IgnoreRaycastLayer;
                    wall.transform.SetParent(root, false);
                    var mid = (from + to) * 0.5f;
                    wall.transform.SetPositionAndRotation(new Vector3(mid.x, plan.SeaLevel, mid.y),
                        Quaternion.LookRotation(new Vector3(to.x - from.x, 0f, to.y - from.y)));
                    // A little longer than the segment so neighbours overlap at the corners.
                    wall.AddComponent<BoxCollider>().size = new Vector3(BarrierThickness, BarrierHeight, length + BarrierThickness);
                    wall.isStatic = true;
                }
            }
        }
        return count;
    }

    static void Crossing(List<Vector2> found, Vector2 p, float a, Vector2 q, float b)
    {
        if ((a < 0f) == (b < 0f)) return;
        found.Add(Vector2.Lerp(p, q, a / (a - b)));
    }

    // ---- navmesh ----

    // Sources are listed by hand instead of collected from colliders: the terrain and floors and stairs are walked
    // on, every other building box only carves, rocks blank out the ground under them, and one box volume marks
    // everything deeper than the wading depth as not walkable.
    static NavMeshData BakeNavMesh(WorldPlan plan, Terrain terrain, List<NavMeshBuildSource> sources)
    {
        sources.Add(new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Terrain,
            sourceObject = terrain.terrainData,
            transform = Matrix4x4.TRS(terrain.transform.position, Quaternion.identity, Vector3.one),
            area = 0,
        });
        const float deepBox = 60f;
        float wadeLevel = plan.SeaLevel - WorldRules.WadeDepth;
        sources.Add(new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.ModifierBox,
            transform = Matrix4x4.TRS(new Vector3(plan.Size * 0.5f, wadeLevel - deepBox * 0.5f, plan.Size * 0.5f), Quaternion.identity, Vector3.one),
            size = new Vector3(plan.Size + 20f, deepBox, plan.Size + 20f),
            area = NotWalkableArea,
        });

        var settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = AgentRadius;
        settings.agentHeight = AgentHeight;
        settings.agentSlope = WorldRules.WalkSlopeDeg;
        settings.agentClimb = AgentStepHeight;
        settings.overrideVoxelSize = true;
        settings.voxelSize = NavVoxelSize;
        settings.overrideTileSize = true;
        settings.tileSize = NavTileSize;
        settings.minRegionArea = 4f; // drops specks such as the top of a low wall

        // Nothing below the wading depth can be walked, so the sea bed is left out of the volume altogether.
        float yMin = wadeLevel - 1.5f, yMax = plan.MaxHeight + 12f;
        var bounds = new Bounds(new Vector3(plan.Size * 0.5f, (yMin + yMax) * 0.5f, plan.Size * 0.5f), new Vector3(plan.Size, yMax - yMin, plan.Size));
        foreach (var problem in settings.ValidationReport(bounds))
            Debug.LogWarning("[WorldBuilder] navmesh settings: " + problem);

        var watch = Stopwatch.StartNew();
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        watch.Stop();
        if (data == null) throw new Exception("navmesh bake produced no data");
        Debug.LogWarning($"[WorldBuilder] navmesh bake took {watch.Elapsed.TotalSeconds:F1} s ({sources.Count} sources, voxel {NavVoxelSize:F3} m, " +
            $"{plan.Size:F0} x {plan.Size:F0} m)");

        // Overwrite in place so the asset keeps its identity across rebuilds.
        var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(data, NavMeshPath);
            return data;
        }
        EditorUtility.CopySerialized(data, existing);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    // ---- runtime data, match, camera ----

    static Vector3 OnGround(WorldMeshes.HeightField ground, float x, float z) => new Vector3(x, ground.Height(x, z), z);

    static WorldInfo FillInfo(GameObject world, WorldPlan plan, WorldMeshes.HeightField ground, List<Placed> stations)
    {
        var regions = plan.Regions.Select((r, i) => new WorldInfo.Region
        {
            Index = i, Id = r.Id, Name = r.Name, Kind = r.Kind,
            Centre = OnGround(ground, r.CentreX, r.CentreZ),
            Hub = OnGround(ground, r.HubX, r.HubZ),
        }).ToArray();

        var elevators = new WorldInfo.Elevator[plan.Elevators.Count];
        for (int i = 0; i < elevators.Length; i++)
        {
            var e = plan.Elevators[i];
            var station = stations[i];
            elevators[i] = new WorldInfo.Elevator
            {
                Name = e.Name, Region = e.Region, SubZone = e.SubZone,
                RegionId = e.Region >= 0 && e.Region < plan.Regions.Count ? plan.Regions[e.Region].Id : "",
                Position = station.Origin, FacingDeg = e.FacingDeg,
                Spawn = station.Spawn, SpawnYawDeg = station.SpawnYaw,
            };
        }

        var passes = plan.Passes.Select(p => new WorldInfo.Pass
        {
            Name = p.Name, RegionA = p.RegionA, RegionB = p.RegionB,
            Position = OnGround(ground, p.X, p.Z), YawDeg = p.YawDeg, Width = p.Width,
        }).ToArray();
        var convergence = plan.ConvergencePoints.Select(p => new WorldInfo.Point
        {
            Name = p.Name, Region = p.Region, Position = OnGround(ground, p.X, p.Z), Radius = p.ClearRadius,
        }).ToArray();
        var subZones = plan.SubZones.Select(s => new WorldInfo.Point
        {
            Name = s.Name, Region = s.Region, Position = OnGround(ground, s.X, s.Z), Radius = s.Radius,
        }).ToArray();

        var info = world.AddComponent<WorldInfo>();
        info.Fill(plan.Size, plan.SeaLevel, plan.Seed, regions, elevators, passes, convergence, subZones);
        EditorUtility.SetDirty(info);
        return info;
    }

    // At most `cap` points, taken one per building in turn so every building gets some before any gets many.
    static List<Vector3> PickLoot(List<Placed> buildings, int cap)
    {
        var picked = new List<Vector3>();
        for (int round = 0; picked.Count < cap; round++)
        {
            bool any = false;
            foreach (var building in buildings)
            {
                if (round >= building.Loot.Count) continue;
                any = true;
                picked.Add(building.Loot[round]);
                if (picked.Count >= cap) break;
            }
            if (!any) break;
        }
        return picked;
    }

    // Temporary until M2-2/M2-3: players come up in the elevators in turn; bots hold one convergence point per region
    // (the one farthest from the region's elevators) and four more stand at loot points spread over the rest of the map.
    const int ExtraBots = 4;
    static void AddMatch(WorldPlan plan, WorldMeshes.HeightField ground, WorldInfo info, List<Vector3> loot)
    {
        var players = info.Elevators.Select(e => MatchKit.At(e.Spawn.x, e.Spawn.y + SpawnLift, e.Spawn.z, e.SpawnYawDeg)).ToArray();
        var chosen = new List<WorldPoint>();
        for (int r = 0; r < plan.Regions.Count; r++)
        {
            // Nobody should be shot at while still standing in the elevator.
            var point = plan.ConvergencePoints.Where(p => p.Region == r)
                .OrderByDescending(p => plan.Elevators.Where(e => e.Region == r).Select(e => (e.X - p.X) * (e.X - p.X) + (e.Z - p.Z) * (e.Z - p.Z)).DefaultIfEmpty(0f).Min())
                .FirstOrDefault();
            if (point != null) chosen.Add(point);
        }
        // Farthest-point sampling over the loot points (the convergence points are one per region and already used)
        // keeps the extra bots far from the elevators and from each other.
        var candidates = loot.Where(l => plan.Elevators.All(e => (e.X - l.x) * (e.X - l.x) + (e.Z - l.z) * (e.Z - l.z) > 60f * 60f)).ToList();
        float Spread(Vector3 l) => chosen.Select(c => (c.X - l.x) * (c.X - l.x) + (c.Z - l.z) * (c.Z - l.z)).DefaultIfEmpty(1e9f).Min();
        var extra = new List<Vector3>();
        for (int n = 0; n < ExtraBots && candidates.Count > 0; n++)
        {
            var best = candidates.OrderByDescending(l => Mathf.Min(Spread(l), extra.Select(e => (e.x - l.x) * (e.x - l.x) + (e.z - l.z) * (e.z - l.z)).DefaultIfEmpty(1e9f).Min())).First();
            extra.Add(best);
            candidates.Remove(best);
        }
        var bots = chosen.Select(point =>
        {
            var at = OnGround(ground, point.X, point.Z);
            return MatchKit.At(at.x, at.y + 0.05f, at.z);
        }).Concat(extra.Select(l => MatchKit.At(l.x, l.y + 0.05f, l.z))).ToArray();
        var director = MatchKit.AddMatch(players, loot.Select(p => MatchKit.At(p.x, p.y, p.z)).ToArray(), new MatchKit.Spawn[0], bots);
        VehicleBuilder.AddToWorld(director, plan, ground.Height); // [VEHICLE HOOK] ADR-065 (5): 6 cars + 4 motorcycles
    }

    // The outdoor map draws with its own URP renderer: the project's, without the SSAO pass. On this map SSAO and the
    // depth pre-pass it needs cost more than the frame can spare (PERF_BUDGET: "SSAO is the first thing to go").
    // Returns its index in the pipeline asset's renderer list.
    static int OutdoorRenderer()
    {
        const string source = "Assets/_Project/Settings/Game_URP_Renderer.asset";
        const string path = "Assets/_Project/Settings/Game_URP_Renderer_Outdoor.asset";
        const string pipelinePath = "Assets/_Project/Settings/Game_URP.asset";
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(path) == null && !AssetDatabase.CopyAsset(source, path))
            throw new Exception("could not copy the renderer to " + path);
        var data = SceneLook.Load<UnityEngine.Rendering.Universal.UniversalRendererData>(path);
        foreach (var feature in data.rendererFeatures.ToList())
        {
            if (!(feature is UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion)) continue;
            data.rendererFeatures.Remove(feature);
            AssetDatabase.RemoveObjectFromAsset(feature);
            Object.DestroyImmediate(feature, true);
        }
        var so = new SerializedObject(data);
        var map = so.FindProperty("m_RendererFeatureMap");
        map.arraySize = data.rendererFeatures.Count;
        for (int i = 0; i < data.rendererFeatures.Count; i++)
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(data.rendererFeatures[i], out _, out long id)) map.GetArrayElementAtIndex(i).longValue = id;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);

        var pipeline = SceneLook.Load<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(pipelinePath);
        var list = new SerializedObject(pipeline);
        var renderers = list.FindProperty("m_RendererDataList");
        for (int i = 0; i < renderers.arraySize; i++)
            if (renderers.GetArrayElementAtIndex(i).objectReferenceValue == data) return i;
        renderers.arraySize++;
        renderers.GetArrayElementAtIndex(renderers.arraySize - 1).objectReferenceValue = data;
        list.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pipeline);
        AssetDatabase.SaveAssets();
        return renderers.arraySize - 1;
    }

    static void AddCamera(WorldPlan plan, int rendererIndex)
    {
        var cam = new GameObject("Main Camera") { tag = "MainCamera" };
        var camera = cam.AddComponent<Camera>();
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = FarClip;
        SceneLook.EnablePost(camera);
        UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(camera).SetRenderer(rendererIndex);
        cam.AddComponent<AudioListener>();
        cam.transform.position = new Vector3(plan.Size * 0.5f, plan.SeaLevel + 140f, plan.Size * 0.5f - 260f);
        cam.transform.LookAt(new Vector3(plan.Size * 0.5f, plan.SeaLevel, plan.Size * 0.5f));
    }

    // ---- pictures for review ----

    // PNGs in Logs/world: the whole island from above and at an angle, one view per region from an elevator toward
    // its nearest buildings, and close views a player would have (street, inside a house, yard, pass, shore, rocks).
    // Does not change the scene on disk.
    public static void RenderOverview()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var info = Object.FindFirstObjectByType<WorldInfo>();
            if (info == null) throw new Exception("the World scene has no WorldInfo (run WorldBuilder.Build first)");
            Directory.CreateDirectory(OverviewDir);
            // The ambient probe is not saved with an unbaked scene (SkyAmbient does this when the game starts).
            DynamicGI.UpdateEnvironment();
            var terrain = Object.FindFirstObjectByType<Terrain>();
            Vector3 Ground(float x, float z) => new Vector3(x, terrain.SampleHeight(new Vector3(x, 0f, z)), z);
            Vector3 Eye(Vector3 at) => Ground(at.x, at.z) + Vector3.up * 1.7f;
            Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.01f ? v.normalized : Vector3.forward; }

            var cam = new GameObject("OverviewCamera").AddComponent<Camera>();
            cam.nearClipPlane = 0.3f;
            SceneLook.EnablePost(cam);
            UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam).SetRenderer(info.RendererIndex);

            // From above: orthographic, no fog, and a LOD bias high enough that nothing is culled for being small.
            float size = info.Size;
            float lodBias = QualitySettings.lodBias;
            QualitySettings.lodBias = 100f;
            RenderSettings.fog = false;
            cam.orthographic = true;
            cam.orthographicSize = size * 0.52f;
            cam.farClipPlane = 1500f;
            var centre = new Vector3(size * 0.5f, info.SeaLevel, size * 0.5f);
            // Not exactly overhead: LookAt needs a direction that is not straight down.
            SceneLook.Shot(cam, OverviewDir + "/world_top.png", centre + new Vector3(0f, 900f, -1f), centre);
            cam.orthographic = false;
            cam.fieldOfView = 50f;
            cam.farClipPlane = 3000f;
            SceneLook.Shot(cam, OverviewDir + "/world_aerial.png", centre + new Vector3(-260f, 420f, -820f), centre + new Vector3(0f, 0f, 40f));
            QualitySettings.lodBias = lodBias;
            RenderSettings.fog = true;

            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = FarClip;
            var buildings = GameObject.Find("World/Buildings");
            foreach (var region in info.Regions)
            {
                var elevators = info.ElevatorsIn(region.Index);
                if (elevators.Count == 0) continue;
                var station = elevators[0].Position;
                var target = NearestBuildings(buildings, station, region.Hub);
                var toward = Flat(target - station);
                // Beside the station on the side of the buildings, at eye height.
                var eye = station + toward * 8f + Vector3.up * 1.7f;
                SceneLook.Shot(cam, $"{OverviewDir}/world_region_{region.Id}.png", eye, target + Vector3.up * 2.5f);
                // From the region's hub toward the middle of the island.
                SceneLook.Shot(cam, $"{OverviewDir}/world_hub_{region.Id}.png", Eye(region.Hub), Eye(region.Hub + Flat(centre - region.Hub) * 50f));
            }

            Transform Nearest(string contains, Vector3 from)
            {
                Transform best = null;
                if (buildings == null) return null;
                foreach (Transform b in buildings.transform)
                    if (b.name.Contains(contains) && (best == null || (b.position - from).sqrMagnitude < (best.position - from).sqrMagnitude)) best = b;
                return best;
            }
            var town = info.Regions.First(r => r.Kind == RegionKind.Town);
            var factory = info.Regions.First(r => r.Kind == RegionKind.Factory);
            var mountain = info.Regions.First(r => r.Kind == RegionKind.Mountain);
            var wetland = info.Regions.First(r => r.Kind == RegionKind.Wetland);

            // Houses: the plan is generated again to know where the doors, rooms and stairs of a building are.
            var plan = WorldGenerator.Generate(LoadSpec());
            BuildingPlan PlanOf(Transform building)
            {
                int index = int.Parse(building.name.Substring(LotPrefix.Length, 2));
                var lot = plan.Lots[index];
                return Buildings.Generate(lot.Kind, lot.SizeX, lot.SizeZ, unchecked((int)Hash(plan.Seed, index)));
            }
            // A stand-in for a 1.8 m player, for scale.
            GameObject Figure(Vector3 feet)
            {
                var figure = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                figure.transform.position = feet + Vector3.up * 0.9f;
                figure.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
                return figure;
            }
            var house = Nearest("House", town.Hub);
            if (house != null)
            {
                Vector3 At(float x, float y, float z) => house.position + house.rotation * new Vector3(x, y, z);
                var building = PlanOf(house);
                var corner = house.position + house.rotation * new Vector3(11f, 0f, 13f);
                SceneLook.Shot(cam, OverviewDir + "/world_house_outside.png", Eye(corner), house.position + Vector3.up * 2.5f);
                var door = building.Doors.First(o => o.Exterior);
                var outward = new Vector3(door.ForwardX, 0f, door.ForwardZ);
                var figure = Figure(At(door.X + outward.x * 1.2f + outward.z * 1.4f, door.Y, door.Z + outward.z * 1.2f - outward.x * 1.4f));
                SceneLook.Shot(cam, OverviewDir + "/world_house_door.png", At(door.X + outward.x * 5f + outward.z * 0.8f, door.Y + 1.65f, door.Z + outward.z * 5f - outward.x * 0.8f),
                    At(door.X - outward.x * 2f, door.Y + 1.3f, door.Z - outward.z * 2f));
                SceneLook.Shot(cam, OverviewDir + "/world_town_street.png", Eye(house.position + Flat(town.Hub - house.position) * 16f + Vector3.right * 3f), house.position + Vector3.up * 1.6f);
                Object.DestroyImmediate(figure);
                int shots = 0;
                foreach (var room in building.Rooms.OrderByDescending(r => r.Area))
                {
                    if (shots >= 3) break;
                    // From one corner of the room toward the opposite one, at eye height.
                    SceneLook.Shot(cam, $"{OverviewDir}/world_house_room{shots}.png", At(room.MinX + 0.45f, room.FloorY + 1.65f, room.MinZ + 0.45f),
                        At(room.MaxX - 0.6f, room.FloorY + 1.0f, room.MaxZ - 0.6f));
                    shots++;
                }
            }
            // A house with stairs: looking up the flight from the ground floor, and the landing upstairs.
            if (buildings != null)
            {
                foreach (Transform candidate in buildings.transform)
                {
                    if (!candidate.name.Contains("House")) continue;
                    var building = PlanOf(candidate);
                    if (building.Stairs.Count == 0) continue;
                    var stair = building.Stairs[0];
                    var climb = Quaternion.Euler(0f, stair.FacingDeg, 0f) * Vector3.forward;
                    Vector3 At(Vector3 local) => candidate.position + candidate.rotation * local;
                    var foot = new Vector3(stair.X, stair.BaseY, stair.Z);
                    SceneLook.Shot(cam, OverviewDir + "/world_house_stairs.png", At(foot - climb * 2.2f + Vector3.up * 1.65f + Vector3.Cross(Vector3.up, climb) * 0.9f),
                        At(foot + climb * stair.Length * 0.6f + Vector3.up * (stair.TopY - stair.BaseY) * 0.6f));
                    var upper = building.Rooms.Where(r => r.Storey > 0).OrderByDescending(r => r.Area).FirstOrDefault();
                    if (upper.Area > 0f)
                        SceneLook.Shot(cam, OverviewDir + "/world_house_upstairs.png", At(new Vector3(upper.MaxX - 0.45f, upper.FloorY + 1.65f, upper.MaxZ - 0.45f)),
                            At(new Vector3(upper.MinX + 0.6f, upper.FloorY + 1.0f, upper.MinZ + 0.6f)));
                    break;
                }
            }
            foreach (var kind in new[] { "FactoryHall", "Warehouse" })
            {
                var hall = Nearest(kind, factory.Hub);
                if (hall == null) continue;
                var corner = hall.position + hall.rotation * new Vector3(22f, 0f, 26f);
                SceneLook.Shot(cam, $"{OverviewDir}/world_{kind}_outside.png", Eye(corner), hall.position + Vector3.up * 3f);
                var inside = hall.position + Vector3.up * 1.75f;
                SceneLook.Shot(cam, $"{OverviewDir}/world_{kind}_inside.png", inside + hall.rotation * new Vector3(-3f, 0f, -4f), inside + hall.rotation * new Vector3(4f, 0.3f, 5f));
            }
            if (info.Elevators.Count > 0)
            {
                var e = info.Elevators[0];
                var outward = Quaternion.Euler(0f, e.SpawnYawDeg, 0f) * Vector3.forward;
                SceneLook.Shot(cam, OverviewDir + "/world_station_inside.png", e.Spawn + Vector3.up * 1.7f, e.Spawn + Vector3.up * 1.6f + outward * 10f);
                SceneLook.Shot(cam, OverviewDir + "/world_station_outside.png", Eye(e.Position + outward * 12f + Vector3.Cross(Vector3.up, outward) * 5f), e.Position + Vector3.up * 1.5f);
            }
            if (info.Passes.Count > 0)
            {
                var pass = info.Passes[0];
                var along = Quaternion.Euler(0f, pass.YawDeg, 0f) * Vector3.forward;
                SceneLook.Shot(cam, OverviewDir + "/world_pass.png", Eye(pass.Position - along * 45f + Vector3.Cross(Vector3.up, along) * 2f), Eye(pass.Position));
            }
            // Shore: walk out from the wetland hub away from the island's centre until the ground is at the waterline.
            {
                var outward = Flat(wetland.Hub - centre);
                var p = wetland.Hub;
                for (int i = 0; i < 600 && Ground(p.x, p.z).y > info.SeaLevel + 0.4f; i++) p += outward;
                var side = Vector3.Cross(Vector3.up, outward);
                SceneLook.Shot(cam, OverviewDir + "/world_shore.png", Eye(p - outward * 4f), Eye(p + outward * 12f + side * 30f) - Vector3.up * 1.2f);
                SceneLook.Shot(cam, OverviewDir + "/world_shore_inland.png", Eye(p + outward * 3f), Eye(wetland.Hub));
            }
            var pool = info.ConvergencePointsIn(wetland.Index);
            if (pool.Count > 0) SceneLook.Shot(cam, OverviewDir + "/world_wetland.png", Eye(pool[0].Position), Eye(pool[0].Position + Flat(wetland.Centre - pool[0].Position) * 40f) - Vector3.up * 1f);
            var rocks = GameObject.Find("World/Rocks");
            if (rocks != null && rocks.transform.childCount > 0)
            {
                Transform rock = null;
                foreach (Transform r in rocks.transform)
                    if (rock == null || (r.position - mountain.Hub).sqrMagnitude < (rock.position - mountain.Hub).sqrMagnitude) rock = r;
                SceneLook.Shot(cam, OverviewDir + "/world_rocks.png", Eye(rock.position + new Vector3(3.5f, 0f, -4.5f)), rock.position + Vector3.up * 0.5f);
            }
            SceneLook.Shot(cam, OverviewDir + "/world_mountain.png", Eye(mountain.Hub), Eye(mountain.Hub + Vector3.forward * 60f) + Vector3.up * 14f);
            RenderVegetationViews(cam, info, terrain);
            Debug.Log("[WorldBuilder] overview pictures written to " + OverviewDir);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.Exit(0);
    }

    // The midpoint of the two buildings nearest to a station (the region hub if there are none).
    static Vector3 NearestBuildings(GameObject buildings, Vector3 from, Vector3 fallback)
    {
        if (buildings == null) return fallback;
        var nearest = new List<Transform>();
        foreach (Transform building in buildings.transform)
            if (building.name.StartsWith(LotPrefix)) nearest.Add(building);
        if (nearest.Count == 0) return fallback;
        nearest.Sort((a, b) => (a.position - from).sqrMagnitude.CompareTo((b.position - from).sqrMagnitude));
        return nearest.Count == 1 ? nearest[0].position : (nearest[0].position + nearest[1].position) * 0.5f;
    }
}
