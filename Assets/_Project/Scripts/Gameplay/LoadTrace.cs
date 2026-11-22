using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Game.Gameplay
{
    // #484 loading timings in a player build: launch with `-loadtrace <out.txt>`. Logs milestones (seconds since the
    // process started), the cost of the known load-time steps and every frame over 50 ms in the 20 s after a scene
    // loads. With `-loadtrace-play` it also presses 시작 on the menu, waits until the local player is in the World
    // and quits, so `menu -> playable` is timed without a person. Off (no cost but one bool) without the flag.
    public sealed class LoadTrace : MonoBehaviour
    {
        const float HitchMs = 50f;
        const float WatchSeconds = 20f;

        public static bool On { get; private set; }
        static string _path;
        static bool _autoPlay;
        static readonly StringBuilder _log = new StringBuilder();

        float _watchUntil;
        double _lastFrame;
        bool _pressed, _playable;
        float _quitAt = -1f;

        // Engine clock (starts with the player, before the first scene); epoch ms lines it up with the launch time outside.
        static double Now => Time.realtimeSinceStartupAsDouble;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-loadtrace");
            On = i >= 0 && i + 1 < args.Length;
            if (!On) return;
            _path = Path.GetFullPath(args[i + 1]);
            _autoPlay = Array.IndexOf(args, "-loadtrace-play") >= 0;
            _log.Clear();
            Mark("subsystems (first script)");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeScene()
        {
            if (!On) return;
            Mark("before first scene load");
            SceneManager.sceneLoaded += (s, _) => Mark("sceneLoaded " + s.name);
            var go = new GameObject("LoadTrace");
            DontDestroyOnLoad(go);
            go.AddComponent<LoadTrace>()._watchUntil = Time.realtimeSinceStartup + WatchSeconds;
        }

        public static void Mark(string what)
        {
            if (!On) return;
            string line = $"[LoadTrace] {Now,8:F3}s e{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()} f{Time.frameCount,-6} {what} | gc0={GC.CollectionCount(0)} mono={Profiler.GetMonoUsedSizeLong() >> 20}MB alloc={Profiler.GetTotalAllocatedMemoryLong() >> 20}MB";
            Debug.Log(line);
            _log.AppendLine(line);
            File.WriteAllText(_path, _log.ToString());
        }

        // One load-time step: `using (LoadTrace.Step("x")) { ... }`.
        public static StepScope Step(string what) => new StepScope(On ? what : null);

        public readonly struct StepScope : IDisposable
        {
            readonly string _what;
            readonly long _start;
            public StepScope(string what) { _what = what; _start = what != null ? Stopwatch.GetTimestamp() : 0; }
            public void Dispose()
            {
                if (_what == null) return;
                Mark($"step {_what}: {(Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency:F1} ms");
            }
        }

        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
        void OnLoaded(Scene s, LoadSceneMode m) => _watchUntil = Time.realtimeSinceStartup + WatchSeconds;

        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float ms = (float)((now - _lastFrame) * 1000.0);   // not unscaledDeltaTime: Unity resets it on the frame after a scene load
            _lastFrame = now;
            if (Time.frameCount > 1 && ms > HitchMs && Time.realtimeSinceStartup < _watchUntil) Mark($"hitch {ms:F0} ms");
            if (Time.frameCount == 2) Mark("first frames rendered (" + SceneManager.GetActiveScene().name + ")");

            if (!_autoPlay) return;
            if (!_pressed && Time.frameCount > 30)
            {
                var menu = FindAnyObjectByType<MainMenu>();
                if (menu != null)
                {
                    _pressed = true;
                    Mark("press 시작 (menu ready)");
                    menu.StartGame();
                }
            }
            if (!_playable && Combatant.LocalPlayer != null)
            {
                _playable = true;
                Mark($"local player spawned (combatants {Combatant.All.Count}, loot {LootItem.All.Count})");
                _quitAt = Time.realtimeSinceStartup + 12f;
            }
            if (_quitAt > 0f && Time.realtimeSinceStartup > _quitAt)
            {
                Mark($"quit (combatants {Combatant.All.Count})");
                _quitAt = -1f;
                Application.Quit();
            }
        }
    }
}
