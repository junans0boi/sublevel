using System.IO;
using Game.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Net
{
    // Server measurement (ADR-242, issue #328): frame time (mean/p50/p95/p99/max), managed heap, combatant and bot counts and
    // lag-compensation rewinds per window. One [Perf] line per window in the log, and a CSV next to it. Started by the
    // dedicated server with --perf. Bot stopwatches (BotPerf: update, sense, item sense, door check) are the last columns.
    public class ServerPerfProbe : MonoBehaviour
    {
        const float WindowSeconds = 10f;

        readonly ServerFrameStats _frames = new ServerFrameStats();
        readonly ServerFrameStats[] _bot = { new ServerFrameStats(), new ServerFrameStats(), new ServerFrameStats(), new ServerFrameStats(), new ServerFrameStats() };
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
            File.WriteAllText(probe._csv, "t,frames,mean_ms,p50_ms,p95_ms,p99_ms,max_ms,heap_mb,combatants,rewinds,bot_ai_mean,bot_ai_p99,sense_mean,sense_p99,loot_mean,loot_p99,doors_mean,doors_p99,path_mean,path_p99\n");
            BotPerf.Enabled = true;
            Combatant.ServerDied += (victim, info) => Debug.Log("[Death] t=" + Time.realtimeSinceStartup.ToString("F0") + " victim_armed=" + (victim.HasAnyWeapon ? 1 : 0) + " cause=" + info.Cause + " attacker=" + (info.Attacker == null ? "none" : (info.Attacker.HasAnyWeapon ? "armed" : "unarmed")) + " weapon=" + info.WeaponId);
            probe._windowStart = Time.realtimeSinceStartup;
            probe._rewindsAtStart = LagCompensation.Rewinds;
            return probe;
        }

        // The bots add their NavMeshAgent only when they start walking (after the elevator): the crowd is formed then.
        static int BotCount() { int n = 0; foreach (var c in Combatant.All) if (c != null && !c.IsPlayer) n++; return n; }
        static int BotsWithAgent() { int n = 0; foreach (var c in Combatant.All) if (c != null && !c.IsPlayer && c.TryGetComponent(out NavMeshAgent _)) n++; return n; }

        void Gather()
        {
            var rng = new System.Random(12345);
            // No --perf-gather: meet at the hub of the final region, or of region 0 when the match has not chosen one yet
            // (--region N picks it, PollutionDirector.FinalRegionOverride).
            if (GatherX == 0f && GatherZ == 0f && PollutionDirector.Instance != null)
            {
                int region = Mathf.Max(0, PollutionDirector.Instance.FinalRegion);
                var hub = PollutionDirector.Instance.RegionCentre(region);
                GatherX = hub.x; GatherZ = hub.z;
                Debug.Log($"[Perf] P3 region {region} (final {PollutionDirector.Instance.FinalRegion}), hub ({hub.x:F0}, {hub.z:F0})");
            }
            int bots = 0, moved = 0, noNav = 0, noAgent = 0;
            foreach (var c in Combatant.All)
            {
                if (c == null || c.IsPlayer) continue;
                bots++;
                if (!c.TryGetComponent(out NavMeshAgent agent)) { noAgent++; continue; }
                var p = new Vector3(GatherX + (float)(rng.NextDouble() * 2 - 1) * GatherRadius, c.transform.position.y, GatherZ + (float)(rng.NextDouble() * 2 - 1) * GatherRadius);
                // The hub's height is unknown (terrain): look for the NavMesh in a tall column around the point, wide first.
                bool found = NavMesh.SamplePosition(p, out var hit, 200f, NavMesh.AllAreas);
                if (!found) { noNav++; continue; }
                if (agent.Warp(hit.position)) moved++;
            }
            string line = $"[Perf] P3 gather: {moved}/{bots} bots warped to ({GatherX:F0}, {GatherZ:F0}) r={GatherRadius:F0} (no agent {noAgent}, no NavMesh {noNav})";
            if (moved < bots * 0.9f) Debug.LogError(line + " — FEWER THAN 90%, the crowd scenario is not valid");
            else Debug.Log(line);
        }

        public static bool GiveKeys;
        bool _keysGiven;

        // Farming (ADR-294): bots holding a gun, the first-gun median, how many are still farming, buildings with 3+ bots inside.
        void LogFarm(float now)
        {
            var index = BuildingLoot.Index;
            int bots = 0, armed = 0, crowded = 0;
            var inside = new System.Collections.Generic.Dictionary<int, int>();
            foreach (var c in Combatant.All)
            {
                if (c == null || c.IsPlayer || c.Dead.Value) continue;
                bots++;
                if (c.HasAnyWeapon) armed++;
                if (index == null) continue;
                int b = index.BuildingAt(c.transform.position.x, c.transform.position.z);
                if (b < 0) continue;
                inside.TryGetValue(b, out int n);
                inside[b] = n + 1;
            }
            foreach (var n in inside.Values) if (n >= 3) crowded++;
            Debug.Log($"[Farm] t={now:F0} bots={bots} armed={armed} farming={BotFarmStats.Farming} firstGunMedian={BotFarmStats.MedianFirstGun():F0}s n={BotFarmStats.FirstGunSeconds.Count} inBuildings={inside.Count} crowded3+={crowded}");
        }

        void Update()
        {
            if (GiveKeys && !_keysGiven && Combatant.All.Count > 1)
            {
                _keysGiven = true;
                int n = 0;
                foreach (var c in Combatant.All)
                    if (c != null && !c.IsPlayer && c.TryGetComponent(out KeyHolder holder)) { holder.ServerAdd(1); n++; }
                Debug.Log($"[Perf] gave a key to {n} bots");
            }
            if (_gather && !_gathered && Combatant.All.Count > 1)
            {
                if (_matchStart < 0f) _matchStart = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - _matchStart >= GatherAfterSeconds && BotsWithAgent() >= 0.9f * BotCount()) { _gathered = true; Gather(); }
            }
            _frames.Add(Time.unscaledDeltaTime * 1000f);
            for (int k = 0; k < BotPerf.Slots; k++) _bot[k].Add(BotPerf.TakeMs(k));
            float now = Time.realtimeSinceStartup;
            if (now - _windowStart < WindowSeconds) return;
            float heap = System.GC.GetTotalMemory(false) / (1024f * 1024f);
            int rewinds = LagCompensation.Rewinds - _rewindsAtStart;
            string line = $"{now:F0},{_frames.Count},{_frames.Mean():F2},{_frames.Percentile(50):F2},{_frames.Percentile(95):F2},{_frames.Percentile(99):F2},{_frames.Max():F2},{heap:F0},{Combatant.All.Count},{rewinds}";
            for (int k = 0; k < BotPerf.Slots; k++) { line += $",{_bot[k].Mean():F3},{_bot[k].Percentile(99):F3}"; _bot[k].Clear(); }
            Debug.Log("[Perf] " + line);
            LogFarm(now);
            File.AppendAllText(_csv, line + "\n");
            _frames.Clear();
            _windowStart = now;
            _rewindsAtStart = LagCompensation.Rewinds;
        }
    }
}
