using System.IO;
using Game.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Net
{
    // Server measurement (ADR-242, issue #328): frame time (mean/p50/p95/p99/max), managed heap, combatant and bot counts and
    // lag-compensation rewinds per window. One [Perf] line per window in the log, and a CSV next to it. Started by the
    // dedicated server with --perf. Per-system stopwatches (bot update, sense, door scan) are a later step.
    public class ServerPerfProbe : MonoBehaviour
    {
        const float WindowSeconds = 10f;

        readonly ServerFrameStats _frames = new ServerFrameStats();
        float _windowStart;
        int _rewindsAtStart;
        string _csv;

        // P3 (final-circle crowd, ADR-242): a scenario named "p3..." warps every bot to a point of the NavMesh within
        // GatherRadius of (GatherX, GatherZ) once GatherAfterSeconds of the match have passed, so 50 bodies fight in one place.
        public static float GatherX, GatherZ, GatherRadius = 60f, GatherAfterSeconds = 20f;
        bool _gather, _gathered;
        float _matchStart = -1f;

        public static ServerPerfProbe Start(string scenario)
        {
            var go = new GameObject("ServerPerfProbe");
            DontDestroyOnLoad(go);
            var probe = go.AddComponent<ServerPerfProbe>();
            Directory.CreateDirectory("Logs/perf");
            probe._gather = scenario.StartsWith("p3");
            probe._csv = "Logs/perf/s8_server_" + scenario + ".csv";
            File.WriteAllText(probe._csv, "t,frames,mean_ms,p50_ms,p95_ms,p99_ms,max_ms,heap_mb,combatants,rewinds\n");
            probe._windowStart = Time.realtimeSinceStartup;
            probe._rewindsAtStart = LagCompensation.Rewinds;
            return probe;
        }

        void Gather()
        {
            var rng = new System.Random(12345);
            // No --perf-gather: meet at the hub of the match's final region (the crowd of the last circle).
            if (GatherX == 0f && GatherZ == 0f && ZoneDirector.Instance != null)
            {
                var hub = ZoneDirector.Instance.RegionCentre(ZoneDirector.Instance.FinalRegion);
                GatherX = hub.x; GatherZ = hub.z;
                Debug.Log($"[Perf] P3 final region {ZoneDirector.Instance.FinalRegion}, hub ({hub.x:F0}, {hub.z:F0})");
            }
            int moved = 0;
            foreach (var c in Combatant.All)
            {
                if (c == null || c.IsPlayer || !c.TryGetComponent(out NavMeshAgent agent)) continue;
                var p = new Vector3(GatherX + (float)(rng.NextDouble() * 2 - 1) * GatherRadius, 0f, GatherZ + (float)(rng.NextDouble() * 2 - 1) * GatherRadius);
                if (NavMesh.SamplePosition(p, out var hit, GatherRadius, NavMesh.AllAreas) && agent.Warp(hit.position)) moved++;
            }
            Debug.Log($"[Perf] P3 gather: {moved} bots warped to ({GatherX:F0}, {GatherZ:F0}) r={GatherRadius:F0}");
        }

        void Update()
        {
            if (_gather && !_gathered && Combatant.All.Count > 1)
            {
                if (_matchStart < 0f) _matchStart = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - _matchStart >= GatherAfterSeconds) { _gathered = true; Gather(); }
            }
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
