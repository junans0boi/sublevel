using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Gameplay;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Animation track (ADR-188, ADR-230): builds Character.controller for every Humanoid roster model.
//   0 Base      locomotion blend (Move), Air (time parameter), Prone, Death                        whole body
//   1 Upper     Lowered / Aim / Shoot / punches                                              Humanoid upper-body mask
//   2 Action    reload clips per stance (speed from the weapon's own reload time)            Humanoid upper-body mask
//   3 Additive  breathing loop and the hit flinch (procedural muscle clips, added on top)    whole body, additive
// Looking up and down is not a layer: CombatantAnimation spreads the pitch over the spine in LateUpdate.
public static class CharacterControllerBuilder
{
    public const string ControllerPath = SceneLook.GeneratedDir + "/Character.controller";
    public const string MaskPath = SceneLook.GeneratedDir + "/CharacterUpperBody.mask";
    public const string ProceduralDir = SceneLook.GeneratedDir + "/Procedural";

    public static AnimatorController Build(CharacterKit.GaitProfile gait)
    {
        Directory.CreateDirectory(ProceduralDir);
        var mask = BuildUpperBodyMask();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.layers = new AnimatorControllerLayer[0];
        controller.parameters = new AnimatorControllerParameter[0];
        foreach (var leftover in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            if (leftover != null && leftover != controller) Object.DestroyImmediate(leftover, true);

        controller.AddParameter(CombatantAnimation.Move, AnimatorControllerParameterType.Float);
        controller.AddParameter(new AnimatorControllerParameter { name = CombatantAnimation.Pace, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        controller.AddParameter(CombatantAnimation.AirPhase, AnimatorControllerParameterType.Float);
        controller.AddParameter(new AnimatorControllerParameter { name = CombatantAnimation.ActionSpeed, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

        // 0 Base.
        controller.AddLayer("Base");
        var locomotion = controller.CreateBlendTreeInController(CombatantAnimation.Locomotion, out var tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = CombatantAnimation.Move;
        tree.useAutomaticThresholds = false;
        // Player report "the run looks stupid" (ADR-390): RifleRun is a crouched shuffle authored at ~3 m/s and RifleWalkBack a ~1 m/s
        // step, so at the game's 4.5-6 m/s the feet skated (back-pedal 2.8 m/s). Past the walk the legs are the UAL jog and sprint, whose
        // strides match the ground; the long gun is held by code over the base layer (ADR-083), so their arm swing never shows.
        // The walk is UAL's too: a Mixamo walk blended with a UAL jog crossed the legs (their cycles start on different feet).
        tree.AddChild(Clip("U_Jog_Fwd_Loop"), -gait.UnarmedRunSpeed);
        tree.AddChild(Clip("U_Walk_Loop"), -gait.UnarmedWalkSpeed);
        tree.children = tree.children.Select((c, i) => { if (i <= 1) c.timeScale = -1f; return c; }).ToArray(); // back-pedal = the walk and jog played backwards
        tree.AddChild(Clip("RifleIdle"), 0f);
        tree.AddChild(Clip("U_Walk_Loop"), gait.UnarmedWalkSpeed);
        tree.AddChild(Clip("U_Jog_Fwd_Loop"), gait.UnarmedRunSpeed);
        tree.AddChild(Clip("U_Sprint_Loop"), gait.SprintSpeed);
        locomotion.speedParameter = CombatantAnimation.Pace; // the legs alone play faster when the character outruns its clips
        locomotion.speedParameterActive = true;
        var baseMachine = controller.layers[0].stateMachine;
        baseMachine.defaultState = locomotion;
        // Bare hands (ADR-255): the UAL walk and jog. The armed tree above is all rifle poses; without a weapon the hands hang and swing.
        var unarmed = controller.CreateBlendTreeInController(CombatantAnimation.Unarmed, out var unarmedTree, 0);
        unarmedTree.blendType = BlendTreeType.Simple1D;
        unarmedTree.blendParameter = CombatantAnimation.Move;
        unarmedTree.useAutomaticThresholds = false;
        unarmedTree.AddChild(Clip("U_Jog_Fwd_Loop"), -gait.UnarmedRunSpeed);
        unarmedTree.AddChild(Clip("U_Walk_Loop"), -gait.UnarmedWalkSpeed);
        unarmedTree.children = unarmedTree.children.Select((c, i) => { if (i <= 1) c.timeScale = -1f; return c; }).ToArray(); // back-pedal = the walk and jog played backwards
        unarmedTree.AddChild(Clip("U_Idle_Loop"), 0f);
        unarmedTree.AddChild(Clip("U_Walk_Loop"), gait.UnarmedWalkSpeed);
        unarmedTree.AddChild(Clip("U_Jog_Fwd_Loop"), gait.UnarmedRunSpeed);
        unarmedTree.AddChild(Clip("U_Sprint_Loop"), gait.SprintSpeed); // sprint (6.3 m/s) no longer plays the jog sped up
        unarmed.speedParameter = CombatantAnimation.Pace;
        unarmed.speedParameterActive = true;
        // Bare-handed crouch (UAL): a real crouch at the capsule's height, so the code knee bend is not used (CombatantAnimation._clipCrouch).
        var crouch = controller.CreateBlendTreeInController(CombatantAnimation.UnarmedCrouch, out var crouchTree, 0);
        crouchTree.blendType = BlendTreeType.Simple1D;
        crouchTree.blendParameter = CombatantAnimation.Move;
        crouchTree.useAutomaticThresholds = false;
        crouchTree.AddChild(Clip("U_Crouch_Fwd_Loop"), -gait.UnarmedCrouchSpeed);
        crouchTree.children = crouchTree.children.Select((c, i) => { if (i == 0) c.timeScale = -1f; return c; }).ToArray();
        crouchTree.AddChild(Clip("U_Crouch_Idle_Loop"), 0f);
        crouchTree.AddChild(Clip("U_Crouch_Fwd_Loop"), gait.UnarmedCrouchSpeed);
        crouch.speedParameter = CombatantAnimation.Pace;
        crouch.speedParameterActive = true;
        // Swimming (#236, UAL Swim_Idle/Fwd, CC0 C-0004): the clips already lie flat, so the prone pitch and the code kick/stroke are off while it plays.
        var swim = controller.CreateBlendTreeInController(CombatantAnimation.SwimState, out var swimTree, 0);
        swimTree.blendType = BlendTreeType.Simple1D;
        swimTree.blendParameter = CombatantAnimation.Move;
        swimTree.useAutomaticThresholds = false;
        swimTree.AddChild(Clip("U_Swim_Idle_Loop"), 0f);
        swimTree.AddChild(Clip("U_Swim_Fwd_Loop"), CombatantAnimation.SwimClipSpeed);
        // No jump clip yet (gap sheet, ADR-187): the air state holds a still moment of the idle and CombatantAnimation poses the jump on top (ADR-048).
        var air = baseMachine.AddState(CombatantAnimation.Air);
        air.motion = Clip("RifleIdle");
        air.timeParameter = CombatantAnimation.AirPhase;
        air.timeParameterActive = true;
        baseMachine.AddState(CombatantAnimation.Death).motion = Clip("RifleDeath");
        // Lying (ADR-231): the Mixamo prone rifle pose; the crawl's roll and knees are bone turns on top of it.
        baseMachine.AddState(CombatantAnimation.Prone).motion = Clip("ProneFiringRifle");

        // 1 Upper: off by default; CombatantAnimation raises its weight while a weapon is held or a punch plays.
        AddMaskedLayer(controller, "Upper", mask, AnimatorLayerBlendingMode.Override);
        var upper = controller.layers[1].stateMachine;
        var lowered = upper.AddState(CombatantAnimation.Lowered);
        lowered.motion = Clip("RifleIdle");
        upper.defaultState = lowered;
        upper.AddState(CombatantAnimation.Aim).motion = Clip("Swat_RifleAimingIdle_skin");
        upper.AddState(CombatantAnimation.Shoot).motion = Clip("FiringRifleStand");
        // Punches (UAL Jab / Cross): CharacterKit.Prepare reads which hand each state ends up using and swaps or mirrors until the right state throws the right fist.
        var left = upper.AddState(CombatantAnimation.PunchLeft);
        var right = upper.AddState(CombatantAnimation.PunchRight);
        right.motion = Clip(gait.PunchSwap ? "U_Punch_Cross" : "U_Punch_Jab");
        left.motion = Clip(gait.PunchSwap ? "U_Punch_Jab" : "U_Punch_Cross");
        right.mirror = gait.MirrorRight;
        left.mirror = gait.MirrorLeft;

        // 2 Action: reload clips by stance. The empty default state lets the layer rest at weight 0.
        AddMaskedLayer(controller, "Action", mask, AnimatorLayerBlendingMode.Override);
        var action = controller.layers[2].stateMachine;
        var none = action.AddState(CombatantAnimation.ActionNone);
        action.defaultState = none;
        foreach (var (state, clip) in new[] { (CombatantAnimation.ReloadStand, "ReloadingRifleStand"), (CombatantAnimation.ReloadCrouch, "ReloadRifleCrouch"), (CombatantAnimation.ReloadProne, "ProneReloadingRifle") })
        {
            var s = action.AddState(state);
            s.motion = Clip(clip);
            s.speedParameter = CombatantAnimation.ActionSpeed;
            s.speedParameterActive = true;
        }

        // 3 Additive: breathing under everything, flinch on a hit.
        controller.AddLayer("Additive");
        var layers = controller.layers;
        layers[3].blendingMode = AnimatorLayerBlendingMode.Additive;
        layers[3].defaultWeight = 1f;
        controller.layers = layers;
        var additive = controller.layers[3].stateMachine;
        var breath = additive.AddState(CombatantAnimation.BreathState);
        breath.motion = ProceduralClips.Breath();
        additive.defaultState = breath;
        additive.AddState(CombatantAnimation.FlinchState).motion = Clip("U_Hit_Chest"); // an additive pose: frame 0 is the reference

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static void AddMaskedLayer(AnimatorController controller, string name, AvatarMask mask, AnimatorLayerBlendingMode mode)
    {
        controller.AddLayer(name);
        var layers = controller.layers;
        int i = layers.Length - 1;
        layers[i].avatarMask = mask;
        layers[i].blendingMode = mode;
        layers[i].defaultWeight = 0f;
        controller.layers = layers;
    }

    // Humanoid mask, not a transform list: it works on every roster model.
    static AvatarMask BuildUpperBodyMask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, MaskPath); }
        foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
        {
            if (part == AvatarMaskBodyPart.LastBodyPart) continue;
            bool on = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers;
            mask.SetHumanoidBodyPartActive(part, on);
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }

    public static AnimationClip Clip(string name)
    {
        var clip = CharacterAnimClips.LoadClip(name);
        if (clip == null) throw new FileNotFoundException("Humanoid clip missing (run CharacterAnimClips.Reimport): " + name);
        return clip;
    }
}

// Humanoid muscle-curve clips made in code: there is no breathing, flinch or punch in the Mixamo set (ledger: nothing generated is shipped as a model/texture,
// these are plain curves we author). Muscle values are the Humanoid range (-1..1); the sign of each was checked with CharacterAnimProbe.
public static class ProceduralClips
{
    static AnimationClip Make(string name, bool loop, float length, params (string muscle, (float t, float v)[] keys)[] curves)
    {
        var path = $"{CharacterControllerBuilder.ProceduralDir}/{name}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = 30f;
        foreach (var (muscle, keys) in curves)
        {
            var curve = new AnimationCurve(keys.Select(k => new Keyframe(k.t, k.v)).ToArray());
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), muscle), curve);
        }
        var s = AnimationUtility.GetAnimationClipSettings(clip);
        s.loopTime = loop; s.loopBlend = loop;
        AnimationUtility.SetAnimationClipSettings(clip, s);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // Chest rises and falls 0.03 of its range over 4 s.
    public static AnimationClip Breath() => Make("Breath", true, 4f,
        ("Chest Front-Back", new[] { (0f, 0f), (2f, 0.05f), (4f, 0f) }),
        ("Spine Front-Back", new[] { (0f, 0f), (2f, 0.02f), (4f, 0f) }));
}
