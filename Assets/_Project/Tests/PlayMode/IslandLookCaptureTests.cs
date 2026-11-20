using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #319 / ADR-543 review: six clear-noon views of the island (R1 road with the sun behind and against it, a shaded wall
    // at 10 m, an R0 field to 150-600 m, a school/shop interior floor, a house doorway looking out), each rendered by
    // RenderProbe (1080p, post on) so the same run gives the PERF_BUDGET A numbers. Run on purpose with
    //   LOOK_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.IslandLookCaptureTests
    [Explicit, Category("Capture")]
    public class IslandLookCaptureTests
    {
        sealed class Idle : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        static readonly Vector3 TownC = new Vector3(960f, 0f, 1000f);   // 청람읍 (IslandData town C, R1)

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            EnvironmentView.ForceTime = null;
            EnvironmentView.ForceWeather = null;
            InteriorLights.Focus = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static bool Ground(Vector3 at, out RaycastHit hit) =>
            Physics.Raycast(new Vector3(at.x, 400f, at.z), Vector3.down, out hit, 800f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        static bool IsRoad(Vector3 at) =>
            Ground(at, out var hit) && hit.collider is TerrainCollider && VehicleSpawner.OnRoad(at.x, at.z);   // roads are painted on the terrain

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }

        static Vector3 Dir(int i, int n) => Quaternion.Euler(0f, 360f * i / n, 0f) * Vector3.forward;

        // Buildings of the generated lots: `Lot<i>_<Kind>` with an InteriorBox child.
        static List<(Transform building, Transform box, string kind)> Lots() =>
            UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(t => t.name.StartsWith("Lot", StringComparison.Ordinal) && t.name.Contains("_") && t.Find("InteriorBox") != null)
                .Select(t => (t, t.Find("InteriorBox"), t.name.Substring(t.name.IndexOf('_') + 1)))
                .OrderBy(l => Vector3.Distance(Flat0(l.Item1.position), TownC))
                .ToList();

        static Vector3 Flat0(Vector3 v) => new Vector3(v.x, 0f, v.z);

        [UnityTest, Timeout(900000)]
        public IEnumerator CaptureIslandLook()
        {
            string folder = Environment.GetEnvironmentVariable("LOOK_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/i319";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            PlayerController.InputOverride = new Idle();
            EnvironmentView.ForceTime = DayTimeChoice.Day;
            EnvironmentView.ForceWeather = WeatherChoice.Clear;
            SceneManager.LoadScene("World");
            float end = Time.realtimeSinceStartup + 90f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(1f);
            var view = EnvironmentView.Current;
            Assert.IsNotNull(view, "the World has an EnvironmentView");
            view.Set(WorldSky.DayHour, WeatherKind.Clear);
            var world = WorldInfo.Current;
            var pc = PlayerController.Local;
            var sunGo = GameObject.Find("Sun");
            Assert.IsNotNull(sunGo, "Sun");
            var sun = Flat(sunGo.transform.forward);   // the way the light travels, flat: looking along it puts the sun behind
            var lots = Lots();
            Debug.Log($"[IslandLook] hour {view.Hour} sun fwd {sunGo.transform.forward} lots {lots.Count}");

            var cam = new GameObject("LookCamera").AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = pc.Camera.farClipPlane;   // the view-distance setting the player has (700 m on 높음, the World's own)
            cam.fieldOfView = 60f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.GetUniversalAdditionalCameraData().SetRenderer(world.RendererIndex);
            InteriorLights.Focus = cam.transform;

            var report = new List<string>();
            bool breakdown = Environment.GetEnvironmentVariable("LOOK_BREAKDOWN") == "1";
            IEnumerator Shot(string name, Vector3 eye, Vector3 target)
            {
                cam.transform.position = eye;
                cam.transform.LookAt(target);
                pc.Teleport(eye - Flat(cam.transform.forward) * 1.2f - Vector3.up * 1.6f);   // just behind the view: its gear stays out of frame
                yield return new WaitForSeconds(1.2f);   // streaming, interior grade fade (0.35 s), reflection fade
                foreach (var r in Combatant.LocalPlayer.GetComponentsInChildren<Renderer>()) r.enabled = false;
                RenderProbe.Result result = default;
                yield return RenderProbe.Measure(cam, r => result = r, screenshotPath: Path.Combine(folder, name + ".png"));
                var interior = UnityEngine.Object.FindFirstObjectByType<InteriorLights>();
                string line = $"{name}: {result} eye {eye} grade {(interior != null ? interior.GradeWeight : -1f):F2} refl {RenderSettings.reflectionIntensity:F2}";
                report.Add($"\"{name}\":{result.ToJson()}");
                Debug.Log("[IslandLook] " + line);
                if (breakdown) yield return Breakdown(name);
            }

            // #452: what the draws are made of. LOOK_BREAKDOWN=1 re-renders the view with one thing switched off at a time.
            IEnumerator Breakdown(string name)
            {
                RenderProbe.Result r = default;
                IEnumerator Probe(string what)
                {
                    yield return RenderProbe.Measure(cam, x => r = x, frames: 6, warmup: 3);
                    Debug.Log($"[IslandLook] breakdown {name} {what}: draw {r.DrawCalls} setpass {r.SetPasses} tris {r.Triangles}");
                }
                yield return Probe("all");
                pc.Camera.enabled = false;
                yield return Probe("player camera off");
                var sunLight = sunGo.GetComponent<Light>();
                var shadows = sunLight.shadows;
                sunLight.shadows = LightShadows.None;
                yield return Probe("player camera off, sun shadows off");
                sunLight.shadows = shadows;
                var worldRoot = GameObject.Find("World").transform;
                string Key(Transform t)
                {
                    var path = new List<string>();
                    for (var x = t; x != null && x != worldRoot; x = x.parent) path.Insert(0, new string(x.name.TakeWhile(ch => !char.IsDigit(ch) && ch != '_').ToArray()));
                    return path.Count == 0 ? "(outside World)" : path.Count == 1 ? path[0] : path[0] + "/" + path[path.Count - 1];
                }
                var groups = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(x => x.enabled && x.gameObject.activeInHierarchy)
                    .GroupBy(x => Key(x.transform)).OrderBy(g => g.Key).ToList();
                foreach (var g in groups)
                {
                    foreach (var x in g) if (x != null) x.forceRenderingOff = true;
                    yield return Probe($"without {g.Key} ({g.Count()} renderers)");
                    foreach (var x in g) if (x != null) x.forceRenderingOff = false;
                }
                foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None)) t.drawHeightmap = false;
                yield return Probe("without terrain heightmap");
                foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None)) t.drawHeightmap = true;
                int mask = cam.cullingMask;
                for (int layer = 0; layer < 32; layer++)
                {
                    if ((mask & (1 << layer)) == 0 || string.IsNullOrEmpty(LayerMask.LayerToName(layer))) continue;
                    cam.cullingMask = mask & ~(1 << layer);
                    yield return Probe($"without layer {LayerMask.LayerToName(layer)}");
                }
                cam.cullingMask = mask;
                pc.Camera.enabled = true;
            }

            // 1-2. An R1 road near the town centre that runs along the sun, both ways.
            Vector3 roadAt = default, roadDir = default;
            float best = -2f;
            for (float r = 0f; r <= 240f && best < 0.9f; r += 6f)
                for (int a = 0; a < 24; a++)
                {
                    var p = TownC + Dir(a, 24) * r;
                    if (!IsRoad(p)) continue;
                    for (int d = 0; d < 16; d++)
                    {
                        var dir = Dir(d, 16);
                        float dot = Vector3.Dot(dir, sun);
                        if (dot <= best) continue;
                        if (IsRoad(p + dir * 15f) && IsRoad(p + dir * 30f) && IsRoad(p - dir * 15f) && IsRoad(p - dir * 30f))
                        { best = dot; roadAt = p; roadDir = dir; }
                    }
                }
            Assert.Greater(best, -2f, "an R1 road near the town centre");
            Ground(roadAt, out var roadHit);
            var eye1 = roadHit.point + Vector3.up * 1.7f;
            yield return Shot("look_01_r1_road_sun_behind", eye1, eye1 + roadDir * 40f - Vector3.up * 1.2f);
            yield return Shot("look_02_r1_road_against_sun", eye1, eye1 - roadDir * 40f - Vector3.up * 1.2f);

            // 3. The shaded side of a town building from 10 m, eaves in view.
            bool wallDone = false;
            foreach (var (b, box, _) in lots.Take(60))
            {
                var faces = new[] { (b.right, box.localScale.x / 2f), (-b.right, box.localScale.x / 2f), (b.forward, box.localScale.z / 2f), (-b.forward, box.localScale.z / 2f) };
                var (n, half) = faces.OrderByDescending(f => Vector3.Dot(f.Item1, sun)).First();
                if (Vector3.Dot(n, sun) < 0.5f) continue;
                if (!Ground(b.position + n * (half + 10.3f), out var g)) continue;
                var eye = g.point + Vector3.up * 1.7f;
                var wall = new Vector3(b.position.x, eye.y + 1.3f, b.position.z) + n * (half + 0.3f);
                if (Physics.CheckSphere(eye, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Linecast(eye, wall - n * 0.6f, out var los, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || !los.transform.IsChildOf(b)) continue;
                yield return Shot("look_03_shaded_wall_10m_" + b.name, eye, wall);
                wallDone = true;
                break;
            }
            Assert.IsTrue(wallDone, "a shaded town wall with a clear 10 m view");

            // 4. R0 ground overlooking open land: the spot and way whose terrain 50-450 m out stays furthest below the eye
            // (trees have no colliders, so the terrain decides; a building in the way rules the way out).
            Assert.IsTrue(world.TryRegion("R0", out var r0), "region R0");
            var starts = world.PassesOf(r0.Index).Select(p => p.Position)
                .Concat(world.ConvergencePointsIn(r0.Index).Select(p => p.Position))
                .Concat(world.ElevatorsIn(r0.Index).Select(e => e.Position))
                .Concat(from x in Enumerable.Range(-4, 9) from z in Enumerable.Range(-4, 9) select r0.Centre + new Vector3(x * 80f, 0f, z * 80f)).ToList();
            float bestClear = float.NegativeInfinity;
            Vector3 fieldEye = default, fieldDir = default;
            foreach (var s in starts)
            {
                if (!Ground(s, out var g) || g.collider is not TerrainCollider) continue;
                var eye = g.point + Vector3.up * 1.7f;
                for (int i = 0; i < 24; i++)
                {
                    var d = Dir(i, 24);
                    if (Physics.Raycast(eye + Vector3.up * 2f, d, 150f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    float clear = float.PositiveInfinity;
                    for (float k = 50f; k <= 450f; k += 50f)
                        clear = Mathf.Min(clear, Ground(eye + d * k, out var h) ? eye.y - h.point.y : float.NegativeInfinity);
                    if (clear > bestClear) { bestClear = clear; fieldEye = eye; fieldDir = d; }
                }
            }
            Assert.Greater(bestClear, float.NegativeInfinity, "an open R0 view");
            Debug.Log($"[IslandLook] R0 view: terrain stays {bestClear:F1} m below the eye from 50 to 450 m");
            yield return Shot("look_04_r0_field_far", fieldEye, fieldEye + fieldDir * 300f - Vector3.up * Mathf.Clamp(bestClear, 5f, 40f));

            // 5. A school (else shop) interior, ground floor, floor in view.
            bool hallDone = false;
            foreach (var kind in new[] { "School", "Market", "Shop" })
            {
                foreach (var (b, box, k) in lots.Where(l => l.kind == kind).Take(8))
                {
                    var bottom = box.position.y - box.localScale.y / 2f;
                    var c = new Vector3(box.position.x, bottom + 1.7f, box.position.z);
                    var corner = -b.right * box.localScale.x * 0.3f - b.forward * box.localScale.z * 0.3f;
                    var eye = c + corner;
                    if (Physics.CheckSphere(eye, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) eye = c;
                    if (Physics.CheckSphere(eye, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    yield return Shot($"look_05_interior_{kind.ToLowerInvariant()}_" + b.name, eye, c - corner - Vector3.up * 1.6f);
                    hallDone = true;
                    break;
                }
                if (hallDone) break;
            }
            Assert.IsTrue(hallDone, "a school or shop interior");

            // 6. From inside a town house, through its open front door.
            bool doorDone = false;
            var houses = lots.Where(l => l.kind == "House").Take(20).ToList();
            foreach (var door in Door.All.OrderBy(d => Vector3.Distance(Flat0(d.transform.position), TownC)).Take(200))
            {
                var at = door.transform.position;
                var house = houses.FirstOrDefault(h => Vector3.Distance(Flat0(h.building.position), Flat0(at)) < Mathf.Max(h.box.localScale.x, h.box.localScale.z) * 0.6f + 0.5f);
                if (house.building == null) continue;
                var inward = Vector3.Dot(house.building.position - at, door.Forward) > 0f ? door.Forward : -door.Forward;
                var eye = at + inward * 2.6f + Vector3.up * 1.7f;
                if (Physics.CheckSphere(eye, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                door.Set(Vector3.Dot(inward, door.Forward) > 0f ? DoorState.OpenBack : DoorState.OpenAhead, quiet: true);
                yield return new WaitForSeconds(1f);
                yield return Shot("look_06_house_doorway_out_" + house.building.name, eye, at - inward * 20f + Vector3.up * 1.2f);
                doorDone = true;
                break;
            }
            Assert.IsTrue(doorDone, "a town house door");

            UnityEngine.Object.Destroy(cam.gameObject);
            RenderProbe.WriteReport(Path.Combine(folder, "look_perf.json"), "{" + string.Join(",", report) + "}");
            Debug.Log("[IslandLook] shots in " + folder);
        }
    }
}
