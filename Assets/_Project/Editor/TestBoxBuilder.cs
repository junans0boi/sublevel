using System.IO;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using Game.AI;
using Game.Gameplay;
using Game.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Generates the M1 test box: materials, prefabs and scene, all from primitives (ADR-033).
// Run headless: tools/unity.sh run TestBoxBuilder.Build
public static class TestBoxBuilder
{
    public const string ScenePath = "Assets/_Project/Scenes/TestBox.unity";
    const string PrefabDir = "Assets/_Project/Prefabs/M1";
    const string MaterialDir = PrefabDir + "/Materials";
    const float Half = 30f;

    public static void Build()
    {
        Directory.CreateDirectory(MaterialDir);
        var floorMat = Lit("Floor", new Color(0.32f, 0.33f, 0.35f));
        var wallMat = Lit("Wall", new Color(0.45f, 0.43f, 0.40f));
        var coverMat = Lit("Cover", new Color(0.55f, 0.42f, 0.25f));
        var playerMat = Lit("Player", new Color(0.20f, 0.40f, 0.75f));
        var dummyMat = Lit("Dummy", new Color(0.75f, 0.22f, 0.20f));
        var lootMats = new[] { Lit("LootWeapon", new Color(0.9f, 0.75f, 0.1f)), Lit("LootAmmo", new Color(0.2f, 0.7f, 0.3f)), Lit("LootMedkit", new Color(0.92f, 0.92f, 0.92f)) };
        var tracerMat = Unlit("Tracer", new Color(1f, 0.9f, 0.5f));

        var player = BuildPlayerPrefab(playerMat, tracerMat);
        var dummy = BuildDummyPrefab(dummyMat, tracerMat);
        var loot = BuildLootPrefab(lootMats);
        AssetDatabase.SaveAssets();
        RebuildSpawnablePrefabs();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildLevel(floorMat, wallMat, coverMat);
        BuildMatch(player, dummy, loot);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(SceneBuilder.BootstrapPath, true),
        };
        AssetDatabase.SaveAssets();
        Debug.Log("[TestBoxBuilder] Saved " + ScenePath);
        EditorApplication.Exit(0);
    }

    // Fish-Net's prefab collection generator is internal; ask it to rebuild so the new prefabs are spawnable right away.
    static void RebuildSpawnablePrefabs()
    {
        var generator = typeof(NetworkManager).Assembly.GetType("FishNet.Editing.PrefabCollectionGenerator.Generator", true);
        var method = generator.GetMethod("GenerateFull", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        method.Invoke(null, new object[] { null, true, true });
    }

    static Material SaveMaterial(string name, string shaderName, Color color)
    {
        var path = MaterialDir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find(shaderName));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material Lit(string name, Color color) => SaveMaterial(name, "Universal Render Pipeline/Lit", color);
    static Material Unlit(string name, Color color) => SaveMaterial(name, "Universal Render Pipeline/Unlit", color);

    static GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static void Set(Object target, string field, System.Action<SerializedProperty> assign)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        if (prop == null) throw new System.Exception($"{target.GetType().Name} has no serialized field {field}");
        assign(prop);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetArray(Object target, string field, Object[] values)
    {
        Set(target, field, p =>
        {
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        });
    }

    // Body + visor under a "Visual" node so the whole figure can tip over on death.
    static Transform Figure(Transform root, Material mat, out Renderer[] renderers)
    {
        var visual = new GameObject("Visual").transform;
        visual.SetParent(root, false);
        var body = Shape(PrimitiveType.Capsule, "Body", visual, new Vector3(0, 0.9f, 0), new Vector3(0.7f, 0.9f, 0.7f), mat, false);
        var visor = Shape(PrimitiveType.Cube, "Visor", visual, new Vector3(0, 1.55f, 0.3f), new Vector3(0.4f, 0.15f, 0.2f), mat, false);
        renderers = new[] { body.GetComponent<Renderer>(), visor.GetComponent<Renderer>() };
        return visual;
    }

    static NetworkObject SavePrefab(GameObject go, string name)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/" + name + ".prefab");
        Object.DestroyImmediate(go);
        return prefab.GetComponent<NetworkObject>();
    }

    static NetworkObject BuildPlayerPrefab(Material mat, Material tracer)
    {
        var go = new GameObject("Player");
        var cc = go.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0);
        go.AddComponent<NetworkObject>();
        go.AddComponent<NetworkTransform>();
        var combatant = go.AddComponent<Combatant>();
        var controller = go.AddComponent<PlayerController>();
        var view = go.AddComponent<CombatantView>();
        var visual = Figure(go.transform, mat, out var renderers);
        Set(combatant, "_isPlayer", p => p.boolValue = true);
        SetArray(controller, "_bodyRenderers", renderers);
        Set(view, "_visual", p => p.objectReferenceValue = visual);
        Set(view, "_tracerMaterial", p => p.objectReferenceValue = tracer);
        return SavePrefab(go, "Player");
    }

    static NetworkObject BuildDummyPrefab(Material mat, Material tracer)
    {
        var go = new GameObject("TrainingDummy");
        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.height = 1.8f; capsule.radius = 0.35f; capsule.center = new Vector3(0, 0.9f, 0);
        go.AddComponent<NetworkObject>();
        go.AddComponent<Combatant>();
        go.AddComponent<TrainingDummy>();
        var view = go.AddComponent<CombatantView>();
        var visual = Figure(go.transform, mat, out _);
        Set(view, "_visual", p => p.objectReferenceValue = visual);
        Set(view, "_tracerMaterial", p => p.objectReferenceValue = tracer);
        return SavePrefab(go, "TrainingDummy");
    }

    static NetworkObject BuildLootPrefab(Material[] mats)
    {
        var go = new GameObject("LootItem");
        var box = go.AddComponent<BoxCollider>();
        box.size = Vector3.one * 0.5f;
        box.isTrigger = true; // never blocks movement or shots
        go.AddComponent<NetworkObject>();
        go.AddComponent<LootItem>();
        var view = go.AddComponent<LootView>();
        var cube = Shape(PrimitiveType.Cube, "Cube", go.transform, Vector3.zero, Vector3.one * 0.4f, mats[0], false);
        Set(view, "_renderer", p => p.objectReferenceValue = cube.GetComponent<Renderer>());
        SetArray(view, "_materialsByKind", mats);
        return SavePrefab(go, "LootItem");
    }

    static void Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat) =>
        Shape(PrimitiveType.Cube, name, parent, center, size, mat, true).isStatic = true;

    static void BuildLevel(Material floorMat, Material wallMat, Material coverMat)
    {
        var level = new GameObject("Level").transform;
        Box(level, "Floor", new Vector3(0, -0.5f, 0), new Vector3(Half * 2, 1, Half * 2), floorMat);
        Box(level, "WallN", new Vector3(0, 2, Half), new Vector3(Half * 2, 4, 1), wallMat);
        Box(level, "WallS", new Vector3(0, 2, -Half), new Vector3(Half * 2, 4, 1), wallMat);
        Box(level, "WallE", new Vector3(Half, 2, 0), new Vector3(1, 4, Half * 2), wallMat);
        Box(level, "WallW", new Vector3(-Half, 2, 0), new Vector3(1, 4, Half * 2), wallMat);
        // Sight blockers: the spawn area is hidden from every dummy, and the dummies from each other's approach.
        Box(level, "BlockSpawn", new Vector3(0, 1.5f, -14), new Vector3(24, 3, 1), wallMat);
        Box(level, "BlockMiddle", new Vector3(0, 1.5f, 1), new Vector3(1, 3, 22), wallMat);
        Box(level, "BlockNorth", new Vector3(0, 1.5f, 12), new Vector3(20, 3, 1), wallMat);

        var covers = new[]
        {
            new Vector3(-20, 0, -10), new Vector3(-10, 0, -6), new Vector3(-22, 0, 4), new Vector3(-9, 0, 6),
            new Vector3(20, 0, -10), new Vector3(10, 0, -6), new Vector3(22, 0, 4), new Vector3(9, 0, 6),
            new Vector3(-16, 0, 18), new Vector3(16, 0, 18), new Vector3(-5, 0, 24), new Vector3(5, 0, 24),
        };
        for (int i = 0; i < covers.Length; i++)
            Box(level, "Cover" + i, covers[i] + Vector3.up * 0.6f, new Vector3(2f, 1.2f, 1f), coverMat);

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(50, -30, 0);

        var cam = new GameObject("Main Camera");
        cam.tag = "MainCamera";
        cam.AddComponent<Camera>().nearClipPlane = 0.05f;
        cam.AddComponent<AudioListener>();
        cam.transform.position = new Vector3(0, 12, -34);
        cam.transform.LookAt(Vector3.zero);
    }

    static Transform[] Points(Transform parent, string prefix, Vector3[] positions, float yaw = 0f)
    {
        var points = new Transform[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            var t = new GameObject(prefix + i).transform;
            t.SetParent(parent, false);
            t.position = positions[i];
            t.rotation = Quaternion.Euler(0, yaw, 0);
            points[i] = t;
        }
        return points;
    }

    static void BuildMatch(NetworkObject player, NetworkObject dummy, NetworkObject loot)
    {
        var go = new GameObject("Match");
        var nm = go.AddComponent<NetworkManager>();
        // The manager lives and dies with the match scene so a restart gets a fresh one.
        Set(nm, "_dontDestroyOnLoad", p => p.boolValue = false);
        nm.SpawnablePrefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>("Assets/DefaultPrefabObjects.asset");
        EditorUtility.SetDirty(nm);
        go.AddComponent<Tugboat>();
        var director = go.AddComponent<MatchDirector>();

        var playerSpawns = Points(go.transform, "PlayerSpawn", new[] { new Vector3(0, 0.1f, -24) });
        var lootSpawns = Points(go.transform, "LootSpawn", new[]
        {
            new Vector3(0, 0.3f, -21), new Vector3(-6, 0.3f, -20), new Vector3(6, 0.3f, -20), new Vector3(-20, 0.3f, -18), new Vector3(20, 0.3f, -18),
            new Vector3(-22, 0.3f, 8), new Vector3(22, 0.3f, 8), new Vector3(-8, 0.3f, 22), new Vector3(8, 0.3f, 22), new Vector3(0, 0.3f, 26),
        });
        var dummySpawns = Points(go.transform, "DummySpawn", new[] { new Vector3(-15, 0, 0), new Vector3(15, 0, 0), new Vector3(0, 0, 20) }, 180f);

        Set(director, "_networkManager", p => p.objectReferenceValue = nm);
        Set(director, "_playerPrefab", p => p.objectReferenceValue = player);
        Set(director, "_lootPrefab", p => p.objectReferenceValue = loot);
        Set(director, "_dummyPrefab", p => p.objectReferenceValue = dummy);
        SetArray(director, "_playerSpawns", playerSpawns);
        SetArray(director, "_lootSpawns", lootSpawns);
        SetArray(director, "_dummySpawns", dummySpawns);

        new GameObject("HUD").AddComponent<Hud>();
    }
}
