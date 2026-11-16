using System.IO;
using System.Linq;
using System.Text;
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
            foreach (var name in CharacterAnimClips.Names)
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
}
