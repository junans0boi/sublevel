using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// S3 roster builder (character track): Humanoid FBX models (CharacterImport) -> normalised prefabs with CharacterDef,
// sockets, tint variants and a LODGroup used only for the 100 m cull (shape LOD is Unity's built-in Mesh LOD, ADR-197).
// tools/unity.sh run CharacterRoster.Build
public static class CharacterRoster
{
    public const string PrefabDir = "Assets/_Project/Prefabs/Roster";
    const string MatDir = PrefabDir + "/Materials";
    public const float Height = 1.8f, EyeHeight = 1.6f, CullDistance = 100f;
    const string MixDir = CharacterImport.RosterDir + "/", UbcDir = CharacterImport.UbcRosterDir + "/";

    public struct Entry { public string Id, Fbx; public bool Mixamo; public float LodBias; }
    public static readonly Entry[] Entries =
    {
        new Entry { Id = "MixamoSwat", Fbx = MixDir + "Roster_Swat.fbx", Mixamo = true, LodBias = 1f }, // LOD0 is 19.4k tris (> 15k cap): start one LOD lower,
        new Entry { Id = "UbcMale", Fbx = UbcDir + "Roster_Superhero_Male.fbx" },
        new Entry { Id = "UbcFemale", Fbx = UbcDir + "Roster_Superhero_Female.fbx" },
    };

    // Eight clothing tints (target average colour of the garment). Team colour is NOT carried here (armband, ADR-192).
    public static readonly (string name, Color c)[] Tints =
    {
        ("Olive", new Color(0.30f, 0.34f, 0.22f)), ("Coyote", new Color(0.46f, 0.38f, 0.26f)),
        ("Charcoal", new Color(0.16f, 0.17f, 0.19f)), ("Navy", new Color(0.12f, 0.17f, 0.28f)),
        ("Forest", new Color(0.14f, 0.26f, 0.17f)), ("Slate", new Color(0.30f, 0.34f, 0.38f)),
        ("Sand", new Color(0.55f, 0.48f, 0.36f)), ("Maroon", new Color(0.30f, 0.13f, 0.13f)),
    };

    public static void Build()
    {
        Directory.CreateDirectory(MatDir);
        var report = new StringBuilder();
        foreach (var e in Entries)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(e.Fbx) == null) { report.AppendLine("MISSING " + e.Fbx + " (run CharacterImport.SyncStaging)"); continue; }
            report.AppendLine(BuildOne(e));
        }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/roster_build.txt", report.ToString());
        Debug.Log(report.ToString());
    }

    static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

    static Material Mat(string name, Color color, Texture2D baseMap = null, Texture2D normal = null, float smooth = 0.25f)
    {
        var path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", color);
        m.SetTexture("_BaseMap", baseMap);
        if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // One material per renderer slot for variant k (tint index, or -1 for the model's own colours).
    static Material[] Materials(Entry e, Renderer[] renderers, int k)
    {
        var tint = k >= 0 ? Tints[k] : ("Base", Color.white);
        var list = new List<Material>();
        foreach (var r in renderers)
            foreach (var src in r.sharedMaterials)
            {
                var n = src.name;
                string id = $"{e.Id}_{tint.Item1}_{n}";
                if (e.Mixamo)
                {
                    bool head = n.StartsWith("Soldier_head");
                    var d = Tex(MixDir + (head ? "Roster_Swat_Soldier_head_diffuse.png" : "Roster_Swat_Soldier_Body_diffuse.png"));
                    var nm = Tex(MixDir + (head ? "Roster_Swat_Soldier_head_normal.png" : "Roster_Swat_Soldier_Body_normal.png"));
                    // The garment texture is blue-grey (~0.40,0.50,0.62): dividing the wanted colour by it keeps the folds and webbing.
                    var c = head ? Color.white : TintFor(tint.Item2);
                    list.Add(Mat(id, c, d, nm, head ? 0.35f : 0.2f));
                }
                else if (n.StartsWith("MI_Superhero"))
                    list.Add(Mat(id, k < 0 ? new Color(0.25f, 0.27f, 0.30f) : tint.Item2, null, Tex(UbcDir + "Roster_" + (n.EndsWith("Female") ? "Superhero_Female" : "Superhero_Male") + "_Normal.png"), 0.3f));
                else if (n == "MI_Eyes") list.Add(Mat($"{e.Id}_Eyes", Color.white, Tex(UbcDir + "Roster_Eye_Brown.png"), Tex(UbcDir + "Roster_Eye_Normal.png"), 0.8f));
                else list.Add(Mat($"{e.Id}_Hair", new Color(0.12f, 0.09f, 0.07f), null, null, 0.3f));
            }
        return list.ToArray();
    }

    static Color TintFor(Color target) => k_tintGuard(new Color(target.r / 0.40f, target.g / 0.50f, target.b / 0.62f));
    static Color k_tintGuard(Color c) => new Color(Mathf.Min(c.r, 1f), Mathf.Min(c.g, 1f), Mathf.Min(c.b, 1f));

    static string BuildOne(Entry e)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(e.Fbx);
        var wrapper = new GameObject("Roster_" + e.Id);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(src, wrapper.transform);
        model.name = "Model";
        var anim = model.GetComponent<Animator>();
        var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Cast<Renderer>().ToArray();

        // The model faces along cross(right, up) of its own arms; turn it to wrapper +Z.
        var rightArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm).position - anim.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
        var fwd = Vector3.Cross(rightArm.normalized, Vector3.up);
        model.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(new Vector3(fwd.x, 0, fwd.z).normalized, Vector3.forward, Vector3.up), Vector3.up);

        // Normalise: baked mesh height -> 1.8 m, feet on the origin.
        Bounds MeshBounds()
        {
            Bounds? b = null;
            foreach (SkinnedMeshRenderer r in renderers)
            {
                if (r.name == "Eyes" || r.name == "Eyebrows") continue;
                var mesh = new Mesh(); r.BakeMesh(mesh);
                var m = r.transform.localToWorldMatrix;
                foreach (var v in mesh.vertices) { var p = m.MultiplyPoint3x4(v); if (b == null) b = new Bounds(p, Vector3.zero); else { var bb = b.Value; bb.Encapsulate(p); b = bb; } }
                Object.DestroyImmediate(mesh);
            }
            return b.Value;
        }
        float raw = MeshBounds().size.y;
        // BakeMesh output does not track the root scale, so measure once at scale 1 and apply the factor.
        var rawBounds = MeshBounds();
        float k0 = Height / raw;
        model.transform.localScale *= k0;
        model.transform.position += Vector3.down * rawBounds.min.y * k0;
        var bounds = new Bounds(rawBounds.center * k0 + Vector3.down * rawBounds.min.y * k0, rawBounds.size * k0);

        var def = wrapper.AddComponent<CharacterDef>();
        def.Id = e.Id; def.Height = Height; def.EyeHeight = EyeHeight;
        def.Animator = anim; def.Renderers = renderers;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
        var s = new HumanoidSockets
        {
            Hips = B(HumanBodyBones.Hips), Spine = B(HumanBodyBones.Spine), Chest = B(HumanBodyBones.Chest), UpperChest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest),
            Neck = B(HumanBodyBones.Neck), Head = B(HumanBodyBones.Head),
            LeftUpperArm = B(HumanBodyBones.LeftUpperArm), LeftLowerArm = B(HumanBodyBones.LeftLowerArm), LeftHand = B(HumanBodyBones.LeftHand),
            RightUpperArm = B(HumanBodyBones.RightUpperArm), RightLowerArm = B(HumanBodyBones.RightLowerArm), RightHand = B(HumanBodyBones.RightHand),
            LeftUpperLeg = B(HumanBodyBones.LeftUpperLeg), LeftLowerLeg = B(HumanBodyBones.LeftLowerLeg), LeftFoot = B(HumanBodyBones.LeftFoot),
            RightUpperLeg = B(HumanBodyBones.RightUpperLeg), RightLowerLeg = B(HumanBodyBones.RightLowerLeg), RightFoot = B(HumanBodyBones.RightFoot),
        };
        // Sockets are children of bones; positions come from the T-pose, so they hold under any pose.
        var hand = s.RightHand; var prox = B(HumanBodyBones.RightMiddleProximal);
        var grip = hand.position + (prox.position - hand.position) * 0.6f;
        s.WeaponSocket = Socket("WeaponSocket", hand, grip, hand.rotation);
        def.WeaponSocketOffsetPos = Vector3.zero; def.WeaponSocketOffsetEuler = Vector3.zero;
        s.HeadSocket = Socket("HeadSocket", s.Head, new Vector3(s.Head.position.x, bounds.max.y, s.Head.position.z), s.Head.rotation);
        var back = (s.UpperChest.position) - wrapper.transform.forward * (0.12f * Height / 1.8f);
        s.BackSocket = Socket("BackSocket", s.UpperChest, back, s.UpperChest.rotation);
        s.EyePoint = Socket("EyePoint", s.Head, new Vector3(s.Head.position.x, EyeHeight, s.Head.position.z + 0.0f), wrapper.transform.rotation);
        def.Sockets = s;
        def.HandSizeScale = (prox.position - hand.position).magnitude / 0.09f; // 0.09 m = reference hand-to-knuckle for a 1.8 m adult

        // Variants: 0 = model colours, 1..8 = tints.
        var variants = new List<CharacterVariant> { new CharacterVariant { Name = "Base", Materials = Materials(e, renderers, -1) } };
        for (int k = 0; k < Tints.Length; k++) variants.Add(new CharacterVariant { Name = Tints[k].name, Materials = Materials(e, renderers, k) });
        def.Variants = variants.ToArray();
        def.ApplyVariant(0);

        // LODGroup only culls (single level); the shape LOD is the importer's Mesh LOD.
        var lod = wrapper.AddComponent<LODGroup>();
        float fov = 60f, cull = Height / (2f * CullDistance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
        lod.SetLODs(new[] { new LOD(cull, renderers) });
        lod.RecalculateBounds();
        // LODGroup measures the largest bounds extent (T-pose arms are wider than the body is tall): re-fit the cull height.
        cull = lod.size / (2f * CullDistance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
        lod.SetLODs(new[] { new LOD(cull, renderers) });
        foreach (var r in renderers) r.meshLodSelectionBias = e.LodBias;

        Debug.Log($"[Roster] {e.Id} lod.size {lod.size:F3} cull height {cull:F5} lods {lod.lodCount} fade {lod.fadeMode}");
        var prefabPath = $"{PrefabDir}/Roster_{e.Id}.prefab";
        PrefabUtility.SaveAsPrefabAsset(wrapper, prefabPath);
        var info = $"{e.Id}: raw height {raw:F3} m -> {bounds.size.y:F3} m (scale {model.transform.localScale.x:F3}), model fwd {fwd}, variants {variants.Count}, {prefabPath}";
        Object.DestroyImmediate(wrapper);
        return info;
    }

    static Transform Socket(string name, Transform parent, Vector3 worldPos, Quaternion worldRot)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(worldPos, worldRot);
        return t;
    }

    // tools/unity.sh run-gfx CharacterRoster.RenderLineup -> Logs/roster/lineup_{front,side}.png
    // 3 models x 3 variants, T-pose, Bullpup (Warden) in the right hand at true length, 1.8 m ruler pole.
    public static void RenderLineup()
    {
        SceneLook.PrepareTextures();
        var profile = RenderSetup.Apply();
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        SceneLook.SetupSky(); SceneLook.AddLights(); SceneLook.AddPostVolume(profile);
        SceneLook.WorldBox("Floor", null, new Vector3(0, -0.05f, 0), new Vector3(30, 0.1f, 30), SceneLook.Floor, 2.5f);
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.fieldOfView = 30; SceneLook.EnablePost(cam);
        Directory.CreateDirectory("Logs/roster");
        int n = 0;
        for (int m = 0; m < Entries.Length; m++)
            for (int v = 0; v < 3; v++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Roster_{Entries[m].Id}.prefab"));
                go.transform.position = new Vector3((n - 4) * 2.1f, 0, 0);
                var def = go.GetComponent<CharacterDef>();
                def.ApplyVariant(v == 0 ? 0 : v * 3);
                var w = CharacterKit.InstantiateWeapon(CharacterKit.Weapons[2]);
                w.transform.SetParent(def.Sockets.WeaponSocket, false);
                w.transform.rotation = go.transform.rotation; // muzzle +Z
                w.transform.position = def.Sockets.WeaponSocket.position;
                n++;
            }
        var pole = GameObject.CreatePrimitive(PrimitiveType.Cube); pole.transform.localScale = new Vector3(0.03f, 1.8f, 0.03f); pole.transform.position = new Vector3(-10.2f, 0.9f, 0);
        SceneLook.Shot(cam, "Logs/roster/lineup_front.png", new Vector3(0, 1.1f, -15f), new Vector3(0, 0.9f, 0));
        SceneLook.Shot(cam, "Logs/roster/lineup_side.png", new Vector3(14f, 1.1f, -0f), new Vector3(0, 0.9f, 0));
        EditorApplication.Exit(0);
    }
}
