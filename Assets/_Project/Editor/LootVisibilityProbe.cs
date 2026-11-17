using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// interact track: in the built World scene, how many loot points can a player standing within reach actually see?
// For every point: embedded in a collider? no standing spot nearby? seen from how many of the standing spots (eye 1.6 m,
// ray to the item's top)? tools/unity.sh run LootVisibilityProbe.Run -> docs/tracks/interact_loot_visibility.txt
public static class LootVisibilityProbe
{
    const float EyeHeight = 1.6f, ItemTop = 0.12f;
    static readonly float[] Radii = { 1.2f, 2.0f, 2.8f };
    const int Angles = 8;

    public static void Run()
    {
        int code = 0;
        try
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/World.unity", OpenSceneMode.Single);
            Physics.SyncTransforms();
            var spawns = Object.FindFirstObjectByType<MatchDirector>().LootSpawns;
            var report = new StringBuilder();
            var byClass = new SortedDictionary<string, int[]>();
            int[] total = new int[6]; // ok, poor, hidden, noStand, embedded, noFloor
            var worst = new List<string>();
            foreach (var t in spawns)
            {
                var p = t.position;
                string role = t.name.StartsWith("LootSpawn_") && t.name.Length > 10 ? t.name.Substring(10, 1) : "?";
                int cls = Classify(p, out float floorY, out int valid, out int seen, out string why);
                total[cls]++;
                if (!byClass.TryGetValue(role, out var row)) byClass[role] = row = new int[6];
                row[cls]++;
                if (cls >= 2 && worst.Count < 40) worst.Add($"{Names[cls]} {t.name} {p:F1} floor {floorY:F2} stand {seen}/{valid} {why}");
            }
            report.AppendLine($"points {spawns.Length}");
            report.AppendLine($"ok(>=25% of spots see it) {total[0]}  poor(<25%) {total[1]}  hidden(0 spots) {total[2]}  noStand {total[3]}  embedded {total[4]}  noFloor {total[5]}");
            float visible = 100f * (total[0] + total[1]) / Mathf.Max(1, spawns.Length);
            report.AppendLine($"seen from at least one spot: {visible:F1}%   ok only: {100f * total[0] / Mathf.Max(1, spawns.Length):F1}%");
            report.AppendLine("loot height over the room floor (m) 0-.2 .2-.5 .5-.8 .8-1.1 1.1-1.4 1.4-1.8 >=1.8: " + string.Join(" ", RiseBuckets));
            foreach (var kv in byClass) report.AppendLine($"role {kv.Key}: ok {kv.Value[0]} poor {kv.Value[1]} hidden {kv.Value[2]} noStand {kv.Value[3]} embedded {kv.Value[4]} noFloor {kv.Value[5]}");
            foreach (var w in worst) report.AppendLine(w);
            File.WriteAllText("docs/tracks/interact_loot_visibility.txt", report.ToString());
            Debug.Log(report.ToString());
        }
        catch (System.Exception e) { Debug.LogError(e); code = 1; }
        EditorApplication.Exit(code);
    }

    // Real World scene, real LootItem prefab shown as the game shows it, from a standing player's eye 2 m away.
    // tools/unity.sh run-gfx LootVisibilityProbe.Shoot [-> Logs/interact/world_*.png]
    public static void Shoot()
    {
        int code = 0;
        try
        {
            SceneLook.PrepareTextures();
            RenderSetup.Apply();
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/World.unity", OpenSceneMode.Single);
            Physics.SyncTransforms();
            var spawns = Object.FindFirstObjectByType<MatchDirector>().LootSpawns;
            var cam = new GameObject("Probe Camera") { tag = "Untagged" }.AddComponent<Camera>();
            cam.fieldOfView = 70; cam.nearClipPlane = 0.05f; cam.farClipPlane = 300f;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestBoxBuilder.PrefabDir + "/LootItem.prefab");
            Directory.CreateDirectory("Logs/interact");
            int taken = 0;
            for (int i = 3; i < spawns.Length && taken < 14; i += 89)
            {
                var t = spawns[i];
                string role = t.name.Length > 10 ? t.name.Substring(10, 1) : "?";
                LootEntry e = role == "W" ? new LootEntry { Kind = LootKind.Weapon, WeaponId = 1 + taken % 5 }
                    : role == "A" ? new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556 }
                    : role == "H" ? new LootEntry { Kind = LootKind.Medkit }
                    : role == "G" ? new LootEntry { Kind = LootKind.Helmet, Tier = 2 }
                    : new LootEntry { Kind = LootKind.Bandage };
                var go = Object.Instantiate(prefab, t.position, Quaternion.identity);
                foreach (var melee in go.GetComponentsInChildren<MeleeModel>(true)) melee.Build();
                go.GetComponent<LootView>().Show(e.Kind, e.WeaponId, e.Ammo, e.Tier, e.Optic);
                Physics.SyncTransforms();
                var b = SceneLook.WorldBounds(go);
                var aim = new Vector3(t.position.x, b.center.y, t.position.z);
                Vector3? from = null;
                for (int r = 0; r < 3 && from == null; r++)
                    for (int a = 0; a < 8 && from == null; a++)
                    {
                        float ang = a * Mathf.PI / 4f, rad = 2f + 0.5f * r;
                        if (!Stand(t.position.x + Mathf.Cos(ang) * rad, t.position.z + Mathf.Sin(ang) * rad, t.position.y - 0.3f, out var feet)) continue;
                        var eye = feet + Vector3.up * EyeHeight;
                        if (!Physics.Linecast(eye, aim, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || hit.collider.transform.IsChildOf(go.transform)) from = eye;
                    }
                if (from != null)
                {
                    var marker = go.GetComponent<LootMarker>();
                    if (marker != null) marker.Tick(from.Value, taken % 3 == 0);
                    SceneLook.Shot(cam, $"Logs/interact/world_{taken:D2}_{role}_{e.Kind}.png", from.Value, aim);
                    Debug.Log($"[LootShoot] {taken} {t.name} {e.Kind} bounds {b.size:F2} eye {from.Value} dist {Vector3.Distance(from.Value, aim):F1}");
                    taken++;
                }
                Object.DestroyImmediate(go);
            }
        }
        catch (System.Exception e) { Debug.LogError(e); code = 1; }
        EditorApplication.Exit(code);
    }

    static readonly string[] Names = { "ok", "poor", "hidden", "noStand", "embedded", "noFloor" };

    static readonly RaycastHit[] Down = new RaycastHit[16];

    public static readonly int[] RiseBuckets = new int[7]; // 0-.2 .2-.5 .5-.8 .8-1.1 1.1-1.4 1.4-1.8 >=1.8 (loot height over where a player stands)

    // The lowest surface a player's capsule fits on within [floorY-1.3, floorY+0.1] at (x, z): the room floor under a table.
    static bool Stand(float x, float z, float floorY, out Vector3 feet)
    {
        feet = default;
        int n = Physics.RaycastNonAlloc(new Vector3(x, floorY + 0.1f, z), Vector3.down, Down, 1.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (Down[i].normal.y < 0.7f || Down[i].point.y >= best) continue;
            var f = Down[i].point;
            if (Physics.CheckCapsule(f + Vector3.up * 0.4f, f + Vector3.up * 1.7f, 0.28f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            best = f.y; feet = f;
        }
        return best < float.MaxValue;
    }

    // 0 ok, 1 poor, 2 hidden, 3 no standing spot, 4 embedded, 5 no floor.
    static int Classify(Vector3 p, out float floorY, out int valid, out int seen, out string why)
    {
        valid = seen = 0; floorY = p.y; why = "";
        if (!Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out var down, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return 5;
        floorY = down.point.y;
        var aim = new Vector3(p.x, floorY + ItemTop, p.z);
        var hits = Physics.OverlapSphere(aim + Vector3.up * 0.03f, 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (hits.Length > 0) { why = hits[0].name + "/" + hits[0].transform.root.name; return 4; }
        float lowest = float.MaxValue;
        for (int r = 0; r < Radii.Length; r++)
            for (int a = 0; a < Angles; a++)
            {
                float ang = a * Mathf.PI * 2f / Angles;
                if (!Stand(p.x + Mathf.Cos(ang) * Radii[r], p.z + Mathf.Sin(ang) * Radii[r], floorY, out var feet)) continue;
                valid++;
                lowest = Mathf.Min(lowest, feet.y);
                var eye = feet + Vector3.up * EyeHeight;
                if (!Physics.Linecast(eye, aim, out var block, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) seen++;
                else if (why == "") why = block.collider.name + "/" + block.collider.transform.root.name;
            }
        if (valid == 0) return 3;
        float rise = floorY - lowest;
        RiseBuckets[rise < 0.2f ? 0 : rise < 0.5f ? 1 : rise < 0.8f ? 2 : rise < 1.1f ? 3 : rise < 1.4f ? 4 : rise < 1.8f ? 5 : 6]++;
        if (seen == 0) return 2;
        return seen * 4 < valid ? 1 : 0;
    }
}
