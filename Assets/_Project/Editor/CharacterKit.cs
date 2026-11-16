using System.IO;
using System.Linq;
using Game.Gameplay;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Builds the animated character used by players and dummies from a Humanoid roster model (ADR-230): the roster prefab
// (CharacterRoster), the shared Humanoid controller (CharacterControllerBuilder) and the weapon models on its WeaponSocket.
public static class CharacterKit
{
<<<<<<< HEAD
    public const string DefaultRoster = "MixamoSwat";
    const string WeaponDir = "Assets/_Project/Art/Weapons/QuaterniusUltimateGuns/";
=======
    public const string CharacterPath = "Assets/_Project/Art/Characters/QuaterniusModularMen/Swat.fbx";
    const string WeaponDir = "Assets/_Project/Art/Weapons/";
    const string ControllerPath = SceneLook.GeneratedDir + "/Character.controller";
    const string HandBone = "Wrist.R";
    const string ReferencePose = "Idle_Gun_Pointing";
    const float CharacterHeight = 1.8f;
>>>>>>> track/ui

    // One row per weapon id (ADR-032 names): model file, length in meters, whether the model's muzzle points
    // along -X, and where the model's center sits relative to the wrist (forward, up) in the aiming pose.
    // No IK: these offsets are the only per-weapon grip adjustment.
    public struct WeaponModel
    {
        public int Id; public string File; public float Length; public bool MuzzleAlongMinusX; public Part[] Parts; public float Forward; public float Up;
    }

    // A kit-bash piece on a gun, in the gun file's own units (X = length, muzzle at -X, Y up), Pos = middle of the piece: another model (File)
    // or, with File null, a plain box of `Size` coloured like the gun (a magazine). ADR-217.
    public struct Part { public string File; public Vector3 Pos; public Vector3 Size; public float Scale; }

    // Where the gun's centre sits ahead of the wrist: the gun is held by its grip, `gripFrac` of its length in front of
    // the butt (pistol 0, shotgun 0.18, SMG 0.22, rifle/DMR/LMG 0.27-0.30, bullpup 0.38), so the centre is
    // length * (0.5 - gripFrac) ahead of the hand. WeaponProbe.Fit prints the stock and muzzle reach per gun.
    static float Grip(float length, float gripFrac) => length * (0.5f - gripFrac);

    public static readonly WeaponModel[] Weapons =
    {
        new WeaponModel { Id = 1, File = "QuaterniusUltimateGuns/Pistol_1.fbx", Length = 0.24f, MuzzleAlongMinusX = true, Forward = Grip(0.24f, 0f), Up = 0.03f },        // VK-9
        new WeaponModel { Id = 2, File = "QuaterniusUltimateGuns/SubmachineGun_1.fbx", Length = 0.50f, MuzzleAlongMinusX = true, Forward = Grip(0.50f, 0.22f), Up = 0.00f }, // Ordo
        new WeaponModel { Id = 3, File = "QuaterniusUltimateGuns/Bullpup_1.fbx", Length = 0.75f, MuzzleAlongMinusX = true, Forward = Grip(0.75f, 0.38f), Up = 0.01f },       // Warden
        new WeaponModel { Id = 4, File = "Roster/Wpn04_Breacher.fbx", Length = 0.95f, MuzzleAlongMinusX = true, Forward = Grip(0.95f, 0.28f), Up = 0.03f, Parts = new[] { new Part { Pos = new Vector3(-0.4f, -0.6f, 0f), Size = new Vector3(0.3f, 0.9f, 0.25f) } } },       // Breacher
        new WeaponModel { Id = 5, File = "Roster/Wpn05_Sentinel.fbx", Length = 1.20f, MuzzleAlongMinusX = true, Forward = Grip(1.20f, 0.27f), Up = 0.03f },   // Sentinel
        new WeaponModel { Id = 6, File = "Roster/Wpn06_Vesper.fbx", Length = 0.28f, MuzzleAlongMinusX = true, Forward = Grip(0.28f, 0f), Up = 0.03f },  // Vesper (provisional fit, ADR-217)
        new WeaponModel { Id = 7, File = "Roster/Wpn07_Brisk.fbx", Length = 0.5f, MuzzleAlongMinusX = true, Forward = Grip(0.5f, 0.22f), Up = 0f },  // Brisk (provisional fit, ADR-217)
        new WeaponModel { Id = 8, File = "Roster/Wpn08_Sable.fbx", Length = 0.5f, MuzzleAlongMinusX = true, Forward = Grip(0.5f, 0.22f), Up = 0f },  // Sable (provisional fit, ADR-217)
        new WeaponModel { Id = 9, File = "Roster/Wpn09_Vanguard.fbx", Length = 0.85f, MuzzleAlongMinusX = true, Forward = Grip(0.85f, 0.3f), Up = 0.01f },  // Vanguard (provisional fit, ADR-217)
        new WeaponModel { Id = 10, File = "Roster/Wpn10_Bulwark.fbx", Length = 0.95f, MuzzleAlongMinusX = true, Forward = Grip(0.95f, 0.32f), Up = 0.02f },  // Bulwark (provisional fit, ADR-217)
        new WeaponModel { Id = 11, File = "Roster/Wpn11_Hauler.fbx", Length = 1f, MuzzleAlongMinusX = true, Forward = Grip(1f, 0.3f), Up = 0.02f, Parts = new[] { new Part { File = "Roster/Acc_Grip.fbx", Pos = new Vector3(-2.5f, 0.25f, 0f) } } },  // Hauler (provisional fit, ADR-217)
        new WeaponModel { Id = 12, File = "Roster/Wpn12_Lancer.fbx", Length = 1f, MuzzleAlongMinusX = true, Forward = Grip(1f, 0.3f), Up = 0.02f, Parts = new[] { new Part { File = "Roster/Acc_Scope_1.fbx", Pos = new Vector3(-0.86f, 0.91f, 0f) } } },  // Lancer (provisional fit, ADR-217)
        new WeaponModel { Id = 13, File = "Roster/Wpn13_Tracer.fbx", Length = 0.85f, MuzzleAlongMinusX = true, Forward = Grip(0.85f, 0.38f), Up = 0.01f, Parts = new[] { new Part { File = "Roster/Acc_Scope_2.fbx", Pos = new Vector3(-0.94f, 1f, 0f) } } },  // Tracer (provisional fit, ADR-217)
        new WeaponModel { Id = 14, File = "Roster/Wpn14_Arbiter.fbx", Length = 1.1f, MuzzleAlongMinusX = true, Forward = Grip(1.1f, 0.27f), Up = 0.03f },  // Arbiter (provisional fit, ADR-217)
        new WeaponModel { Id = 15, File = "Roster/Wpn15_Obelisk.fbx", Length = 1.25f, MuzzleAlongMinusX = true, Forward = Grip(1.25f, 0.27f), Up = 0.03f },  // Obelisk (provisional fit, ADR-217)
        new WeaponModel { Id = 16, File = "Roster/Wpn16_Duo.fbx", Length = 0.85f, MuzzleAlongMinusX = true, Forward = Grip(0.85f, 0.18f), Up = 0.03f },  // Duo (provisional fit, ADR-217)
        new WeaponModel { Id = 31, File = "Roster/Wpn31_Bastion.fbx", Length = 1.05f, MuzzleAlongMinusX = true, Forward = Grip(1.05f, 0.27f), Up = 0.02f, Parts = new[] { new Part { File = "Roster/Acc_Bipod.fbx", Pos = new Vector3(-2.8f, -0.13f, 0f) }, new Part { Pos = new Vector3(-0.57f, -0.75f, 0f), Size = new Vector3(0.45f, 0.75f, 0.28f) } } },  // Bastion (provisional fit, ADR-217)
        new WeaponModel { Id = 32, File = "Roster/Wpn32_Anvil.fbx", Length = 0.9f, MuzzleAlongMinusX = true, Forward = Grip(0.9f, 0.27f), Up = 0.02f, Parts = new[] { new Part { File = "Roster/Acc_Bipod.fbx", Pos = new Vector3(-1.45f, -0.13f, 0f) }, new Part { Pos = new Vector3(-0.7f, -0.8f, 0f), Size = new Vector3(0.5f, 0.8f, 0.3f) } } },  // Anvil (provisional fit, ADR-217)
        new WeaponModel { Id = 33, File = "Roster/Wpn33_Marshal.fbx", Length = 1.15f, MuzzleAlongMinusX = true, Forward = Grip(1.15f, 0.27f), Up = 0.03f },  // Marshal (provisional fit, ADR-217)
        new WeaponModel { Id = 34, File = "Roster/Wpn34_Fang.fbx", Length = 0.8f, MuzzleAlongMinusX = true, Forward = Grip(0.8f, 0.3f), Up = 0.01f, Parts = new[] { new Part { File = "Roster/Acc_Silencer_Short.fbx", Pos = new Vector3(-3.45f, 0.55f, 0f) } } },  // Fang (provisional fit, ADR-217)
        new WeaponModel { Id = 35, File = "Roster/Wpn35_Hornet.fbx", Length = 0.35f, MuzzleAlongMinusX = true, Forward = Grip(0.35f, 0.22f), Up = 0.02f },  // Hornet (provisional fit, ADR-217)
    };


    // What the clips themselves say about pace and foot height, measured once per build from the model.
    public struct GaitProfile
    {
        public float WalkSpeed, RunSpeed, BackSpeed; // ground speed each clip was authored for (m/s)
        public float FootPlantHeight, FootLiftHeight;                       // above the character's origin
    }

    static GaitProfile _gait;

    // Length of arrays indexed by weapon id (guns and the code-built melee weapons, ADR-073).
    public static int WeaponSlots => Mathf.Max(Weapons.Max(w => w.Id) + 1, MeleeKit.Slots);

    public struct Visual
    {
        public Transform Root;
        public Animator Animator;
        public Renderer[] BodyRenderers;   // the character without the weapons
        public GameObject[] WeaponModels;  // indexed by weapon id
        public WeaponVisual.Fit[] WeaponFits; // indexed by weapon id
        public Transform Spine, Hips, FootLeft, FootRight;
        public Transform[] JumpBones;      // upper legs, lower legs, upper arms; left then right of each
        public GaitProfile Gait;
        public CharacterDef Def;
        public float ProneHeadAhead;       // head joint ahead of the model's origin in the prone clip (m)
    }

    // Humanoid clip import, gait measurement and the animator controller. Call once before AddTo.
    public static void Prepare()
    {
        CharacterAnimClips.EnsureImported();
        _gait = MeasureGait();
        CharacterControllerBuilder.Build(_gait);
    }

    static GameObject RosterInstance(string rosterId)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterRoster.PrefabDir}/Roster_{rosterId}.prefab");
        if (prefab == null) throw new FileNotFoundException($"roster prefab missing: {rosterId} (run CharacterImport.SyncStaging, CharacterRoster.Build)");
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }

    // Ground speed of the locomotion clips (how far the body travels over one cycle) and the foot height range of a walk.
    // The clips carry their root motion; sampling moves the hips by it, so the hips' travel along the model's forward is the speed.
    static GaitProfile MeasureGait()
    {
        var go = RosterInstance(DefaultRoster);
        try
        {
            var def = go.GetComponent<CharacterDef>();
            var hips = def.Sockets.Hips;
            float Speed(string name)
            {
                var clip = CharacterControllerBuilder.Clip(name);
                clip.SampleAnimation(def.Animator.gameObject, 0f);
                var a = go.transform.InverseTransformPoint(hips.position);
                clip.SampleAnimation(def.Animator.gameObject, clip.length);
                var b = go.transform.InverseTransformPoint(hips.position);
                return Mathf.Abs(b.z - a.z) / clip.length;
            }
            var gait = new GaitProfile { WalkSpeed = Speed("RifleWalk"), RunSpeed = Speed("RifleRun"), BackSpeed = Speed("RifleWalkBack") };
            var walk = CharacterControllerBuilder.Clip("RifleWalk");
            float low = float.MaxValue, high = float.MinValue;
            for (int i = 0; i <= 48; i++)
            {
                walk.SampleAnimation(def.Animator.gameObject, walk.length * i / 48f);
                foreach (var foot in new[] { def.Sockets.LeftFoot, def.Sockets.RightFoot })
                {
                    // Height over the model's own feet: the hips ride up and down with the root's height, so measure against the model.
                    float y = go.transform.InverseTransformPoint(foot.position).y;
                    low = Mathf.Min(low, y); high = Mathf.Max(high, y);
                }
            }
            gait.FootPlantHeight = low + (high - low) * 0.25f;
            gait.FootLiftHeight = low + (high - low) * 0.55f;
            Debug.Log($"[CharacterKit] gait: walk {gait.WalkSpeed:F2} run {gait.RunSpeed:F2} back {gait.BackSpeed:F2} m/s, foot height {low:F3}..{high:F3} m");
            if (gait.RunSpeed < 1f || gait.WalkSpeed < 0.3f || gait.RunSpeed <= gait.WalkSpeed || high - low < 0.03f)
                throw new System.Exception("gait measurement looks wrong; the locomotion clips may not carry root motion");
            return gait;
        }
        finally { Object.DestroyImmediate(go); }
    }

    // Adds the character under `parent`, standing at its origin and facing its forward, with all weapons attached but hidden.
    public static Visual AddTo(Transform parent, string rosterId = DefaultRoster)
    {
        var go = RosterInstance(rosterId);
        go.name = "Visual";
        var def = go.GetComponent<CharacterDef>();
        var model = def.Animator.transform;
        var s = def.Sockets;

        // Weapons are fitted to the hand in the aiming pose, which is also the pose the prefab is saved in.
        var modelPos = model.localPosition; var modelRot = model.localRotation;
        CharacterControllerBuilder.Clip("Swat_RifleAimingIdle_skin").SampleAnimation(model.gameObject, 0f);
        model.localPosition = modelPos; model.localRotation = modelRot;

        var hand = s.WeaponSocket;
        var models = new GameObject[WeaponSlots];
        var fits = new WeaponVisual.Fit[WeaponSlots];
        foreach (var weapon in Weapons)
        {
            var weaponModel = InstantiateWeapon(weapon);
            var target = hand.position + go.transform.forward * (weapon.Forward + GripShift) + go.transform.up * weapon.Up;
            weaponModel.transform.position += target - SceneLook.WorldBounds(weaponModel).center;
            var bounds = SceneLook.WorldBounds(weaponModel);
            fits[weapon.Id] = new WeaponVisual.Fit { Rotation = weaponModel.transform.rotation, CenterOffset = bounds.center - weaponModel.transform.position, Size = bounds.size };
            weaponModel.transform.SetParent(hand, true);
            models[weapon.Id] = weaponModel;
        }
        MeleeKit.AttachToHand(hand, models, fits);
        foreach (var m in models)
            if (m != null) m.SetActive(false);

        // Lying: how far ahead of the model's origin the head ends up, so CombatantAnimation can put it under the eyes.
        CharacterControllerBuilder.Clip("ProneFiringRifle").SampleAnimation(model.gameObject, 0f);
        float proneHeadAhead = go.transform.InverseTransformPoint(s.Head.position).z - go.transform.InverseTransformPoint(model.position).z;
        CharacterControllerBuilder.Clip("Swat_RifleAimingIdle_skin").SampleAnimation(model.gameObject, 0f);
        model.localPosition = modelPos; model.localRotation = modelRot;

        var animator = def.Animator;
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CharacterControllerBuilder.ControllerPath);
        animator.applyRootMotion = false;
        // CombatantAnimation picks the culling mode by distance (ADR-233): always animated within 40 m of the camera (footsteps, held weapon), culled beyond.
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var local = go.transform.position;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        return new Visual
        {
            Root = go.transform, Animator = animator, BodyRenderers = def.Renderers, WeaponModels = models, WeaponFits = fits,
            Spine = s.Spine, Hips = s.Hips, FootLeft = s.LeftFoot, FootRight = s.RightFoot, Gait = _gait, Def = def, ProneHeadAhead = proneHeadAhead,
            JumpBones = new[] { s.LeftUpperLeg, s.RightUpperLeg, s.LeftLowerLeg, s.RightLowerLeg, s.LeftUpperArm, s.RightUpperArm },
        };
    }

    // The old per-weapon offsets were measured from the wrist; the socket sits in the palm, this far further along the hand.
    const float GripShift = -0.05f;

    // The weapon model at true size, muzzle along world +Z.
    public static GameObject InstantiateWeapon(WeaponModel weapon)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(SceneLook.Load<GameObject>(WeaponDir + weapon.File));
        go.name = "Weapon" + weapon.Id;
        if (weapon.Parts != null) foreach (var part in weapon.Parts) AddPart(go, part);
        var size = SceneLook.WorldBounds(go).size;
        go.transform.rotation = Quaternion.Euler(0, weapon.MuzzleAlongMinusX ? 90 : -90, 0) * go.transform.rotation;
        go.transform.localScale *= weapon.Length / Mathf.Max(size.x, size.z);
        return go;
    }
<<<<<<< HEAD
=======

    static void AddPart(GameObject gun, Part part)
    {
        GameObject child;
        if (part.File != null) child = (GameObject)PrefabUtility.InstantiatePrefab(SceneLook.Load<GameObject>(WeaponDir + part.File));
        else
        {
            child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(child.GetComponent<Collider>());
            child.GetComponent<Renderer>().sharedMaterial = gun.GetComponentInChildren<Renderer>().sharedMaterials[0];
        }
        child.name = part.File != null ? System.IO.Path.GetFileNameWithoutExtension(part.File) : "Magazine";
        // Placed in world space while the gun still stands unscaled at its spawn point, so the FBX root's import scale and
        // rotation do not matter; the later fit scale then carries the part along.
        child.transform.position = gun.transform.position + part.Pos;
        child.transform.SetParent(gun.transform, true);
        if (part.File != null)
        {
            child.transform.localScale *= part.Scale > 0f ? part.Scale : 1f;
            // The piece files keep their origin anywhere, so Pos is where the middle of the piece ends up.
            var b = SceneLook.WorldBounds(child);
            child.transform.position += gun.transform.position + part.Pos - b.center;
        }
        else child.transform.localScale = part.Size / gun.transform.lossyScale.x;
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
        foreach (var name in new[] { RunClip, WalkClip, BackClip, "Roll", "Run_Left", "Run_Shoot", LoweredClip, AimClip, "Gun_Shoot", ShootClip })
        {
            var clip = Clip(name);
            for (int i = 0; i < phases; i++)
                clip.SampleAnimation(slots[i].Root.gameObject, clip.length * i / phases);
            SceneLook.Shot(cam, $"Logs/lookdev/clips_{name}.png", new Vector3(14f, 1.2f, 4.9f), new Vector3(0, 0.9f, 4.9f));
            // From the front-left: the left hand and the weapon's front end are hidden in the side view.
            SceneLook.Shot(cam, $"Logs/lookdev/clips_{name}_front.png", new Vector3(-9f, 1.6f, 14f), new Vector3(0, 0.9f, 4.9f));
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
>>>>>>> track/ui
}
