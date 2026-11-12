using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Builds the M1.5 look-check scene (one character, one weapon, a few PBR materials, HDRI sky)
// and renders screenshots to Logs/lookdev (ADR-035).
// Run with a graphics device: tools/unity.sh run-gfx LookDevBuilder.Build
public static class LookDevBuilder
{
    public const string ScenePath = "Assets/_Project/Scenes/LookDev.unity";
    const string OutDir = "Logs/lookdev";
    const string GeneratedDir = "Assets/_Project/Prefabs/M1_5";
    const string MaterialDir = GeneratedDir + "/Materials";
    const string TextureDir = "Assets/_Project/Art/Materials";
    const string StagingDir = "AssetStaging/acg";
    const string CharacterPath = "Assets/_Project/Art/Characters/QuaterniusModularMen/Swat.fbx";
    const string WeaponPath = "Assets/_Project/Art/Weapons/QuaterniusUltimateGuns/Bullpup_1.fbx";
    const string HdriPath = "Assets/_Project/Art/HDRI/OvercastSoilPuresky/overcast_soil_puresky_2k.hdr";
    const float CharacterHeight = 1.8f;
    const float WeaponLength = 0.75f;
    // The raw HDRI lights the scene about twice too bright; ambient is kept low so the key light sets the contrast.
    const float SkyExposure = 0.7f;
    const float AmbientIntensity = 0.38f;
    const float ReflectionIntensity = 0.5f;

    public static void Build()
    {
        Directory.CreateDirectory(MaterialDir);
        Directory.CreateDirectory(OutDir);
        foreach (var id in new[] { "Concrete015", "Concrete034", "Metal032", "MetalPlates006", "PaintedMetal004" }) PackMask(id);
        MuteColor("PaintedMetal004", 0.45f, 0.5f);
        AssetDatabase.Refresh();
        var postProfile = RenderSetup.Apply();
        RemapCharacterMaterials();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SetupSky();

        var floor = Box("Floor", new Vector3(0, -0.05f, 0), new Vector3(20, 0.1f, 20), Pbr("Concrete015", 8f, smoothness: 0.7f));
        Box("BackWall", new Vector3(0, 1.5f, 4f), new Vector3(12, 3, 0.3f), Pbr("Concrete034", 3f));
        Box("SideWall", new Vector3(-4, 1.5f, 1f), new Vector3(0.3f, 3, 6), Pbr("Metal032", 2f));
        // The plate pattern is kept small and its relief and gloss toned down so it reads as textured metal, not as big triangles.
        var plates = Pbr("MetalPlates006", 4f, bumpScale: 0.5f, smoothness: 0.6f);
        Box("Pillar", new Vector3(-2.2f, 1.5f, 3.4f), new Vector3(0.6f, 3, 0.6f), plates);
        Box("CrateA", new Vector3(1.6f, 0.5f, 0.6f), Vector3.one, plates);
        Box("CrateB", new Vector3(2.6f, 0.35f, 1.6f), new Vector3(1.4f, 0.7f, 1.0f), Pbr("PaintedMetal004", 1f));
        floor.isStatic = true;

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.5f;
        sun.color = new Color(1f, 0.95f, 0.88f);
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(48, -35, 0);

        // Weak cool fill across the key so shadowed faces keep some shape; aimed away from the cameras to avoid floor glare.
        var fill = new GameObject("Fill").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.3f;
        fill.color = new Color(0.75f, 0.85f, 1f);
        fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.Euler(20, 60, 0);

        var volume = new GameObject("PostProcess").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = postProfile;

        var character = PlaceCharacter(new Vector3(0, 0, 0), 120f);
        HoldWeapon(character);
        var display = PlaceWeapon("WeaponDisplay");
        display.transform.rotation = Quaternion.Euler(0, 90, 0) * display.transform.rotation;
        display.transform.position += new Vector3(1.6f, 1.0f, 0.6f) - WorldBounds(display).center + Vector3.up * WorldBounds(display).extents.y;

        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.fieldOfView = 50;
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

        var wideFrom = new Vector3(2.5f, 1.6f, -5.5f);
        var wideAt = new Vector3(0.3f, 1.1f, 0.5f);
        Shot(cam, "lookdev_wide.png", wideFrom, wideAt);
        Shot(cam, "lookdev_character.png", new Vector3(0.6f, 1.45f, -2.4f), new Vector3(0, 1.1f, 0));
        Shot(cam, "lookdev_weapon.png", new Vector3(1.9f, 1.45f, -0.5f), WorldBounds(display).center);
        cam.transform.position = wideFrom;
        cam.transform.LookAt(wideAt);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[LookDevBuilder] Saved " + ScenePath + " and screenshots in " + OutDir);
        EditorApplication.Exit(0);
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

    // Tileable soft noise: RGB darkens the base color a little, A varies smoothness. Self-made, so it lives outside Art/ (ADR-027).
    static Texture2D FabricNoise()
    {
        const string path = GeneratedDir + "/Textures/FabricNoise.png";
        if (!File.Exists(path))
        {
            const int size = 256;
            var random = new System.Random(7);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            var sum = new float[size * size];
            foreach (var cells in new[] { 8, 32, 128 })
            {
                var lattice = new float[cells * cells];
                for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)random.NextDouble();
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float fx = x * cells / (float)size, fy = y * cells / (float)size;
                        int x0 = (int)fx, y0 = (int)fy, x1 = (x0 + 1) % cells, y1 = (y0 + 1) % cells;
                        float tx = Mathf.SmoothStep(0, 1, fx - x0), ty = Mathf.SmoothStep(0, 1, fy - y0);
                        sum[y * size + x] += Mathf.Lerp(
                            Mathf.Lerp(lattice[y0 * cells + x0], lattice[y0 * cells + x1], tx),
                            Mathf.Lerp(lattice[y1 * cells + x0], lattice[y1 * cells + x1], tx), ty) / 3f;
                    }
            }
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                var shade = Mathf.Lerp(0.78f, 1f, sum[i]);
                pixels[i] = new Color(shade, shade, shade, Mathf.Lerp(0.5f, 1f, sum[i]));
            }
            tex.SetPixels(pixels);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
        }
        return Load<Texture2D>(path);
    }

    // Replaces the flat colors embedded in the character FBX with project materials: slightly varied
    // colors, per-surface roughness and a soft noise so large areas are not one flat tone.
    static void RemapCharacterMaterials()
    {
        var noise = FabricNoise();
        var looks = new (string source, Color color, float smoothness)[]
        {
            ("Swat", new Color(0.33f, 0.37f, 0.43f), 0.18f),        // uniform fabric
            ("Swat_Black", new Color(0.13f, 0.13f, 0.14f), 0.38f), // vest, pads, boots
            ("Visor", new Color(0.03f, 0.035f, 0.04f), 0.9f),
            ("Skin", new Color(0.80f, 0.62f, 0.50f), 0.45f),
        };
        var importer = (ModelImporter)AssetImporter.GetAtPath(CharacterPath);
        foreach (var (source, color, smoothness) in looks)
        {
            var path = $"{MaterialDir}/Character_{source}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", noise);
            mat.SetTextureScale("_BaseMap", new Vector2(3f, 3f));
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_SmoothnessTextureChannel", 1f);
            mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            EditorUtility.SetDirty(mat);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), source), mat);
        }
        AssetDatabase.SaveAssets();
        importer.SaveAndReimport();
    }

    static Texture2D LoadStaged(string path)
    {
        if (!File.Exists(path)) return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        tex.LoadImage(File.ReadAllBytes(path));
        return tex;
    }

    static Material Pbr(string id, float tiling, string name = null, float bumpScale = 1f, float smoothness = 1f)
    {
        var path = $"{MaterialDir}/{name ?? id}.mat";
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
        mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static T Load<T>(string path) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) throw new FileNotFoundException("asset missing: " + path);
        return asset;
    }

    static void SetupSky()
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
        RenderSettings.ambientIntensity = AmbientIntensity;
        RenderSettings.reflectionIntensity = ReflectionIntensity;
        DynamicGI.UpdateEnvironment();
        // Without a lighting bake the sky probe can stay black in batchmode; fall back to a gradient matching the overcast sky.
        var probe = RenderSettings.ambientProbe;
        if (probe[0, 0] + probe[1, 0] + probe[2, 0] < 0.01f)
        {
            Debug.Log("[LookDevBuilder] sky ambient probe is empty, using gradient ambient");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.50f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.20f);
        }
    }

    static GameObject Box(string name, Vector3 center, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = center;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Bounds WorldBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return bounds;
    }

    static GameObject PlaceCharacter(Vector3 position, float yaw)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(CharacterPath));
        go.name = "Character";
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            foreach (var m in r.sharedMaterials)
                if (!AssetDatabase.GetAssetPath(m).StartsWith(MaterialDir))
                    throw new System.Exception($"character material not remapped: {r.name}/{m.name}");
            var uvs = new System.Collections.Generic.List<Vector2>();
            r.sharedMesh.GetUVs(0, uvs);
            var min = uvs.Count > 0 ? uvs[0] : Vector2.zero;
            var max = min;
            foreach (var uv in uvs) { min = Vector2.Min(min, uv); max = Vector2.Max(max, uv); }
            Debug.Log($"[LookDevBuilder] {r.name}: {uvs.Count} uvs, range {min} .. {max}");
        }
        var clips = AssetDatabase.LoadAllAssetsAtPath(CharacterPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToArray();
        Debug.Log("[LookDevBuilder] clips: " + string.Join(", ", clips.Select(c => c.name)));
        var pose = clips.First(c => c.name.EndsWith("Idle_Gun_Pointing"));
        pose.SampleAnimation(go, 0f);

        var height = WorldBounds(go).size.y;
        go.transform.localScale *= CharacterHeight / height;
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        go.transform.position += Vector3.up * (position.y - WorldBounds(go).min.y);
        Debug.Log($"[LookDevBuilder] character raw height {height:F3}, bounds {WorldBounds(go)}");
        return go;
    }

    // Instantiates the weapon scaled to WeaponLength with its muzzle along world +Z (the pack models point the muzzle along -X).
    static GameObject PlaceWeapon(string name)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(WeaponPath));
        go.name = name;
        var size = WorldBounds(go).size;
        Debug.Log($"[LookDevBuilder] weapon raw bounds {WorldBounds(go)}");
        if (size.x > size.z) go.transform.rotation = Quaternion.Euler(0, 90, 0) * go.transform.rotation;
        go.transform.localScale *= WeaponLength / Mathf.Max(size.x, size.z);
        return go;
    }

    static void HoldWeapon(GameObject character)
    {
        var hand = character.GetComponentsInChildren<Transform>().First(t => t.name == "Wrist.R");
        var weapon = PlaceWeapon("Weapon");
        weapon.transform.rotation = character.transform.rotation * weapon.transform.rotation;
        var forward = character.transform.forward;
        var target = hand.position + forward * 0.09f + Vector3.up * 0.01f;
        weapon.transform.position += target - WorldBounds(weapon).center;
        weapon.transform.SetParent(hand, true);
        Debug.Log($"[LookDevBuilder] hand at {hand.position}, forward {forward}");
    }

    static void Shot(Camera cam, string file, Vector3 from, Vector3 lookAt)
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
        File.WriteAllBytes(Path.Combine(OutDir, file), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
    }
}
