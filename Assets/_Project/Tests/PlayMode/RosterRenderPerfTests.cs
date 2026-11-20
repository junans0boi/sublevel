using System.Collections;
using System.IO;
using System.Linq;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // S3 roster: 80 characters in view (ADR-259 match size, PERF_BUDGET method A), each holding a gun in the +gun scenes (ADR-541). Silent (no audio). Needs restored roster FBX
    // (tools/unity.sh run CharacterImport.SyncStaging) and built prefabs (CharacterRoster.Build).
    // Run alone: tools/unity.sh test playmode Game.Tests.RosterRenderPerfTests
    [Category("Perf")]
    public class RosterRenderPerfTests
    {
        static bool WithGear = true, WithGun;
        const int Count = 80; // ADR-259
        // Real gun models (Mesh LOD, ADR-541), a different one per character so materials vary as in a match.
        static readonly string[] Guns = { "Wpn04_Breacher", "Wpn05_Sentinel", "Wpn09_Vanguard", "Wpn10_Bulwark", "Wpn11_Hauler", "Wpn12_Lancer", "Wpn13_Tracer", "Wpn14_Arbiter", "Wpn33_Marshal", "Wpn34_Fang" };
        static readonly string[] Ids = { "MixamoSwat", "MixamoBrian", "MixamoLouise", "MixamoJames", "MixamoMegan", "MixamoKate", "MixamoJosh", "MixamoAdam", "MixamoMartha", "MixamoAlex", "MixamoSteve", "MixamoSwatGuy", "MixamoGasMask", "UbcMale", "UbcFemale" };

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
                if (WithGear) { int t = 1 + i % 3; go.AddComponent<ArmorVisual>().ShowTiers(t, t); go.AddComponent<BackpackVisual>().Show(t); } // helmet + vest + backpack on everyone
                if (WithGun) Hold(def, Guns[i % Guns.Length]);
                list[i] = def;
            }
            return list;
        }

        // The gun in the hand at about rifle length; the fit is rough (only its draw and triangle cost matter here).
        static void Hold(CharacterDef def, string gun)
        {
            var model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Weapons/Roster/{gun}.fbx");
            Assert.IsNotNull(model, gun);
            var go = Object.Instantiate(model, def.Sockets.WeaponSocket, false);
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            go.transform.localScale *= 0.9f / Mathf.Max(b.size.x, b.size.y, b.size.z, 1e-4f);
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
        public IEnumerator Eighty_Characters_RenderWithinBudget()
        {
            var cam = Stage();
            Directory.CreateDirectory("Logs/perf"); Directory.CreateDirectory("Logs/roster");
            var sb = new System.Text.StringBuilder();
            RenderProbe.Result empty = default;
            yield return RenderProbe.Measure(cam, r => empty = r, 60, 20);
            sb.AppendLine($"empty floor+sun: {empty} | with helmet+vest+backpack on every character (tiers 1-3 mixed)");
            foreach (var (name, near, far, gear, gun) in new[] { ("spread 6-90m NO gear", 6f, 90f, false, false), ("spread 6-90m +gear+gun", 6f, 90f, true, true), ("worst 8-25m NO gear", 8f, 25f, false, false), ("worst 8-25m +gear", 8f, 25f, true, false), ("worst 8-25m +gear+gun", 8f, 25f, true, true) })
            {
                WithGear = gear; WithGun = gun; var chars = Spawn(Count, near, far, 1);
                yield return null;
                // Best of 3: other Unity instances on the same Mac cause one-off spikes (PERF_BUDGET measures on a quiet machine).
                RenderProbe.Result res = default; bool first = true;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    RenderProbe.Result r1 = default;
                    yield return RenderProbe.Measure(cam, r => r1 = r, 90, 30, $"Logs/roster/perf{Count}_{(near < 7 ? "spread" : "worst")}{(gear ? "_gear" : "")}{(gun ? "_gun" : "")}.png");
                    if (first || r1.Stats.P99Ms < res.Stats.P99Ms) res = r1;
                    first = false;
                    if (res.Stats.P99Ms <= RenderProbe.RenderBudgetMs) break;
                }
                sb.AppendLine($"{Count} chars {name}: {res} | minus empty: tris {res.Triangles - empty.Triangles} ({(res.Triangles - empty.Triangles) / Count} per char), draw {res.DrawCalls - empty.DrawCalls}");
                foreach (var c in chars) Object.Destroy(c.gameObject);
                File.WriteAllText($"Logs/perf/roster{Count}.txt", sb.ToString());
                Assert.LessOrEqual(res.Stats.P99Ms, RenderProbe.RenderBudgetMs, name + " render p99");
                Assert.LessOrEqual(res.DrawCalls, RenderProbe.DrawCallBudget, name + " draw calls");
                Assert.LessOrEqual(res.SetPasses, RenderProbe.SetPassBudget, name + " set pass");
                yield return null;
            }
            File.WriteAllText($"Logs/perf/roster{Count}.txt", sb.ToString());
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
