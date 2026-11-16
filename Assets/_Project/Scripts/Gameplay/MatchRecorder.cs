using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // What happened in one match, compact enough to read by eye (#77): the plan, every region change, every kill,
    // the end and why bots got stuck. Written as JSON by MatchRecorder.
    [Serializable]
    public class MatchLog
    {
        [Serializable]
        public struct RegionEvent
        {
            public float t;          // match seconds since recording began
            public string region;
            public string state;
        }

        [Serializable]
        public struct Kill
        {
            public float t;
            public string killer;    // "" for the zone, a fall, a missile
            public string victim;
            public string cause;
            public int weapon;
            public string region;    // where the victim died
            public int alive;        // left standing after this one
        }

        [Serializable]
        public struct Stall
        {
            public string reason;
            public int count;
        }

        public string started = "";
        public string scene = "";
        public int lockdownSeed;
        public int deploymentSeed;
        public int participants;
        public int bots;
        public float timeScale = 1f;
        public string[] regions = new string[0];
        public string[] closingOrder = new string[0];
        public string finalRegion = "";
        public bool finalInClosingOrder;
        public float finalRevealedAt = -1f;
        public List<RegionEvent> regionEvents = new List<RegionEvent>();
        public List<Kill> kills = new List<Kill>();
        public bool over;
        public string winner = "";
        public int winnerKills;
        public string winnerRegion = "";
        public int aliveAtEnd;
        public int aliveInClosedRegionAtEnd;
        public float durationSeconds;      // match time
        public float durationRealSeconds;  // wall clock
        public int stallTotal;
        public List<Stall> stalls = new List<Stall>();
    }

    // Records a match on the server (host) into a MatchLog: add it once the match scene is loaded, call Finish at
    // the end and Write the file. Used by the match simulation test and `-autoplay` (#77).
    public class MatchRecorder : MonoBehaviour
    {
        public MatchLog Log { get; } = new MatchLog();
        public int KillCount => Log.kills.Count;
        public float Elapsed => Time.time - _startTime;
        public bool PlanKnown { get; private set; }

        float _startTime;
        float _startReal;
        bool _subscribed;

        // The folder the editor keeps match logs in: <project>/Logs/match.
        public static string DefaultFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "match"));

        // A recorder in the active scene (it goes away with the scene).
        public static MatchRecorder Attach()
        {
            var go = new GameObject("MatchRecorder");
            return go.AddComponent<MatchRecorder>();
        }

        void Awake()
        {
            _startTime = Time.time;
            _startReal = Time.realtimeSinceStartup;
            Log.started = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            Log.scene = gameObject.scene.name;
        }

        void OnEnable()
        {
            if (_subscribed) return;
            _subscribed = true;
            Combatant.ServerDied += OnDied;
            PollutionDirector.ServerRegionStateChanged += OnRegion;
            PollutionDirector.ServerFinalRevealed += OnFinal;
        }

        void OnDisable()
        {
            if (!_subscribed) return;
            _subscribed = false;
            Combatant.ServerDied -= OnDied;
            PollutionDirector.ServerRegionStateChanged -= OnRegion;
            PollutionDirector.ServerFinalRevealed -= OnFinal;
        }

        void Update()
        {
            if (!PlanKnown) TryReadPlan();
        }

        void TryReadPlan()
        {
            var zones = PollutionDirector.Instance;
            var plan = zones != null && zones.Match != null ? zones.Match.Timeline : null;
            if (plan == null) return;
            PlanKnown = true;
            var director = MatchDirector.Instance;
            if (director != null)
            {
                Log.lockdownSeed = director.LockdownSeed;
                Log.deploymentSeed = director.DeploymentSeed;
                Log.bots = director.BotCount;
            }
            Log.regions = new string[zones.RegionCount];
            for (int r = 0; r < zones.RegionCount; r++) Log.regions[r] = zones.RegionIdOf(r);
            Log.closingOrder = new string[plan.Order.Length];
            for (int i = 0; i < plan.Order.Length; i++)
            {
                Log.closingOrder[i] = zones.RegionIdOf(plan.Order[i]);
                if (plan.Order[i] == plan.FinalRegion && i < plan.Order.Length - 1) Log.finalInClosingOrder = true; // the final region is the last one confirmed
            }
            Log.finalRegion = zones.RegionIdOf(plan.FinalRegion);
        }

        void OnRegion(int region, ZoneState state)
        {
            var zones = PollutionDirector.Instance;
            Log.regionEvents.Add(new MatchLog.RegionEvent { t = Round(Elapsed), region = zones != null ? zones.RegionIdOf(region) : region.ToString(), state = state.ToString() });
        }

        void OnFinal(int region)
        {
            Log.finalRevealedAt = Round(Elapsed);
        }

        void OnDied(Combatant victim, DamageInfo info)
        {
            var state = MatchState.Instance;
            var zones = PollutionDirector.Instance;
            var killer = info.Attacker != null && info.Attacker != victim ? info.Attacker : null;
            int alive = 0;
            foreach (var c in Combatant.All)
                if (c != null && c.IsServerInitialized && !c.Dead.Value) alive++;
            Log.kills.Add(new MatchLog.Kill
            {
                t = Round(Elapsed),
                killer = killer != null ? NameOf(killer, state) : "",
                victim = NameOf(victim, state),
                cause = info.Cause.ToString(),
                weapon = info.WeaponId,
                region = zones != null && victim != null ? zones.RegionIdAt(victim.transform.position) : "",
                alive = alive,
            });
        }

        static string NameOf(Combatant c, MatchState state)
        {
            if (c == null) return "";
            string name = state != null ? state.ServerNameOf(c) : "";
            return string.IsNullOrEmpty(name) ? c.name : name;
        }

        // Fills in the end of the match from the server's view right now.
        public MatchLog Finish()
        {
            if (!PlanKnown) TryReadPlan();
            var state = MatchState.Instance;
            var zones = PollutionDirector.Instance;
            Log.over = state != null && state.IsOver;
            Log.participants = 0;
            Log.aliveAtEnd = 0;
            Log.aliveInClosedRegionAtEnd = 0;
            Combatant last = null;
            foreach (var c in Combatant.All)
            {
                if (c == null || !c.IsServerInitialized) continue;
                Log.participants++;
                if (c.Dead.Value) continue;
                Log.aliveAtEnd++;
                last = c;
                if (zones != null && zones.StateAt(c.transform.position) == ZoneState.Closed) Log.aliveInClosedRegionAtEnd++;
            }
            if (state != null && !string.IsNullOrEmpty(state.WinnerName)) Log.winner = state.WinnerName;
            else if (last != null && Log.aliveAtEnd == 1) Log.winner = NameOf(last, state);
            if (last != null && Log.aliveAtEnd == 1)
            {
                Log.winnerKills = state != null ? state.KillsOf(last) : 0;
                Log.winnerRegion = zones != null ? zones.RegionIdAt(last.transform.position) : "";
            }
            Log.durationSeconds = Round(Elapsed);
            Log.durationRealSeconds = Round(Time.realtimeSinceStartup - _startReal);
            Log.timeScale = Time.timeScale;
            Log.stallTotal = BotStats.Total;
            Log.stalls.Clear();
            foreach (var pair in BotStats.Snapshot()) Log.stalls.Add(new MatchLog.Stall { reason = pair.Key, count = pair.Value });
            return Log;
        }

        // Writes the log as match-<time>.json into `folder` (created when missing); returns the file's path.
        public string Write(string folder)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "match-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(Log, true));
            Debug.Log("[MatchRecorder] match log: " + path);
            return path;
        }

        static float Round(float seconds) => Mathf.Round(seconds * 10f) / 10f;
    }
}
