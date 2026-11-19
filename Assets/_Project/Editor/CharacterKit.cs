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
    public const string DefaultRoster = "MixamoSwat";
    const string WeaponDir = "Assets/_Project/Art/Weapons/";

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
        public float UnarmedWalkSpeed, UnarmedRunSpeed, UnarmedCrouchSpeed, SprintSpeed;        // the bare-handed walk, jog, crouch and sprint clips (m/s)
        // Punch states (ADR-255): which clip each hand plays (the Jab/Cross swapped, or mirrored, until the right fist goes out for PunchRight and the left for
        // PunchLeft), and the second in the clip when that fist is furthest out (the hit).
        public bool PunchSwap, MirrorRight, MirrorLeft;
        public float PunchLeftImpact, PunchRightImpact;
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
        // The punch clips' hands are read from the built controller (the way the game plays them), then the states are fixed up and rebuilt.
        for (int pass = 0; pass < 3; pass++)
        {
            var right = MeasurePunch(CombatantAnimation.PunchRight); var left = MeasurePunch(CombatantAnimation.PunchLeft);
            _gait.PunchRightImpact = right.impact; _gait.PunchLeftImpact = left.impact;
            bool rightOk = right.rightHand, leftOk = !left.rightHand;
            if (rightOk && leftOk) break;
            if (pass == 0 && !rightOk && !leftOk) _gait.PunchSwap = !_gait.PunchSwap;
            else { if (!rightOk) _gait.MirrorRight = !_gait.MirrorRight; if (!leftOk) _gait.MirrorLeft = !_gait.MirrorLeft; }
            CharacterControllerBuilder.Build(_gait);
        }
        Debug.Log($"[CharacterKit] punches: swap {_gait.PunchSwap} mirror R {_gait.MirrorRight} L {_gait.MirrorLeft}, fist out at {_gait.PunchRightImpact:F3}s (right) {_gait.PunchLeftImpact:F3}s (left)");
    }

    // Plays an Upper-layer punch state of the built controller on a roster model: which hand goes out furthest (forward of the chest) and when.
    static (bool rightHand, float impact) MeasurePunch(string state)
    {
        var go = RosterInstance(DefaultRoster);
        try
        {
            var def = go.GetComponent<CharacterDef>();
            var animator = def.Animator;
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CharacterControllerBuilder.ControllerPath);
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // the roster prefab culls, and nothing here is on screen
            animator.Rebind();
            animator.SetLayerWeight(1, 1f);
            float length = 1f;
            foreach (var c in animator.runtimeAnimatorController.animationClips) if (c.name.StartsWith("U_Punch")) length = Mathf.Max(length, c.length);
            float bestL = float.MinValue, bestR = float.MinValue, atL = 0f, atR = 0f, restL = 0f, restR = 0f;
            for (int k = 0; k <= 100; k++)
            {
                animator.Play(state, 1, k / 100f);
                animator.Update(0f);
                float chest = go.transform.InverseTransformPoint(def.Sockets.Chest.position).z;
                float l = go.transform.InverseTransformPoint(def.Sockets.LeftHand.position).z - chest;
                float r = go.transform.InverseTransformPoint(def.Sockets.RightHand.position).z - chest;
                if (k == 0) { restL = l; restR = r; }
                if (l > bestL) { bestL = l; atL = k / 100f; }
                if (r > bestR) { bestR = r; atR = k / 100f; }
            }
            bool right = bestR - restR > bestL - restL;
            float normalized = right ? atR : atL;
            var clipLength = animator.GetCurrentAnimatorClipInfo(1).Length > 0 ? animator.GetCurrentAnimatorClipInfo(1)[0].clip.length : 0f;
            // The state's clip length (the clip behind the state), so the impact is in seconds.
            foreach (var layer in ((UnityEditor.Animations.AnimatorController)animator.runtimeAnimatorController).layers[1].stateMachine.states)
                if (layer.state.name == state) clipLength = ((AnimationClip)layer.state.motion).length;
            return (right, normalized * clipLength);
        }
        finally { Object.DestroyImmediate(go); }
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
                float travel = Mathf.Abs(b.z - a.z) / clip.length;
                return travel > 0.1f ? travel : PlantedFootSpeed(clip, def, go); // in-place clips (UAL): how fast the planted foot slides back
            }
            var gait = new GaitProfile
            {
                WalkSpeed = Speed("RifleWalk"), RunSpeed = Speed("RifleRun"), BackSpeed = Speed("RifleWalkBack"),
                UnarmedWalkSpeed = Speed("U_Walk_Loop"), UnarmedRunSpeed = Speed("U_Jog_Fwd_Loop"), UnarmedCrouchSpeed = Speed("U_Crouch_Fwd_Loop"),
            };
            // The sprint runs on its toes, which the ankle-height test above reads as a slow stance (it measured 5.2, the same as the jog, and
            // the shown stride outran the ground by ~4 m/s, ADR-390). Its mid-stance speed, as a ratio to the jog's, scales the jog (~1.45).
            gait.SprintSpeed = gait.UnarmedRunSpeed * MidStanceSpeed(CharacterControllerBuilder.Clip("U_Sprint_Loop"), def, go) / MidStanceSpeed(CharacterControllerBuilder.Clip("U_Jog_Fwd_Loop"), def, go);
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
            Debug.Log($"[CharacterKit] unarmed gait: walk {gait.UnarmedWalkSpeed:F2} jog {gait.UnarmedRunSpeed:F2} sprint {gait.SprintSpeed:F2} m/s");
            Debug.Log($"[CharacterKit] gait: walk {gait.WalkSpeed:F2} run {gait.RunSpeed:F2} back {gait.BackSpeed:F2} m/s, foot height {low:F3}..{high:F3} m");
            if (gait.SprintSpeed <= gait.UnarmedRunSpeed || gait.UnarmedRunSpeed <= gait.WalkSpeed) // the trees' thresholds must rise walk < jog < sprint
                throw new System.Exception($"gait measurement out of order: walk {gait.WalkSpeed:F2} jog {gait.UnarmedRunSpeed:F2} sprint {gait.SprintSpeed:F2}");
            if (gait.RunSpeed < 1f || gait.WalkSpeed < 0.3f || gait.RunSpeed <= gait.WalkSpeed || high - low < 0.03f)
                throw new System.Exception("gait measurement looks wrong; the locomotion clips may not carry root motion");
            return gait;
        }
        finally { Object.DestroyImmediate(go); }
    }

    // How fast the foot passes under the hips while it is down (mid-stance, where heel strike and push-off do not distort it), mean of both
    // feet: an in-place cycle's ground speed. Only its ratio between two clips is used.
    static float MidStanceSpeed(AnimationClip clip, CharacterDef def, GameObject go)
    {
        const int samples = 240;
        float dt = clip.length / samples, sum = 0f;
        int n = 0;
        foreach (var foot in new[] { def.Sockets.LeftFoot, def.Sockets.RightFoot })
        {
            var y = new float[samples + 1]; var z = new float[samples + 1];
            for (int i = 0; i <= samples; i++)
            {
                clip.SampleAnimation(def.Animator.gameObject, i * dt);
                var p = go.transform.InverseTransformPoint(foot.position);
                y[i] = p.y; z[i] = p.z - go.transform.InverseTransformPoint(def.Sockets.Hips.position).z;
            }
            float low = y.Min(), high = y.Max();
            for (int i = 1; i <= samples; i++)
                if ((z[i - 1] > 0f) != (z[i] > 0f) && y[i] < low + 0.35f * (high - low)) { sum += Mathf.Abs(z[i] - z[i - 1]) / dt; n++; }
        }
        return n > 0 ? sum / n : 1f;
    }

    // Ground speed of an in-place cycle: the speed at which a foot near its lowest point moves back along the model's z.
    static float PlantedFootSpeed(AnimationClip clip, CharacterDef def, GameObject go)
    {
        const int samples = 96;
        float dt = clip.length / samples;
        var feet = new[] { def.Sockets.LeftFoot, def.Sockets.RightFoot };
        var z = new float[2, samples + 1]; var y = new float[2, samples + 1];
        float low = float.MaxValue, high = float.MinValue;
        for (int i = 0; i <= samples; i++)
        {
            clip.SampleAnimation(def.Animator.gameObject, i * dt);
            for (int f = 0; f < 2; f++)
            {
                var p = go.transform.InverseTransformPoint(feet[f].position);
                z[f, i] = p.z; y[f, i] = p.y;
                low = Mathf.Min(low, p.y); high = Mathf.Max(high, p.y);
            }
        }
        float grounded = low + (high - low) * 0.15f, travelled = 0f, time = 0f;
        for (int f = 0; f < 2; f++)
            for (int i = 1; i <= samples; i++)
            {
                if (y[f, i] > grounded || y[f, i - 1] > grounded) continue;
                travelled += Mathf.Abs(z[f, i] - z[f, i - 1]);
                time += dt;
            }
        return time > 0f ? travelled / time : 0f;
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
}
