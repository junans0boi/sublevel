using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Real helmet, vest and backpack models (ASSET_LEDGER G-0001..G-0009): each source under Art/Gear/<Id>/ is baked into
// one mesh in the WornGearMeshes frame (metres, +y up, +z forward, same origin and size as the code-built model it
// replaces, so the existing fits on the characters still hold) plus URP Lit materials, saved to Resources/Gear where
// WornGearMeshes loads them for the worn visuals and the ground pickups.
// tools/unity.sh run GearKit.MeasureBodies   -> Resources/Gear/Bodies/<roster id>.asset (torso surface for GearFit)
// tools/unity.sh run GearKit.Bake            -> Resources/Gear/{Helmet,Vest,Backpack}{1,2,3}.asset + _0.mat (_1.mat: see-through part)
// tools/unity.sh run-gfx GearKit.Probe       -> Logs/gear/probe_<Id>.png (front, right side, top; baked over code-built)
// tools/unity.sh run-gfx GearKit.Shoot       -> Logs/gear/worn_<model>_{front,back,side}.png, pose_t<tier>_{side,front}.png, ground_{all,near}.png
public static class GearKit
{
    public const string ArtRoot = AssetImportRules.ArtRoot + "Gear/";
    public const string OutRoot = "Assets/_Project/Resources/" + WornGearMeshes.ResourceDir + "/";
    static readonly string[] ModelExtensions = { ".fbx", ".dae", ".obj" };

    sealed class Source
    {
        public string Id; public LootKind Kind; public int Tier;
        public Vector3 Turn;     // euler degrees: the imported model's axes -> the gear frame
        public float Size = 1f;  // on top of the size matched to the code-built model
        public Vector3 Shift;    // metres in the gear frame, after matching
        public string Glass;     // material-name fragment of a see-through part (own sub-mesh, transparent material)
        public Color GlassTint = Color.white;
        public string Skip;      // mesh-name fragment of a part left out
        public Color Tint = Color.white;
    }

    // Size, Shift and Tint were tuned on the worn captures (Shoot): the code-built anchors fit the body, the real models
    // differ in proportions (straps, cummerbunds, frames).
    static readonly Source[] Sources =
    {
        new Source { Id = "Helmet1", Kind = LootKind.Helmet, Tier = 1, Size = 1.05f, Shift = new Vector3(0, -0.025f, 0) },
        new Source { Id = "Helmet2", Kind = LootKind.Helmet, Tier = 2 },
        // The face shield reads as a smoked visor, not a white sheet.
        new Source { Id = "Helmet3", Kind = LootKind.Helmet, Tier = 3, Glass = "glass", GlassTint = new Color(0.45f, 0.5f, 0.55f, 0.45f) },
        new Source { Id = "Vest1", Kind = LootKind.Vest, Tier = 1, Size = 0.9f, Shift = new Vector3(0, -0.05f, 0) },
        // Exported Z-up with the front on -y. Olive (the source is black; recoloured in the colour map, darkened here) to
        // stand apart from the black tier 1.
        new Source { Id = "Vest2", Kind = LootKind.Vest, Tier = 2, Turn = new Vector3(90, 180, 0), Tint = new Color(0.62f, 0.62f, 0.6f) },
        new Source { Id = "Vest3", Kind = LootKind.Vest, Tier = 3 },
        // Z-up, back panel on +y. The pale canvas is toned toward khaki.
        new Source { Id = "Pack1", Kind = LootKind.Backpack, Tier = 1, Turn = new Vector3(90, 0, 0), Tint = new Color(0.85f, 0.82f, 0.72f) },
        // Its shoulder straps hang out past the back panel, which the match takes for the body side: moved in onto the back.
        new Source { Id = "Pack2", Kind = LootKind.Backpack, Tier = 2, Turn = new Vector3(90, 0, 0), Shift = new Vector3(0, 0, 0.15f) },
        // The bag without its carrying frame: the frame's shoulder straps and hip belt stood off the body and pushed the
        // bag aside. Made the largest pack.
        new Source { Id = "Pack3", Kind = LootKind.Backpack, Tier = 3, Skip = "frame", Size = 1.1f },
    };

    public static void Bake()
    {
        int code = 0;
        try
        {
            Directory.CreateDirectory(OutRoot);
            foreach (var s in Sources) BakeOne(s);
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e) { Debug.LogError("[GearKit] failed: " + e); code = 1; }
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    static string ModelPath(Source s)
    {
        foreach (var ext in ModelExtensions)
        {
            var p = $"{ArtRoot}{s.Id}/{s.Id}{ext}";
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException("no model for " + s.Id);
    }

    static void BakeOne(Source s)
    {
        var path = ModelPath(s);
        var raw = ReadParts(path, s.Glass, s.Skip, out var rawBounds);
        var turn = Quaternion.Euler(s.Turn);
        // Into the gear frame, then matched to the code-built model of the same piece (its size and anchor were fitted to
        // the characters): helmet by width with the crowns level, vest by width centred, pack by height on its bottom and
        // body-side face.
        var verts = raw.Positions.Select(p => turn * p).ToList();
        var b = BoundsOf(verts);
        var t = WornGearMeshes.CodeModel(s.Kind, s.Tier).bounds;
        float scale = (s.Kind == LootKind.Backpack ? t.size.y / b.size.y : t.size.x / b.size.x) * s.Size;
        Vector3 from, to;
        switch (s.Kind)
        {
            case LootKind.Helmet: from = new Vector3(b.center.x, b.max.y, b.center.z) * scale; to = new Vector3(t.center.x, t.max.y, t.center.z); break;
            case LootKind.Vest: from = b.center * scale; to = t.center; break;
            default: from = new Vector3(b.center.x, b.min.y, b.max.z) * scale; to = new Vector3(t.center.x, t.min.y, t.max.z); break;
        }
        var offset = to - from + s.Shift;
        for (int i = 0; i < verts.Count; i++) verts[i] = verts[i] * scale + offset;

        var mesh = new Mesh { name = s.Kind.ToString() + s.Tier };
        if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(raw.Normals.Select(n => (turn * n).normalized).ToList());
        mesh.SetUVs(0, raw.Uvs);
        mesh.subMeshCount = raw.Glass.Count > 0 ? 2 : 1;
        mesh.SetTriangles(raw.Opaque, 0);
        if (raw.Glass.Count > 0) mesh.SetTriangles(raw.Glass, 1);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        // Unity Mesh LOD (as the roster characters, ADR-192): fifty geared characters in view cost 2.65M triangles without it.
        MeshLodUtility.GenerateMeshLods(mesh);
        Save(mesh, OutRoot + mesh.name + ".asset");

        var dir = $"{ArtRoot}{s.Id}/{s.Id}";
        Save(Lit($"{dir}_color.png", $"{dir}_normal.png", $"{dir}_mask.png", s.Tint, false), $"{OutRoot}{mesh.name}_0.mat");
        if (raw.Glass.Count > 0) Save(Lit($"{dir}_glass_color.png", null, $"{dir}_glass_mask.png", s.GlassTint, true), $"{OutRoot}{mesh.name}_1.mat");
        Debug.Log($"[GearKit] {s.Id} -> {mesh.name}: {raw.Opaque.Count / 3 + raw.Glass.Count / 3} tris ({raw.Glass.Count / 3} see-through), raw size {rawBounds.size:F3}, " +
                  $"scale {scale:F4}, baked size {mesh.bounds.size:F3} (code {t.size:F3}), submeshes {mesh.subMeshCount}, LODs {mesh.lodCount}");
    }

    sealed class Parts
    {
        public readonly List<Vector3> Positions = new List<Vector3>(), Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Opaque = new List<int>(), Glass = new List<int>();
    }

    // Every mesh of the model in the model root's frame, as one vertex list with the see-through triangles apart.
    static Parts ReadParts(string path, string glass, string skip, out Bounds bounds)
    {
        var parts = new Parts();
        AssetImportRules.ReadableWhileSplitting = path;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        try
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                if (skip != null && r.name.ToLowerInvariant().Contains(skip)) { Debug.Log($"[GearKit] {Path.GetFileName(path)} {r.name} left out"); continue; }
                var m = root.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                int first = parts.Positions.Count;
                var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv;
                for (int i = 0; i < v.Length; i++)
                {
                    parts.Positions.Add(m.MultiplyPoint3x4(v[i]));
                    parts.Normals.Add(n.Length == v.Length ? m.MultiplyVector(n[i]).normalized : Vector3.up);
                    parts.Uvs.Add(uv.Length == v.Length ? uv[i] : Vector2.zero);
                }
                var mats = r.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var name = sub < mats.Length && mats[sub] != null ? mats[sub].name : "";
                    bool see = glass != null && name.ToLowerInvariant().Contains(glass);
                    var into = see ? parts.Glass : parts.Opaque;
                    var tris = mesh.GetTriangles(sub);
                    // A mirrored transform flips the winding.
                    bool flip = m.determinant < 0f;
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        into.Add(first + tris[i]);
                        into.Add(first + tris[flip ? i + 2 : i + 1]);
                        into.Add(first + tris[flip ? i + 1 : i + 2]);
                    }
                    Debug.Log($"[GearKit] {Path.GetFileName(path)} {r.name} sub {sub} '{name}' {tris.Length / 3} tris{(see ? " see-through" : "")}");
                }
            }
        }
        finally
        {
            AssetImportRules.ReadableWhileSplitting = null;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        bounds = BoundsOf(parts.Positions);
        return parts;
    }

    static Bounds BoundsOf(List<Vector3> points)
    {
        var b = new Bounds(points[0], Vector3.zero);
        foreach (var p in points) b.Encapsulate(p);
        return b;
    }

    static Material Lit(string color, string normal, string mask, Color tint, bool seeThrough)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(color));
        mat.SetColor("_BaseColor", tint);
        if (normal != null) { mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal)); mat.EnableKeyword("_NORMALMAP"); }
        // Mask: R metallic, G occlusion, A smoothness (packed from the source roughness/gloss maps).
        var m = AssetDatabase.LoadAssetAtPath<Texture2D>(mask);
        mat.SetTexture("_MetallicGlossMap", m); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        mat.SetTexture("_OcclusionMap", m); mat.EnableKeyword("_OCCLUSIONMAP");
        mat.SetFloat("_Smoothness", 1f);
        mat.SetFloat("_OcclusionStrength", 1f);
        if (seeThrough)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        return mat;
    }

    // Overwrites in place so references (and GUIDs) survive a re-bake.
    static void Save(Object asset, string path)
    {
        var old = AssetDatabase.LoadAssetAtPath<Object>(path);
        if (old != null && old.GetType() == asset.GetType()) { EditorUtility.CopySerialized(asset, old); EditorUtility.SetDirty(old); }
        else { AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(asset, path); }
    }

    // Every roster character's torso surface at its standing pose (GearBody): the skinned meshes baked, the vertices whose
    // strongest bone is a torso bone (hips to shoulders; arms, head and legs left out) binned by height and angle around
    // the torso axis, the outer radius and the average torso-bone weights per cell.
    public static void MeasureBodies()
    {
        int code = 0;
        try
        {
            Directory.CreateDirectory(OutRoot + "Bodies");
            foreach (var e in CharacterRoster.Entries) MeasureBody(e.Id);
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception ex) { Debug.LogError("[GearKit] failed: " + ex); code = 1; }
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    const float RowStep = 0.02f;
    const int Columns = 36;

    static void MeasureBody(string id)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterRoster.PrefabDir}/Roster_{id}.prefab"));
        try
        {
            var def = go.GetComponent<CharacterDef>();
            var anim = def.Animator;
            int nb = GearBody.Bones.Length;
            var bones = new Transform[nb];
            for (int i = 0; i < nb; i++) bones[i] = GearBody.Bone(anim, i);
            var origin = bones[GearBody.Root].position; var rot = go.transform.rotation; var inv = Quaternion.Inverse(rot);
            var frameToWorld = Matrix4x4.TRS(origin, rot, Vector3.one);
            var slot = new Dictionary<Transform, int>();
            for (int i = 0; i < nb; i++) if (!slot.ContainsKey(bones[i])) slot[bones[i]] = i;

            // Torso vertices in the frame, with their weights over the torso bones.
            var pts = new List<Vector3>(); var ws = new List<float[]>(); var shoulderPts = new List<Vector3>();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (smr.sharedMesh == null || !smr.gameObject.activeInHierarchy) continue;
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                var verts = baked.vertices; var bw = smr.sharedMesh.boneWeights; var smrBones = smr.bones;
                for (int i = 0; i < verts.Length && i < bw.Length; i++)
                {
                    var w = bw[i];
                    var top = smrBones[w.boneIndex0];
                    if (top == null || !slot.TryGetValue(top, out int topSlot) || topSlot >= 7) continue; // arms (upper arm and below), head, legs
                    var p = inv * (m.MultiplyPoint3x4(verts[i]) - origin);
                    var cell = new float[nb];
                    void Add(int bi, float wt) { if (wt > 0f && smrBones[bi] != null && slot.TryGetValue(smrBones[bi], out int k)) cell[k] += wt; }
                    Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
                    pts.Add(p); ws.Add(cell);
                    if (topSlot == 5 || topSlot == 6) shoulderPts.Add(p);
                }
                Object.DestroyImmediate(baked);
            }
            var hips = inv * (bones[0].position - origin);
            float shoulderTop = shoulderPts.Count > 0 ? shoulderPts.Max(p => p.y) : 0.12f;
            float waist = hips.y + 0.06f;
            var body = ScriptableObject.CreateInstance<GearBody>();
            body.Id = id; body.Dy = RowStep; body.Na = Columns;
            body.Y0 = waist - 0.06f;
            body.Ny = Mathf.CeilToInt((shoulderTop + 0.06f - body.Y0) / RowStep) + 1;
            body.ShoulderTop = shoulderTop; body.Waist = waist;
            int ny = body.Ny, na = Columns;

            // Axis centre per row: the middle of the row's x and z extents.
            body.Centre = new Vector2[ny];
            var lo = Enumerable.Repeat(new Vector2(float.MaxValue, float.MaxValue), ny).ToArray();
            var hi = Enumerable.Repeat(new Vector2(float.MinValue, float.MinValue), ny).ToArray();
            foreach (var p in pts)
            {
                int r = Mathf.RoundToInt((p.y - body.Y0) / RowStep); if (r < 0 || r >= ny) continue;
                lo[r] = Vector2.Min(lo[r], new Vector2(p.x, p.z)); hi[r] = Vector2.Max(hi[r], new Vector2(p.x, p.z));
            }
            for (int r = 0; r < ny; r++) body.Centre[r] = lo[r].x < float.MaxValue ? (lo[r] + hi[r]) * 0.5f : Vector2.zero;
            for (int r = 0; r < ny; r++) // empty rows: nearest filled
                if (lo[r].x == float.MaxValue)
                    for (int d = 1; d < ny; d++)
                    {
                        int f = r - d >= 0 && lo[r - d].x < float.MaxValue ? r - d : r + d < ny && lo[r + d].x < float.MaxValue ? r + d : -1;
                        if (f >= 0) { body.Centre[r] = body.Centre[f]; break; }
                    }
            int mid = Mathf.Clamp(Mathf.RoundToInt((0f - body.Y0) / RowStep), 0, ny - 1);
            body.Width = hi[mid].x - lo[mid].x;

            body.Radius = new float[ny * na];
            for (int i = 0; i < body.Radius.Length; i++) body.Radius[i] = float.MaxValue;
            var sums = new float[ny * na * nb]; var counts = new int[ny * na];
            var outer = new float[ny * na];
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                int r = Mathf.RoundToInt((p.y - body.Y0) / RowStep); if (r < 0 || r >= ny) continue;
                var c = body.Centre[r];
                float dx = p.x - c.x, dz = p.z - c.y;
                int col = Mathf.RoundToInt(GearBody.Column(Mathf.Atan2(dx, dz), na)) % na, cell = r * na + col;
                float rad = Mathf.Sqrt(dx * dx + dz * dz);
                if (rad > outer[cell]) outer[cell] = rad;
                counts[cell]++;
                for (int k = 0; k < nb; k++) sums[cell * nb + k] += ws[i][k];
            }
            for (int i = 0; i < outer.Length; i++) if (counts[i] > 0) body.Radius[i] = outer[i];
            GearFit.Fill(body.Radius, ny, na);
            // Gear is stiff: it bridges hollows (between and under the breasts, along the spine) instead of following the
            // skin. Each row becomes its convex hull, and down the body a row is never more than 6 mm per row narrower
            // than the one above (a vest hangs from the chest).
            for (int r = 0; r < ny; r++) Hull(body.Radius, r, na);
            for (int r = ny - 2; r >= 0; r--)
                for (int c = 0; c < na; c++)
                    body.Radius[r * na + c] = Mathf.Max(body.Radius[r * na + c], body.Radius[(r + 1) * na + c] - 0.006f);
            body.Weights = new float[ny * na * nb];
            for (int cell = 0; cell < ny * na; cell++)
            {
                int from = cell;
                if (counts[from] == 0) // nearest filled cell in the row, else the Root bone
                {
                    int r = cell / na, c0 = cell % na; from = -1;
                    for (int d = 1; d <= na / 2 && from < 0; d++)
                    {
                        if (counts[r * na + (c0 + d) % na] > 0) from = r * na + (c0 + d) % na;
                        else if (counts[r * na + (c0 - d + na) % na] > 0) from = r * na + (c0 - d + na) % na;
                    }
                }
                float total = 0f;
                if (from >= 0) for (int k = 0; k < nb; k++) total += sums[from * nb + k];
                for (int k = 0; k < nb; k++) body.Weights[cell * nb + k] = total > 0f ? sums[from * nb + k] / total : k == GearBody.Root ? 1f : 0f;
            }
            body.BindPoses = new Matrix4x4[nb];
            for (int i = 0; i < nb; i++) body.BindPoses[i] = bones[i].worldToLocalMatrix * frameToWorld;
            Save(body, $"{OutRoot}Bodies/{id}.asset");
            Debug.Log($"[GearKit] body {id}: {pts.Count} torso vertices, rows {ny}, shoulder top {shoulderTop:F3}, waist {waist:F3}, width {body.Width:F3}, " +
                      $"chest radius front {body.RadiusAt(0f, 0f):F3} side {body.RadiusAt(0f, Mathf.PI / 2):F3} back {body.RadiusAt(0f, Mathf.PI):F3}");
        }
        finally { Object.DestroyImmediate(go); }
    }

    // Replaces one row's radii by the convex hull of its points (a ray from the axis at each column's angle).
    static void Hull(float[] radius, int row, int na)
    {
        var pts = new List<Vector2>();
        for (int c = 0; c < na; c++) { float a = c * 2f * Mathf.PI / na, r = radius[row * na + c]; pts.Add(new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r)); }
        pts.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y.CompareTo(q.y));
        float Cross(Vector2 o, Vector2 p, Vector2 q) => (p.x - o.x) * (q.y - o.y) - (p.y - o.y) * (q.x - o.x);
        var h = new List<Vector2>();
        foreach (var p in pts) { while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], p) <= 0f) h.RemoveAt(h.Count - 1); h.Add(p); }
        int lower = h.Count + 1;
        for (int i = pts.Count - 2; i >= 0; i--) { var p = pts[i]; while (h.Count >= lower && Cross(h[h.Count - 2], h[h.Count - 1], p) <= 0f) h.RemoveAt(h.Count - 1); h.Add(p); }
        h.RemoveAt(h.Count - 1);
        for (int c = 0; c < na; c++)
        {
            float a = c * 2f * Mathf.PI / na; var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            float best = radius[row * na + c];
            for (int i = 0; i < h.Count; i++)
            {
                Vector2 p = h[i], q = h[(i + 1) % h.Count], e = q - p;
                float den = dir.x * e.y - dir.y * e.x; if (Mathf.Abs(den) < 1e-6f) continue;
                float t = (p.x * e.y - p.y * e.x) / den, u = (p.x * dir.y - p.y * dir.x) / den;
                if (t > 0f && u >= -1e-4f && u <= 1f + 1e-4f) best = Mathf.Max(best, t);
            }
            radius[row * na + c] = best;
        }
    }

    // Each piece close up, baked (top row) over the code-built one it replaces (bottom row), seen from the front, the
    // right side (+x) and above: checks the turn and the match. Camera on +z, so the front column shows the +z face.
    public static void Probe()
    {
        int code = 0;
        try
        {
            SceneLook.PrepareTextures();
            var profile = RenderSetup.Apply();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneLook.SetupSky(); SceneLook.AddLights(); SceneLook.AddPostVolume(profile);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.fieldOfView = 30; cam.nearClipPlane = 0.05f; SceneLook.EnablePost(cam);
            Directory.CreateDirectory("Logs/gear");
            var views = new[] { Quaternion.identity, Quaternion.Euler(0, -90, 0), Quaternion.Euler(90, 0, 0) };
            foreach (var s in Sources)
            {
                var made = new List<GameObject>();
                for (int v = 0; v < 3; v++)
                {
                    var at = new Vector3((v - 1) * 0.8f, 1f, 0f);
                    made.Add(Place("Baked", WornGearMeshes.Model(s.Kind, s.Tier), WornGearMeshes.Materials(s.Kind, s.Tier), at + Vector3.up * 0.75f, views[v]));
                    made.Add(Place("Code", WornGearMeshes.CodeModel(s.Kind, s.Tier), new[] { WornGearMeshes.Palette(s.Tier), WornGearMeshes.Palette(s.Tier), WornGearMeshes.Palette(s.Tier) }, at, views[v]));
                }
                var c = new Vector3(0, 1.4f, 0);
                SceneLook.Shot(cam, $"Logs/gear/probe_{s.Id}.png", c + new Vector3(0, 0.2f, 4.2f), c);
                foreach (var g in made) Object.DestroyImmediate(g);
            }
        }
        catch (System.Exception e) { Debug.LogError("[GearKit] failed: " + e); code = 1; }
        EditorApplication.Exit(code);
    }

    static readonly string[] Models = { "MixamoBrian", "UbcFemale", "MixamoSwatGuy" };
    static readonly string[] Poses = { "RifleIdle", "RifleRun", "RifleCrouchWalk", "ProneFiringRifle" };

    // The pieces worn (tiers 1-3 side by side on three body types, from the front, back and side), every tier in the
    // idle, run, crouch and prone poses (clipping), and the nine ground pickups as the LootItem prefab shows them.
    public static void Shoot()
    {
        int code = 0;
        try
        {
            SceneLook.PrepareTextures();
            var profile = RenderSetup.Apply();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneLook.SetupSky(); SceneLook.AddLights(); SceneLook.AddPostVolume(profile);
            SceneLook.WorldBox("Floor", null, new Vector3(0, -0.05f, 0), new Vector3(60, 0.1f, 60), SceneLook.Floor, 2.5f);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.fieldOfView = 24; cam.nearClipPlane = 0.05f; SceneLook.EnablePost(cam);
            Directory.CreateDirectory("Logs/gear");
            var c = new Vector3(0, 1.25f, 0);
            foreach (var id in Models)
            {
                var made = new List<GameObject>();
                for (int tier = 1; tier <= 3; tier++) made.Add(Wear(id, new Vector3((tier - 2) * 1.1f, 0, 0), tier, "RifleIdle", 0f));
                SceneLook.Shot(cam, $"Logs/gear/worn_{id}_front.png", c + new Vector3(0.8f, 0.4f, 5.6f), c);
                SceneLook.Shot(cam, $"Logs/gear/worn_{id}_back.png", c + new Vector3(-0.8f, 0.4f, -5.6f), c);
                foreach (var g in made) g.transform.rotation = Quaternion.Euler(0, -90, 0);
                SceneLook.Shot(cam, $"Logs/gear/worn_{id}_side.png", c + new Vector3(0.2f, 0.3f, 5.6f), c);
                foreach (var g in made) Object.DestroyImmediate(g);
            }
            for (int tier = 1; tier <= 3; tier++)
            {
                var made = new List<GameObject>();
                for (int p = 0; p < Poses.Length; p++)
                {
                    var clip = CharacterAnimClips.LoadClip(Poses[p]);
                    made.Add(Wear("MixamoBrian", new Vector3((p - 1.5f) * 1.4f, 0, 0), tier, Poses[p], clip != null ? clip.length * 0.5f : 0f));
                }
                cam.fieldOfView = 30;
                SceneLook.Shot(cam, $"Logs/gear/pose_t{tier}_front.png", new Vector3(1.5f, 1.6f, 6.5f), new Vector3(0, 0.8f, 0));
                SceneLook.Shot(cam, $"Logs/gear/pose_t{tier}_side.png", new Vector3(7f, 1.4f, -0.5f), new Vector3(0, 0.7f, 0));
                SceneLook.Shot(cam, $"Logs/gear/pose_t{tier}_back.png", new Vector3(-1.5f, 1.8f, -6.5f), new Vector3(0, 0.8f, 0));
                cam.fieldOfView = 24;
                foreach (var g in made) Object.DestroyImmediate(g);
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestBoxBuilder.PrefabDir + "/LootItem.prefab");
            var kinds = new[] { LootKind.Helmet, LootKind.Vest, LootKind.Backpack };
            for (int k = 0; k < 3; k++)
                for (int tier = 1; tier <= 3; tier++)
                {
                    var go = Object.Instantiate(prefab);
                    go.transform.position = new Vector3((tier - 2) * 0.8f, 0.3f, k * 0.9f);
                    var view = go.GetComponent<LootView>();
                    view.Show(kinds[k], 0, default, tier, 0);
                    var b = SceneLook.WorldBounds(go);
                    Debug.Log($"[GearKit] ground {kinds[k]} {tier}: mesh {(view.ShownMesh != null ? view.ShownMesh.name : "-")}, bottom {b.min.y:F3}, size {b.size:F2}");
                }
            cam.fieldOfView = 30;
            SceneLook.Shot(cam, "Logs/gear/ground_all.png", new Vector3(0, 2.4f, -2.2f), new Vector3(0, 0, 0.9f));
            SceneLook.Shot(cam, "Logs/gear/ground_eye.png", new Vector3(-1.2f, 1.6f, -2.6f), new Vector3(0, 0, 0.9f));
        }
        catch (System.Exception e) { Debug.LogError("[GearKit] failed: " + e); code = 1; }
        EditorApplication.Exit(code);
    }

    // A roster character with the three pieces of a tier, in a frame of a clip. The visuals mount while the character
    // still stands as the prefab was saved (as Awake does), then the clip moves the bones they hang on.
    static GameObject Wear(string id, Vector3 at, int tier, string clipName, float time)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterRoster.PrefabDir}/Roster_{id}.prefab"));
        go.transform.position = at;
        go.AddComponent<ArmorVisual>().ShowTiers(tier, tier);
        go.AddComponent<BackpackVisual>().Show(tier);
        var clip = CharacterAnimClips.LoadClip(clipName);
        if (clip != null) clip.SampleAnimation(go.GetComponent<CharacterDef>().Animator.gameObject, time);
        else Debug.LogWarning("[GearKit] no clip " + clipName);
        return go;
    }

    static GameObject Place(string name, Mesh mesh, Material[] materials, Vector3 at, Quaternion turn)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(at, turn);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = materials;
        return go;
    }
}
