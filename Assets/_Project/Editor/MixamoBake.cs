using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Retargets the Mixamo rifle clips onto the Quaternius Swat skeleton offline and writes plain generic clips (ADR-166).
// Swat's feet are not children of the lower legs, so Unity's Humanoid retargeting cannot be used. Instead, every frame of
// the Mixamo clip is read in world space and the Swat bones are aimed the same way: chain bones point at the world
// direction of their Mixamo counterpart, end bones copy the world rotation change, the hips follow the scaled hip motion.
public static class MixamoBake
{
    const string MixamoDir = "Assets/_Project/Art/Characters/Mixamo";
    const string SkinPath = MixamoDir + "/Swat_RifleAimingIdle_skin.fbx";
    // Legacy tool (ADR-187): Swat.fbx now sits in placeholder/QuaterniusModularMen (#262); copy it back here before running.
    const string SwatPath = "Assets/_Project/Art/Characters/QuaterniusModularMen/Swat.fbx";
    public const string BakedDir = "Assets/_Project/Prefabs/M1_5/Baked";
    const float Fps = 30f;
    static readonly string[] Loops = { "RifleAimingIdle", "RifleIdle", "RifleRun", "RifleWalk", "RifleWalkBack", "RifleSideStep", "RifleCrouchWalk", "RifleCrouchWalkBack", "RifleKneelIdle" };

    // (Swat bone, Mixamo bone, aim at this Swat child, i.e. use direction retargeting) ; child null = copy rotation change.
    static readonly (string swat, string mix, string aimChild)[] Map =
    {
        ("Abdomen", "Spine", "Torso"), ("Torso", "Spine1", "Chest"), ("Chest", "Spine2", "Neck"), ("Neck", "Neck", "Head"), ("Head", "Head", null),
        ("Shoulder.L", "LeftShoulder", "UpperArm.L"), ("UpperArm.L", "LeftArm", "LowerArm.L"), ("LowerArm.L", "LeftForeArm", "Wrist.L"), ("Wrist.L", "LeftHand", null),
        ("Shoulder.R", "RightShoulder", "UpperArm.R"), ("UpperArm.R", "RightArm", "LowerArm.R"), ("LowerArm.R", "RightForeArm", "Wrist.R"), ("Wrist.R", "RightHand", null),
        ("UpperLeg.L", "LeftUpLeg", "LowerLeg.L"), ("LowerLeg.L", "LeftLeg", "Foot.L"), ("Foot.L", "LeftFoot", null),
        ("UpperLeg.R", "RightUpLeg", "LowerLeg.R"), ("LowerLeg.R", "RightLeg", "Foot.R"), ("Foot.R", "RightFoot", null),
    };
    static readonly string[] FingerSwat = { "Index", "Middle", "Ring", "Pinky", "Thumb" };

    static Vector3 BodyRest;

    static IEnumerable<(string swat, string mix, string aimChild)> AllPairs()
    {
        foreach (var m in Map) yield return m;
        foreach (var side in new[] { "L", "R" })
            foreach (var f in FingerSwat)
                for (int i = 1; i <= 3; i++)
                    yield return ($"{f}{i}.{side}", $"{(side == "L" ? "Left" : "Right")}Hand{f}{i}", null);
    }

    static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name || t.name == "mixamorig:" + name);

    class Pair { public Transform T, S, TChild, SChild; public Quaternion TRest, SRest; public Vector3 TRestPos, SRestPos; public float Len; public AnimationCurve[] Pos; public Vector3 TDirRest; public string Path; public bool Aim; }

    // tools/unity.sh run MixamoBake.Run
    public static void Run()
    {
        var tgtGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SwatPath));
        var srcGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SkinPath));
        var pairs = new List<Pair>();
        foreach (var (swat, mix, child) in AllPairs())
        {
            var t = Find(tgtGo.transform, swat); var s = Find(srcGo.transform, mix);
            if (t == null || s == null) { Debug.LogWarning($"[MixamoBake] skip {swat}/{mix}: {(t == null ? "swat" : "mixamo")} bone missing"); continue; }
            var p = new Pair { T = t, S = s, Path = AnimationUtility.CalculateTransformPath(t, tgtGo.transform), TRest = t.rotation, SRest = s.rotation, TRestPos = t.position, SRestPos = s.position, Aim = child != null };
            if (child != null)
            {
                p.TChild = Find(tgtGo.transform, child);
                var mixChild = Map.First(m => m.swat == child).mix;
                p.SChild = Find(srcGo.transform, mixChild);
                p.TDirRest = (p.TChild.position - t.position).normalized; p.Len = (p.TChild.position - t.position).magnitude;
            }
            pairs.Add(p);
        }
        var tHips = Find(tgtGo.transform, "Hips"); var sHips = Find(srcGo.transform, "Hips");
        BodyRest = tHips.parent.position;
        var tHipsRest = tHips.position; var tHipsLocalRest = tHips.localPosition; var tHipsRot = tHips.rotation; var sHipsRot = sHips.rotation; var sHipsRest = sHips.position;
        // Both rigs stand on y = 0 in their rest pose, so the hip height ratio is the leg length ratio.
        float tFoot = pairs.First(p => p.T.name == "Foot.L").T.position.y, sFoot = pairs.First(p => p.S.name.EndsWith("LeftFoot")).S.position.y;
        float ratio = (tHipsRest.y - tFoot) / Mathf.Max(0.0001f, sHipsRest.y - sFoot);
        Debug.Log($"[MixamoBake] pairs {pairs.Count}, hip ratio {ratio:F3}");

        Directory.CreateDirectory(BakedDir);
        foreach (var file in Directory.GetFiles(MixamoDir, "*.fbx").OrderBy(f => f))
        {
            var path = file.Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("Swat_")) continue;
            var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (source == null) { Debug.LogError($"[MixamoBake] {name}: no clip"); continue; }
            Bake(name, source, srcGo, tgtGo, pairs, tHips, sHips, tHipsRest, sHipsRest, tHipsRot, sHipsRot, tHipsLocalRest, ratio);
        }
        // The skin download carries the standing aim pose.
        var aim = AssetDatabase.LoadAllAssetsAtPath(SkinPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (aim != null) Bake("RifleAimingIdle", aim, srcGo, tgtGo, pairs, tHips, sHips, tHipsRest, sHipsRest, tHipsRot, sHipsRot, tHipsLocalRest, ratio);
        else Debug.LogError("[MixamoBake] no clip in skin file");
        Object.DestroyImmediate(tgtGo); Object.DestroyImmediate(srcGo);
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    static void Bake(string name, AnimationClip source, GameObject srcGo, GameObject tgtGo, List<Pair> pairs, Transform tHips, Transform sHips,
        Vector3 tHipsRest, Vector3 sHipsRest, Quaternion tHipsRot, Quaternion sHipsRot, Vector3 tHipsLocalRest, float ratio)
    {
        int frames = Mathf.Max(2, Mathf.RoundToInt(source.length * Fps));
        var rotCurves = new Dictionary<string, AnimationCurve[]>();
        foreach (var p in pairs) rotCurves[p.Path] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
        string hipsPath = AnimationUtility.CalculateTransformPath(tHips, tgtGo.transform);
        rotCurves[hipsPath] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
        var hipPos = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
        bool loop = Loops.Contains(name);
        source.SampleAnimation(srcGo, 0f); var start = sHips.position;
        source.SampleAnimation(srcGo, source.length); var drift = sHips.position - start;
        var tBody = tHips.parent; var tBodyRest = BodyRest;
        string bodyPath = AnimationUtility.CalculateTransformPath(tBody, tgtGo.transform);
        for (int i = 0; i <= frames; i++)
        {
            float time = Mathf.Min(source.length, i / Fps);
            source.SampleAnimation(srcGo, time);
            // Hips: rotation change and scaled translation, world space.
            tHips.rotation = sHips.rotation * Quaternion.Inverse(sHipsRot) * tHipsRot;
            // The legs hang under Body, not Hips, so Body carries the hip motion. Loops run in place: the straight-line travel is removed.
            var offset = sHips.position - sHipsRest;
            if (loop) { offset.x -= drift.x * time / source.length; offset.z -= drift.z * time / source.length; }
            tBody.position = tBodyRest + offset * ratio;
            // Parents before children: the list is ordered that way (spine, arms, legs, fingers).
            foreach (var p in pairs)
            {
                if (p.Aim)
                {
                    var want = (p.SChild.position - p.S.position).normalized;
                    p.T.rotation = Quaternion.FromToRotation(p.TDirRest, want) * p.TRest;
                }
                else p.T.rotation = p.S.rotation * Quaternion.Inverse(p.SRest) * p.TRest;
            }
            foreach (var p in pairs)
                if (p.T.name.StartsWith("Foot."))
                {
                    // The feet are not children of the legs in this rig: place them from the Mixamo foot motion.
                    var shin = pairs.First(q => q.T.name == "LowerLeg" + p.T.name.Substring(4));
                    p.T.position = shin.T.position + (shin.SChild.position - shin.S.position).normalized * shin.Len;
                    p.Pos ??= new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                    for (int c = 0; c < 3; c++) p.Pos[c].AddKey(time, p.T.localPosition[c]);
                }
            foreach (var p in pairs) Add(rotCurves[p.Path], time, p.T.localRotation);
            Add(rotCurves[hipsPath], time, tHips.localRotation);
            hipPos[0].AddKey(time, tBody.localPosition.x); hipPos[1].AddKey(time, tBody.localPosition.y); hipPos[2].AddKey(time, tBody.localPosition.z);
        }
        var clip = new AnimationClip { frameRate = Fps, name = name };
        foreach (var kv in rotCurves)
            for (int c = 0; c < 4; c++) clip.SetCurve(kv.Key, typeof(Transform), "localRotation." + "xyzw"[c], kv.Value[c]);
        for (int c = 0; c < 3; c++) clip.SetCurve(bodyPath, typeof(Transform), "localPosition." + "xyz"[c], hipPos[c]);
        foreach (var p in pairs.Where(p => p.Pos != null))
            for (int c = 0; c < 3; c++) { clip.SetCurve(p.Path, typeof(Transform), "localPosition." + "xyz"[c], p.Pos[c]); p.Pos[c] = new AnimationCurve(); }
        clip.EnsureQuaternionContinuity();
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = Loops.Contains(name);
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        var outPath = $"{BakedDir}/Mx_{name}.anim";
        AssetDatabase.DeleteAsset(outPath);
        AssetDatabase.CreateAsset(clip, outPath);
        Debug.Log($"[MixamoBake] {name}: {frames} frames, length {source.length:F2}s -> {outPath}");
        // Restore the rest pose for the next clip.
        source.SampleAnimation(srcGo, 0f);
    }

    static void Add(AnimationCurve[] curves, float time, Quaternion q)
    {
        curves[0].AddKey(time, q.x); curves[1].AddKey(time, q.y); curves[2].AddKey(time, q.z); curves[3].AddKey(time, q.w);
    }

    // tools/unity.sh run-gfx MixamoBake.RenderSheet -> Logs/lookdev/mx_<clip>.png (+_front): eight phases of each baked clip.
    public static void RenderSheet()
    {
        SceneLook.PrepareTextures();
        var profile = RenderSetup.Apply();
        CharacterKit.Prepare();
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        SceneLook.SetupSky(); SceneLook.AddLights(); SceneLook.AddPostVolume(profile);
        SceneLook.WorldBox("Floor", null, new Vector3(0, -0.05f, 0), new Vector3(40, 0.1f, 40), SceneLook.Floor, 2.5f);
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.fieldOfView = 30; SceneLook.EnablePost(cam);
        Directory.CreateDirectory("Logs/lookdev");
        const int phases = 8;
        var slots = new CharacterKit.Visual[phases];
        for (int i = 0; i < phases; i++)
        {
            var slot = new GameObject("Phase" + i).transform;
            slot.position = new Vector3(0, 0, i * 1.4f);
            slots[i] = CharacterKit.AddTo(slot);
            slots[i].WeaponModels[3].SetActive(true);
            slots[i].Animator.enabled = false;
        }
        foreach (var name in new[] { "RifleIdle", "RifleRun", "RifleWalk", "RifleCrouchWalk", "ProneFiringRifle", "RifleKneelIdle", "ReloadingRifleStand", "RifleDeath" })
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{BakedDir}/Mx_{name}.anim");
            for (int i = 0; i < phases; i++) clip.SampleAnimation(slots[i].Root.gameObject, clip.length * i / phases);
            Debug.Log($"[MixamoBake] sheet {name} len {clip.length:F2} head y {slots[7].Root.GetComponentsInChildren<Transform>().First(t => t.name == "Head").position.y:F2}");
            SceneLook.Shot(cam, $"Logs/lookdev/mx_{name}.png", new Vector3(14f, 1.2f, 4.9f), new Vector3(0, 0.9f, 4.9f));
            SceneLook.Shot(cam, $"Logs/lookdev/mx_{name}_front.png", new Vector3(-9f, 1.6f, 14f), new Vector3(0, 0.9f, 4.9f));
        }
        EditorApplication.Exit(0);
    }
}
