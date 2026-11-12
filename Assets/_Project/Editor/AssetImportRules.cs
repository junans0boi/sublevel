using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Import settings applied to everything under Art/ and Audio/ (ADR-026, PERF_BUDGET).
public class AssetImportRules : AssetPostprocessor
{
    public const string ArtRoot = "Assets/_Project/Art/";
    public const string AudioRoot = "Assets/_Project/Audio/";
    public const string HdriRoot = ArtRoot + "HDRI/";
    public const string CharacterRoot = ArtRoot + "Characters/";
    public const int MaxTextureSize = 2048;
    public const float CharacterSmoothingAngle = 75f;

    static readonly Regex NormalSuffix = new Regex(@"_(normal|nor|nrm|n)(_gl|_dx)?$", RegexOptions.IgnoreCase);
    static readonly Regex LinearSuffix = new Regex(@"_(roughness|rough|metallic|metal|ao|mask|orm|arm|height|disp)$", RegexOptions.IgnoreCase);

    // Bump when a rule changes so existing assets are reimported.
    public override uint GetVersion() => 4;

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(ArtRoot)) return;
        var importer = (TextureImporter)assetImporter;
        var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

        importer.maxTextureSize = Mathf.Min(importer.maxTextureSize, MaxTextureSize);
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.isReadable = false;
        importer.mipmapEnabled = true;
        if (NormalSuffix.IsMatch(name)) importer.textureType = TextureImporterType.NormalMap;
        else if (LinearSuffix.IsMatch(name)) importer.sRGBTexture = false;

        // Sky panoramas become cubemaps with a specular mip chain so they can drive the skybox and reflections.
        if (assetPath.StartsWith(HdriRoot))
        {
            importer.textureShape = TextureImporterShape.TextureCube;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.cubemapConvolution = TextureImporterCubemapConvolution.Specular;
            importer.SetTextureSettings(settings);
        }
    }

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(ArtRoot)) return;
        var importer = (ModelImporter)assetImporter;
        importer.isReadable = false;
        importer.meshCompression = ModelImporterMeshCompression.Medium;
        importer.importCameras = false;
        importer.importLights = false;
        // Low-poly character packs ship faceted normals; recompute them so limbs shade as rounded forms.
        if (assetPath.StartsWith(CharacterRoot))
        {
            importer.importNormals = ModelImporterNormals.Calculate;
            importer.normalSmoothingSource = ModelImporterNormalSmoothingSource.FromAngle;
            importer.normalSmoothingAngle = CharacterSmoothingAngle;
        }
        // LODGroups come from the _LOD0/_LOD1 naming convention, which the model importer handles natively.
    }

    // Flat-colored character packs ship without texture coordinates; give them box-projected ones so
    // materials can use tiling detail maps. The source file is untouched.
    void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.StartsWith(CharacterRoot)) return;
        foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) EnsureUvs(skinned.sharedMesh);
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) EnsureUvs(filter.sharedMesh);
    }

    static void EnsureUvs(Mesh mesh)
    {
        if (mesh == null) return;
        var existing = mesh.uv;
        for (int i = 1; i < existing.Length; i++)
            if (existing[i] != existing[0]) return; // already unwrapped

        var positions = mesh.vertices;
        var normals = mesh.normals;
        var bounds = mesh.bounds;
        var scale = 1f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 1e-6f);
        var uvs = new Vector2[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            var p = (positions[i] - bounds.min) * scale;
            var n = i < normals.Length ? normals[i] : Vector3.up;
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            uvs[i] = ax >= ay && ax >= az ? new Vector2(p.z, p.y)
                : ay >= az ? new Vector2(p.x, p.z)
                : new Vector2(p.x, p.y);
        }
        mesh.uv = uvs;
    }

    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(AudioRoot)) return;
        var importer = (AudioImporter)assetImporter;
        var settings = importer.defaultSampleSettings;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = 0.6f;
        settings.loadType = AudioClipLoadType.CompressedInMemory;
        importer.defaultSampleSettings = settings;
    }
}
