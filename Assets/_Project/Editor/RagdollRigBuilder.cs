using System.Collections.Generic;
using Game.Gameplay;
using UnityEngine;

// ADR-395 (#196): bakes the ragdoll of a Humanoid roster model (11 parts: pelvis, chest, head, upper/lower arms and legs)
// into a CombatantRagdoll on the character's visual root. Runs inside CharacterKit.AddTo, so every combatant prefab
// TestBoxBuilder.Build makes (players, bots, dummy, all 15 roster models) gets one. Must run in the bind (T) pose,
// before any clip is sampled: the joint limits count from that pose.
// Limits follow Unity's ragdoll wizard, but the arm and elbow axes are mirrored per side (the wizard's are not, so one
// elbow bends backwards). CharacterJoint angles are the opposite sign of Unity's transform rotation about the same
// axis, so for each joint the axis is chosen so that the natural bend is a positive transform turn and the limit
// range is then negative (e.g. the knee: -120..0).
public static class RagdollRigBuilder
{
    public const int Parts = 11;

    public static CombatantRagdoll Add(GameObject root, CharacterDef def)
    {
        var a = def.Animator;
        Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
        float k = def.Height / 1.8f;
        var hips = B(HumanBodyBones.Hips);
        var spine = B(HumanBodyBones.Spine);
        var chest = B(HumanBodyBones.Chest) ?? spine;
        var neck = B(HumanBodyBones.Neck);
        var head = B(HumanBodyBones.Head);
        var lua = B(HumanBodyBones.LeftUpperArm); var lla = B(HumanBodyBones.LeftLowerArm); var lh = B(HumanBodyBones.LeftHand);
        var rua = B(HumanBodyBones.RightUpperArm); var rla = B(HumanBodyBones.RightLowerArm); var rh = B(HumanBodyBones.RightHand);
        var lul = B(HumanBodyBones.LeftUpperLeg); var lll = B(HumanBodyBones.LeftLowerLeg); var lf = B(HumanBodyBones.LeftFoot);
        var rul = B(HumanBodyBones.RightUpperLeg); var rll = B(HumanBodyBones.RightLowerLeg); var rf = B(HumanBodyBones.RightFoot);

        var up = Vector3.up;
        var right = rua.position - lua.position; right.y = 0f; right.Normalize();
        var fwd = Vector3.Cross(right, up);

        var parts = new List<RagdollPart>(Parts);
        // Pelvis: a box over the hips and the tops of the thighs.
        float hipWidth = Vector3.Distance(lul.position, rul.position);
        var pelvisCentre = (hips.position + (lul.position + rul.position) * 0.5f) * 0.5f + up * 0.04f * k;
        parts.Add(BoxPart(hips, -1, pelvisCentre, new Vector3(hipWidth + 0.16f * k, 0.24f * k, 0.22f * k), 11f, default, default, 0, 0, 0, 0));
        // Chest: a box from the lower spine to the neck.
        float shoulders = Vector3.Distance(lua.position, rua.position);
        var top = neck != null ? neck.position : head.position;
        float chestHeight = Vector3.Distance(spine.position, top) + 0.06f * k;
        parts.Add(BoxPart(chest, 0, (spine.position + top) * 0.5f, new Vector3(shoulders * 0.8f, chestHeight, 0.22f * k), 18f, right, fwd, -30f, 30f, 15f, 15f));
        // Head: a ball over the skull (the head bone sits at its base).
        parts.Add(SpherePart(head, 1, head.position + up * 0.08f * k, 0.11f * k, 5f, right, fwd, -40f, 25f, 25f, 25f));
        // Arms: bend forward is a positive turn about cross(arm, forward); swing about forward raises and lowers.
        AddLimb(parts, lua, lla, 1, 0.055f * k, 2.5f, 0f, Vector3.Cross(Dir(lua, lla), fwd), fwd, -80f, 30f, 60f, 20f);
        AddLimb(parts, lla, lh, 3, 0.045f * k, 2f, 0.08f * k, Vector3.Cross(Dir(lla, lh), fwd), fwd, -130f, 0f, 0f, 0f);
        AddLimb(parts, rua, rla, 1, 0.055f * k, 2.5f, 0f, Vector3.Cross(Dir(rua, rla), fwd), fwd, -80f, 30f, 60f, 20f);
        AddLimb(parts, rla, rh, 5, 0.045f * k, 2f, 0.08f * k, Vector3.Cross(Dir(rla, rh), fwd), fwd, -130f, 0f, 0f, 0f);
        // Legs: the thigh swings forward up to 80 (positive joint angle about right), the knee bends back (-120..0).
        AddLimb(parts, lul, lll, 0, 0.08f * k, 9f, 0f, right, fwd, -20f, 80f, 30f, 10f);
        AddLimb(parts, lll, lf, 7, 0.06f * k, 5f, 0.05f * k, Vector3.Cross(Dir(lll, lf), -fwd), fwd, -120f, 0f, 0f, 0f);
        AddLimb(parts, rul, rll, 0, 0.08f * k, 9f, 0f, right, fwd, -20f, 80f, 30f, 10f);
        AddLimb(parts, rll, rf, 9, 0.06f * k, 5f, 0.05f * k, Vector3.Cross(Dir(rll, rf), -fwd), fwd, -120f, 0f, 0f, 0f);

        // Every bone from a part up to the hips keeps its bind pose for the joints.
        var rest = new List<RagdollRestBone>();
        var seen = new HashSet<Transform>();
        foreach (var p in parts)
            for (var t = p.Bone; t != null && seen.Add(t); t = t == hips ? null : t.parent)
                rest.Add(new RagdollRestBone { Bone = t, Position = t.localPosition, Rotation = t.localRotation });

        var ragdoll = root.AddComponent<CombatantRagdoll>();
        ragdoll.Bake(parts.ToArray(), rest.ToArray(), a);
        float armDrop = Vector3.Angle(Dir(lua, lla), -right);
        Debug.Log($"[Ragdoll] {def.Id}: {parts.Count} parts, {rest.Count} rest bones, arm {armDrop:F0} deg below level (T-pose ~0)");
        return ragdoll;
    }

    static Vector3 Dir(Transform from, Transform to) => (to.position - from.position).normalized;

    static void AddLimb(List<RagdollPart> parts, Transform bone, Transform end, int parent, float radius, float mass, float extend,
        Vector3 axis, Vector3 swing, float low, float high, float swing1, float swing2)
    {
        var d = Dir(bone, end);
        var tip = end.position + d * extend;
        float length = Vector3.Distance(bone.position, tip);
        var local = bone.InverseTransformDirection(d);
        int dirAxis = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (Mathf.Abs(local.x) > Mathf.Abs(local.z) ? 0 : 2) : (Mathf.Abs(local.y) > Mathf.Abs(local.z) ? 1 : 2);
        float scale = Mathf.Abs(bone.lossyScale[dirAxis]);
        parts.Add(new RagdollPart
        {
            Bone = bone, Parent = parent, Shape = RagdollPart.Capsule, Mass = mass,
            Center = bone.InverseTransformPoint((bone.position + tip) * 0.5f),
            Size = new Vector3(radius / scale, (length + radius) / scale, dirAxis),
            Axis = bone.InverseTransformDirection(axis.normalized), SwingAxis = bone.InverseTransformDirection(swing.normalized),
            LowTwist = low, HighTwist = high, Swing1 = swing1, Swing2 = swing2,
        });
    }

    static RagdollPart BoxPart(Transform bone, int parent, Vector3 centre, Vector3 worldSize, float mass, Vector3 axis, Vector3 swing,
        float low, float high, float swing1, float swing2)
    {
        var size = Vector3.zero;
        var s = bone.lossyScale;
        for (int i = 0; i < 3; i++)
        {
            var e = Vector3.zero; e[i] = 1f;
            var w = bone.TransformDirection(e);
            size[i] = (Mathf.Abs(w.x) * worldSize.x + Mathf.Abs(w.y) * worldSize.y + Mathf.Abs(w.z) * worldSize.z) / Mathf.Abs(s[i]);
        }
        return new RagdollPart
        {
            Bone = bone, Parent = parent, Shape = RagdollPart.Box, Mass = mass, Center = bone.InverseTransformPoint(centre), Size = size,
            Axis = parent < 0 ? Vector3.right : bone.InverseTransformDirection(axis), SwingAxis = parent < 0 ? Vector3.forward : bone.InverseTransformDirection(swing),
            LowTwist = low, HighTwist = high, Swing1 = swing1, Swing2 = swing2,
        };
    }

    static RagdollPart SpherePart(Transform bone, int parent, Vector3 centre, float radius, float mass, Vector3 axis, Vector3 swing,
        float low, float high, float swing1, float swing2) => new RagdollPart
    {
        Bone = bone, Parent = parent, Shape = RagdollPart.Sphere, Mass = mass, Center = bone.InverseTransformPoint(centre),
        Size = new Vector3(radius / Mathf.Abs(bone.lossyScale.x), 0f, 0f),
        Axis = bone.InverseTransformDirection(axis), SwingAxis = bone.InverseTransformDirection(swing),
        LowTwist = low, HighTwist = high, Swing1 = swing1, Swing2 = swing2,
    };
}
