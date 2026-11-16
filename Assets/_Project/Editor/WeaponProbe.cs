using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// tools/unity.sh run WeaponProbe.Report: bounds (world, after import) and triangle counts of every gun model (ADR-217).
public static class WeaponProbe
{
    public static void Report()
    {
        foreach (var path in Directory.GetFiles("Assets/_Project/Art/Weapons", "*.fbx", SearchOption.AllDirectories).OrderBy(p => p))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            int tris = go.GetComponentsInChildren<MeshFilter>().Sum(m => m.sharedMesh.triangles.Length / 3);
            Debug.Log($"WPN {Path.GetFileName(path)} size=({b.size.x:F2},{b.size.y:F2},{b.size.z:F2}) center=({b.center.x:F2},{b.center.y:F2},{b.center.z:F2}) tris={tris} mats={rs.Sum(r => r.sharedMaterials.Length)}");
        }
    }
}
