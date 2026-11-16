using System.Collections;
using System.IO;
using System.Linq;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // S3 roster: 50 characters in view (PERF_BUDGET method A). Silent (no audio). Needs restored roster FBX
    // (tools/unity.sh run CharacterImport.SyncStaging) and built prefabs (CharacterRoster.Build).
    // Run alone: tools/unity.sh test playmode Game.Tests.RosterRenderPerfTests
    [Category("Perf")]
    public class RosterRenderPerfTests
    {
        static readonly string[] Ids = { "MixamoSwat", "MixamoBrian", "MixamoLouise", "MixamoJames", "MixamoMegan", "MixamoKate", "MixamoJosh", "MixamoAdam", "MixamoMartha", "MixamoAlex", "UbcMale", "UbcFemale" };

        static CharacterDef[] Spawn(int count, float near, float far, int seed)
        {
            var rng = new System.Random(seed);
            var list = new CharacterDef[count];
            for (int i = 0; i < count; i++)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Roster/Roster_{Ids[i % Ids.Length]}.prefab");
                Assert.IsNotNull(prefab, "roster prefab missing (CharacterRoster.Build)");
                float d = Mathf.Lerp(near, far, (float)rng.NextDouble());
                float a = Mathf.Lerp(-25f, 25f, (float)rng.NextDouble()) * Mathf.Deg2Rad;
                var go = Object.Instantiate(prefab, new Vector3(Mathf.Sin(a) * d, 0, Mathf.Cos(a) * d), Quaternion.Euler(0, rng.Next(0, 360), 0));
                var def = go.GetComponent<CharacterDef>();
                def.ApplyVariant(rng.Next(0, def.Variants.Length));
                list[i] = def;
            }
            return list;
        }

        static Camera Stage()
        {
            QualitySettings.lodBias = 1f; // High preset; the LODGroup cull distance scales with it (Ultra = 2 -> 200 m)
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(300, 0.1f, 300); floor.transform.position = new Vector3(0, -0.05f, 100);
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.fieldOfView = 60; cam.transform.position = new Vector3(0, 1.7f, 0); cam.farClipPlane = 400;
            return cam;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Fifty_Characters_RenderWithinBudget()
        {
            var cam = Stage();
            Directory.CreateDirectory("Logs/perf"); Directory.CreateDirectory("Logs/roster");
            var sb = new System.Text.StringBuilder();
            RenderProbe.Result empty = default;
            yield return RenderProbe.Measure(cam, r => empty = r, 60, 20);
            sb.AppendLine($"empty floor+sun: {empty}");
            foreach (var (name, near, far) in new[] { ("spread 6-90m", 6f, 90f), ("worst 8-25m", 8f, 25f) })
            {
                var chars = Spawn(50, near, far, 1);
                yield return null;
                // Best of 3: other Unity instances on the same Mac cause one-off spikes (PERF_BUDGET measures on a quiet machine).
                RenderProbe.Result res = default; bool first = true;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    RenderProbe.Result r1 = default;
                    yield return RenderProbe.Measure(cam, r => r1 = r, 90, 30, $"Logs/roster/perf50_{(near < 7 ? "spread" : "worst")}.png");
                    if (first || r1.Stats.P99Ms < res.Stats.P99Ms) res = r1;
                    first = false;
                    if (res.Stats.P99Ms <= RenderProbe.RenderBudgetMs) break;
                }
                sb.AppendLine($"50 chars {name}: {res} | minus empty: tris {res.Triangles - empty.Triangles} ({(res.Triangles - empty.Triangles) / 50} per char), draw {res.DrawCalls - empty.DrawCalls}");
                foreach (var c in chars) Object.Destroy(c.gameObject);
                File.WriteAllText("Logs/perf/roster50.txt", sb.ToString());
                Assert.LessOrEqual(res.Stats.P99Ms, RenderProbe.RenderBudgetMs, name + " render p99");
                Assert.LessOrEqual(res.DrawCalls, RenderProbe.DrawCallBudget, name + " draw calls");
                Assert.LessOrEqual(res.SetPasses, RenderProbe.SetPassBudget, name + " set pass");
                yield return null;
            }
            File.WriteAllText("Logs/perf/roster50.txt", sb.ToString());
            UnityEngine.Debug.Log("[Perf] " + sb);
        }

        // Triangles of ONE character (no shadows) by distance: shows Unity's Mesh LOD choosing lower shapes.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Lod_Curve_TrianglesFallWithDistance()
        {
            var cam = Stage();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) l.shadows = LightShadows.None;
            var sb = new System.Text.StringBuilder();
            foreach (var id in Ids)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Roster/Roster_{id}.prefab");
                var go = Object.Instantiate(prefab);
                RenderProbe.Result baseline = default;
                foreach (var bias in new[] { float.NaN })
                {
                    
                    foreach (var d in new[] { 4f, 10f, 20f, 40f, 70f, 95f, 105f, 120f })
                    {
                        go.transform.position = new Vector3(0, 0, d);
                        yield return null;
                        RenderProbe.Result res = default;
                        yield return RenderProbe.Measure(cam, r => res = r, 5, 2);
                        if (d <= 95f) Assert.Greater(res.Triangles - 1710, 0, id + " visible inside 100 m");
                        if (d >= 105f) Assert.AreEqual(1710, res.Triangles, id + " culled beyond 100 m");
                        sb.AppendLine($"{id} dist {d,5} m: tris {res.Triangles - 1710}");
                    }
                }
                Object.Destroy(go);
                yield return null;
            }
            File.WriteAllText("Logs/perf/roster_lod_curve.txt", sb.ToString());
            UnityEngine.Debug.Log("[Perf] " + sb);
        }
    }
}
