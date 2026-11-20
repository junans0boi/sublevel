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
        readonly ServerFrameStats _allFrames = new ServerFrameStats();   // the whole run, for the statistics row
        string _scenario, _statsFile;
        public static int Seed = -1, Level = -1;
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
            // A built server's working directory is not the project: SUBLEVEL_PERF_DIR (tools/bot_stats.sh sets it) says where the files go.
            string dir = System.Environment.GetEnvironmentVariable("SUBLEVEL_PERF_DIR");
            if (string.IsNullOrEmpty(dir)) dir = "Logs/perf";
            Directory.CreateDirectory(dir);
            probe._gather = scenario.StartsWith("p3");
            probe._csv = dir + "/s8_server_" + scenario + ".csv";
            probe._scenario = scenario;
            probe._statsFile = dir + "/botstats_" + scenario + ".csv";
            File.WriteAllText(probe._csv, "t,frames,mean_ms,p50_ms,p95_ms,p99_ms,max_ms,heap_mb,combatants,rewinds,bot_ai_mean,bot_ai_p99,sense_mean,sense_p99,loot_mean,loot_p99,doors_mean,doors_p99,path_mean,path_p99\n");
            BotPerf.Enabled = true;
            Combatant.ServerDied += (victim, info) => BotMatchStats.Died(victim.ObjectId, info.Attacker != null ? info.Attacker.ObjectId : -1);
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
        // Squads (ADR-312): each member's distance to its team's centre, sampled every window; median and 90th percentile so far.
        readonly ServerFrameStats _squad = new ServerFrameStats();

        void LogSquad()
        {
            var teams = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Combatant>>();
            foreach (var c in Combatant.All)
            {
                if (c == null || c.IsPlayer || !c.HasTeam || c.Dead.Value || !c.Standing) continue;
                int t = c.TeamId.Value;
                if (!teams.TryGetValue(t, out var list)) teams[t] = list = new System.Collections.Generic.List<Combatant>(4);
                list.Add(c);
            }
            foreach (var list in teams.Values)
            {
                if (list.Count < 2) continue;
                var centre = Vector3.zero;
                foreach (var c in list) centre += c.transform.position;
                centre /= list.Count;
                foreach (var c in list) { var d = c.transform.position - centre; d.y = 0f; _squad.Add(d.magnitude); }
            }
            Debug.Log($"[Squad] samples={_squad.Count} teams={teams.Count} medianToCentre={_squad.Percentile(50):F1}m p90={_squad.Percentile(90):F1}m");
        }

        static readonly string[] LevelNames = { "easy", "normal", "hard" };

        // The 80-player statistics row (ADR-313, #377): one file per scenario, rewritten every window, so the last window's row is the run's.
        void WriteStatsRow(float now)
        {
            var row = new BotStatsCsv().Add("scenario", _scenario).Add("level", Level >= 0 ? LevelNames[Level] : "mix").Add("seed", Seed).Add("seconds", now);
            int bots = 0, stalled = 0;
            var all = new System.Collections.Generic.List<float>();
            for (int l = 0; l < BotMatchStats.Levels; l++) { bots += BotMatchStats.Bots[l]; stalled += BotMatchStats.StalledBots[l]; all.AddRange(BotMatchStats.FirstGun[l]); }
            row.Add("bots", bots);
            // Bots that held a gun within 4 minutes of the drop, of all bots of the level (the dead count in the total).
            row.Add("armed4min_all", BotStatsCsv.Share(BotStatsCsv.CountAtMost(all, 240f), bots));
            for (int l = 0; l < BotMatchStats.Levels; l++)
            {
                var guns = BotMatchStats.FirstGun[l];
                row.Add("bots_" + LevelNames[l], BotMatchStats.Bots[l]);
                row.Add("armed4min_" + LevelNames[l], BotStatsCsv.Share(BotStatsCsv.CountAtMost(guns, 240f), BotMatchStats.Bots[l]));
                row.Add("firstGunMedian_" + LevelNames[l], BotStatsCsv.Percentile(guns, 50f));
                row.Add("kills_" + LevelNames[l], BotMatchStats.Kills[l]);
                row.Add("deaths_" + LevelNames[l], BotMatchStats.Deaths[l]);
            }
            row.Add("firstGunMedian_all", BotStatsCsv.Percentile(all, 50f)).Add("firstGunP90_all", BotStatsCsv.Percentile(all, 90f));
            row.Add("deaths3min", BotStatsCsv.CountAtMost(BotMatchStats.DeathSeconds, 180f)).Add("deaths5min", BotStatsCsv.CountAtMost(BotMatchStats.DeathSeconds, 300f)).Add("deathsTotal", BotMatchStats.DeathSeconds.Count);
            // The survivors minute by minute since the drop (of the bots): the curve the match is meant to follow (ADR-315).
            for (int minute = 1; minute <= 8; minute++) row.Add("alive" + minute + "min", bots - BotStatsCsv.CountAtMost(BotMatchStats.DeathSeconds, minute * 60f));
            row.Add("engagements", BotMatchStats.Engagements);
            row.Add("avgEngageSeconds", BotMatchStats.Engagements > 0 ? BotMatchStats.EngageSeconds / BotMatchStats.Engagements : 0f);
            row.Add("firstEngageArmed", BotStatsCsv.Share(BotMatchStats.FirstEngagesArmed, BotMatchStats.FirstEngages));
            row.Add("stalledBots", BotStatsCsv.Share(stalled, bots));
            row.Add("peekCycles", BotBrawlStats.PeekCycles).Add("avgExpose", BotBrawlStats.ExposeEpisodes > 0 ? BotBrawlStats.ExposeSeconds / BotBrawlStats.ExposeEpisodes : 0f);
            row.Add("hideShare", BotBrawlStats.EngagedSeconds > 0f ? BotBrawlStats.HideSeconds / BotBrawlStats.EngagedSeconds : 0f);
            row.Add("noiseInvestigated", BotStatsCsv.Share(BotBrawlStats.NoiseInvestigatedBots, BotBrawlStats.NoiseHeardBots));
            row.Add("frameMeanMs", _allFrames.Mean()).Add("frameP99Ms", _allFrames.Percentile(99f)).Add("combatantsLeft", Combatant.All.Count);
            File.WriteAllText(_statsFile, row.Header + "\n" + row.Line + "\n");
        }

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
            _allFrames.Add(Time.unscaledDeltaTime * 1000f);
            for (int k = 0; k < BotPerf.Slots; k++) _bot[k].Add(BotPerf.TakeMs(k));
            float now = Time.realtimeSinceStartup;
            if (now - _windowStart < WindowSeconds) return;
            float heap = System.GC.GetTotalMemory(false) / (1024f * 1024f);
            int rewinds = LagCompensation.Rewinds - _rewindsAtStart;
            string line = $"{now:F0},{_frames.Count},{_frames.Mean():F2},{_frames.Percentile(50):F2},{_frames.Percentile(95):F2},{_frames.Percentile(99):F2},{_frames.Max():F2},{heap:F0},{Combatant.All.Count},{rewinds}";
            for (int k = 0; k < BotPerf.Slots; k++) { line += $",{_bot[k].Mean():F3},{_bot[k].Percentile(99):F3}"; _bot[k].Clear(); }
            Debug.Log("[Perf] " + line);
            Debug.Log($"[PerfNet] t={now:F0} doorBroadcasts={DoorSystem.ServerBroadcasts} glassBroadcasts={GlassPanes.ServerBroadcasts}"); // cumulative; every client gets each one
            LogFarm(now);
            LogSquad();
            Debug.Log($"[Noise] botsHeard={BotBrawlStats.NoiseHeardBots} investigated={BotBrawlStats.NoiseInvestigatedBots} share={(BotBrawlStats.NoiseHeardBots > 0 ? (float)BotBrawlStats.NoiseInvestigatedBots / BotBrawlStats.NoiseHeardBots : 0f):P0}");
            Debug.Log($"[Cover] cycles={BotBrawlStats.PeekCycles} exposeEpisodes={BotBrawlStats.ExposeEpisodes} cutShort={BotBrawlStats.ExposeCutShort} avgExpose={(BotBrawlStats.ExposeEpisodes > 0 ? BotBrawlStats.ExposeSeconds / BotBrawlStats.ExposeEpisodes : 0f):F2}s hideShare={(BotBrawlStats.EngagedSeconds > 0f ? BotBrawlStats.HideSeconds / BotBrawlStats.EngagedSeconds : 0f):P0} searches={BotBrawlStats.CoverSearches}");
            Debug.Log($"[Brawl] unarmedEngages={BotBrawlStats.UnarmedEngages} fistFights={BotBrawlStats.FistFights} fleeStarts={BotBrawlStats.FleeStarts} fleeBroke={BotBrawlStats.FleeBroke} fleeCaught={BotBrawlStats.FleeCaught} corpseTrips={BotBrawlStats.CorpseTrips} arrived={BotBrawlStats.CorpseArrived} gaveUp={BotBrawlStats.CorpseGaveUp} retreatShare={BotBrawlStats.StateShare((int)BotState.Retreat):P1} engageShare={BotBrawlStats.StateShare((int)BotState.Engage):P1} roamShare={BotBrawlStats.StateShare((int)BotState.Roam):P1} lootShare={BotBrawlStats.StateShare((int)BotState.Loot):P1}");
            File.AppendAllText(_csv, line + "\n");
            WriteStatsRow(now);
            _frames.Clear();
            _windowStart = now;
            _rewindsAtStart = LagCompensation.Rewinds;
        }
    }
}
