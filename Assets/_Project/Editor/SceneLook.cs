using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// The approved M1.5 look (ADR-035/037) as reusable pieces: PBR materials from the CC0 texture sets,
// HDRI sky and ambient, key + fill lights, the post-processing volume and screenshot capture.
public static class SceneLook
{
    public const string GeneratedDir = "Assets/_Project/Prefabs/M1_5";
    public const string MaterialDir = GeneratedDir + "/Materials";
    const string TextureDir = "Assets/_Project/Art/Materials";
    const string StagingDir = "AssetStaging/acg";
    const string HdriPath = "Assets/_Project/Art/HDRI/KloofendalPartlyCloudy/kloofendal_48d_partly_cloudy_puresky_2k.hdr";
    // The raw HDRI lights the scene about twice too bright; ambient is kept low so the key light sets the contrast.
    const float SkyExposure = 0.7f;
    const float AmbientIntensity = 0.38f;
    const float ReflectionIntensity = 0.35f;

    static readonly Dictionary<string, Mesh> BoxMeshes = new Dictionary<string, Mesh>();

    // Derived texture files (packed masks, muted colors). Needs the staged downloads only the first time.
    public static void PrepareTextures()
    {
        Directory.CreateDirectory(MaterialDir);
        foreach (var id in new[] { "Concrete015", "Concrete034", "Metal032", "MetalPlates006", "PaintedMetal004" }) PackMask(id);
        MuteColor("PaintedMetal004", 0.45f, 0.5f);
        AssetDatabase.Refresh();
    }

    // URP Lit reads metallic from R and smoothness from A of one texture; ambientCG ships separate
    // roughness/metalness maps, so pack them once from the staged download.
    static void PackMask(string id)
    {
        var outPath = $"{TextureDir}/{id}/{id}_mask.png";
        if (File.Exists(outPath)) return;
        var rough = LoadStaged($"{StagingDir}/{id}/{id}_1K-JPG_Roughness.jpg");
        if (rough == null) throw new FileNotFoundException("staged roughness map missing for " + id);
        var metal = LoadStaged($"{StagingDir}/{id}/{id}_1K-JPG_Metalness.jpg");
        var r = rough.GetPixels32();
        var m = metal != null ? metal.GetPixels32() : null;
        var packed = new Color32[r.Length];
        for (int i = 0; i < r.Length; i++)
            packed[i] = new Color32(m != null ? m[i].r : (byte)0, 0, 0, (byte)(255 - r[i].r));
        var tex = new Texture2D(rough.width, rough.height, TextureFormat.RGBA32, false, true);
        tex.SetPixels32(packed);
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rough);
        if (metal != null) Object.DestroyImmediate(metal);
    }

    // Rewrites an asset's color map from the staged original with lower saturation and brightness.
    static void MuteColor(string id, float saturation, float brightness)
    {
        var source = LoadStaged($"{StagingDir}/{id}/{id}_1K-JPG_Color.jpg");
        if (source == null) return; // no staged original on this machine: keep the committed map
        var pixels = source.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            var grey = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            pixels[i] = new Color(
                Mathf.Lerp(grey, c.r, saturation) * brightness,
                Mathf.Lerp(grey, c.g, saturation) * brightness,
                Mathf.Lerp(grey, c.b, saturation) * brightness);
        }
        source.SetPixels(pixels);
        File.WriteAllBytes($"{TextureDir}/{id}/{id}_color.jpg", source.EncodeToJPG(92));
        Object.DestroyImmediate(source);
    }

    static Texture2D LoadStaged(string path)
    {
        if (!File.Exists(path)) return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        tex.LoadImage(File.ReadAllBytes(path));
        return tex;
    }

    static Material Pbr(string id, string name, float bumpScale = 1f, float smoothness = 1f)
    {
        var path = $"{MaterialDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetTexture("_BaseMap", Load<Texture2D>($"{TextureDir}/{id}/{id}_color.jpg"));
        mat.SetTexture("_BumpMap", Load<Texture2D>($"{TextureDir}/{id}/{id}_normal.jpg"));
        mat.SetTexture("_MetallicGlossMap", Load<Texture2D>($"{TextureDir}/{id}/{id}_mask.png"));
        mat.EnableKeyword("_NORMALMAP");
        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_BumpScale", bumpScale);
        mat.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Materials for geometry made with WorldBox: the pattern size is set by the mesh, not the material.
    public static Material Floor => Pbr("Concrete015", "World_Concrete015", smoothness: 0.35f);
    public static Material Wall => Pbr("Concrete034", "World_Concrete034");
    public static Material Panel => Pbr("Metal032", "World_Metal032");
    // The plate pattern is kept small and its relief and gloss toned down so it reads as textured metal, not as big triangles.
    public static Material Plates => Pbr("MetalPlates006", "World_MetalPlates006", bumpScale: 0.5f, smoothness: 0.6f);
    public static Material Accent => Pbr("PaintedMetal004", "World_PaintedMetal004");

    // ---- Optic parts (OpticMeshes, ADR-060): shared by the combatant and loot prefabs and the item icons ----

    static Material LitAsset(string name)
    {
        var path = $"{MaterialDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        return mat;
    }

    // Dark matte metal: plain near-black colour (the PaintedMetal014 colour map read as chipped stone on the large
    // eyepiece housing), with that CC0 set's normal map, kept faint, for a little wear.
    public static Material OpticMetal
    {
        get
        {
            const string id = "PaintedMetal014";
            var mat = LitAsset("Optic_Metal");
            mat.SetTexture("_BaseMap", null);
            mat.SetTexture("_BumpMap", Load<Texture2D>($"{TextureDir}/{id}/{id}_normal.jpg"));
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", null);
            mat.DisableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_BumpScale", 0.3f);
            mat.SetFloat("_Metallic", 0.55f);
            mat.SetFloat("_Smoothness", 0.3f);
            mat.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.13f));
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }

    // The faces of scope lenses: dark, glossy, opaque.
    public static Material OpticGlass
    {
        get
        {
            var mat = LitAsset("Optic_Glass");
            mat.SetColor("_BaseColor", new Color(0.04f, 0.07f, 0.09f));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.92f);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }

    // The window of the 1x sights: faintly tinted, see-through. The transparent setup is done by hand because the
    // blend mode is normally applied by the material inspector.
    public static Material OpticWindow
    {
        get
        {
            var mat = LitAsset("Optic_Window");
            mat.SetColor("_BaseColor", new Color(0.55f, 0.75f, 0.72f, 0.16f));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.95f);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }

    public static T Load<T>(string path) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) throw new FileNotFoundException("asset missing: " + path);
        return asset;
    }

    public static void SetupSky()
    {
        var cubemap = Load<Cubemap>(HdriPath);
        var path = MaterialDir + "/Sky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Cubemap"));
            AssetDatabase.CreateAsset(sky, path);
        }
        sky.SetTexture("_Tex", cubemap);
        sky.SetFloat("_Exposure", SkyExposure);
        EditorUtility.SetDirty(sky);

        RenderSettings.skybox = sky;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = cubemap;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        // The ambient probe is not saved with an unbaked scene; this recomputes it when the scene starts.
        new GameObject("SkyAmbient").AddComponent<Game.Gameplay.SkyAmbient>();
        RenderSettings.ambientIntensity = AmbientIntensity;
        RenderSettings.reflectionIntensity = ReflectionIntensity;
        DynamicGI.UpdateEnvironment();
        // Without a lighting bake the sky probe can stay black in batchmode; fall back to a gradient matching the overcast sky.
        var probe = RenderSettings.ambientProbe;
        if (probe[0, 0] + probe[1, 0] + probe[2, 0] < 0.01f)
        {
            Debug.Log("[SceneLook] sky ambient probe is empty, using gradient ambient");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.50f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.20f);
        }
    }

    // Warm shadow-casting key plus a weak cool fill across it so shadowed faces keep some shape.
    public static void AddLights()
    {
        var key = new GameObject("Sun").AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.5f;
        key.color = new Color(1f, 0.95f, 0.88f);
        key.shadows = LightShadows.Soft;
        key.transform.rotation = Quaternion.Euler(48, -35, 0);

        var fill = new GameObject("Fill").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.3f;
        fill.color = new Color(0.75f, 0.85f, 1f);
        fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.Euler(20, 60, 0);
    }

    public static void AddPostVolume(VolumeProfile profile)
    {
        var volume = new GameObject("PostProcess").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
    }

    public static void EnablePost(Camera cam) => cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

    // A static box whose texture repeats every `metersPerTile`, whatever its size (a scaled cube would stretch the pattern).
    public static GameObject WorldBox(string name, Transform parent, Vector3 center, Vector3 size, Material mat, float metersPerTile)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size, metersPerTile);
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.AddComponent<BoxCollider>().size = size;
        go.isStatic = true;
        return go;
    }

    static Mesh BoxMesh(Vector3 size, float metersPerTile)
    {
        var key = $"{size.x:R}x{size.y:R}x{size.z:R}/{metersPerTile:R}";
        if (BoxMeshes.TryGetValue(key, out var cached) && cached != null) return cached;
        var h = size * 0.5f;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        // (normal, right, up) for the six faces
        var faces = new (Vector3 n, Vector3 r, Vector3 u)[]
        {
            (Vector3.up, Vector3.right, Vector3.forward), (Vector3.down, Vector3.right, Vector3.back),
            (Vector3.forward, Vector3.left, Vector3.up), (Vector3.back, Vector3.right, Vector3.up),
            (Vector3.right, Vector3.forward, Vector3.up), (Vector3.left, Vector3.back, Vector3.up),
        };
        foreach (var (n, r, u) in faces)
        {
            float halfR = Mathf.Abs(Vector3.Dot(r, h)), halfU = Mathf.Abs(Vector3.Dot(u, h));
            var c = Vector3.Scale(n, h);
            int first = vertices.Count;
            for (int i = 0; i < 4; i++)
            {
                float sr = i == 1 || i == 2 ? 1f : -1f, su = i >= 2 ? 1f : -1f;
                vertices.Add(c + r * (sr * halfR) + u * (su * halfU));
                normals.Add(n);
                uvs.Add(new Vector2((sr + 1f) * halfR, (su + 1f) * halfU) / metersPerTile);
            }
            triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
        }
        var mesh = new Mesh { name = "Box " + key };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        BoxMeshes[key] = mesh;
        return mesh;
    }

    public static Bounds WorldBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return bounds;
    }

    public static void Shot(Camera cam, string path, Vector3 from, Vector3 lookAt)
    {
        cam.transform.position = from;
        cam.transform.LookAt(lookAt);
        var rt = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
        var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        // The first render after a change comes out with wrong shadowing (lighting data not ready yet), so draw twice and keep the second.
        cam.Render();
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = prevActive;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
    }
}
