using System;
using System.Collections;
using System.IO;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Inefficiency audit (docs/INEFFICIENCY_AUDIT.md): main-thread GC allocation and frame time of a host match in the
    // World scene with bots, the player alive (HUD running). Measurement only, no budget assert (PERF_BUDGET "프레임 중
    // GC 할당" has no judged method yet). Before/after numbers for the audit fixes come from this test.
    //   tools/unity.sh test playmode Game.Tests.MatchGcAllocTests
    // MATCH_GC_BOTS overrides the bot count. Writes Logs/perf/match_gc.json and a [MatchGc] log line.
    [Explicit, Category("Long")]
    public class MatchGcAllocTests
    {
        static readonly int Bots = int.TryParse(Environment.GetEnvironmentVariable("MATCH_GC_BOTS"), out int n) ? n : 40;
        const float WarmupSeconds = 15f;
        const int Frames = 600;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            BotRules.Current = GameData.Rules.Bot;
            MatchRules.Current = new MatchRules { BotCount = Bots };
            float start = Time.realtimeSinceStartup;
            SceneManager.LoadScene("World");
            yield return null;
            Debug.Log($"[MatchGc] World load {Time.realtimeSinceStartup - start:F2} s");
            float end = Time.realtimeSinceStartup + 60f;
            while (Combatant.LocalPlayer == null || MatchState.Instance == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for the local player in World");
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MatchRules.Current = GameData.Rules.Match ?? new MatchRules();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [Serializable]
        public class Result
        {
            public int Bots;
            public int Combatants;
            public long GcBytesPerFrameAvg;
            public long GcBytesPerFrameP50;
            public long GcBytesPerFrameMax;
            public long GcCountPerFrameAvg;
            public float MainThreadMsAvg;
            public float MainThreadMsP99;
        }

        [UnityTest, Timeout(5 * 60 * 1000)]
        public IEnumerator HostMatch_GcAllocAndMainThread()
        {
            float warm = Time.realtimeSinceStartup + WarmupSeconds;
            while (Time.realtimeSinceStartup < warm) yield return null;

#if UNITY_EDITOR
            if (Environment.GetEnvironmentVariable("MATCH_GC_BREAKDOWN") == "1") { yield return Breakdown(); yield break; }
#endif
            var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            var gcCount = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
            var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
            var bytes = new long[Frames];
            var ms = new float[Frames];
            long countSum = 0;
            yield return null;
            for (int i = 0; i < Frames; i++)
            {
                yield return null;
                bytes[i] = gc.LastValue;
                countSum += gcCount.LastValue;
                ms[i] = main.LastValue * 1e-6f;
            }
            gc.Dispose(); gcCount.Dispose(); main.Dispose();

            int combatants = 0;
            foreach (var c in Combatant.All) if (c != null) combatants++;
            long sum = 0;
            foreach (var b in bytes) sum += b;
            var sorted = (long[])bytes.Clone();
            Array.Sort(sorted);
            var stats = FrameStats.Compute(ms, 0);
            var r = new Result
            {
                Bots = Bots,
                Combatants = combatants,
                GcBytesPerFrameAvg = sum / Frames,
                GcBytesPerFrameP50 = sorted[Frames / 2],
                GcBytesPerFrameMax = sorted[Frames - 1],
                GcCountPerFrameAvg = countSum / Frames,
                MainThreadMsAvg = stats.AvgMs,
                MainThreadMsP99 = stats.P99Ms,
            };
            Directory.CreateDirectory("Logs/perf");
            File.WriteAllText("Logs/perf/match_gc.json", JsonUtility.ToJson(r, true));
            Debug.Log($"[MatchGc] bots {r.Bots} combatants {r.Combatants} gc avg {r.GcBytesPerFrameAvg} B p50 {r.GcBytesPerFrameP50} B max {r.GcBytesPerFrameMax} B allocs/frame {r.GcCountPerFrameAvg} main avg {r.MainThreadMsAvg:F2} ms p99 {r.MainThreadMsP99:F2} ms");
            Assert.Greater(combatants, 1);
        }

#if UNITY_EDITOR
        // MATCH_GC_BREAKDOWN=1: profile 300 frames and sum GC.Alloc bytes per calling marker ("parent < grandparent").
        // Writes Logs/perf/match_gc_breakdown.txt. The profiler itself costs time, so main-thread numbers are not taken.
        static IEnumerator Breakdown()
        {
            const int frames = 300;
            UnityEditorInternal.ProfilerDriver.ClearAllFrames();
            UnityEditorInternal.ProfilerDriver.enabled = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            for (int i = 0; i < frames + 2; i++) yield return null;
            UnityEngine.Profiling.Profiler.enabled = false;
            UnityEditorInternal.ProfilerDriver.enabled = false;

            var sums = new System.Collections.Generic.Dictionary<string, double>();
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            int last = UnityEditorInternal.ProfilerDriver.lastFrameIndex, first = Mathf.Max(UnityEditorInternal.ProfilerDriver.firstFrameIndex, last - frames + 1);
            int used = 0;
            double total = 0;
            var kids = new System.Collections.Generic.List<int>();
            for (int f = first; f <= last; f++)
            {
                using var view = UnityEditorInternal.ProfilerDriver.GetHierarchyFrameDataView(f, 0,
                    UnityEditor.Profiling.HierarchyFrameDataView.ViewModes.Default, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory, false);
                if (view == null || !view.valid) continue;
                used++;
                void Walk(int id, string parent, string grand)
                {
                    var children = new System.Collections.Generic.List<int>();
                    view.GetItemChildren(id, children);
                    foreach (var c in children)
                    {
                        string name = view.GetItemName(c);
                        if (name == "GC.Alloc")
                        {
                            double b = view.GetItemColumnDataAsDouble(c, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory);
                            string key = parent + " < " + grand;
                            sums[key] = (sums.TryGetValue(key, out var v) ? v : 0) + b;
                            counts[key] = (counts.TryGetValue(key, out var n) ? n : 0) + 1;
                            total += b;
                        }
                        else Walk(c, name, parent);
                    }
                }
                Walk(view.GetRootItemID(), "root", "");
            }
            var keys = new System.Collections.Generic.List<string>(sums.Keys);
            keys.Sort((a, b) => sums[b].CompareTo(sums[a]));
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"frames {used} total {total / Mathf.Max(1, used):F0} B/frame");
            for (int i = 0; i < Mathf.Min(60, keys.Count); i++)
                sb.AppendLine($"{sums[keys[i]] / Mathf.Max(1, used),8:F0} B/f {counts[keys[i]] / (float)Mathf.Max(1, used),6:F1} n/f  {keys[i]}");
            Directory.CreateDirectory("Logs/perf");
            File.WriteAllText("Logs/perf/match_gc_breakdown.txt", sb.ToString());
            Debug.Log("[MatchGcBreakdown]\n" + sb);
            Assert.Greater(used, 0, "profiler frames");
        }
#endif
    }
}
