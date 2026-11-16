using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

    public struct Entry { public string Id, Fbx; public bool Mixamo; public string Prefix; public bool Military; }
    public static readonly Entry[] Entries =
    {
        new Entry { Id = "MixamoSwat", Fbx = MixDir + "Roster_Swat.fbx", Mixamo = true, Prefix = "Roster_Swat" },
        new Entry { Id = "MixamoBrian", Fbx = MixDir + "Roster_Brian.fbx", Mixamo = true, Prefix = "Roster_Brian" },
        new Entry { Id = "MixamoLouise", Fbx = MixDir + "Roster_Louise.fbx", Mixamo = true, Prefix = "Roster_Louise" },
        new Entry { Id = "MixamoJames", Fbx = MixDir + "Roster_James.fbx", Mixamo = true, Prefix = "Roster_James" },
        new Entry { Id = "MixamoMegan", Fbx = MixDir + "Roster_Megan.fbx", Mixamo = true, Prefix = "Roster_Megan" },
        new Entry { Id = "MixamoKate", Fbx = MixDir + "Roster_Kate.fbx", Mixamo = true, Prefix = "Roster_Kate" },
        new Entry { Id = "MixamoJosh", Fbx = MixDir + "Roster_Josh.fbx", Mixamo = true, Prefix = "Roster_Josh" },
        new Entry { Id = "MixamoAdam", Fbx = MixDir + "Roster_Adam.fbx", Mixamo = true, Prefix = "Roster_Adam" },
        new Entry { Id = "MixamoMartha", Fbx = MixDir + "Roster_Martha.fbx", Mixamo = true, Prefix = "Roster_Martha" },
        new Entry { Id = "MixamoAlex", Fbx = MixDir + "Roster_Alex.fbx", Mixamo = true, Prefix = "Roster_Alex" },
        new Entry { Id = "MixamoSteve", Fbx = MixDir + "Roster_Steve.fbx", Mixamo = true, Prefix = "Roster_Steve", Military = true },
        new Entry { Id = "MixamoSwatGuy", Fbx = MixDir + "Roster_SwatGuy.fbx", Mixamo = true, Prefix = "Roster_SwatGuy", Military = true },
        new Entry { Id = "MixamoGasMask", Fbx = MixDir + "Roster_GasMask.fbx", Mixamo = true, Prefix = "Roster_GasMask", Military = true },
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

    static Texture2D FindTex(string prefix, string key, string suffix)
    {
        foreach (var g in AssetDatabase.FindAssets("t:Texture2D " + prefix + "_", new[] { MixDir.TrimEnd('/') }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g); var f = Path.GetFileNameWithoutExtension(p);
            if (f.StartsWith(prefix + "_") && f.Contains(key) && f.EndsWith("_" + suffix)) return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
        return null;
    }

    static string AssetPath(Object o) => AssetDatabase.GetAssetPath(o);

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

    // Variant keys per model (key "Base" = the model's own look).
    public static string[] VariantKeys(Entry e)
    {
        if (!e.Mixamo) return new[] { "Base", "Light_Olive", "Light_Charcoal", "Light_Navy", "Light_Coyote", "Dark_Olive", "Dark_Charcoal", "Dark_Navy", "Dark_Coyote" };
        if (e.Military) return new[] { "Base" }; // camo/gear atlases: a hue shift would break them
        if (e.Prefix == "Roster_Swat") return new[] { "Base" }.Concat(Tints.Select(t => t.name)).ToArray();
        return new[] { "Base" }.Concat(CharacterOutfit.CivilianShifts.Select(c => c.name)).ToArray();
    }

    // One material per renderer slot for a variant key. `baked` holds the CharacterOutfit textures (UBC and civilians).
    static Material[] Materials(Entry e, Renderer[] renderers, string key, Dictionary<string, Texture2D> baked)
    {
        var swatTint = Tints.FirstOrDefault(t => t.name == key);
        var list = new List<Material>();
        foreach (var r in renderers)
            foreach (var src in r.sharedMaterials)
            {
                var n = src.name; var low = n.ToLowerInvariant();
                string id = $"{e.Id}_{key}_{n}";
                if (e.Mixamo && e.Prefix != "Roster_Swat")
                {
                    // Civilian atlas: the FBX does not link textures, so by Mixamo convention:
                    // body -> 1001, body1 -> 1002, hair -> the highest-numbered texture (dark colour if that is 1001), others dark.
                    var all = AssetDatabase.FindAssets("t:Texture2D " + e.Prefix + "_", new[] { MixDir.TrimEnd('/') })
                        .Select(AssetDatabase.GUIDToAssetPath).Where(x => x.EndsWith("_Diffuse.png")).OrderBy(x => x).ToArray();
                    string num = e.Id == "MixamoSteve" ? (low.EndsWith("body1") ? "1002" : low.EndsWith("body2") ? "1001" : null) // Steve: body2 carries the head
                        : low.EndsWith("body2") ? "1003" : low.EndsWith("body1") ? "1002" : low.EndsWith("body") ? "1001"
                        : low.EndsWith("hair") && all.Length > 1 ? Regex.Match(all.Last(), @"_(\d{4})_").Groups[1].Value : null;
                    var d = num != null ? FindTex(e.Prefix, num, "Diffuse") : null;
                    var nm = num != null ? FindTex(e.Prefix, num, "Normal") : null;
                    if (low.EndsWith("body") && baked != null && baked.TryGetValue(key, out var bt)) d = bt;
                    list.Add(Mat(id, d == null ? new Color(0.06f, 0.05f, 0.04f) : Color.white, d, nm, 0.3f));
                }
                else if (e.Mixamo)
                {
                    bool head = n.StartsWith("Soldier_head");
                    var d = Tex(MixDir + (head ? "Roster_Swat_Soldier_head_diffuse.png" : "Roster_Swat_Soldier_Body_diffuse.png"));
                    var nm = Tex(MixDir + (head ? "Roster_Swat_Soldier_head_normal.png" : "Roster_Swat_Soldier_Body_normal.png"));
                    // The garment texture is blue-grey (~0.40,0.50,0.62): dividing the wanted colour by it keeps the folds and webbing.
                    var c = head || key == "Base" ? Color.white : TintFor(swatTint.c);
                    list.Add(Mat(id, c, d, nm, head ? 0.35f : 0.2f));
                }
                else if (n.StartsWith("MI_Superhero"))
                    list.Add(Mat(id, Color.white, baked[key == "Base" ? "Light_Charcoal" : key], Tex(UbcDir + "Roster_" + (n.EndsWith("Female") ? "Superhero_Female" : "Superhero_Male") + "_Normal.png"), 0.3f));
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
        def.Id = e.Id; def.HasHelmetMask = e.Prefix == "Roster_Swat" || e.Military; def.Height = Height; def.EyeHeight = EyeHeight;
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
        // Existing worn-gear visuals (ArmorVisual, BackpackVisual) still find mounts by bone NAME ("Head", "Torso"/"Chest"/"Abdomen").
        // Mixamo/UBC bones are named differently, so add same-named child aliases; remove once those visuals read Sockets (animation track).
        AddAlias(model.transform, "Head", s.Head, new Vector3(0, bounds.max.y - 0.16f - s.Head.position.y, 0)); AddAlias(model.transform, "Chest", s.UpperChest, Vector3.zero);
        def.Sockets = s;
        def.HandSizeScale = (prox.position - hand.position).magnitude / 0.09f; // 0.09 m = reference hand-to-knuckle for a 1.8 m adult

        // Baked recolours (UBC / civilians) need the body parts that share the body texture.
        Dictionary<string, Texture2D> baked = null; string bakeStats = "";
        if (!e.Military && (!e.Mixamo || e.Prefix != "Roster_Swat"))
        {
            var parts = new List<(SkinnedMeshRenderer, int)>();
            foreach (SkinnedMeshRenderer r in renderers)
            {
                var mats = r.sharedMaterials;
                for (int sm = 0; sm < mats.Length && sm < r.sharedMesh.subMeshCount; sm++)
                {
                    var mn = mats[sm].name.ToLowerInvariant();
                    if (e.Mixamo ? mn.EndsWith("body") : mn.StartsWith("mi_superhero")) parts.Add((r, sm));
                }
            }
            string light = e.Mixamo ? AssetPath(FindTex(e.Prefix, "1001", "Diffuse")) : UbcDir + (e.Id == "UbcMale" ? "Roster_Superhero_Male_Ligh.png" : "Roster_Superhero_Female_Light_BaseColor.png");
            string dark = e.Mixamo ? null : UbcDir + (e.Id == "UbcMale" ? "Roster_Superhero_Male_Dark.png" : "Roster_Superhero_Female_Dark_BaseColor.png");
            baked = CharacterOutfit.Bake(e, parts, anim, light, dark, out bakeStats);
        }
        var variants = VariantKeys(e).Select(k => new CharacterVariant { Name = k, Materials = Materials(e, renderers, k, baked) }).ToList();
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
        // Start at the first Mesh LOD whose total is within the 15k cap (interface: LOD0 <= 15k).
        int startLod = 0;
        for (; startLod < 9; startLod++)
        {
            int tris = 0;
            foreach (SkinnedMeshRenderer r in renderers)
            {
                var m = r.sharedMesh; int lc = Mathf.Max(1, m.lodCount);
                for (int sm = 0; sm < m.subMeshCount; sm++) tris += (int)(m.lodCount > 0 ? m.GetLod(sm, Mathf.Min(startLod, lc - 1)).indexCount : m.GetIndexCount(sm)) / 3;
            }
            if (tris <= 15000) break;
        }
        foreach (var r in renderers) r.meshLodSelectionBias = startLod;
        def.StartLod = startLod;

        Debug.Log($"[Roster] {e.Id} startLod {startLod} mats {string.Join(",", renderers.SelectMany(r => r.sharedMaterials).Select(x => x.name))} lod.size {lod.size:F3} cull height {cull:F5} lods {lod.lodCount} fade {lod.fadeMode}");
        var prefabPath = $"{PrefabDir}/Roster_{e.Id}.prefab";
        PrefabUtility.SaveAsPrefabAsset(wrapper, prefabPath);
        var info = $"{bakeStats}\n{e.Id}: raw height {raw:F3} m -> {bounds.size.y:F3} m (scale {model.transform.localScale.x:F3}), model fwd {fwd}, variants {variants.Count}, {prefabPath}";
        Object.DestroyImmediate(wrapper);
        return info;
    }

    static void AddAlias(Transform root, string name, Transform parent, Vector3 worldOffset)
    {
        if (root.GetComponentsInChildren<Transform>(true).Any(t => t.name == name)) return;
        var t = new GameObject(name).transform;
        t.SetParent(parent, false); t.localRotation = Quaternion.identity; t.position = parent.position + worldOffset; // crown-based so a worn helmet clears the eyes
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
            for (int v = 0; v < 2; v++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Roster_{Entries[m].Id}.prefab"));
                go.transform.position = new Vector3((m - 6.5f) * 2.1f, 0, v * 3f);
                var def = go.GetComponent<CharacterDef>();
                def.ApplyVariant(v == 0 ? 0 : 3);
                var w = CharacterKit.InstantiateWeapon(CharacterKit.Weapons[2]);
                w.transform.SetParent(def.Sockets.WeaponSocket, false);
                w.transform.rotation = go.transform.rotation; // muzzle +Z
                w.transform.position = def.Sockets.WeaponSocket.position;
                n++;
            }
        var pole = GameObject.CreatePrimitive(PrimitiveType.Cube); pole.transform.localScale = new Vector3(0.03f, 1.8f, 0.03f); pole.transform.position = new Vector3(-10.2f, 0.9f, 0);
        SceneLook.Shot(cam, "Logs/roster/lineup_front.png", new Vector3(0, 1.1f, -31f), new Vector3(0, 0.9f, 0));
        SceneLook.Shot(cam, "Logs/roster/lineup_side.png", new Vector3(30f, 1.1f, 0f), new Vector3(0, 0.9f, 0));
        EditorApplication.Exit(0);
    }

    // Low-ready rifle pose from the T-pose by rotating world-space bone rotations (no clip needed; a stand-in for S4's clips).
    public static void PoseLowReady(CharacterDef def)
    {
        var s = def.Sockets;
        void Rot(Transform t, Quaternion q) => t.rotation = q * t.rotation;
        Rot(s.RightUpperArm, Quaternion.AngleAxis(-78f, Vector3.up)); Rot(s.RightUpperArm, Quaternion.AngleAxis(38f, Vector3.right));
        Rot(s.RightLowerArm, Quaternion.AngleAxis(-70f, Vector3.up));
        Rot(s.LeftUpperArm, Quaternion.AngleAxis(78f, Vector3.up)); Rot(s.LeftUpperArm, Quaternion.AngleAxis(38f, Vector3.right));
        Rot(s.LeftLowerArm, Quaternion.AngleAxis(48f, Vector3.up));
    }

    // tools/unity.sh run-gfx CharacterRoster.RenderPosed -> Logs/roster/posed_<id>_{side,front,head}.png (low-ready, Warden, helmet+vest+backpack)
    public static void RenderPosed()
    {
        SceneLook.PrepareTextures();
        var profile = RenderSetup.Apply();
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        SceneLook.SetupSky(); SceneLook.AddLights(); SceneLook.AddPostVolume(profile);
        SceneLook.WorldBox("Floor", null, new Vector3(0, -0.05f, 0), new Vector3(30, 0.1f, 30), SceneLook.Floor, 2.5f);
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.fieldOfView = 30; SceneLook.EnablePost(cam);
        Directory.CreateDirectory("Logs/roster");
        foreach (var id in new[] { "MixamoGasMask", "MixamoSwatGuy" })
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Roster_{id}.prefab"));
            var def = go.GetComponent<CharacterDef>();
            if (id == "MixamoSwat") def.ApplyVariant(0); else def.ApplyVariant(id.StartsWith("Ubc") ? 2 : 0);
            PoseLowReady(def);
            var w = CharacterKit.InstantiateWeapon(CharacterKit.Weapons[2]);
            w.transform.SetParent(def.Sockets.WeaponSocket, false);
            // Rifle: grip at the right hand, muzzle forward, slightly raised toward the support hand (low ready).
            w.transform.rotation = Quaternion.Euler(10f, 0, 0) * go.transform.rotation;
            w.transform.position = def.Sockets.WeaponSocket.position + go.transform.forward * 0.18f;
            var armor = go.AddComponent<ArmorVisual>(); armor.ShowTiers(2, 2);
            var pack = go.AddComponent<BackpackVisual>(); pack.Show(2);
            var c = SceneLook.WorldBounds(go).center; c.y = 0.95f;
            SceneLook.Shot(cam, $"Logs/roster/posed_{id}_side.png", c + new Vector3(4.2f, 0.2f, 0), c);
            SceneLook.Shot(cam, $"Logs/roster/posed_{id}_front.png", c + new Vector3(0, 0.2f, 4.2f), c);
            var head = def.Sockets.Head.position;
            cam.fieldOfView = 12; SceneLook.Shot(cam, $"Logs/roster/posed_{id}_head.png", head + new Vector3(2.2f, 0.05f, 1.2f), head + Vector3.up * 0.05f); cam.fieldOfView = 30;
            Object.DestroyImmediate(go);
        }
        EditorApplication.Exit(0);
    }

    static void Stage(out Camera cam)
    {
        SceneLook.PrepareTextures();
        var profile = RenderSetup.Apply();
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        SceneLook.SetupSky(); SceneLook.AddLights(); SceneLook.AddPostVolume(profile);
        SceneLook.WorldBox("Floor", null, new Vector3(0, -0.05f, 0), new Vector3(60, 0.1f, 60), SceneLook.Floor, 2.5f);
        cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.fieldOfView = 30; SceneLook.EnablePost(cam);
        Directory.CreateDirectory("Logs/roster");
    }

    static GameObject Geared(string id, Vector3 at, int tier)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Roster_{id}.prefab"));
        go.transform.position = at;
        PoseLowReady(go.GetComponent<CharacterDef>());
        go.AddComponent<ArmorVisual>().ShowTiers(tier, tier);
        go.AddComponent<BackpackVisual>().Show(tier);
        return go;
    }

    // tools/unity.sh run-gfx CharacterRoster.RenderGearSheet -> Logs/roster/gear_t{1,2,3}_{front,back}.png (all 15 models in a row),
    // gear_detail_<id>.png (tiers 1-3 side by side, three-quarter front and back) and gear_legacy.png (the old Swat character).
    public static void RenderGearSheet()
    {
        Stage(out var cam);
        for (int tier = 1; tier <= 3; tier++)
        {
            var roots = new List<GameObject>();
            for (int m = 0; m < Entries.Length; m++)
                roots.Add(Geared(Entries[m].Id, new Vector3((m - (Entries.Length - 1) * 0.5f) * 1.15f, 0, 0), tier));
            SceneLook.Shot(cam, $"Logs/roster/gear_t{tier}_front.png", new Vector3(0, 1.3f, 16f), new Vector3(0, 1.0f, 0));
            SceneLook.Shot(cam, $"Logs/roster/gear_t{tier}_back.png", new Vector3(0, 1.3f, -16f), new Vector3(0, 1.0f, 0));
            foreach (var r in roots) Object.DestroyImmediate(r);
        }
        foreach (var id in new[] { "MixamoSwat", "MixamoBrian", "MixamoSteve", "MixamoGasMask", "UbcMale", "UbcFemale" })
        {
            var roots = new List<GameObject>();
            for (int tier = 1; tier <= 3; tier++) { var g = Geared(id, new Vector3((tier - 2) * 1.3f, 0, 0), tier); g.transform.rotation = Quaternion.Euler(0, 0, 0); roots.Add(g); }
            cam.fieldOfView = 24;
            SceneLook.Shot(cam, $"Logs/roster/gear_detail_{id}_front.png", new Vector3(2.4f, 1.9f, 5.2f), new Vector3(0, 1.3f, 0));
            SceneLook.Shot(cam, $"Logs/roster/gear_detail_{id}_back.png", new Vector3(-2.4f, 1.9f, -5.2f), new Vector3(0, 1.3f, 0));
            cam.fieldOfView = 30;
            foreach (var r in roots) Object.DestroyImmediate(r);
        }
        // The old character (CharacterKit, no CharacterDef): the hand-tuned constants path.
        SceneLook.PrepareTextures();
        CharacterKit.Prepare();
        for (int tier = 1; tier <= 3; tier++)
        {
            var slot = new GameObject("Legacy" + tier).transform; slot.position = new Vector3((tier - 2) * 1.3f, 0, 0);
            var v = CharacterKit.AddTo(slot);
            v.Root.gameObject.AddComponent<ArmorVisual>().ShowTiers(tier, tier);
            v.Root.gameObject.AddComponent<BackpackVisual>().Show(tier);
        }
        cam.fieldOfView = 24;
        SceneLook.Shot(cam, "Logs/roster/gear_legacy_front.png", new Vector3(2.4f, 1.9f, 5.2f), new Vector3(0, 1.3f, 0));
        SceneLook.Shot(cam, "Logs/roster/gear_legacy_back.png", new Vector3(-2.4f, 1.9f, -5.2f), new Vector3(0, 1.3f, 0));
        EditorApplication.Exit(0);
    }
}
