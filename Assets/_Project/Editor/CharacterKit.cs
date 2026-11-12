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

    const string MaskPath = SceneLook.GeneratedDir + "/CharacterUpperBody.mask";
    static readonly string[] UpperBodyRootCandidates = { "Torso", "Chest", "Abdomen" };
    const string FootLeftBone = "Foot.L", FootRightBone = "Foot.R", HipsBone = "Hips";

    // Clips used from the character file (ADR-045, ADR-049). Base layer: a blend over speed along the legs'
    // heading plus whole-body states. The file's Run_Left/Run_Right turn the whole body sideways, so they are not used. Upper layer (arms and torso, armed only): weapon lowered, raised, firing.
    const string IdleClip = "Idle", WalkClip = "Walk", RunClip = "Run", BackClip = "Run_Back";
    const string HitClip = "HitRecieve", DeathClip = "Death";
    const string LoweredClip = "Idle_Gun", AimClip = ReferencePose, ShootClip = "Idle_Gun_Shoot";
    static readonly string[] LoopingClips = { IdleClip, WalkClip, RunClip, BackClip, LoweredClip, AimClip };

    // What the clips themselves say about pace and foot height, measured once per build from the model.
    public struct GaitProfile
    {
        public float WalkSpeed, RunSpeed, BackSpeed; // ground speed each clip was authored for (m/s)
        public float FootPlantHeight, FootLiftHeight;                       // above the character's origin
    }

    static GaitProfile _gait;

    // Length of arrays indexed by weapon id.
    public static int WeaponSlots => Weapons.Max(w => w.Id) + 1;

    public struct Visual
    {
        public Transform Root;
        public Animator Animator;
        public Renderer[] BodyRenderers;   // the character without the weapons
        public GameObject[] WeaponModels;  // indexed by weapon id
        public WeaponVisual.Fit[] WeaponFits; // indexed by weapon id
        public Transform Spine, Hips, FootLeft, FootRight;
        public GaitProfile Gait;
    }

    // Import settings and shared assets. Call once before AddTo.
    public static void Prepare()
    {
        RemapMaterialsAndLoopClips();
        _gait = MeasureGait();
        BuildController(_gait, BuildUpperBodyMask());
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
            clip.loopTime = LoopingClips.Any(name => clip.name.EndsWith("|" + name));
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

    static GameObject Instance()
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(SceneLook.Load<GameObject>(CharacterPath));
        go.transform.localScale *= CharacterHeight / RawHeight(go);
        return go;
    }

    // Model height in the reference pose, before scaling.
    static float RawHeight(GameObject go)
    {
        Clip(ReferencePose).SampleAnimation(go, 0f);
        return SceneLook.WorldBounds(go).size.y / go.transform.localScale.y;
    }

    static Transform Bone(GameObject go, string name)
    {
        var bone = go.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == name);
        if (bone == null) throw new System.Exception("bone missing in character: " + name);
        return bone;
    }

    static Transform UpperBodyRoot(GameObject go)
    {
        var all = go.GetComponentsInChildren<Transform>();
        foreach (var name in UpperBodyRootCandidates)
        {
            var bone = all.FirstOrDefault(t => t.name == name);
            if (bone != null) return bone;
        }
        throw new System.Exception("no upper-body root bone found in character");
    }

    // The clips run in place, so the planted foot slides back at the speed the clip was animated for.
    static GaitProfile MeasureGait()
    {
        var go = Instance();
        try
        {
            Transform left = Bone(go, FootLeftBone), right = Bone(go, FootRightBone);
            // Ground speed = how fast a foot slides back while it is on the ground. Only samples where the foot is
            // near its lowest point count, so the flight phase of a run does not dilute the result.
            float Speed(string clipName, Vector3 axis)
            {
                var clip = Clip(clipName);
                const int samples = 96;
                float dt = clip.length / samples;
                var feet = new[] { left, right };
                var positions = new Vector3[feet.Length, samples + 1];
                float low = float.MaxValue, high = float.MinValue;
                for (int i = 0; i <= samples; i++)
                {
                    clip.SampleAnimation(go, i * dt);
                    for (int f = 0; f < feet.Length; f++)
                    {
                        positions[f, i] = feet[f].position;
                        low = Mathf.Min(low, positions[f, i].y);
                        high = Mathf.Max(high, positions[f, i].y);
                    }
                }
                float grounded = low + (high - low) * 0.15f, travelled = 0f, time = 0f;
                for (int f = 0; f < feet.Length; f++)
                    for (int i = 1; i <= samples; i++)
                    {
                        if (positions[f, i].y > grounded || positions[f, i - 1].y > grounded) continue;
                        travelled += Mathf.Abs(Vector3.Dot(positions[f, i] - positions[f, i - 1], axis));
                        time += dt;
                    }
                return time > 0f ? travelled / time : 0f;
            }

            var gait = new GaitProfile
            {
                WalkSpeed = Speed(WalkClip, Vector3.forward), RunSpeed = Speed(RunClip, Vector3.forward),
                BackSpeed = Speed(BackClip, Vector3.forward),
            };

            // Foot height range over a walk cycle (the lowest lift of any gait, so every gait crosses the thresholds).
            var walk = Clip(WalkClip);
            float low = float.MaxValue, high = float.MinValue;
            for (int i = 0; i <= 48; i++)
            {
                walk.SampleAnimation(go, walk.length * i / 48f);
                foreach (var foot in new[] { left, right })
                {
                    low = Mathf.Min(low, foot.position.y);
                    high = Mathf.Max(high, foot.position.y);
                }
            }
            gait.FootPlantHeight = low + (high - low) * 0.25f;
            gait.FootLiftHeight = low + (high - low) * 0.55f;
            Debug.Log($"[CharacterKit] gait: walk {gait.WalkSpeed:F2} run {gait.RunSpeed:F2} back {gait.BackSpeed:F2} m/s, foot height {low:F3}..{high:F3} m");
            if (gait.RunSpeed < 1f || gait.WalkSpeed < 0.3f || gait.RunSpeed <= gait.WalkSpeed || high - low < 0.03f)
                throw new System.Exception("gait measurement looks wrong; the locomotion clips may not be in-place cycles");
            return gait;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // Transform mask covering the torso, arms and head, so the upper layer leaves the legs to the base layer.
    static AvatarMask BuildUpperBodyMask()
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(SceneLook.Load<GameObject>(CharacterPath));
        try
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, MaskPath);
            }
            mask.transformCount = 0;
            mask.AddTransformPath(go.transform, true);
            var upperPath = AnimationUtility.CalculateTransformPath(UpperBodyRoot(go), go.transform);
            int active = 0;
            for (int i = 0; i < mask.transformCount; i++)
            {
                var path = mask.GetTransformPath(i);
                bool upper = path == upperPath || path.StartsWith(upperPath + "/");
                mask.SetTransformActive(i, upper);
                if (upper) active++;
            }
            Debug.Log($"[CharacterKit] upper-body mask from '{upperPath}': {active} of {mask.transformCount} transforms");
            EditorUtility.SetDirty(mask);
            return mask;
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static AnimatorController BuildController(GaitProfile gait, AvatarMask upperBody)
    {
        // Rebuilt from scratch every time; the asset itself is kept so references to it stay valid.
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.layers = new AnimatorControllerLayer[0];
        controller.parameters = new AnimatorControllerParameter[0];
        foreach (var leftover in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            if (leftover != null && leftover != controller) Object.DestroyImmediate(leftover, true);

        controller.AddParameter(CombatantAnimation.Move, AnimatorControllerParameterType.Float);
        controller.AddParameter(new AnimatorControllerParameter { name = CombatantAnimation.Pace, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        controller.AddParameter(CombatantAnimation.AirPhase, AnimatorControllerParameterType.Float);

        // Base layer: idle in the middle, each moving clip at the speed it was animated for.
        controller.AddLayer("Base");
        var locomotion = controller.CreateBlendTreeInController(CombatantAnimation.Locomotion, out var tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = CombatantAnimation.Move;
        tree.useAutomaticThresholds = false;
        tree.AddChild(Clip(BackClip), -gait.BackSpeed);
        tree.AddChild(Clip(IdleClip), 0f);
        tree.AddChild(Clip(WalkClip), gait.WalkSpeed);
        tree.AddChild(Clip(RunClip), gait.RunSpeed);
        // The legs alone play faster when the character outruns its clips.
        locomotion.speedParameter = CombatantAnimation.Pace;
        locomotion.speedParameterActive = true;
        var baseMachine = controller.layers[0].stateMachine;
        baseMachine.defaultState = locomotion;
        // No jump clip in the file: the air state holds whichever moment of the run cycle the parameter picks (ADR-048).
        var air = baseMachine.AddState(CombatantAnimation.Air);
        air.motion = Clip(RunClip);
        air.timeParameter = CombatantAnimation.AirPhase;
        air.timeParameterActive = true;
        baseMachine.AddState(CombatantAnimation.Hit).motion = Clip(HitClip);
        baseMachine.AddState(CombatantAnimation.Death).motion = Clip(DeathClip);

        // Upper layer: off by default; CombatantAnimation raises its weight while a weapon is held.
        controller.AddLayer("Upper");
        var layers = controller.layers;
        layers[1].avatarMask = upperBody;
        layers[1].defaultWeight = 0f;
        controller.layers = layers;
        var upperMachine = controller.layers[1].stateMachine;
        var lowered = upperMachine.AddState(CombatantAnimation.Lowered);
        lowered.motion = Clip(LoweredClip);
        upperMachine.defaultState = lowered;
        upperMachine.AddState(CombatantAnimation.Aim).motion = Clip(AimClip);
        upperMachine.AddState(CombatantAnimation.Shoot).motion = Clip(ShootClip);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
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
        var bodyRenderers = go.GetComponentsInChildren<Renderer>(true);
        var models = new GameObject[WeaponSlots];
        var fits = new WeaponVisual.Fit[WeaponSlots];
        foreach (var weapon in Weapons)
        {
            var model = InstantiateWeapon(weapon);
            var target = hand.position + Vector3.forward * weapon.Forward + Vector3.up * weapon.Up;
            model.transform.position += target - SceneLook.WorldBounds(model).center;
            // The character faces +Z here, so this is the model's pose "pointing forward" for the eye view.
            var bounds = SceneLook.WorldBounds(model);
            fits[weapon.Id] = new WeaponVisual.Fit { Rotation = model.transform.rotation, CenterOffset = bounds.center - model.transform.position, Size = bounds.size };
            model.transform.SetParent(hand, true);
            models[weapon.Id] = model;
        }
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
        return new Visual
        {
            Root = go.transform, Animator = animator, BodyRenderers = bodyRenderers, WeaponModels = models, WeaponFits = fits,
            Spine = UpperBodyRoot(go), Hips = Bone(go, HipsBone), FootLeft = Bone(go, FootLeftBone), FootRight = Bone(go, FootRightBone), Gait = _gait,
        };
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

    // tools/unity.sh run-gfx CharacterKit.RenderClipSheet -> Logs/lookdev/clips_<name>.png (eight phases per clip,
    // seen from the side) and every clip's length in the log. For choosing poses by eye.
    public static void RenderClipSheet()
    {
        SceneLook.PrepareTextures();
        var profile = RenderSetup.Apply();
        Prepare();
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        SceneLook.SetupSky();
        SceneLook.AddLights();
        SceneLook.AddPostVolume(profile);
        SceneLook.WorldBox("Floor", null, new Vector3(0, -0.05f, 0), new Vector3(40, 0.1f, 40), SceneLook.Floor, 2.5f);
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.fieldOfView = 30;
        SceneLook.EnablePost(cam);
        Directory.CreateDirectory("Logs/lookdev");

        foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(CharacterPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")))
            Debug.Log($"[CharacterKit] clip {clip.name}: {clip.length:F2}s");

        const int phases = 8;
        var slots = new Visual[phases];
        for (int i = 0; i < phases; i++)
        {
            var slot = new GameObject("Phase" + i).transform;
            slot.position = new Vector3(0, 0, i * 1.4f);
            slots[i] = AddTo(slot);
            slots[i].WeaponModels[3].SetActive(true);
        }
        foreach (var name in new[] { RunClip, WalkClip, BackClip, "Roll", "Run_Left" })
        {
            var clip = Clip(name);
            for (int i = 0; i < phases; i++)
                clip.SampleAnimation(slots[i].Root.gameObject, clip.length * i / phases);
            SceneLook.Shot(cam, $"Logs/lookdev/clips_{name}.png", new Vector3(14f, 1.2f, 4.9f), new Vector3(0, 0.9f, 4.9f));
        }
        EditorApplication.Exit(0);
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

        // Row 1: each weapon in the aiming pose, seen from the side. Row 2: one frame of every clip in use.
        for (int i = 0; i < Weapons.Length; i++)
        {
            var slot = new GameObject("Hold" + Weapons[i].Id).transform;
            slot.position = new Vector3(0, 0, i * 1.6f);
            var visual = AddTo(slot);
            visual.WeaponModels[Weapons[i].Id].SetActive(true);
        }
        SceneLook.Shot(cam, "Logs/lookdev/m15_weapons.png", new Vector3(11f, 1.3f, 2.4f), new Vector3(0, 1.05f, 2.4f));

        var poses = new[] { IdleClip, WalkClip, RunClip, BackClip, LoweredClip, AimClip, ShootClip, HitClip, DeathClip };
        for (int i = 0; i < poses.Length; i++)
        {
            var slot = new GameObject("Pose" + poses[i]).transform;
            slot.position = new Vector3(40, 0, i * 1.5f);
            var visual = AddTo(slot);
            visual.WeaponModels[3].SetActive(true);
            var clip = Clip(poses[i]);
            clip.SampleAnimation(visual.Root.gameObject, clip.length * 0.5f);
        }
        SceneLook.WorldBox("Floor2", null, new Vector3(40, -0.05f, 5), new Vector3(30, 0.1f, 30), SceneLook.Floor, 2.5f);
        SceneLook.Shot(cam, "Logs/lookdev/m15_poses.png", new Vector3(70f, 1.3f, 7.5f), new Vector3(40, 1.0f, 7.5f));
        EditorApplication.Exit(0);
    }
}
