using System.Collections.Generic;
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

// Generates the zone D (factory) blockout scene from ZoneDBlockout: primitive boxes in flat colors, a baked navmesh,
// markers for convergence points and elevators, and a host-run match so it can be walked (ADR-040). No external assets.
// Run headless: tools/unity.sh run ZoneDBuilder.Build
public static class ZoneDBuilder
{
    public const string ScenePath = "Assets/_Project/Scenes/ZoneD.unity";
    const string GeneratedDir = "Assets/_Project/Prefabs/M2";
    const string MaterialDir = GeneratedDir + "/Materials";
    const string NavMeshPath = GeneratedDir + "/ZoneD_NavMesh.asset";

    // Radius and height are the player's CharacterController; slope and step are no more permissive than the
    // controller's (45 degrees, 0.3 m), so a bot never takes a route the player could not.
    const float AgentRadius = 0.35f, AgentHeight = 1.8f, AgentMaxSlope = 35f, AgentStepHeight = 0.3f;
    const int NotWalkableArea = 1; // built-in navmesh area

    static readonly Color[] ContainerColors =
    {
        new Color(0.42f, 0.22f, 0.17f), new Color(0.19f, 0.28f, 0.38f), new Color(0.22f, 0.33f, 0.27f),
    };

    public static void Build()
    {
        var layout = ZoneDBlockout.Create();
        var violations = ZoneLayoutRules.Validate(layout);
        if (violations.Count > 0)
            throw new System.Exception("zone D layout breaks design rules: " + string.Join("; ", violations.Select(v => v.Rule + " " + v.Detail)));

        Directory.CreateDirectory(MaterialDir);
        var postProfile = RenderSetup.Apply();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Lighting(postProfile);

        var zone = new GameObject("ZoneD");
        // Two groups so the navmesh bake can tell what is walked on from what only gets in the way.
        var walkable = Child(zone.transform, "Walkable");
        var obstacles = Child(zone.transform, "Obstacles");
        int containers = 0;
        foreach (var block in layout.Blocks)
        {
            Material mat;
            if (block.Kind == BlockKind.Container)
            {
                int shade = containers++ % ContainerColors.Length;
                mat = Flat("Container" + shade, ContainerColors[shade]);
            }
            else mat = Flat(block.Kind.ToString(), KindColor(block.Kind));
            AddBlock(IsWalkedOn(block.Kind) ? walkable : obstacles, block, mat);
        }

        var convergence = layout.ConvergencePoints.Select(p => Marker(zone.transform, p.Name, new Vector3(p.X, 0, p.Z), 0)).ToArray();
        var elevators = layout.Elevators.Select(e => Marker(zone.transform, e.Name, new Vector3(e.X, 0, e.Z), e.FacingDeg)).ToArray();
        var landmarks = layout.Landmarks.Select(p => Marker(zone.transform, p.Name, new Vector3(p.X, p.Y, p.Z), 0)).ToArray();
        for (int i = 0; i < convergence.Length; i++) ConvergencePatch(convergence[i], layout.ConvergencePoints[i].ClearRadius);

        var info = zone.AddComponent<ZoneInfo>();
        Set(info, "_zoneId", p => p.stringValue = layout.Id);
        SetArray(info, "_convergencePoints", convergence);
        SetArray(info, "_elevators", elevators);
        SetArray(info, "_landmarks", landmarks);

        var navMesh = BakeNavMesh(zone.transform, obstacles, layout);
        Set(zone.AddComponent<ZoneNavMesh>(), "_data", p => p.objectReferenceValue = navMesh);

        // Temporary until M2-2/M2-3: the player starts at the elevators, training dummies hold the convergence points.
        MatchKit.AddMatch(
            layout.Elevators.Select(e => MatchKit.At(e.X, 0.1f, e.Z, e.FacingDeg)).ToArray(),
            layout.LootPoints.Select(p => MatchKit.At(p.X, p.Y, p.Z)).ToArray(),
            layout.ConvergencePoints.Select(p => MatchKit.At(p.X, 0, p.Z)).ToArray());

        var cam = new GameObject("Main Camera") { tag = "MainCamera" };
        var camera = cam.AddComponent<Camera>();
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 400f;
        SceneLook.EnablePost(camera);
        cam.AddComponent<AudioListener>();
        cam.transform.position = new Vector3(-95f, 70f, -80f);
        cam.transform.LookAt(Vector3.zero);

        EditorSceneManager.SaveScene(scene, ScenePath);
        SceneBuilder.RegisterBuildScenes();
        AssetDatabase.SaveAssets();
        Debug.Log($"[ZoneDBuilder] Saved {ScenePath}: {layout.Blocks.Count} blocks, {layout.LootPoints.Count} loot points");
        EditorApplication.Exit(0);
    }

    static Transform Child(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static bool IsWalkedOn(BlockKind kind) =>
        kind == BlockKind.Floor || kind == BlockKind.Platform || kind == BlockKind.Ramp || kind == BlockKind.ElevatorPad;

    static Color KindColor(BlockKind kind)
    {
        switch (kind)
        {
            case BlockKind.Floor: return new Color(0.33f, 0.34f, 0.35f);
            case BlockKind.PerimeterWall: return new Color(0.40f, 0.41f, 0.42f);
            case BlockKind.Wall: return new Color(0.52f, 0.52f, 0.50f);
            case BlockKind.Cover: return new Color(0.27f, 0.30f, 0.33f);
            case BlockKind.Platform: return new Color(0.38f, 0.36f, 0.32f);
            case BlockKind.Ramp: return new Color(0.50f, 0.44f, 0.26f);
            case BlockKind.ElevatorPad: return new Color(0.78f, 0.62f, 0.14f);
            case BlockKind.ElevatorShaft: return new Color(0.22f, 0.22f, 0.24f);
            default: return Color.magenta;
        }
    }

    static Material Flat(string name, Color color)
    {
        var path = $"{MaterialDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", 0.15f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void AddBlock(Transform parent, ZoneBlock block, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = block.Name;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(new Vector3(block.X, block.Y, block.Z), Quaternion.Euler(block.PitchDeg, 0, block.RollDeg));
        go.transform.localScale = new Vector3(block.SizeX, block.SizeY, block.SizeZ);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.isStatic = true;
    }

    static Transform Marker(Transform parent, string name, Vector3 position, float yaw)
    {
        var t = Child(parent, name);
        t.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        return t;
    }

    // A painted patch on the floor so the convergence point can be seen in the blockout. No collider: it is not geometry.
    static void ConvergencePatch(Transform marker, float radius)
    {
        var patch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        patch.name = "Patch";
        Object.DestroyImmediate(patch.GetComponent<Collider>());
        patch.transform.SetParent(marker, false);
        patch.transform.localPosition = new Vector3(0, 0.01f, 0);
        patch.transform.localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);
        var renderer = patch.GetComponent<Renderer>();
        renderer.sharedMaterial = Flat("ConvergencePatch", new Color(0.45f, 0.30f, 0.26f));
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    // Built-in sky and gradient ambient: the blockout uses no HDRI or textures.
    static void Lighting(VolumeProfile postProfile)
    {
        var path = MaterialDir + "/Sky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            AssetDatabase.CreateAsset(sky, path);
        }
        sky.SetFloat("_AtmosphereThickness", 0.8f);
        sky.SetFloat("_Exposure", 0.9f);
        sky.SetColor("_SkyTint", new Color(0.55f, 0.58f, 0.62f));
        sky.SetColor("_GroundColor", new Color(0.30f, 0.30f, 0.31f));
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.50f, 0.54f, 0.60f);
        RenderSettings.ambientEquatorColor = new Color(0.38f, 0.40f, 0.42f);
        RenderSettings.ambientGroundColor = new Color(0.18f, 0.18f, 0.18f);
        RenderSettings.reflectionIntensity = 0.3f;
        SceneLook.AddLights();
        SceneLook.AddPostVolume(postProfile);
    }

    static NavMeshData BakeNavMesh(Transform zone, Transform obstacles, ZoneLayout layout)
    {
        Physics.SyncTransforms();
        var sources = new List<NavMeshBuildSource>();
        // Obstacles carve the mesh but their tops are not walkable, so crates and containers do not become islands.
        var markups = new List<NavMeshBuildMarkup> { new NavMeshBuildMarkup { root = obstacles, overrideArea = true, area = NotWalkableArea } };
        NavMeshBuilder.CollectSources(zone, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);

        var settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = AgentRadius;
        settings.agentHeight = AgentHeight;
        settings.agentSlope = AgentMaxSlope;
        settings.agentClimb = AgentStepHeight;
        var bounds = new Bounds(new Vector3(0, layout.WallHeight * 0.5f, 0), new Vector3(layout.HalfX * 2 + 4, layout.WallHeight + 4, layout.HalfZ * 2 + 4));
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        if (data == null) throw new System.Exception("navmesh bake produced no data");

        Debug.Log($"[ZoneDBuilder] navmesh baked from {sources.Count} colliders");
        // Overwrite in place so the asset keeps its identity (and the scene its reference) across rebuilds.
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
}
