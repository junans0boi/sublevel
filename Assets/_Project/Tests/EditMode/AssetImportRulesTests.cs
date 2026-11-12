using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    // Imports throwaway files under Art/ and checks the settings the pipeline applied.
    public class AssetImportRulesTests
    {
        const string Dir = "Assets/_Project/Art/_PipelineTest/Tex";

        [SetUp]
        public void SetUp() => Directory.CreateDirectory(Dir);

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset("Assets/_Project/Art/_PipelineTest");
            AssetDatabase.Refresh();
        }

        static TextureImporter ImportTexture(string name, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var path = Dir + "/" + name;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return (TextureImporter)AssetImporter.GetAtPath(path);
        }

        [Test]
        public void OversizedTexture_IsCappedCompressedAndNotReadable()
        {
            var importer = ImportTexture("big_albedo.png", 4096);
            Assert.LessOrEqual(importer.maxTextureSize, 2048);
            Assert.IsFalse(importer.isReadable);
            Assert.AreEqual(TextureImporterCompression.Compressed, importer.textureCompression);
            var loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(importer.assetPath);
            Assert.LessOrEqual(loaded.width, 2048);
        }

        [Test]
        public void NormalSuffix_ImportsAsNormalMap()
        {
            var importer = ImportTexture("wall_normal.png", 64);
            Assert.AreEqual(TextureImporterType.NormalMap, importer.textureType);
        }

        [Test]
        public void MaskSuffix_ImportsAsLinear()
        {
            var importer = ImportTexture("wall_roughness.png", 64);
            Assert.IsFalse(importer.sRGBTexture);
        }
    }
}
