using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Gameplay;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Animation track (ADR-188, ADR-230): builds Character.controller for every Humanoid roster model.
//   0 Base      locomotion blend (Move), Air (time parameter), Death                         whole body
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
        tree.AddChild(Clip("RifleWalkBack"), -gait.BackSpeed);
        tree.AddChild(Clip("RifleIdle"), 0f);
        tree.AddChild(Clip("RifleWalk"), gait.WalkSpeed);
        tree.AddChild(Clip("RifleRun"), gait.RunSpeed);
        locomotion.speedParameter = CombatantAnimation.Pace; // the legs alone play faster when the character outruns its clips
        locomotion.speedParameterActive = true;
        var baseMachine = controller.layers[0].stateMachine;
        baseMachine.defaultState = locomotion;
        // No jump clip yet (gap sheet, ADR-187): the air state holds a still moment of the idle and CombatantAnimation poses the jump on top (ADR-048).
        var air = baseMachine.AddState(CombatantAnimation.Air);
        air.motion = Clip("RifleIdle");
        air.timeParameter = CombatantAnimation.AirPhase;
        air.timeParameterActive = true;
        baseMachine.AddState(CombatantAnimation.Death).motion = Clip("RifleDeath");

        // 1 Upper: off by default; CombatantAnimation raises its weight while a weapon is held or a punch plays.
        AddMaskedLayer(controller, "Upper", mask, AnimatorLayerBlendingMode.Override);
        var upper = controller.layers[1].stateMachine;
        var lowered = upper.AddState(CombatantAnimation.Lowered);
        lowered.motion = Clip("RifleIdle");
        upper.defaultState = lowered;
        upper.AddState(CombatantAnimation.Aim).motion = Clip("Swat_RifleAimingIdle_skin");
        upper.AddState(CombatantAnimation.Shoot).motion = Clip("FiringRifleStand");
        upper.AddState(CombatantAnimation.PunchLeft).motion = ProceduralClips.Punch(false);
        upper.AddState(CombatantAnimation.PunchRight).motion = ProceduralClips.Punch(true);

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
        additive.AddState(CombatantAnimation.FlinchState).motion = ProceduralClips.Flinch();

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

    // Chest and head snap back and settle in 0.3 s.
    public static AnimationClip Flinch() => Make("Flinch", false, 0.3f,
        ("Chest Front-Back", new[] { (0f, 0f), (0.06f, 0.35f), (0.3f, 0f) }),
        ("Spine Front-Back", new[] { (0f, 0f), (0.06f, 0.25f), (0.3f, 0f) }),
        ("Head Nod Down-Up", new[] { (0f, 0f), (0.06f, 0.3f), (0.3f, 0f) }));

    // Placeholder jab until a punch clip is chosen from the gap sheet (ADR-187): the arm's five Humanoid muscles are searched so the fist
    // goes from a guard (in front of the chest) to full reach and back, all other muscles stay at the rifle-idle pose. Deterministic.
    public static AnimationClip Punch(bool right)
    {
        string side = right ? "Right" : "Left";
        string[] arm = { "Arm Down-Up", "Arm Front-Back", "Arm Twist In-Out", "Forearm Stretch", "Forearm Twist In-Out", "Shoulder Down-Up", "Shoulder Front-Back" };
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterRoster.PrefabDir}/Roster_{CharacterKit.DefaultRoster}.prefab"));
        try
        {
            var def = go.GetComponent<CharacterDef>();
            var handler = new HumanPoseHandler(def.Animator.avatar, def.Animator.transform);
            var pose = new HumanPose();
            CharacterControllerBuilder.Clip("RifleIdle").SampleAnimation(def.Animator.gameObject, 0f);
            handler.GetHumanPose(ref pose);
            var baseline = (float[])pose.muscles.Clone();
            var shoulder = right ? def.Sockets.RightUpperArm : def.Sockets.LeftUpperArm;
            var hand = right ? def.Sockets.RightHand : def.Sockets.LeftHand;
            var idx = arm.Select(a => System.Array.IndexOf(HumanTrait.MuscleName, $"{side} {a}")).ToArray();
            handler.SetHumanPose(ref pose);
            Vector3 Local(Transform t) => go.transform.InverseTransformPoint(t.position);
            float[] Solve(Vector3 offsetFromShoulder, float[] start)
            {
                var target = Local(shoulder) + offsetFromShoulder;
                var best = (float[])start.Clone();
                float Cost(float[] v)
                {
                    for (int k = 0; k < idx.Length; k++) pose.muscles[idx[k]] = v[k];
                    handler.SetHumanPose(ref pose);
                    return (Local(hand) - target).sqrMagnitude;
                }
                pose.muscles = (float[])baseline.Clone();
                float bestCost = Cost(best);
                var rng = new System.Random(5);
                for (float step = 0.5f; step > 0.01f; step *= 0.8f)
                    for (int n = 0; n < 120; n++)
                    {
                        var c = (float[])best.Clone();
                        for (int k = 0; k < c.Length; k++) c[k] = Mathf.Clamp(c[k] + ((float)rng.NextDouble() * 2f - 1f) * step, -1f, 1f);
                        float cc = Cost(c);
                        if (cc < bestCost) { bestCost = cc; best = c; }
                    }
                return best;
            }
            var rest = idx.Select(i => baseline[i]).ToArray();
            var guard = Solve(new Vector3(right ? -0.05f : 0.05f, -0.10f, 0.30f), rest);
            var reach = Solve(new Vector3(0f, -0.03f, 0.58f), guard);
            var curves = new System.Collections.Generic.List<(string, (float, float)[])>();
            for (int k = 0; k < idx.Length; k++)
                curves.Add(($"{side} {arm[k]}", new[] { (0f, guard[k]), (0.1f, reach[k]), (0.18f, reach[k]), (0.4f, guard[k]) }));
            // The rest of the body holds the rifle-idle pose (absolute values, this is an override layer).
            for (int m = 0; m < baseline.Length; m++)
            {
                var name = HumanTrait.MuscleName[m];
                if (name.StartsWith("Left Hand") || name.StartsWith("Right Hand") || name.Contains("Finger") || name.Contains("Thumb")) continue;
                if (name.StartsWith($"{side} Arm") || name.StartsWith($"{side} Forearm") || name.StartsWith($"{side} Shoulder")) continue;
                if (name.Contains("Leg") || name.Contains("Foot") || name.Contains("Toes")) continue;
                curves.Add((name, new[] { (0f, baseline[m]), (0.4f, baseline[m]) }));
            }
            return Make(right ? "PunchRight" : "PunchLeft", false, 0.4f, curves.ToArray());
        }
        finally { Object.DestroyImmediate(go); }
    }
}
