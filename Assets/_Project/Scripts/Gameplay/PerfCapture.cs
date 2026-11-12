using System.IO;
using System;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Player-build frame-time capture: launch with `-perfcapture <output.json>`; writes stats and a screenshot
    // (same path, .png) and quits.
    public class PerfCapture : MonoBehaviour
    {
        const int Frames = 600;
        const float WarmupSeconds = 3f;
        const float SpawnTimeoutSeconds = 30f;
        const float SpikeThresholdMs = 8f;
        const float VsyncSpikeThresholdMs = 20f; // a missed 60 Hz refresh

        // What the capture saw, so a run also proves the match actually started in the build.
        [Serializable]
        public struct Result
        {
            public FrameStats Frames;
            public string Scene;
            public bool LocalPlayerSpawned;
            public int Combatants;
            public int LootItems;
            public int Width;
            public int Height;
            public float DisplayHz; // the 60 fps check (-perfvsync) is only valid on a 60 Hz display
            public bool TimedOut;   // no local player appeared; the numbers are not a match measurement
            // Frames well above the average, to tell a steady cost from isolated hitches.
            public int[] SpikeFrames;
            public float[] SpikeMs;
        }

        string _path;
        float[] _times;
        int _count;
        float _startAt;
        int _quitAtFrame;
        bool _vsync;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-perfcapture");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("PerfCapture");
            DontDestroyOnLoad(go);
            var capture = go.AddComponent<PerfCapture>();
            capture._path = args[i + 1];
            capture._vsync = Array.IndexOf(args, "-perfvsync") >= 0;
            capture._times = new float[Frames];
            capture._startAt = Time.realtimeSinceStartup + WarmupSeconds;
        }

        void Update()
        {
            // Uncapped so the numbers show headroom, not the display's refresh rate.
            // With `-perfvsync` the frame rate is tied to the display instead, to check for missed frames at 60 fps.
            QualitySettings.vSyncCount = _vsync ? 1 : 0;
            Application.targetFrameRate = -1;
            if (_quitAtFrame > 0)
            {
                if (Time.frameCount >= _quitAtFrame) Application.Quit();
                return;
            }
            // Warm-up counts from the moment the player is in the match, so a `-scene` switch is not part of the sample.
            if (Combatant.LocalPlayer == null && Time.realtimeSinceStartup < SpawnTimeoutSeconds)
            {
                _startAt = Time.realtimeSinceStartup + WarmupSeconds;
                return;
            }
            if (Time.realtimeSinceStartup < _startAt) return;
            _times[_count++] = Time.unscaledDeltaTime * 1000f;
            if (_count < Frames) return;
            var spikeFrames = new System.Collections.Generic.List<int>();
            var spikeMs = new System.Collections.Generic.List<float>();
            for (int i = 0; i < Frames; i++)
                if (_times[i] > (_vsync ? VsyncSpikeThresholdMs : SpikeThresholdMs)) { spikeFrames.Add(i); spikeMs.Add(_times[i]); }
            var result = new Result
            {
                Frames = FrameStats.Compute(_times, 0),
                Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                LocalPlayerSpawned = Combatant.LocalPlayer != null,
                Combatants = Combatant.All.Count,
                LootItems = LootItem.All.Count,
                Width = Screen.width,
                Height = Screen.height,
                DisplayHz = (float)Screen.currentResolution.refreshRateRatio.value,
                TimedOut = Combatant.LocalPlayer == null,
                SpikeFrames = spikeFrames.ToArray(),
                SpikeMs = spikeMs.ToArray(),
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)));
            File.WriteAllText(_path, JsonUtility.ToJson(result, true));
            ScreenCapture.CaptureScreenshot(Path.ChangeExtension(Path.GetFullPath(_path), ".png"));
            _quitAtFrame = Time.frameCount + 3; // the screenshot is written at the end of a later frame
        }
    }
}
