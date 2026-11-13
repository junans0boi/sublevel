using System.IO;
using Game.Core;
using Game.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// #114: every kind of pickup from the real LootItem prefab, spawned a little above a floor, as the game shows it.
// tools/unity.sh run-gfx LootCapture.Shoot -> Logs/lookdev/loot_*.png
public static class LootCapture
{
    public static void Shoot()
    {
        int code = 0;
        try
        {
            SceneLook.PrepareTextures();
            var profile = RenderSetup.Apply();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneLook.SetupSky();
            SceneLook.AddLights();
            SceneLook.AddPostVolume(profile);
            SceneLook.WorldBox("Floor", null, new Vector3(0, -0.05f, 0), new Vector3(30, 0.1f, 30), SceneLook.Floor, 2.5f);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.fieldOfView = 40;
            cam.nearClipPlane = 0.05f;
            SceneLook.EnablePost(cam);
            Physics.SyncTransforms();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestBoxBuilder.PrefabDir + "/LootItem.prefab");
            var rows = new[]
            {
                new[] { W(1), W(2), W(3), W(4), W(5), W(21), W(22), W(23), W(24) },
                new[] { A(AmmoType.Pistol9), A(AmmoType.Rifle556), A(AmmoType.Rifle762), A(AmmoType.Shotgun12), K(LootKind.Medkit), K(LootKind.FirstAidKit), K(LootKind.Bandage), K(LootKind.Grenade), K(LootKind.Smoke) },
                new[] { T(LootKind.Helmet, 1), T(LootKind.Helmet, 2), T(LootKind.Helmet, 3), T(LootKind.Vest, 1), T(LootKind.Vest, 2), T(LootKind.Vest, 3), K(LootKind.Key), K(LootKind.Jerrycan), O(1), O(3), O(6) },
                new[] { T(LootKind.Backpack, 1), T(LootKind.Backpack, 2), T(LootKind.Backpack, 3), T(LootKind.Vest, 2), T(LootKind.Helmet, 2) }, // ADR-091
                new[] { K(LootKind.Drink), K(LootKind.Pill), K(LootKind.Syringe), K(LootKind.Flash), K(LootKind.Molotov) }, // ADR-099/102/103
            };
            const float spacing = 0.75f;
            for (int r = 0; r < rows.Length; r++)
            {
                for (int i = 0; i < rows[r].Length; i++)
                {
                    var e = rows[r][i];
                    var go = Object.Instantiate(prefab); // not a prefab instance: the view reparents its children, as at run time
                    // Above the floor like a dropped item; the view sets it down.
                    go.transform.position = new Vector3((i - (rows[r].Length - 1) * 0.5f) * spacing, 0.3f, r * 1.1f);
                    // Edit mode runs no Awake: build the code-built melee meshes as waking would.
                    foreach (var melee in go.GetComponentsInChildren<MeleeModel>(true)) melee.Build();
                    var view = go.GetComponent<LootView>();
                    view.Show(e.Kind, e.WeaponId, e.Ammo, e.Tier, e.Optic);
                    var b = SceneLook.WorldBounds(go);
                    Debug.Log($"[LootCapture] {e.Kind} w{e.WeaponId} a{e.Ammo} t{e.Tier} o{e.Optic}: mesh {(view.ShownMesh != null ? view.ShownMesh.name : "-")}, bottom {b.min.y:F3}, size {b.size}");
                }
            }
            Directory.CreateDirectory("Logs/lookdev");
            SceneLook.Shot(cam, "Logs/lookdev/loot_all.png", new Vector3(0, 3.6f, -3.6f), new Vector3(0, 0, 1.1f));
            // Close-ups: four pickups at a time.
            for (int r = 0; r < rows.Length; r++)
                for (int first = 0; first < rows[r].Length; first += 4)
                {
                    int last = Mathf.Min(first + 3, rows[r].Length - 1);
                    float x = ((first + last) * 0.5f - (rows[r].Length - 1) * 0.5f) * spacing;
                    SceneLook.Shot(cam, $"Logs/lookdev/loot_row{r}_{first / 4}.png", new Vector3(x, 1.1f, r * 1.1f - 1.9f), new Vector3(x, 0.05f, r * 1.1f));
                }
            // The backpacks from the front (ADR-091).
            SceneLook.Shot(cam, "Logs/lookdev/loot_backpack_front.png", new Vector3(-0.75f, 1.0f, 3 * 1.1f + 1.7f), new Vector3(-0.75f, 0.15f, 3 * 1.1f));
            // As a standing player sees one close by.
            SceneLook.Shot(cam, "Logs/lookdev/loot_eye.png", new Vector3(-1.5f, 1.6f, -1.2f), new Vector3(-1.5f, 0, 1.1f));
        }
        catch (System.Exception e)
        {
            Debug.LogError("[LootCapture] failed: " + e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    // ADR-091: the worn backpack on the player character, from behind, the side and the front.
    // tools/unity.sh run-gfx LootCapture.ShootWornBackpack -> Logs/lookdev/backpack_worn_*.png
    public static void ShootWornBackpack()
    {
        int code = 0;
        try
        {
            SceneLook.PrepareTextures();
            var profile = RenderSetup.Apply();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneLook.SetupSky();
            SceneLook.AddLights();
            SceneLook.AddPostVolume(profile);
            SceneLook.WorldBox("Floor", null, new Vector3(0, -0.05f, 0), new Vector3(30, 0.1f, 30), SceneLook.Floor, 2.5f);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.fieldOfView = 40;
            cam.nearClipPlane = 0.05f;
            SceneLook.EnablePost(cam);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestBoxBuilder.PrefabDir + "/Player.prefab");
            for (int tier = 1; tier <= 3; tier++)
            {
                var go = Object.Instantiate(prefab, new Vector3((tier - 2) * 1.2f, 0f, 0f), Quaternion.identity);
                var visual = go.AddComponent<BackpackVisual>();
                visual.Show(tier);
                var b = visual.Renderer.bounds;
                Debug.Log($"[LootCapture] worn backpack {tier}: bounds {b.min:F2}..{b.max:F2}");
            }
            Directory.CreateDirectory("Logs/lookdev");
            SceneLook.Shot(cam, "Logs/lookdev/backpack_worn_back.png", new Vector3(0f, 1.6f, -4.2f), new Vector3(0f, 1.0f, 0f));
            SceneLook.Shot(cam, "Logs/lookdev/backpack_worn_side.png", new Vector3(4.2f, 1.4f, -0.4f), new Vector3(0f, 1.0f, 0f));
            SceneLook.Shot(cam, "Logs/lookdev/backpack_worn_front.png", new Vector3(0.6f, 1.6f, 4.2f), new Vector3(0f, 1.0f, 0f));
        }
        catch (System.Exception e)
        {
            Debug.LogError("[LootCapture] failed: " + e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    static LootEntry W(int id) => new LootEntry { Kind = LootKind.Weapon, WeaponId = id };
    static LootEntry A(AmmoType ammo) => new LootEntry { Kind = LootKind.Ammo, Ammo = ammo };
    static LootEntry K(LootKind kind) => new LootEntry { Kind = kind };
    static LootEntry T(LootKind kind, int tier) => new LootEntry { Kind = kind, Tier = tier };
    static LootEntry O(int optic) => new LootEntry { Kind = LootKind.Optic, Optic = optic };
}
