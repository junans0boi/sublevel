using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Game.Core;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // M0-8: measures render cost of procedural stress scenes to derive PERF_BUDGET numbers.
    // Run alone: tools/unity.sh test playmode Game.Tests.PerfBaselineTests
    [Category("Perf")]
    public class PerfBaselineTests
    {
        const string OutPath = "Logs/perf/baseline.json";
        const int Width = 1920, Height = 1080;
        const int Frames = 90, Warmup = 30;

        [Serializable]
        public struct Step
        {
            public string Name;
            public int Objects;
            public int Materials;
            public long Triangles;
            public long DrawCalls;
            public long Batches;
            public long SetPassCalls;
            public long GcAllocBytesPerFrame;
            public long UsedMemoryMb;
            public FrameStats Render;
        }

        [Serializable]
        public class Report
        {
            public string Device;
            public string Gpu;
            public string Resolution;
            public List<Step> Steps = new List<Step>();
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator MeasureStressSteps()
        {
            var report = new Report
            {
                Device = SystemInfo.deviceModel,
                Gpu = SystemInfo.graphicsDeviceName,
                Resolution = Width + "x" + Height,
            };

            // (name, primitive, count, distinct materials)
            var steps = new (string, PrimitiveType, int, int)[]
            {
                ("cubes_1k", PrimitiveType.Cube, 1000, 8),
                ("cubes_3k", PrimitiveType.Cube, 3000, 16),
                ("cubes_6k", PrimitiveType.Cube, 6000, 32),
                ("cubes_12k", PrimitiveType.Cube, 12000, 32),
                ("spheres_1k", PrimitiveType.Sphere, 1000, 16),
                ("spheres_3k", PrimitiveType.Sphere, 3000, 16),
                ("spheres_6k", PrimitiveType.Sphere, 6000, 16),
            };

            foreach (var (name, primitive, count, materials) in steps)
            {
                Step result = default;
                yield return Measure(name, primitive, count, materials, r => result = r);
                report.Steps.Add(result);
                UnityEngine.Debug.Log($"[Perf] {name}: avg {result.Render.AvgMs:F2}ms p99 {result.Render.P99Ms:F2}ms draw {result.DrawCalls} batches {result.Batches} tris {result.Triangles}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllText(OutPath, JsonUtility.ToJson(report, true));
            Assert.AreEqual(steps.Length, report.Steps.Count);
            foreach (var s in report.Steps) Assert.Greater(s.Render.AvgMs, 0f, s.Name);
        }

        static IEnumerator Measure(string name, PrimitiveType primitive, int count, int materialCount, Action<Step> done)
        {
            var root = new GameObject("Stress_" + name);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.IsNotNull(shader, "URP Lit shader not found");
            var mats = new Material[materialCount];
            for (int i = 0; i < materialCount; i++)
                mats[i] = new Material(shader) { color = Color.HSVToRGB(i / (float)materialCount, 0.5f, 0.8f) };

            var template = GameObject.CreatePrimitive(primitive);
            UnityEngine.Object.Destroy(template.GetComponent<Collider>());
            int side = Mathf.CeilToInt(Mathf.Sqrt(count));
            for (int i = 0; i < count; i++)
            {
                var go = UnityEngine.Object.Instantiate(template, root.transform);
                go.transform.position = new Vector3((i % side) * 1.5f, 0.5f + (i % 3), (i / side) * 1.5f);
                go.GetComponent<MeshRenderer>().sharedMaterial = mats[i % materialCount];
            }
            UnityEngine.Object.Destroy(template);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.SetParent(root.transform);
            floor.transform.localScale = Vector3.one * side;
            floor.transform.position = new Vector3(side * 0.75f, 0, side * 0.75f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = mats[0];

            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(root.transform);
            lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;

            var camGo = new GameObject("PerfCamera");
            camGo.transform.SetParent(root.transform);
            // Elevated corner view so the whole grid is inside the frustum (worst case: nothing culled).
            camGo.transform.position = new Vector3(-side * 0.4f, side * 0.9f, -side * 0.4f);
            camGo.transform.LookAt(new Vector3(side * 0.75f, 0, side * 0.75f));
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = side * 6f;
            cam.enabled = false; // rendered manually so only our call is timed
            var rt = new RenderTexture(Width, Height, 24);
            cam.targetTexture = rt;
            var probe = new Texture2D(1, 1, TextureFormat.RGB24, false);

            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            var mem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");

            var times = new float[Frames];
            long gcSum = 0;
            var sw = new Stopwatch();
            for (int i = 0; i < Frames; i++)
            {
                yield return null;
                sw.Restart();
                cam.Render();
                // Reading one pixel back blocks until the GPU has finished the frame.
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                RenderTexture.active = prev;
                sw.Stop();
                times[i] = (float)sw.Elapsed.TotalMilliseconds;
                if (i >= Warmup) gcSum += gc.LastValue;
            }

            done(new Step
            {
                Name = name,
                Objects = count,
                Materials = materialCount,
                Triangles = tris.LastValue,
                DrawCalls = draws.LastValue,
                Batches = batches.LastValue,
                SetPassCalls = setPass.LastValue,
                GcAllocBytesPerFrame = gcSum / (Frames - Warmup),
                UsedMemoryMb = mem.LastValue / (1024 * 1024),
                Render = FrameStats.Compute(times, Warmup),
            });

            draws.Dispose(); batches.Dispose(); setPass.Dispose(); tris.Dispose(); gc.Dispose(); mem.Dispose();
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(probe);
            foreach (var m in mats) UnityEngine.Object.Destroy(m);
            UnityEngine.Object.Destroy(root);
            yield return null;
        }
    }
}
