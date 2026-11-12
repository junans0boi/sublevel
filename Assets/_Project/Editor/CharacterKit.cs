using System.IO;
using System.Linq;
using Game.Gameplay;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Builds the animated character used by players and dummies from the CC0 model: project materials,
// looping clips, an animator controller and the four weapon models attached to the right hand.
public static class CharacterKit
{
    public const string CharacterPath = "Assets/_Project/Art/Characters/QuaterniusModularMen/Swat.fbx";
    const string WeaponDir = "Assets/_Project/Art/Weapons/QuaterniusUltimateGuns/";
    const string ControllerPath = SceneLook.GeneratedDir + "/Character.controller";
    const string HandBone = "Wrist.R";
    const string ReferencePose = "Idle_Gun_Pointing";
    const float CharacterHeight = 1.8f;

    // One row per weapon id (ADR-032 names): model file, length in meters, whether the model's muzzle points
    // along -X, and where the model's center sits relative to the wrist (forward, up) in the aiming pose.
    // No IK: these offsets are the only per-weapon grip adjustment.
    public struct WeaponModel
    {
        public int Id; public string File; public float Length; public bool MuzzleAlongMinusX; public float Forward; public float Up;
    }

    public static readonly WeaponModel[] Weapons =
    {
        new WeaponModel { Id = 1, File = "Pistol_1.fbx", Length = 0.24f, MuzzleAlongMinusX = true, Forward = 0.12f, Up = 0.03f },        // VK-9
        new WeaponModel { Id = 2, File = "SubmachineGun_1.fbx", Length = 0.50f, MuzzleAlongMinusX = true, Forward = 0.14f, Up = 0.00f }, // Ordo
        new WeaponModel { Id = 3, File = "Bullpup_1.fbx", Length = 0.75f, MuzzleAlongMinusX = true, Forward = 0.09f, Up = 0.01f },       // Warden
        new WeaponModel { Id = 4, File = "Shotgun_1.fbx", Length = 0.95f, MuzzleAlongMinusX = true, Forward = 0.30f, Up = 0.03f },       // Breacher
    };

    // Animator state -> clip in the character file. Looping ones are marked.
    static readonly (string state, string clip, bool loop)[] States =
    {
        (CombatantAnimation.Idle, "Idle_Gun_Pointing", true),
        (CombatantAnimation.Forward, "Run_Shoot", true),
        (CombatantAnimation.Back, "Run_Back", true),
        (CombatantAnimation.Left, "Run_Left", true),
        (CombatantAnimation.Right, "Run_Right", true),
        (CombatantAnimation.Shoot, "Idle_Gun_Shoot", false),
        (CombatantAnimation.Hit, "HitRecieve", false),
        (CombatantAnimation.Death, "Death", false),
    };

    // Length of arrays indexed by weapon id.
    public static int WeaponSlots => Weapons.Max(w => w.Id) + 1;

    public struct Visual
    {
        public Transform Root;
        public Animator Animator;
        public Renderer[] Renderers;       // body and weapons
        public GameObject[] WeaponModels;  // indexed by weapon id
    }

    // Import settings and shared assets. Call once before AddTo.
    public static void Prepare()
    {
        RemapMaterialsAndLoopClips();
        BuildController();
    }

    // Tileable soft noise: RGB darkens the base color a little, A varies smoothness. Self-made, so it lives outside Art/ (ADR-027).
    static Texture2D FabricNoise()
    {
        const string path = SceneLook.GeneratedDir + "/Textures/FabricNoise.png";
        if (!File.Exists(path))
        {
            const int size = 256;
            var random = new System.Random(7);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            var sum = new float[size * size];
            foreach (var cells in new[] { 8, 32, 128 })
            {
                var lattice = new float[cells * cells];
                for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)random.NextDouble();
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float fx = x * cells / (float)size, fy = y * cells / (float)size;
                        int x0 = (int)fx, y0 = (int)fy, x1 = (x0 + 1) % cells, y1 = (y0 + 1) % cells;
                        float tx = Mathf.SmoothStep(0, 1, fx - x0), ty = Mathf.SmoothStep(0, 1, fy - y0);
                        sum[y * size + x] += Mathf.Lerp(
                            Mathf.Lerp(lattice[y0 * cells + x0], lattice[y0 * cells + x1], tx),
                            Mathf.Lerp(lattice[y1 * cells + x0], lattice[y1 * cells + x1], tx), ty) / 3f;
                    }
            }
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                var shade = Mathf.Lerp(0.78f, 1f, sum[i]);
                pixels[i] = new Color(shade, shade, shade, Mathf.Lerp(0.5f, 1f, sum[i]));
            }
            tex.SetPixels(pixels);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
        }
        return SceneLook.Load<Texture2D>(path);
    }

    // Replaces the flat colors embedded in the character FBX with project materials: slightly varied
    // colors, per-surface roughness and a soft noise so large areas are not one flat tone.
    static void RemapMaterialsAndLoopClips()
    {
        var noise = FabricNoise();
        var looks = new (string source, Color color, float smoothness)[]
        {
            ("Swat", new Color(0.33f, 0.37f, 0.43f), 0.18f),        // uniform fabric
            ("Swat_Black", new Color(0.13f, 0.13f, 0.14f), 0.38f), // vest, pads, boots
            ("Visor", new Color(0.03f, 0.035f, 0.04f), 0.9f),
            ("Skin", new Color(0.80f, 0.62f, 0.50f), 0.45f),
        };
        var importer = (ModelImporter)AssetImporter.GetAtPath(CharacterPath);
        foreach (var (source, color, smoothness) in looks)
        {
            var path = $"{SceneLook.MaterialDir}/Character_{source}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", noise);
            mat.SetTextureScale("_BaseMap", new Vector2(3f, 3f));
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_SmoothnessTextureChannel", 1f);
            mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            EditorUtility.SetDirty(mat);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), source), mat);
        }

        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
            clip.loopTime = States.Any(s => s.loop && clip.name.EndsWith("|" + s.clip));
        importer.clipAnimations = clips;

        AssetDatabase.SaveAssets();
        importer.SaveAndReimport();
    }

    static AnimationClip Clip(string name)
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath(CharacterPath).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__") && c.name.EndsWith("|" + name));
        if (clip == null) throw new FileNotFoundException("animation clip missing in character file: " + name);
        return clip;
    }

    static AnimatorController BuildController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var machine = controller.layers[0].stateMachine;
        foreach (var existing in machine.states.ToArray()) machine.RemoveState(existing.state);
        foreach (var (state, clip, _) in States)
        {
            var added = machine.AddState(state);
            added.motion = Clip(clip);
            if (state == CombatantAnimation.Idle) machine.defaultState = added;
        }
        EditorUtility.SetDirty(controller);
        return controller;
    }

    // Adds the character under `parent`, standing at its origin and facing its forward, with all weapons attached but hidden.
    public static Visual AddTo(Transform parent)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(SceneLook.Load<GameObject>(CharacterPath));
        go.name = "Visual";
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            foreach (var m in r.sharedMaterials)
                if (!AssetDatabase.GetAssetPath(m).StartsWith(SceneLook.MaterialDir))
                    throw new System.Exception($"character material not remapped: {r.name}/{m.name}");

        // Weapons are fitted to the hand in the aiming pose, with the character still at the world origin.
        Clip(ReferencePose).SampleAnimation(go, 0f);
        go.transform.localScale *= CharacterHeight / SceneLook.WorldBounds(go).size.y;
        go.transform.position += Vector3.down * SceneLook.WorldBounds(go).min.y;

        var hand = go.GetComponentsInChildren<Transform>().First(t => t.name == HandBone);
        var models = new GameObject[WeaponSlots];
        foreach (var weapon in Weapons)
        {
            var model = InstantiateWeapon(weapon);
            var target = hand.position + Vector3.forward * weapon.Forward + Vector3.up * weapon.Up;
            model.transform.position += target - SceneLook.WorldBounds(model).center;
            model.transform.SetParent(hand, true);
            models[weapon.Id] = model;
        }
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (var model in models)
            if (model != null) model.SetActive(false);

        // The model imports without an Animator; clips bind by bone path, so no avatar is needed.
        var animator = go.GetComponent<Animator>();
        if (animator == null) animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = SceneLook.Load<AnimatorController>(ControllerPath);
        animator.applyRootMotion = false;
        // Server-side characters must keep animating (and moving their hand-held weapon) while off screen.
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var local = go.transform.position;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        return new Visual { Root = go.transform, Animator = animator, Renderers = renderers, WeaponModels = models };
    }

    // The weapon model at true size, muzzle along world +Z.
    public static GameObject InstantiateWeapon(WeaponModel weapon)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(SceneLook.Load<GameObject>(WeaponDir + weapon.File));
        go.name = "Weapon" + weapon.Id;
        var size = SceneLook.WorldBounds(go).size;
        go.transform.rotation = Quaternion.Euler(0, weapon.MuzzleAlongMinusX ? 90 : -90, 0) * go.transform.rotation;
        go.transform.localScale *= weapon.Length / Mathf.Max(size.x, size.z);
        return go;
    }

    // tools/unity.sh run-gfx CharacterKit.RenderPreview -> Logs/lookdev/m15_weapons.png, m15_poses.png
    public static void RenderPreview()
    {
        SceneLook.PrepareTextures();
        var profile = RenderSetup.Apply();
        Prepare();
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        SceneLook.SetupSky();
        SceneLook.AddLights();
        SceneLook.AddPostVolume(profile);
        SceneLook.WorldBox("Floor", null, new Vector3(0, -0.05f, 0), new Vector3(30, 0.1f, 30), SceneLook.Floor, 2.5f);
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.fieldOfView = 30;
        SceneLook.EnablePost(cam);
        Directory.CreateDirectory("Logs/lookdev");

        // Row 1: each weapon in the aiming pose, seen from the side. Row 2: one frame of every animation state.
        for (int i = 0; i < Weapons.Length; i++)
        {
            var slot = new GameObject("Hold" + Weapons[i].Id).transform;
            slot.position = new Vector3(0, 0, i * 1.6f);
            var visual = AddTo(slot);
            visual.WeaponModels[Weapons[i].Id].SetActive(true);
        }
        SceneLook.Shot(cam, "Logs/lookdev/m15_weapons.png", new Vector3(11f, 1.3f, 2.4f), new Vector3(0, 1.05f, 2.4f));

        for (int i = 0; i < States.Length; i++)
        {
            var slot = new GameObject("Pose" + States[i].state).transform;
            slot.position = new Vector3(40, 0, i * 1.5f);
            var visual = AddTo(slot);
            visual.WeaponModels[3].SetActive(true);
            var clip = Clip(States[i].clip);
            clip.SampleAnimation(visual.Root.gameObject, clip.length * 0.5f);
        }
        SceneLook.WorldBox("Floor2", null, new Vector3(40, -0.05f, 5), new Vector3(30, 0.1f, 30), SceneLook.Floor, 2.5f);
        SceneLook.Shot(cam, "Logs/lookdev/m15_poses.png", new Vector3(62f, 1.3f, 5.25f), new Vector3(40, 1.0f, 5.25f));
        EditorApplication.Exit(0);
    }
}
