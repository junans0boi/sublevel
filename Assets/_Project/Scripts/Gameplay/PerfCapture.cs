using System.IO;
using System;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Player-build frame-time capture: launch with `-perfcapture <output.json>`; writes stats and quits.
    public class PerfCapture : MonoBehaviour
    {
        const int Frames = 600;
        const float WarmupSeconds = 3f;

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
        }

        string _path;
        float[] _times;
        int _count;
        float _startAt;

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
            capture._times = new float[Frames];
            capture._startAt = Time.realtimeSinceStartup + WarmupSeconds;
        }

        void Update()
        {
            // Uncapped so the numbers show headroom, not the display's refresh rate.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            if (Time.realtimeSinceStartup < _startAt) return;
            _times[_count++] = Time.unscaledDeltaTime * 1000f;
            if (_count < Frames) return;
            var result = new Result
            {
                Frames = FrameStats.Compute(_times, 0),
                Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                LocalPlayerSpawned = Combatant.LocalPlayer != null,
                Combatants = Combatant.All.Count,
                LootItems = LootItem.All.Count,
                Width = Screen.width,
                Height = Screen.height,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)));
            File.WriteAllText(_path, JsonUtility.ToJson(result, true));
            Application.Quit();
        }
    }
}
