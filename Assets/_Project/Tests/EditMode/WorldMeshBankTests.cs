using System.Linq;
using Game.Core;
using Game.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Tests
{
    // #240: World.unity keeps no generated meshes; WorldMeshBank builds them from World_Meshes.bytes when the scene opens.
    public class WorldMeshBankTests
    {
        const string BlobPath = "Assets/_Project/Prefabs/World/World_Meshes.bytes";

        [Test]
        public void An_opened_World_scene_has_a_mesh_on_every_filter_the_bank_fills()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/World.unity", OpenSceneMode.Single);
            try
            {
                var bank = Object.FindFirstObjectByType<WorldMeshBank>();
                Assert.NotNull(bank, "WorldMeshBank on the World root");
                var records = WorldMeshBlob.Read(AssetDatabase.LoadAssetAtPath<TextAsset>(BlobPath).bytes);
                Assert.Greater(records.Count, 1000);

                var so = new SerializedObject(bank);
                var filters = so.FindProperty("_filters");
                var index = so.FindProperty("_meshIndex");
                Assert.AreEqual(filters.arraySize, index.arraySize);
                Assert.Greater(filters.arraySize, 1000);
                for (int i = 0; i < filters.arraySize; i++)
                {
                    var filter = (MeshFilter)filters.GetArrayElementAtIndex(i).objectReferenceValue;
                    var record = records[index.GetArrayElementAtIndex(i).intValue];
                    Assert.NotNull(filter, "filter " + i);
                    Assert.NotNull(filter.sharedMesh, record.Name);
                    Assert.AreEqual(record.VertexCount, filter.sharedMesh.vertexCount, record.Name);
                    Assert.AreEqual(record.Name, filter.sharedMesh.name);
                }
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }
    }
}
