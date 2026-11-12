using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Game.Core;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;

namespace Game.Tests
{
    // Render-budget measurement (PERF_BUDGET "판정 조건" A): renders a camera into a 1080p target by hand and times it,
    // including the GPU, so the number does not depend on the editor's own frame pacing.
    public static class RenderProbe
    {
        public const int Width = 1920, Height = 1080;
        public const float RenderBudgetMs = 10f;
        public const long DrawCallBudget = 2500, TriangleBudget = 1500000, SetPassBudget = 60;

        public struct Result
        {
            public FrameStats Stats;
            public long DrawCalls, Triangles, SetPasses;

            public override string ToString() =>
                $"avg {Stats.AvgMs:F2}ms p99 {Stats.P99Ms:F2}ms draw {DrawCalls} tris {Triangles} setpass {SetPasses}";

            public string ToJson() =>
                $"{{\"AvgMs\":{Stats.AvgMs:F3},\"P99Ms\":{Stats.P99Ms:F3},\"DrawCalls\":{DrawCalls},\"Triangles\":{Triangles},\"SetPass\":{SetPasses}}}";
        }

        // Leaves the camera as it found it. `screenshotPath` keeps the last frame so the scene can be checked by eye.
        public static IEnumerator Measure(Camera cam, Action<Result> done, int frames = 90, int warmup = 30, string screenshotPath = null)
        {
            bool wasEnabled = cam.enabled;
            var previousTarget = cam.targetTexture;
            cam.enabled = false; // rendered manually so only our call is timed
            var rt = new RenderTexture(Width, Height, 24);
            cam.targetTexture = rt;
            var probe = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");

            var times = new float[frames];
            var sw = new Stopwatch();
            Result result = default;
            try
            {
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                sw.Restart();
                cam.Render();
                // Reading one pixel back forces the GPU to finish the frame inside the timed section.
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                RenderTexture.active = prev;
                sw.Stop();
                times[i] = (float)sw.Elapsed.TotalMilliseconds;
            }
            result = new Result
            {
                Stats = FrameStats.Compute(times, warmup),
                DrawCalls = draws.LastValue, Triangles = tris.LastValue, SetPasses = setPass.LastValue,
            };

            if (screenshotPath != null)
            {
                var shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = prev;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshotPath)));
                File.WriteAllBytes(screenshotPath, shot.EncodeToPNG());
                UnityEngine.Object.Destroy(shot);
            }
            }
            finally
            {
                // Also runs when the measurement throws or the test is abandoned mid-way.
                draws.Dispose(); tris.Dispose(); setPass.Dispose();
                if (cam != null)
                {
                    cam.targetTexture = previousTarget;
                    cam.enabled = wasEnabled;
                }
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                UnityEngine.Object.Destroy(probe);
            }
            done(result);
        }

        public static void WriteReport(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, json);
        }

        // PERF_BUDGET targets (ADR-028). Lower bounds prove the counters were live.
        public static void AssertWithinBudget(Result r)
        {
            Assert.That(r.Stats.P99Ms, Is.GreaterThan(0f).And.LessThanOrEqualTo(RenderBudgetMs), "render time p99");
            Assert.That(r.DrawCalls, Is.GreaterThan(0).And.LessThanOrEqualTo(DrawCallBudget), "draw calls");
            Assert.That(r.Triangles, Is.GreaterThan(0).And.LessThanOrEqualTo(TriangleBudget), "triangles");
            Assert.That(r.SetPasses, Is.GreaterThan(0).And.LessThanOrEqualTo(SetPassBudget), "set-pass calls");
        }
    }
}
