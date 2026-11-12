using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Game.Core;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // M1.5: render cost of the look-check scene with and without post-processing + SSAO (PERF_BUDGET method).
    // Run alone: tools/unity.sh test playmode Game.Tests.LookDevPerfTests
    [Category("Perf")]
    public class LookDevPerfTests
    {
        const string ScenePath = "Assets/_Project/Scenes/LookDev.unity";
        const string OutPath = "Logs/perf/lookdev.json";
        const int Frames = 150, Warmup = 30;
        const float RenderBudgetMs = 10f;

        [Serializable]
        public class Report
        {
            public string Resolution = "1920x1080";
            public long DrawCalls;
            public long Triangles;
            public long SetPassCalls;
            public FrameStats WithoutPost;
            public FrameStats WithPost;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator LookDev_WithPostProcessing_RendersWithinBudget()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("editor-only measurement");
            yield break;
#endif
            var cam = Camera.main;
            Assert.IsNotNull(cam, "LookDev scene should have a main camera (run LookDevBuilder.Build)");
            var camData = cam.GetUniversalAdditionalCameraData();
            Assert.IsTrue(camData.renderPostProcessing, "look-check camera should render post-processing");
            var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            ScriptableRendererFeature ssao = null;
            foreach (var feature in pipeline.rendererDataList[0].rendererFeatures)
                if (feature is ScreenSpaceAmbientOcclusion) ssao = feature;
            Assert.IsNotNull(ssao, "SSAO renderer feature should be configured (RenderSetup)");

            cam.enabled = false; // rendered manually so only our call is timed
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            var report = new Report();
            try
            {
                camData.renderPostProcessing = false;
                ssao.SetActive(false);
                yield return Measure(cam, rt, null, s => report.WithoutPost = s);
                camData.renderPostProcessing = true;
                ssao.SetActive(true);
                yield return Measure(cam, rt, report, s => report.WithPost = s);
            }
            finally
            {
                ssao.SetActive(true);
                cam.targetTexture = null;
                rt.Release();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllText(OutPath, JsonUtility.ToJson(report, true));
            UnityEngine.Debug.Log($"[Perf] lookdev without post avg {report.WithoutPost.AvgMs:F2}ms p99 {report.WithoutPost.P99Ms:F2}ms | with post+SSAO avg {report.WithPost.AvgMs:F2}ms p99 {report.WithPost.P99Ms:F2}ms | draw {report.DrawCalls} tris {report.Triangles} setpass {report.SetPassCalls}");
            Assert.LessOrEqual(report.WithPost.P99Ms, RenderBudgetMs, "render p99 over PERF_BUDGET");
        }

        static IEnumerator Measure(Camera cam, RenderTexture rt, Report counters, Action<FrameStats> done)
        {
            var probe = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var times = new float[Frames];
            var sw = new Stopwatch();
            for (int i = 0; i < Frames; i++)
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
            if (counters != null)
            {
                counters.DrawCalls = draws.LastValue;
                counters.Triangles = tris.LastValue;
                counters.SetPassCalls = setPass.LastValue;
            }
            draws.Dispose(); tris.Dispose(); setPass.Dispose();
            UnityEngine.Object.Destroy(probe);
            done(FrameStats.Compute(times, Warmup));
        }
    }
}
