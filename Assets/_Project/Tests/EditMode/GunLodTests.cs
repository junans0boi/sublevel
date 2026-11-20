using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    // ADR-541 (#281): every gun model mesh on the player, bot and loot prefabs carries Unity Mesh LODs,
    // so 80 armed characters and the guns on the floor drop to simpler shapes with distance.
    public class GunLodTests
    {
        const string PrefabDir = "Assets/_Project/Prefabs/M1";
        const int MinTriangles = 100; // code-built blocks (magazine wells, stock parts) are 12-triangle cubes

        static int[] GunIds()
        {
            // The gun table lives in the editor-only CharacterKit; read the ids by reflection.
            var kit = System.Type.GetType("CharacterKit, Assembly-CSharp-Editor");
            Assert.IsNotNull(kit, "CharacterKit");
            var weapons = (System.Array)kit.GetField("Weapons").GetValue(null);
            var id = kit.GetNestedType("WeaponModel").GetField("Id");
            return weapons.Cast<object>().Select(w => (int)id.GetValue(w)).ToArray();
        }

        [Test]
        public void EveryGunMesh_OnEveryPrefab_HasMeshLods()
        {
            var ids = GunIds();
            var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.IO.Path.GetDirectoryName(p).Replace('\\', '/') == PrefabDir)
                .Where(p => { var n = System.IO.Path.GetFileName(p); return n.StartsWith("Player") || n.StartsWith("Bot") || n == "LootItem.prefab"; })
                .ToArray();
            Assert.Greater(prefabs.Length, 2, "player, bot and loot prefabs");
            var missing = new System.Text.StringBuilder();
            int checkedMeshes = 0;
            foreach (var path in prefabs)
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var id in ids)
                {
                    var gun = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Weapon" + id);
                    Assert.IsNotNull(gun, $"{path}: Weapon{id}");
                    foreach (var f in gun.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (f.name == "Magazine") continue; // empty submeshes, no Mesh LOD (MagazineKit.AddLods)
                        var m = f.sharedMesh;
                        if (m == null || Enumerable.Range(0, m.subMeshCount).Sum(s => (long)m.GetIndexCount(s)) / 3 < MinTriangles) continue;
                        checkedMeshes++;
                        if (m.lodCount < 2) missing.AppendLine($"{path} Weapon{id}/{f.name}: {m.name} lodCount {m.lodCount}");
                    }
                }
            }
            Assert.Greater(checkedMeshes, ids.Length, "gun meshes found");
            Assert.IsEmpty(missing.ToString(), "gun meshes without Mesh LOD (meta generateMeshLods: 1; tools/unity.sh run MagazineKit.LodSplits)");
        }
    }
}
