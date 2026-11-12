using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Import settings applied to everything under Art/ and Audio/ (ADR-026, PERF_BUDGET).
public class AssetImportRules : AssetPostprocessor
{
    public const string ArtRoot = "Assets/_Project/Art/";
    public const string AudioRoot = "Assets/_Project/Audio/";
    public const int MaxTextureSize = 2048;

    static readonly Regex NormalSuffix = new Regex(@"_(normal|nor|nrm|n)(_gl|_dx)?$", RegexOptions.IgnoreCase);
    static readonly Regex LinearSuffix = new Regex(@"_(roughness|rough|metallic|metal|ao|mask|orm|arm|height|disp)$", RegexOptions.IgnoreCase);

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
    }

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(ArtRoot)) return;
        var importer = (ModelImporter)assetImporter;
        importer.isReadable = false;
        importer.meshCompression = ModelImporterMeshCompression.Medium;
        importer.importCameras = false;
        importer.importLights = false;
        // LODGroups come from the _LOD0/_LOD1 naming convention, which the model importer handles natively.
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
