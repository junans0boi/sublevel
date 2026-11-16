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

    // tools/unity.sh run-gfx WeaponProbe.RenderHold -> Logs/lookdev/guns_hold_<n>.png: seven armed characters per picture, side view.
    public static void RenderHold()
    {
        SceneLook.PrepareTextures();
        var profile = RenderSetup.Apply();
        CharacterKit.Prepare();
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        SceneLook.SetupSky(); SceneLook.AddLights(); SceneLook.AddPostVolume(profile);
        SceneLook.WorldBox("Floor", null, new Vector3(0, -0.05f, 0), new Vector3(30, 0.1f, 30), SceneLook.Floor, 2.5f);
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.fieldOfView = 30; SceneLook.EnablePost(cam);
        Directory.CreateDirectory("Logs/lookdev");
        var ws = CharacterKit.Weapons.Where(w => w.Id <= 5 || w.Id >= 6).OrderBy(w => w.Id).ToArray();
        for (int b = 0; b * 7 < ws.Length; b++)
        {
            var group = new GameObject("Batch" + b);
            for (int k = 0; k < 7 && b * 7 + k < ws.Length; k++)
            {
                var w = ws[b * 7 + k];
                var slot = new GameObject("Hold" + w.Id).transform;
                slot.SetParent(group.transform); slot.position = new Vector3(0, 0, k * 1.5f);
                CharacterKit.AddTo(slot).WeaponModels[w.Id].SetActive(true);
            }
            SceneLook.Shot(cam, $"Logs/lookdev/guns_hold_{b}.png", new Vector3(12f, 1.3f, 4.5f), new Vector3(0, 1.05f, 4.5f));
            Object.DestroyImmediate(group);
        }
        EditorApplication.Exit(0);
    }
}
