using System;
using System.IO;
using Game.Core;
using Game.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The event mode's race scenes (ADR-512/513, #445): one scene per course of RaceEvent.Courses, an outdoor flat field with the circuit
// (the shared circuit builder), a grandstand and a pit building along the main straight, the sky and sun of the look-dev set, and a
// hosted match whose RaceDirector runs the race. No new assets.
// tools/unity.sh run WorldBuilder.BuildRaceScenes -> Assets/_Project/Scenes/Race_<course>.unity
public static partial class WorldBuilder
{
    public static void BuildRaceScenes()
    {
        try { foreach (var c in RaceEvent.Courses) BuildRaceScene(c); }
        catch (Exception e) { Debug.LogException(e); Debug.LogError("[WorldBuilder] race scenes FAILED: " + e.Message); EditorApplication.Exit(1); return; }
        EditorApplication.Exit(0);
    }

    static void BuildRaceScene(RaceEvent.CourseInfo info)
    {
        string scenePath = $"Assets/_Project/Scenes/{info.Scene}.unity";
        Directory.CreateDirectory(WorldMaterials.GeneratedDir);
        SceneLook.PrepareTextures();
        var post = RenderSetup.Apply();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SceneLook.SetupSky();
        SceneLook.AddLights();
        SceneLook.AddPostVolume(post);
        WorldMaterials.Prepare();
        var store = new WorldMeshes.Store($"{WorldMaterials.GeneratedDir}/{info.Scene}_Meshes.asset", $"{WorldMaterials.GeneratedDir}/{info.Scene}_Meshes.bytes");

        var c = info.Course;
        var root = new GameObject("RaceCourse").transform;
        var cols = Child(root, "Colliders");

        // The field: grass round the whole course, 60 m beyond it, top at y = 0, one box collider.
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        foreach (var p in c.Path) { x0 = Mathf.Min(x0, p.X); x1 = Mathf.Max(x1, p.X); z0 = Mathf.Min(z0, p.Z); z1 = Mathf.Max(z1, p.Z); }
        foreach (var p in c.BranchPath) { x0 = Mathf.Min(x0, p.X); x1 = Mathf.Max(x1, p.X); z0 = Mathf.Min(z0, p.Z); z1 = Mathf.Max(z1, p.Z); }
        x0 -= 60f; x1 += 60f; z0 -= 60f; z1 += 60f;
        var turf = WorldMaterials.RaceGrass; var field = new RaceTiles(turf.Tile);
        field.Slab(x0, x1, z0, z1, -0.002f, 1f);
        RaceCollider(cols, new Vector3((x0 + x1) * 0.5f, -0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 1f, z1 - z0), Quaternion.identity);

        var kit = new CircuitKit(cols);
        BuildCircuit(kit, c, null);
        BuildStart(kit, c, RaceEvent.GridSlots);

        // Along the main straight, around the line: a grandstand on the right of the start (stepped rows facing the track) and a pit building
        // on the left, both clear of the run-off and barrier.
        var concrete = WorldMaterials.ConcreteDark; var stands = new RaceTiles(concrete.Tile); var wallMat = WorldMaterials.Plaster(0); var houses = new RaceTiles(wallMat.Tile);
        float edge = c.Width * 0.5f + c.Runoff + 0.5f;
        for (float s = -70f; s <= 70f; s += 10f)
        {
            var (px, pz, yaw) = c.PointAt(s);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 At(float right, float y) => new Vector3(px, y, pz) + rot * new Vector3(right, 0f, 0f);
            for (int row = 0; row < 6; row++)
            {
                float top = 0.5f + row * 0.6f;
                stands.Add(At(edge + 3f + row * 1.3f, top * 0.5f), new Vector3(1.3f, top, 10.2f), rot);
                kit.Navy.Add(At(edge + 2.8f + row * 1.3f, top + 0.22f), new Vector3(0.5f, 0.44f, 10.2f), rot);
            }
            houses.Add(At(-(edge + 9f), 3f), new Vector3(10f, 6f, 10.2f), rot);                         // pit garages
            kit.Red.Add(At(-(edge + 3.9f), 5.4f), new Vector3(0.2f, 0.6f, 10.2f), rot);                  // a red band over the garage doors
        }

        kit.Emit(root, store);
        field.Emit(root, store, "Field", turf.Material); stands.Emit(root, store, "Stands", concrete.Material); houses.Emit(root, store, "Garages", wallMat.Material);
        // No static batching: the meshes come from the blob at load (WorldMeshBank), after the batching would have run on empty filters
        // (the whole course rendered black in play mode). Same as the World scene.
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
            if ((flags & StaticEditorFlags.BatchingStatic) != 0) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
        }
        store.Finish(root.gameObject);

        // The match: people start where the grid is (RaceDirector seats them in the cars), no loot, no dummies, the car models for the race.
        var grid = c.GridSpots(RaceEvent.GridSlots);
        var spawns = new MatchKit.Spawn[grid.Count];
        for (int i = 0; i < grid.Count; i++)
        {
            float rad = grid[i].yaw * Mathf.Deg2Rad;
            spawns[i] = MatchKit.At(grid[i].x - Mathf.Cos(rad) * 2.4f, 0.2f, grid[i].z + Mathf.Sin(rad) * 2.4f, grid[i].yaw);   // beside the driver's door
        }
        var director = MatchKit.AddMatch(spawns, new MatchKit.Spawn[0], new MatchKit.Spawn[0]);
        VehicleBuilder.AddParked(director);
        var race = director.gameObject.AddComponent<RaceDirector>();
        race.CourseId = info.Id;
        EditorUtility.SetDirty(race);

        var cam = new GameObject("Main Camera") { tag = "MainCamera" };
        var camera = cam.AddComponent<Camera>();
        camera.nearClipPlane = 0.05f; camera.farClipPlane = 900f;
        SceneLook.EnablePost(camera);
        cam.AddComponent<AudioListener>();
        var (lx, lz, _) = c.PointAt(0f);
        cam.transform.position = new Vector3(lx - 40f, 30f, lz + 60f);
        cam.transform.LookAt(new Vector3(lx, 0f, lz));

        EditorSceneManager.SaveScene(scene, scenePath);
        SceneBuilder.RegisterBuildScenes();
        AssetDatabase.SaveAssets();
        Debug.Log($"[WorldBuilder] race scene {scenePath}: {info.Name}, lap {c.LapLength:F0} m, {c.Sectors} checkpoints, {grid.Count} grid slots, {cols.childCount} colliders");
    }
}
