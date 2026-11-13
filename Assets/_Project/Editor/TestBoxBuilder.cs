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
using static EditorFields;

// Generates the test box: prefabs and scene. Layout and gameplay objects date from M1; since M1.5 the
// characters, weapons, surfaces, sky and sounds use the CC0 assets (ADR-035..037, replacing ADR-033's primitives).
// Run headless: tools/unity.sh run TestBoxBuilder.Build
public static class TestBoxBuilder
{
    public const string ScenePath = "Assets/_Project/Scenes/TestBox.unity";
    public const string PrefabDir = "Assets/_Project/Prefabs/M1";
    const string MaterialDir = PrefabDir + "/Materials";
    const string WeaponAudioDir = "Assets/_Project/Audio/Weapons/";
    const string FoleyDir = "Assets/_Project/Audio/Foley/KenneyImpactSounds/";
    const float Half = 30f;

    public static void Build()
    {
        Directory.CreateDirectory(MaterialDir);
        SceneLook.PrepareTextures();
        var postProfile = RenderSetup.Apply();
        CharacterKit.Prepare();
        var lootMats = new[] { Lit("LootWeapon", new Color(0.9f, 0.75f, 0.1f)), Lit("LootAmmo", new Color(0.2f, 0.7f, 0.3f)), Lit("LootMedkit", new Color(0.92f, 0.92f, 0.92f)) };
        // Brighter than white so the bloom picks the flashes and tracers up.
        var glowMat = FxMaterial("ShotGlow", new Color(2f, 1.6f, 0.9f), true);
        var dustMat = FxMaterial("ShotDust", Color.white, false);

        var player = BuildPlayerPrefab(glowMat, dustMat);
        var dummy = BuildDummyPrefab(glowMat, dustMat);
        var loot = BuildLootPrefab(lootMats);
        AssetDatabase.SaveAssets();
        RebuildSpawnablePrefabs();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildLevel(postProfile);
        MatchKit.AddMatch(
            new[] { MatchKit.At(0, 0.1f, -24) },
            new[]
            {
                MatchKit.At(0, 0.3f, -21), MatchKit.At(-6, 0.3f, -20), MatchKit.At(6, 0.3f, -20), MatchKit.At(-20, 0.3f, -18), MatchKit.At(20, 0.3f, -18),
                MatchKit.At(-22, 0.3f, 8), MatchKit.At(22, 0.3f, 8), MatchKit.At(-8, 0.3f, 22), MatchKit.At(8, 0.3f, 22), MatchKit.At(0, 0.3f, 26),
            },
            new[] { MatchKit.At(-15, 0, 0, 180f), MatchKit.At(15, 0, 0, 180f), MatchKit.At(0, 0, 20, 180f) });
        EditorSceneManager.SaveScene(scene, ScenePath);
        SceneBuilder.RegisterBuildScenes();
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

    // Unlit and see-through, for shot effects: additive for light, alpha blended for dust. The particle shader
    // multiplies vertex colours in, which is what gives the effect meshes and tracers their soft edges.
    // Set up by hand because the blend mode is normally applied by the material inspector.
    static Material FxMaterial(string name, Color color, bool additive)
    {
        var mat = SaveMaterial(name, "Universal Render Pipeline/Particles/Unlit", color);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", additive ? 2f : 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetShaderPassEnabled("DepthOnly", false);
        mat.SetShaderPassEnabled("ShadowCaster", false);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Shot effect materials and the hit-confirmation sound of a combatant.
    static void WireShotFx(CombatantView view, GameObject go, Material glow, Material dust)
    {
        Set(view, "_glowMaterial", p => p.objectReferenceValue = glow);
        Set(view, "_dustMaterial", p => p.objectReferenceValue = dust);
        // Heard by the shooter only, so not placed in the world.
        var confirm = Source(go.transform, "ConfirmAudio", 1.6f, 1f, 10f);
        confirm.spatialBlend = 0f;
        Set(view, "_confirmSource", p => p.objectReferenceValue = confirm);
        Set(view, "_hitConfirmClip", p => p.objectReferenceValue = SceneLook.Load<AudioClip>(FoleyDir + "impactPunch_medium_001.ogg"));
        Set(view, "_killConfirmClip", p => p.objectReferenceValue = SceneLook.Load<AudioClip>(FoleyDir + "impactPunch_medium_002.ogg"));
    }

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

    // Model, animation, held weapons and sounds shared by every combatant prefab.
    static Renderer[] AddCharacter(GameObject go)
    {
        var visual = CharacterKit.AddTo(go.transform);
        go.AddComponent<CombatantMotion>();
        var animation = go.AddComponent<CombatantAnimation>();
        Set(animation, "_animator", p => p.objectReferenceValue = visual.Animator);
        Set(animation, "_spine", p => p.objectReferenceValue = visual.Spine);
        Set(animation, "_hips", p => p.objectReferenceValue = visual.Hips);
        Set(animation, "_footLeft", p => p.objectReferenceValue = visual.FootLeft);
        Set(animation, "_footRight", p => p.objectReferenceValue = visual.FootRight);
        SetArray(animation, "_jumpBones", visual.JumpBones);
        Set(animation, "_forwardClipSpeed", p => p.floatValue = visual.Gait.RunSpeed);
        Set(animation, "_backClipSpeed", p => p.floatValue = visual.Gait.BackSpeed);
        Set(animation, "_footPlantHeight", p => p.floatValue = visual.Gait.FootPlantHeight);
        Set(animation, "_footLiftHeight", p => p.floatValue = visual.Gait.FootLiftHeight);
        var weaponVisual = go.AddComponent<WeaponVisual>();
        SetArray(weaponVisual, "_modelsByWeaponId", visual.WeaponModels);
        Set(weaponVisual, "_fitByWeaponId", p =>
        {
            p.arraySize = visual.WeaponFits.Length;
            for (int i = 0; i < visual.WeaponFits.Length; i++)
            {
                var fit = p.GetArrayElementAtIndex(i);
                fit.FindPropertyRelative("Rotation").quaternionValue = visual.WeaponFits[i].Rotation;
                fit.FindPropertyRelative("CenterOffset").vector3Value = visual.WeaponFits[i].CenterOffset;
                fit.FindPropertyRelative("Size").vector3Value = visual.WeaponFits[i].Size;
            }
        });

        var audio = go.AddComponent<CombatantAudio>();
        Set(audio, "_weaponSource", p => p.objectReferenceValue = Source(go.transform, "WeaponAudio", 1.3f, 3f, 70f));
        Set(audio, "_bodySource", p => p.objectReferenceValue = Source(go.transform, "BodyAudio", 0.3f, 1.5f, 25f));
        // Indexed by weapon id: none, VK-9, Ordo, Warden, Breacher.
        SetArray(audio, "_shotByWeapon", Clips(WeaponAudioDir + "FreeFirearmLibrary/", null, "shot_pistol.wav", "shot_smg.wav", "shot_rifle.wav", "shot_shotgun.wav"));
        SetArray(audio, "_reloadByWeapon", Clips(WeaponAudioDir + "OgaGunReload/", null, "reload_pistol.wav", "reload_rifle.wav", "reload_rifle.wav", "reload_shotgun.wav"));
        SetArray(audio, "_footsteps", Clips(FoleyDir, "footstep_concrete_000.ogg", "footstep_concrete_001.ogg", "footstep_concrete_002.ogg", "footstep_concrete_003.ogg", "footstep_concrete_004.ogg"));
        SetArray(audio, "_hits", Clips(FoleyDir, "impactPunch_medium_000.ogg", "impactPunch_medium_001.ogg", "impactPunch_medium_002.ogg"));
        return visual.BodyRenderers;
    }

    static AudioSource Source(Transform parent, string name, float height, float minDistance, float maxDistance)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0, height, 0);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        return source;
    }

    static Object[] Clips(string dir, params string[] files)
    {
        var clips = new Object[files.Length];
        for (int i = 0; i < files.Length; i++)
            if (files[i] != null) clips[i] = SceneLook.Load<AudioClip>(dir + files[i]);
        return clips;
    }

    static NetworkObject SavePrefab(GameObject go, string name)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/" + name + ".prefab");
        Object.DestroyImmediate(go);
        return prefab.GetComponent<NetworkObject>();
    }

    static NetworkObject BuildPlayerPrefab(Material glow, Material dust)
    {
        var go = new GameObject("Player");
        var cc = go.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0);
        go.AddComponent<NetworkObject>();
        go.AddComponent<NetworkTransform>();
        var combatant = go.AddComponent<Combatant>();
        go.AddComponent<CombatantStance>();
        var controller = go.AddComponent<PlayerController>();
        var view = go.AddComponent<CombatantView>();
        var renderers = AddCharacter(go);
        Set(combatant, "_isPlayer", p => p.boolValue = true);
        SetArray(controller, "_bodyRenderers", renderers);
        WireShotFx(view, go, glow, dust);
        return SavePrefab(go, "Player");
    }

    static NetworkObject BuildDummyPrefab(Material glow, Material dust)
    {
        var go = new GameObject("TrainingDummy");
        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.height = 1.8f; capsule.radius = 0.35f; capsule.center = new Vector3(0, 0.9f, 0);
        go.AddComponent<NetworkObject>();
        go.AddComponent<Combatant>();
        go.AddComponent<CombatantStance>();
        go.AddComponent<TrainingDummy>();
        var view = go.AddComponent<CombatantView>();
        AddCharacter(go);
        WireShotFx(view, go, glow, dust);
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
        // Weapon pickups show the weapon itself, lying across the pickup point.
        var models = new GameObject[CharacterKit.WeaponSlots];
        foreach (var weapon in CharacterKit.Weapons)
        {
            var model = CharacterKit.InstantiateWeapon(weapon);
            model.transform.position -= SceneLook.WorldBounds(model).center;
            model.transform.SetParent(go.transform, false);
            model.SetActive(false);
            models[weapon.Id] = model;
        }
        SetArray(view, "_weaponModelsById", models);
        return SavePrefab(go, "LootItem");
    }

    static void BuildLevel(UnityEngine.Rendering.VolumeProfile postProfile)
    {
        SceneLook.SetupSky();
        SceneLook.AddLights();
        SceneLook.AddPostVolume(postProfile);

        var level = new GameObject("Level").transform;
        SceneLook.WorldBox("Floor", level, new Vector3(0, -0.5f, 0), new Vector3(Half * 2, 1, Half * 2), SceneLook.Floor, 2.5f);
        SceneLook.WorldBox("WallN", level, new Vector3(0, 2, Half), new Vector3(Half * 2, 4, 1), SceneLook.Wall, 3f);
        SceneLook.WorldBox("WallS", level, new Vector3(0, 2, -Half), new Vector3(Half * 2, 4, 1), SceneLook.Wall, 3f);
        SceneLook.WorldBox("WallE", level, new Vector3(Half, 2, 0), new Vector3(1, 4, Half * 2), SceneLook.Wall, 3f);
        SceneLook.WorldBox("WallW", level, new Vector3(-Half, 2, 0), new Vector3(1, 4, Half * 2), SceneLook.Wall, 3f);
        // Sight blockers: the spawn area is hidden from every dummy, and the dummies from each other's approach.
        SceneLook.WorldBox("BlockSpawn", level, new Vector3(0, 1.5f, -14), new Vector3(24, 3, 1), SceneLook.Panel, 2.5f);
        SceneLook.WorldBox("BlockMiddle", level, new Vector3(0, 1.5f, 1), new Vector3(1, 3, 22), SceneLook.Panel, 2.5f);
        SceneLook.WorldBox("BlockNorth", level, new Vector3(0, 1.5f, 12), new Vector3(20, 3, 1), SceneLook.Panel, 2.5f);

        var covers = new[]
        {
            new Vector3(-20, 0, -10), new Vector3(-10, 0, -6), new Vector3(-22, 0, 4), new Vector3(-9, 0, 6),
            new Vector3(20, 0, -10), new Vector3(10, 0, -6), new Vector3(22, 0, 4), new Vector3(9, 0, 6),
            new Vector3(-16, 0, 18), new Vector3(16, 0, 18), new Vector3(-5, 0, 24), new Vector3(5, 0, 24),
        };
        for (int i = 0; i < covers.Length; i++)
            // Every third cover is the muted red accent; the rest are plate metal.
            SceneLook.WorldBox("Cover" + i, level, covers[i] + Vector3.up * 0.6f, new Vector3(2f, 1.2f, 1f),
                i % 3 == 0 ? SceneLook.Accent : SceneLook.Plates, i % 3 == 0 ? 1.2f : 0.25f);

        var cam = new GameObject("Main Camera");
        cam.tag = "MainCamera";
        var camera = cam.AddComponent<Camera>();
        camera.nearClipPlane = 0.05f;
        SceneLook.EnablePost(camera);
        cam.AddComponent<AudioListener>();
        cam.transform.position = new Vector3(0, 12, -34);
        cam.transform.LookAt(Vector3.zero);
    }
}
