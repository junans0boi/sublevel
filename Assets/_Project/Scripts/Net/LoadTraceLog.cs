using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace Game.Net
{
    // The log behind Game.Gameplay's LoadTrace (#484), here so the Net and AI assemblies can time their own load-time
    // steps too (#491: release builds have no profiler markers). Off (no cost but one bool) until LoadTrace opens it.
    public static class LoadTraceLog
    {
        public static bool On { get; private set; }
        static string _path;
        static readonly StringBuilder _log = new StringBuilder();

        // Engine clock (starts with the player, before the first scene); epoch ms lines it up with the launch time outside.
        static double Now => Time.realtimeSinceStartupAsDouble;

        public static void Open(string path)
        {
            _path = Path.GetFullPath(path);
            _log.Clear();
            On = true;
        }

        public static void Mark(string what)
        {
            if (!On) return;
            string line = $"[LoadTrace] {Now,8:F3}s e{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()} f{Time.frameCount,-6} {what} | gc0={GC.CollectionCount(0)} mono={Profiler.GetMonoUsedSizeLong() >> 20}MB alloc={Profiler.GetTotalAllocatedMemoryLong() >> 20}MB";
            Debug.Log(line);
            _log.AppendLine(line);
            File.WriteAllText(_path, _log.ToString());
        }

        // One load-time step: `using (LoadTraceLog.Step("x")) { ... }`.
        public static StepScope Step(string what) => new StepScope(On ? what : null);

        // Many small calls of one kind (per pickup, per bot): summed until FlushSums writes them out.
        static readonly System.Collections.Generic.Dictionary<string, (double ms, int n)> _sums = new System.Collections.Generic.Dictionary<string, (double, int)>();
        public static SumScope Sum(string what) => new SumScope(On ? what : null);

        public static void FlushSums()
        {
            foreach (var kv in _sums) if (kv.Value.ms >= 2.0) Mark($"sum {kv.Key}: {kv.Value.ms:F1} ms x{kv.Value.n}");
            _sums.Clear();
        }

        public static void ClearSums() => _sums.Clear();

        public static void AddSum(string what, double ms)
        {
            _sums.TryGetValue(what, out var s);
            _sums[what] = (s.ms + ms, s.n + 1);
        }

        public readonly struct SumScope : IDisposable
        {
            readonly string _what;
            readonly long _start;
            public SumScope(string what) { _what = what; _start = what != null ? Stopwatch.GetTimestamp() : 0; }
            public void Dispose()
            {
                if (_what == null) return;
                AddSum(_what, (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency);
            }
        }

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
    }
}
