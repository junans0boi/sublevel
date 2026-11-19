using System.IO;
using System.Linq;
using System.Text;
using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEngine;

// Animation track: joint heights/angles of every Humanoid clip on a roster model, as CSV (ADR-191: numbers, not stills).
// tools/unity.sh run CharacterAnimProbe.Run [-> Logs/anim_probe.csv]
public static class CharacterAnimProbe
{
    public static void Run()
    {
        var sb = new StringBuilder("model,clip,t,hipsY,chestY,headY,footLY,footRY,hipsX,hipsZ,handLtoHandR,rHandFwd,lHandFwd,trunkPitchDeg,footLZ,footRZ\n");
        foreach (var id in new[] { "MixamoSwat", "UbcMale", "MixamoBrian" })
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterRoster.PrefabDir}/Roster_{id}.prefab"));
            var def = go.GetComponent<CharacterDef>();
            var s = def.Sockets;
            foreach (var name in CharacterAnimClips.Names.Concat(CharacterAnimClips.UalNames.Select(n => "U_" + n)))
            {
                var clip = CharacterAnimClips.LoadClip(name);
                if (clip == null) continue;
                for (int i = 0; i <= 4; i++)
                {
                    float t = clip.length * i / 4f;
                    clip.SampleAnimation(def.Animator.gameObject, t);
                    Vector3 L(Transform x) => go.transform.InverseTransformPoint(x.position);
                    var h = L(s.Hips); var c = L(s.Chest); var hd = L(s.Head);
                    var trunk = (c - h); float pitch = Mathf.Atan2(trunk.z, trunk.y) * Mathf.Rad2Deg;
                    sb.AppendLine(string.Join(",", id, name, t.ToString("F2"), h.y.ToString("F3"), c.y.ToString("F3"), hd.y.ToString("F3"),
                        L(s.LeftFoot).y.ToString("F3"), L(s.RightFoot).y.ToString("F3"), h.x.ToString("F3"), h.z.ToString("F3"),
                        Vector3.Distance(L(s.LeftHand), L(s.RightHand)).ToString("F3"), L(s.RightHand).z.ToString("F3"), L(s.LeftHand).z.ToString("F3"), pitch.ToString("F1"),
                        L(s.LeftFoot).z.ToString("F3"), L(s.RightFoot).z.ToString("F3")));
                }
            }
            Object.DestroyImmediate(go);
        }
        File.WriteAllText("Logs/anim_probe.csv", sb.ToString());
        EditorApplication.Exit(0);
    }

    // Foot paths of the locomotion clips (120 samples a cycle, model space): tools/unity.sh run CharacterAnimProbe.Feet -> Logs/anim_feet.csv
    public static void Feet()
    {
        var sb = new StringBuilder("clip,len,t,hipsZ,footLY,footLZ,footRY,footRZ,toeLY,toeLZ,toeRY,toeRZ\n");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterRoster.PrefabDir}/Roster_MixamoSwat.prefab"));
        var s = go.GetComponent<CharacterDef>().Sockets;
        var anim = go.GetComponent<CharacterDef>().Animator;
        Transform toeL = anim.GetBoneTransform(HumanBodyBones.LeftToes), toeR = anim.GetBoneTransform(HumanBodyBones.RightToes);
        foreach (var name in new[] { "RifleWalk", "RifleRun", "RifleWalkBack", "U_Walk_Loop", "U_Jog_Fwd_Loop", "U_Sprint_Loop" })
        {
            var clip = CharacterAnimClips.LoadClip(name);
            for (int i = 0; i <= 120; i++)
            {
                float t = clip.length * i / 120f;
                clip.SampleAnimation(go.GetComponent<CharacterDef>().Animator.gameObject, t);
                Vector3 L(Transform x) => go.transform.InverseTransformPoint(x.position);
                sb.AppendLine(string.Join(",", name, clip.length.ToString("F3"), t.ToString("F3"), L(s.Hips).z.ToString("F3"), L(s.LeftFoot).y.ToString("F3"), L(s.LeftFoot).z.ToString("F3"), L(s.RightFoot).y.ToString("F3"), L(s.RightFoot).z.ToString("F3"), L(toeL).y.ToString("F3"), L(toeL).z.ToString("F3"), L(toeR).y.ToString("F3"), L(toeR).z.ToString("F3")));
            }
        }
        Object.DestroyImmediate(go);
        File.WriteAllText("Logs/anim_feet.csv", sb.ToString());
        EditorApplication.Exit(0);
    }

    // Which way each arm / chest muscle moves the right hand and chest: tools/unity.sh run CharacterAnimProbe.Muscles -> Logs/anim_muscles.txt
    public static void Muscles()
    {
        var sb = new StringBuilder();
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterRoster.PrefabDir}/Roster_MixamoSwat.prefab"));
        var def = go.GetComponent<CharacterDef>();
        var handler = new HumanPoseHandler(def.Animator.avatar, def.Animator.transform);
        var pose = new HumanPose();
        // Start from the aim clip's pose so the baseline is a rifle position.
        CharacterAnimClips.LoadClip("RifleIdle").SampleAnimation(def.Animator.gameObject, 0f);
        handler.GetHumanPose(ref pose);
        var baseline = (float[])pose.muscles.Clone();
        Vector3 L(Transform t) => go.transform.InverseTransformPoint(t.position);
        handler.SetHumanPose(ref pose);
        var h0 = L(def.Sockets.RightHand); var c0 = L(def.Sockets.Head);
        sb.AppendLine($"baseline right hand {h0.ToString("F3")} head {c0.ToString("F3")}");
        foreach (var name in new[] { "Right Arm Down-Up", "Right Arm Front-Back", "Right Arm Twist In-Out", "Right Forearm Stretch", "Right Forearm Twist In-Out", "Right Shoulder Down-Up", "Right Shoulder Front-Back",
                                     "Chest Front-Back", "Spine Front-Back", "Head Nod Down-Up" })
        {
            int idx = System.Array.IndexOf(HumanTrait.MuscleName, name);
            foreach (float v in new[] { -1f, 1f })
            {
                pose.muscles = (float[])baseline.Clone();
                pose.muscles[idx] = v;
                handler.SetHumanPose(ref pose);
                sb.AppendLine($"{name} = {v:+0;-0}: right hand {(L(def.Sockets.RightHand) - h0).ToString("F3")} head {(L(def.Sockets.Head) - c0).ToString("F3")} baseline value {baseline[idx]:F2}");
            }
        }
        File.WriteAllText("Logs/anim_muscles.txt", sb.ToString());
        Object.DestroyImmediate(go);
        EditorApplication.Exit(0);
    }

    // Lowest point of each roster model's body mesh (baked in the saved pose) and its ankle heights, relative to the prefab's origin: tools/unity.sh run CharacterAnimProbe.Soles
    public static void Soles()
    {
        var sb = new StringBuilder("model,meshMinY,ankleL,ankleR,modelLocalY,modelScale\n");
        foreach (var id in PlayerAppearance.ModelIds)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterRoster.PrefabDir}/Roster_{id}.prefab"));
            var def = go.GetComponent<CharacterDef>();
            float min = float.MaxValue;
            foreach (SkinnedMeshRenderer r in def.Renderers)
            {
                if (r.name == "Eyes" || r.name == "Eyebrows") continue;
                var mesh = new Mesh(); r.BakeMesh(mesh);
                var m = r.transform.localToWorldMatrix;
                foreach (var v in mesh.vertices) min = Mathf.Min(min, go.transform.InverseTransformPoint(m.MultiplyPoint3x4(v)).y);
                Object.DestroyImmediate(mesh);
            }
            sb.AppendLine($"{id},{min:F3},{go.transform.InverseTransformPoint(def.Sockets.LeftFoot.position).y:F3},{go.transform.InverseTransformPoint(def.Sockets.RightFoot.position).y:F3},{def.Animator.transform.localPosition.y:F3},{def.Animator.transform.localScale.x:F3}");
            Object.DestroyImmediate(go);
        }
        File.WriteAllText("Logs/anim_soles.csv", sb.ToString());
        EditorApplication.Exit(0);
    }

    // The saved pose of the built prefabs: tools/unity.sh run CharacterAnimProbe.PrefabPose -> Logs/anim_prefabpose.txt
    public static void PrefabPose()
    {
        var sb = new StringBuilder();
        foreach (var name in new[] { "Player", "Player_MixamoSwat", "Bot" })
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/M1/{name}.prefab"));
            var def = go.GetComponentInChildren<CharacterDef>(true);
            var a = def.Animator;
            sb.AppendLine($"{name}: model {def.Id}, ankles {go.transform.InverseTransformPoint(a.GetBoneTransform(HumanBodyBones.LeftFoot).position).y:F3}/{go.transform.InverseTransformPoint(a.GetBoneTransform(HumanBodyBones.RightFoot).position).y:F3}, hips {go.transform.InverseTransformPoint(a.GetBoneTransform(HumanBodyBones.Hips).position).y:F3}, head {go.transform.InverseTransformPoint(a.GetBoneTransform(HumanBodyBones.Head).position).y:F3}, animator pos {a.transform.localPosition}, controller {(a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "none")}");
            Object.DestroyImmediate(go);
        }
        File.WriteAllText("Logs/anim_prefabpose.txt", sb.ToString());
        EditorApplication.Exit(0);
    }

    // Scales, facing and saved pose of every Player_<model> prefab: tools/unity.sh run CharacterAnimProbe.PlayerPrefabs -> Logs/anim_playerprefabs.csv
    public static void PlayerPrefabs()
    {
        var sb = new StringBuilder("prefab,modelId,wrapperScale,modelScale,modelEulerY,hipsScale,spineScale,chestScale,spineLossy,hipsForwardDotRoot,chestForwardDotRoot,headFwdDotRoot,rightHandDotRootRight,ankleY,hipsY\n");
        foreach (var name in new[] { "Player" }.Concat(PlayerAppearance.ModelIds.Select(i => "Player_" + i)))
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/M1/{name}.prefab"));
            var def = go.GetComponentInChildren<CharacterDef>(true);
            var a = def.Animator; var root = go.transform;
            Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
            float Dot(Transform t, Vector3 axis) => Vector3.Dot(t.TransformDirection(axis), root.forward);
            // The face direction of the body: hips->head is up, so use the line between the hands' shoulders (right is +x of the root).
            var across = (B(HumanBodyBones.RightUpperArm).position - B(HumanBodyBones.LeftUpperArm).position).normalized;
            sb.AppendLine(string.Join(",", name, def.Id, def.transform.localScale.x.ToString("F3"), a.transform.localScale.x.ToString("F3"), a.transform.localEulerAngles.y.ToString("F0"),
                B(HumanBodyBones.Hips).localScale.x.ToString("F3"), B(HumanBodyBones.Spine).localScale.x.ToString("F3"), B(HumanBodyBones.Chest).localScale.x.ToString("F3"),
                B(HumanBodyBones.Spine).lossyScale.x.ToString("F3"),
                Vector3.Dot(Vector3.Cross(across, Vector3.up), root.forward).ToString("F2"), "-", "-",
                Vector3.Dot(across, root.right).ToString("F2"),
                root.InverseTransformPoint(B(HumanBodyBones.LeftFoot).position).y.ToString("F3"), root.InverseTransformPoint(B(HumanBodyBones.Hips).position).y.ToString("F3")));
            Object.DestroyImmediate(go);
        }
        File.WriteAllText("Logs/anim_playerprefabs.csv", sb.ToString());
        EditorApplication.Exit(0);
    }
}
