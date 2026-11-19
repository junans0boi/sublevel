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

        // Local ground velocity (m/s, x right, z forward) for the 8-way trees (ADR-391).
        controller.AddParameter(CombatantAnimation.MoveX, AnimatorControllerParameterType.Float);
        controller.AddParameter(CombatantAnimation.MoveZ, AnimatorControllerParameterType.Float);

        // 0 Base. Locomotion (long gun at the hip), LocomotionAim (aiming) and Unarmed are 2D freeform-directional trees on the local
        // velocity: each clip sits at its own ground velocity, so the blend matches the ground in every direction (ADR-391, #302). Forward
        // and back are the UAL walk/jog/sprint (ADR-390); sideways and diagonal are the Mixamo strafe set (C-0011), whose upper body faces
        // ahead while the legs step across.
        controller.AddLayer("Base");
        AnimatorState Tree2D(string name, (string clip, Vector2 at, bool reverse, bool mirror)[] children)
        {
            var state = controller.CreateBlendTreeInController(name, out var t, 0);
            t.blendType = BlendTreeType.FreeformDirectional2D;
            t.blendParameter = CombatantAnimation.MoveX;
            t.blendParameterY = CombatantAnimation.MoveZ;
            foreach (var c in children) t.AddChild(Clip(c.clip), c.at);
            t.children = t.children.Select((c, k) => { if (children[k].reverse) c.timeScale = -1f; c.mirror = children[k].mirror; return c; }).ToArray();
            state.speedParameter = CombatantAnimation.Pace; // the legs alone play faster when the character outruns its clips
            state.speedParameterActive = true;
            return state;
        }
        var g = gait;
        (string, Vector2, bool, bool) C(string clip, Vector2 at, bool reverse = false, bool mirror = false) => (clip, at, reverse, mirror);
        var forwardBack = new[]
        {
            C("U_Walk_Loop", new Vector2(0f, g.UnarmedWalkSpeed)), C("U_Jog_Fwd_Loop", new Vector2(0f, g.UnarmedRunSpeed)), C("U_Sprint_Loop", new Vector2(0f, g.SprintSpeed)),
            C("U_Walk_Loop", new Vector2(0f, -g.UnarmedWalkSpeed), true), C("U_Jog_Fwd_Loop", new Vector2(0f, -g.UnarmedRunSpeed), true),
        };
        // The Mixamo backward diagonals shuffle low and slid 2.4-2.8 m/s: backing off at an angle is the back-pedal with the legs turned.
        string[] rifleRuns = { "Rifle_RunRight", "Rifle_RunLeft", "Rifle_RunForwardRight", "Rifle_RunForwardLeft" };
        var locomotion = Tree2D(CombatantAnimation.Locomotion, new[] { C("RifleIdle", Vector2.zero) }.Concat(forwardBack)
            .Concat(new[] { C("Rifle_WalkStrafeRight", g.Strafe["Rifle_WalkStrafeRight"]), C("Rifle_WalkStrafeLeft", g.Strafe["Rifle_WalkStrafeLeft"]) })
            .Concat(rifleRuns.Select(n => C(n, g.Strafe[n]))).ToArray());
        var baseMachine = controller.layers[0].stateMachine;
        baseMachine.defaultState = locomotion;
        // Aiming: the aimed sidesteps instead of the running ones; the gun is shouldered by code as before.
        Tree2D(CombatantAnimation.LocomotionAim, new[] { C("RifleIdle", Vector2.zero) }.Concat(forwardBack)
            .Concat(new[] { "Rifle_AimStrafeRight", "Rifle_RunForwardRight", "Rifle_RunForwardLeft" }.Select(n => C(n, g.Strafe[n])))
            // The aimed sidestep to the left drags its feet (slid ~3 m/s at any pace): the right one mirrored instead.
            .Concat(new[] { C("Rifle_AimStrafeRight", g.AimLeftMirrored, mirror: true) }).ToArray());
        // Bare hands (ADR-255): the UAL walk/jog/sprint ahead and behind, the Mixamo bare-handed strafes to the sides (no diagonal clip: the
        // freeform blend mixes the neighbours).
        Tree2D(CombatantAnimation.Unarmed, new[] { C("U_Idle_Loop", Vector2.zero) }.Concat(forwardBack)
            .Concat(new[] { "Unarmed_WalkStrafeRight", "Unarmed_WalkStrafeLeft", "Unarmed_RunStrafeRight", "Unarmed_RunStrafeLeft" }.Select(n => C(n, g.Strafe[n]))).ToArray());
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
