using System;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Gameplay
{
    // #484 loading timings in a player build: launch with `-loadtrace <out.txt>`. Logs milestones (seconds since the
    // process started), the cost of the known load-time steps and every frame over 50 ms in the 20 s after a scene
    // loads. With `-loadtrace-play` it also presses 시작 on the menu, waits until the local player is in the World
    // and quits, so `menu -> playable` is timed without a person. Off (no cost but one bool) without the flag.
    [DefaultExecutionOrder(-32000)] // Update first, so a hitch splits into the scripts and what came before them
    public sealed class LoadTrace : MonoBehaviour
    {
        static float HitchMs = 50f; // `-loadtrace-ms <n>` lowers it (a slower PC turns 20 ms here into 50 ms)
        const float WatchSeconds = 20f;

        public static bool On => LoadTraceLog.On;
        static bool _autoPlay;
        static bool _stay; // -playtrace: watch the whole run, FeelCapture quits

        float _watchUntil;
        double _lastFrame, _beforeRender;
        bool _pressed, _playable;
        float _quitAt = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-loadtrace");
            if (i < 0 || i + 1 >= args.Length) return;
            LoadTraceLog.Open(args[i + 1]);
            _autoPlay = Array.IndexOf(args, "-loadtrace-play") >= 0;
            _stay = Array.IndexOf(args, "-playtrace") >= 0;
            int ms = Array.IndexOf(args, "-loadtrace-ms");
            HitchMs = ms >= 0 && ms + 1 < args.Length && float.TryParse(args[ms + 1], out var v) ? v : 50f;
            Mark("subsystems (first script)");
            HookPlayerLoop();
        }

        // Times every engine update step (scripts, animation, physics, rendering...) into the sums a hitch writes out.
        static void HookPlayerLoop()
        {
            var root = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            var phases = root.subSystemList;
            for (int p = 0; p < phases.Length; p++)
            {
                var subs = phases[p].subSystemList;
                if (subs == null) continue;
                var list = new System.Collections.Generic.List<UnityEngine.LowLevel.PlayerLoopSystem>();
                foreach (var sub in subs)
                {
                    string name = "loop " + sub.type.Name;
                    list.Add(new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(LoadTrace), updateDelegate = () => Lap(name) });
                    list.Add(sub);
                }
                list.Add(new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(LoadTrace), updateDelegate = () => Lap(null) });
                phases[p].subSystemList = list.ToArray();
            }
            root.subSystemList = phases;
            UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(root);
        }

        static string _lapName;
        static long _lapStart;
        static void Lap(string next)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_lapName != null) LoadTraceLog.AddSum(_lapName, (now - _lapStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            _lapName = next;
            _lapStart = now;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeScene()
        {
            if (!On) return;
            Mark("before first scene load");
            SceneManager.sceneLoaded += (s, _) => Mark("sceneLoaded " + s.name);
            var go = new GameObject("LoadTrace");
            DontDestroyOnLoad(go);
            go.AddComponent<Tail>();
            go.AddComponent<LoadTrace>()._watchUntil = Time.realtimeSinceStartup + WatchSeconds;
        }

        public static void Mark(string what) => LoadTraceLog.Mark(what);

        // One load-time step: `using (LoadTrace.Step("x")) { ... }`.
        public static LoadTraceLog.StepScope Step(string what) => LoadTraceLog.Step(what);

        void OnEnable() { SceneManager.sceneLoaded += OnLoaded; Application.onBeforeRender += BeforeRender; }
        void OnDisable() { SceneManager.sceneLoaded -= OnLoaded; Application.onBeforeRender -= BeforeRender; }
        void BeforeRender() => _beforeRender = Time.realtimeSinceStartupAsDouble;

        // Last LateUpdate before FishNet's send loop (short.MaxValue), so the send splits off the scripts.
        [DefaultExecutionOrder(32000)]
        sealed class Tail : MonoBehaviour { void LateUpdate() => _lateEnd = Time.realtimeSinceStartupAsDouble; }
        static double _lateEnd;
        void OnLoaded(Scene s, LoadSceneMode m) => _watchUntil = Time.realtimeSinceStartup + WatchSeconds;

        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float ms = (float)((now - _lastFrame) * 1000.0);   // not unscaledDeltaTime: Unity resets it on the frame after a scene load
            _lastFrame = now;
            // The part since the last frame's scripts ended: rendering, then this frame's Starts and fixed steps before this Update.
            if (Time.frameCount > 1 && ms > HitchMs && (_stay || Time.realtimeSinceStartup < _watchUntil))
            {
                LoadTraceLog.FlushSums();
                Mark($"hitch {ms:F0} ms (scripts {(_lateEnd - (now - ms / 1000.0)) * 1000.0:F0} ms, net send {(_beforeRender - _lateEnd) * 1000.0:F0} ms, render+net read {(now - _beforeRender) * 1000.0:F0} ms)");
            }
            else LoadTraceLog.ClearSums();
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
                if (!_stay) _quitAt = Time.realtimeSinceStartup + 12f;
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
