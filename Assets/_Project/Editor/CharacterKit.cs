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
    const string WeaponDir = "Assets/_Project/Art/Weapons/QuaterniusUltimateGuns/";

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
        // Sentinel: no sniper model in the pack, so the assault rifle scaled long stands in (placeholder shape).
        new WeaponModel { Id = 5, File = "AssaultRifle_1.fbx", Length = 1.05f, MuzzleAlongMinusX = true, Forward = 0.30f, Up = 0.03f },   // Sentinel
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
        // Server-side characters must keep animating (and moving their hand-held weapon) while off screen (ADR-233 decides the final mode).
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
        var size = SceneLook.WorldBounds(go).size;
        go.transform.rotation = Quaternion.Euler(0, weapon.MuzzleAlongMinusX ? 90 : -90, 0) * go.transform.rotation;
        go.transform.localScale *= weapon.Length / Mathf.Max(size.x, size.z);
        return go;
    }
}
