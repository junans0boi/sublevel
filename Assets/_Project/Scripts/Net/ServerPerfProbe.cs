using System.IO;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Server measurement (ADR-242, issue #328): frame time (mean/p50/p95/p99/max), managed heap, combatant and bot counts and
    // lag-compensation rewinds per window. One [Perf] line per window in the log, and a CSV next to it. Started by the
    // dedicated server with --perf. Per-system stopwatches (bot update, sense, door scan) are a later step.
    public class ServerPerfProbe : MonoBehaviour
    {
        const float WindowSeconds = 10f;

        readonly FrameStats _frames = new FrameStats();
        float _windowStart;
        int _rewindsAtStart;
        string _csv;

        public static ServerPerfProbe Start(string scenario)
        {
            var go = new GameObject("ServerPerfProbe");
            DontDestroyOnLoad(go);
            var probe = go.AddComponent<ServerPerfProbe>();
            Directory.CreateDirectory("Logs/perf");
            probe._csv = "Logs/perf/s8_server_" + scenario + ".csv";
            File.WriteAllText(probe._csv, "t,frames,mean_ms,p50_ms,p95_ms,p99_ms,max_ms,heap_mb,combatants,rewinds\n");
            probe._windowStart = Time.realtimeSinceStartup;
            probe._rewindsAtStart = LagCompensation.Rewinds;
            return probe;
        }

        void Update()
        {
            _frames.Add(Time.unscaledDeltaTime * 1000f);
            float now = Time.realtimeSinceStartup;
            if (now - _windowStart < WindowSeconds) return;
            float heap = System.GC.GetTotalMemory(false) / (1024f * 1024f);
            int rewinds = LagCompensation.Rewinds - _rewindsAtStart;
            string line = $"{now:F0},{_frames.Count},{_frames.Mean():F2},{_frames.Percentile(50):F2},{_frames.Percentile(95):F2},{_frames.Percentile(99):F2},{_frames.Max():F2},{heap:F0},{Combatant.All.Count},{rewinds}";
            Debug.Log("[Perf] " + line);
            File.AppendAllText(_csv, line + "\n");
            _frames.Clear();
            _windowStart = now;
            _rewindsAtStart = LagCompensation.Rewinds;
        }
    }
}
