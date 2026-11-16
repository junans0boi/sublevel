using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEngine.Profiling;
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

    // tools/unity.sh run-gfx WeaponProbe.RenderRoster -> Logs/lookdev/guns_roster_<n>.png: the guns of CharacterKit.Weapons in id order, 4 per picture, side view, with their kit-bash parts.
    public static void RenderRoster()
    {
        SceneLook.PrepareTextures();
        var profile = RenderSetup.Apply();
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        SceneLook.SetupSky(); SceneLook.AddLights(); SceneLook.AddPostVolume(profile);
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.fieldOfView = 30; SceneLook.EnablePost(cam);
        Directory.CreateDirectory("Logs/lookdev");
        var ids = (System.Environment.GetEnvironmentVariable("GUN_IDS") ?? "4,9,10,11,12,13,31,32,34").Split(',').Select(int.Parse).ToArray();
        var ws = ids.Select(i => CharacterKit.Weapons.First(w => w.Id == i)).ToArray();
        for (int b = 0; b * 4 < ws.Length; b++)
        {
            var group = new GameObject("B" + b);
            for (int k = 0; k < 4 && b * 4 + k < ws.Length; k++)
            {
                var go = CharacterKit.InstantiateWeapon(ws[b * 4 + k]);
                go.transform.SetParent(group.transform); go.transform.position = new Vector3(0, 1f, k * 1.6f);
                Debug.Log("ROSTER k=" + go.transform.localScale.x + " " + b + " " + k + " id" + ws[b * 4 + k].Id);
            }
            SceneLook.Shot(cam, $"Logs/lookdev/guns_roster_{b}.png", new Vector3(7f, 1f, 2.4f), new Vector3(0, 1f, 2.4f));
            Object.DestroyImmediate(group);
        }
        EditorApplication.Exit(0);
    }

    // tools/unity.sh run-gfx WeaponProbe.Fit: per gun, in the aiming pose, how far the butt sticks out behind the wrist and the muzzle in front
    // of it, against the arm (upper arm to wrist) and the character height. A butt behind the wrist by more than the arm reaches into the torso.
    public static void Fit()
    {
        SceneLook.PrepareTextures();
        CharacterKit.Prepare();
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        var slot = new GameObject("Fit").transform;
        var v = CharacterKit.AddTo(slot);
        var wrist = v.Root.GetComponentsInChildren<Transform>().First(t => t.name == "Wrist.R");
        var upper = v.Root.GetComponentsInChildren<Transform>().First(t => t.name == "UpperArm.R");
        float arm = Vector3.Distance(upper.position, wrist.position);
        Debug.Log($"FIT shoulder_behind_wrist={wrist.position.z - upper.position.z:F2} arm_chord={arm:F2} height={SceneLook.WorldBounds(v.Root.gameObject).size.y:F2}");
        foreach (var w in CharacterKit.Weapons.OrderBy(w => w.Id))
        {
            var m = v.WeaponModels[w.Id]; m.SetActive(true);
            var b = SceneLook.WorldBounds(m);
            float rear = wrist.position.z - b.min.z, front = b.max.z - wrist.position.z;
            Debug.Log($"FIT id={w.Id} length={b.size.z:F2} butt_behind_wrist={rear:F2} ({rear / (wrist.position.z - upper.position.z):F2} shoulder) muzzle_ahead={front:F2} up={b.center.y - wrist.position.y:F2}");
            m.SetActive(false);
        }
        EditorApplication.Exit(0);
    }

    // tools/unity.sh run-gfx WeaponProbe.Perf: 21 armed characters in one 1080p view (post on). The editor cannot read the profiler's draw counters
    // in batch mode, so draw calls (one per submesh of every active renderer, before batching), triangles and materials are counted from the
    // scene, and the render time is measured by hand (camera into a 1080p target, one-pixel read-back, p99 over 120 frames), guns hidden and shown.
    public static void Perf()
    {
        SceneLook.PrepareTextures();
        var profile = RenderSetup.Apply();
        CharacterKit.Prepare();
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        SceneLook.SetupSky(); SceneLook.AddLights(); SceneLook.AddPostVolume(profile);
        SceneLook.WorldBox("Floor", null, new Vector3(0, -0.05f, 0), new Vector3(40, 0.1f, 40), SceneLook.Floor, 2.5f);
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.fieldOfView = 45; SceneLook.EnablePost(cam);
        var ws = CharacterKit.Weapons.OrderBy(w => w.Id).ToArray();
        var models = new GameObject[ws.Length];
        for (int i = 0; i < ws.Length; i++)
        {
            var slot = new GameObject("Hold" + ws[i].Id).transform;
            slot.position = new Vector3((i % 7 - 3) * 1.6f, 0, (i / 7) * 2f);
            models[i] = CharacterKit.AddTo(slot).WeaponModels[ws[i].Id];
        }
        cam.transform.position = new Vector3(0, 2.2f, -7f); cam.transform.LookAt(new Vector3(0, 1f, 2f));
        cam.enabled = false;
        var rt = new RenderTexture(1920, 1080, 24); cam.targetTexture = rt;
        var probe = new Texture2D(1, 1, TextureFormat.RGB24, false);
        var seen = new System.Collections.Generic.HashSet<Mesh>(); long bytes = 0;
        foreach (var m in models.SelectMany(g => g.GetComponentsInChildren<MeshFilter>(true))) if (seen.Add(m.sharedMesh)) bytes += Profiler.GetRuntimeMemorySizeLong(m.sharedMesh);
        Debug.Log($"PERF gun meshes={seen.Count} mesh_memory_KB={bytes / 1024}");
        foreach (var hide in new[] { true, false })
        {
            foreach (var g in models) g.SetActive(!hide);
            var rs = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            long draws = 0, tris = 0; var mats = new System.Collections.Generic.HashSet<Material>();
            foreach (var r in rs)
            {
                Mesh mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                int subs = System.Math.Min(mesh.subMeshCount, r.sharedMaterials.Length);
                draws += subs;
                for (int k = 0; k < subs; k++) { tris += mesh.GetSubMesh(k).indexCount / 3; if (r.sharedMaterials[k] != null) mats.Add(r.sharedMaterials[k]); }
            }
            var times = new float[150]; var sw = new System.Diagnostics.Stopwatch();
            for (int i = 0; i < times.Length; i++)
            {
                sw.Restart(); cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt; probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = prev;
                times[i] = (float)sw.Elapsed.TotalMilliseconds;
            }
            var t = times.Skip(30).OrderBy(x => x).ToArray();
            Debug.Log($"PERF {(hide ? "hidden" : "shown ")}: renderers={rs.Length} draw_submeshes={draws} tris={tris} materials={mats.Count} avg_ms={t.Average():F2} p99_ms={t[(int)(t.Length * 0.99)]:F2}");
        }
        EditorApplication.Exit(0);
    }
}
