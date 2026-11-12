using System;
using System.Collections;
using System.IO;
using Game.Core;
using NUnit.Framework;
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

            var report = new Report();
            RenderProbe.Result withoutPost = default, withPost = default;
            try
            {
                camData.renderPostProcessing = false;
                ssao.SetActive(false);
                yield return RenderProbe.Measure(cam, r => withoutPost = r, Frames, Warmup);
                camData.renderPostProcessing = true;
                ssao.SetActive(true);
                yield return RenderProbe.Measure(cam, r => withPost = r, Frames, Warmup);
            }
            finally
            {
                ssao.SetActive(true);
            }
            report.WithoutPost = withoutPost.Stats;
            report.WithPost = withPost.Stats;
            report.DrawCalls = withPost.DrawCalls;
            report.Triangles = withPost.Triangles;
            report.SetPassCalls = withPost.SetPasses;

            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllText(OutPath, JsonUtility.ToJson(report, true));
            UnityEngine.Debug.Log($"[Perf] lookdev without post avg {report.WithoutPost.AvgMs:F2}ms p99 {report.WithoutPost.P99Ms:F2}ms | with post+SSAO avg {report.WithPost.AvgMs:F2}ms p99 {report.WithPost.P99Ms:F2}ms | draw {report.DrawCalls} tris {report.Triangles} setpass {report.SetPassCalls}");
            RenderProbe.AssertWithinBudget(withPost);
        }
    }
}
