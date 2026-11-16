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
        const string HdriDir = "Assets/_Project/Art/HDRI/_PipelineTest";
        const string CharacterDir = "Assets/_Project/Art/Characters/_PipelineTest";

        [SetUp]
        public void SetUp() => Directory.CreateDirectory(Dir);

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset("Assets/_Project/Art/_PipelineTest");
            AssetDatabase.DeleteAsset(HdriDir);
            AssetDatabase.DeleteAsset(CharacterDir);
            AssetDatabase.Refresh();
        }

        static TextureImporter ImportTexture(string name, int size, string dir = Dir)
        {
            Directory.CreateDirectory(dir);
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var path = dir + "/" + name;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return (TextureImporter)AssetImporter.GetAtPath(path);
        }

        [Test]
        public void OversizedTexture_IsCappedCompressedAndNotReadable()
        {
            var importer = ImportTexture("big_albedo.png", 4096);
            // Someone loosens the settings by hand; the next import must put them back.
            importer.maxTextureSize = 8192;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            importer = (TextureImporter)AssetImporter.GetAtPath(importer.assetPath);
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

        // Poly Haven names end in the resolution (brick_wall_001_nor_gl_2k); the suffix rules must look past it (ADR-184).
        // Checked through a real import: the Editor scripts are not visible to this assembly.
        [Test]
        public void PolyHavenNormalAndArm_ImportWithRightColourSpace()
        {
            var normal = ImportTexture("brick_wall_001_nor_gl_2k.png", 64);
            Assert.AreEqual(TextureImporterType.NormalMap, normal.textureType);
            var arm = ImportTexture("brick_wall_001_arm_2k.png", 64);
            Assert.IsFalse(arm.sRGBTexture);
            var diff = ImportTexture("brick_wall_001_diff_2k.png", 64);
            Assert.IsTrue(diff.sRGBTexture);
        }

        [Test]
        public void TextureUnderHdri_ImportsAsCubemap()
        {
            var importer = ImportTexture("sky.png", 64, HdriDir);
            Assert.AreEqual(TextureImporterShape.TextureCube, importer.textureShape);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Cubemap>(importer.assetPath));
        }

        [Test]
        public void ModelUnderCharacters_RecalculatesSmoothNormalsAndGetsUvs()
        {
            Directory.CreateDirectory(CharacterDir);
            var path = CharacterDir + "/tri.obj";
            File.WriteAllText(path, "v 0 0 0\nv 1 0 0\nv 0 1 0\nvn 0 0 1\nf 1//1 2//1 3//1\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            Assert.AreEqual(ModelImporterNormals.Calculate, importer.importNormals);
            Assert.AreEqual(75f, importer.normalSmoothingAngle);
            var uvs = AssetDatabase.LoadAssetAtPath<Mesh>(path).uv;
            Assert.AreEqual(3, uvs.Length);
            Assert.AreNotEqual(uvs[0], uvs[1], "a model without texture coordinates should get projected ones");
        }
    }
}
