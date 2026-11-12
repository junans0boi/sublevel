using System.Collections;
using System.IO;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public class PerfCaptureTests
    {
        const string OutDir = "Logs/perf";
        const int Frames = 120;
        const int Warmup = 30;

        [UnityTest]
        public IEnumerator BootstrapScene_CapturesFrameTimeAndScreenshot()
        {
            var load = SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            Assert.IsNotNull(GameObject.Find("Floor"), "Bootstrap scene should contain Floor");

            Directory.CreateDirectory(OutDir);
            var times = new float[Frames];
            for (int i = 0; i < Frames; i++)
            {
                yield return null;
                times[i] = Time.unscaledDeltaTime * 1000f;
            }

            var shotPath = Path.Combine(OutDir, "screenshot.png");
            File.WriteAllBytes(shotPath, CaptureCamera(Camera.main, 1280, 720));

            var stats = FrameStats.Compute(times, Warmup);
            File.WriteAllText(Path.Combine(OutDir, "frametime.json"), JsonUtility.ToJson(stats, true));

            Assert.AreEqual(Frames - Warmup, stats.Count);
            Assert.Greater(stats.AvgMs, 0f);
            Assert.IsTrue(File.Exists(shotPath));
            Assert.Greater(new FileInfo(shotPath).Length, 1000);
        }

        // WaitForEndOfFrame is not evoked in batchmode, so render the camera to a RenderTexture instead.
        static byte[] CaptureCamera(Camera cam, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            var png = tex.EncodeToPNG();
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
            return png;
        }
    }
}
