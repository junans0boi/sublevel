using System;
using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // M4 gate (#77, ROADMAP "2+ region full match with bots completes"): a whole match in the World scene, played to
    // the end by bots alone. The player is killed at the start (MatchState counts it as the first one out), 15 bots
    // play a quick lockdown (LockdownRules.QuickMatch) with the clock running TimeScale times faster.
    // Must end with exactly one survivor, the final region never in the closing order and nobody alive in a closed
    // region. A compact log goes to Logs/match/match-<time>.json.
    // Long: skip it in normal runs (TEST_CATEGORY='!Long'); run only it with
    //   TEST_CATEGORY=Long tools/unity.sh test playmode
    // or by name: tools/unity.sh test playmode Game.Tests.MatchSimulationTests
    [Category("Long")]
    public class MatchSimulationTests
    {
        // FishNet ticks on unscaled time; everything the match decides runs on the server in Update with scaled time
        // (NavMeshAgent, fire cadence, lockdown clock, radiation), and the host reads the server's state directly.
        // 2 keeps the frame step well under Time.maximumDeltaTime at editor frame rates. Set 1 if anything misbehaves.
        const float TimeScale = 2f;
        const int Bots = 15;
        const float RealSecondsLimit = 6f * 60f;
        const int DefaultSeed = 77;

        float _timeScaleBefore;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        // MATCH_SIM_SEED overrides the seed of the lockdown plan and the deployment (the log names both).
        static int Seed()
        {
            var text = Environment.GetEnvironmentVariable("MATCH_SIM_SEED");
            return int.TryParse(text, out int seed) ? seed : DefaultSeed;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _timeScaleBefore = Time.timeScale;
            // Data values, whatever an earlier test left behind.
            BotRules.Current = GameData.Rules.Bot;
            MatchRules.Current = new MatchRules { BotCount = Bots };
            ZoneDirector.RulesOverride = LockdownRules.QuickMatch();
            MatchDirector.LockdownSeedOverride = Seed();
            MatchDirector.DeploymentSeedOverride = Seed();
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => Combatant.LocalPlayer != null && MatchState.Instance != null, 40f, "the local player in World");
            yield return WaitUntil(() => ZoneDirector.Instance != null && ZoneDirector.Instance.ServerReady, 15f, "the zone director to start");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = _timeScaleBefore;
            MatchRules.Current = GameData.Rules.Match ?? new MatchRules();
            ZoneDirector.RulesOverride = null;
            MatchDirector.LockdownSeedOverride = null;
            MatchDirector.DeploymentSeedOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static int AliveCount(out Combatant last)
        {
            last = null;
            int alive = 0;
            foreach (var c in Combatant.All)
            {
                if (c == null || !c.IsServerInitialized || c.Dead.Value) continue;
                alive++;
                last = c;
            }
            return alive;
        }

        [UnityTest, Timeout(9 * 60 * 1000)]
        public IEnumerator FullMatch_FifteenBots_EndsWithOneSurvivor()
        {
            var zones = ZoneDirector.Instance;
            var state = MatchState.Instance;
            var plan = zones.ServerPlan;
            Assert.IsNotNull(plan, "the server plans the lockdown");
            Assert.AreEqual(4, zones.RegionCount);
            CollectionAssert.DoesNotContain(plan.Order, plan.FinalRegion, "the final region must never be in the closing order");
            Assert.AreEqual(zones.RegionCount - 1, plan.Order.Length);

            var recorder = MatchRecorder.Attach();
            yield return WaitUntil(() => recorder.PlanKnown, 5f, "the recorder to read the plan");

            // Everyone is in: the player and the bots.
            yield return WaitUntil(() => AliveCount(out _) == Bots + 1, 15f, "the player and " + Bots + " bots");
            Assert.AreEqual(Bots, MatchDirector.Instance.BotCount);

            // Only bots play: the player is the first one out (zone damage passes the spawn protection).
            var player = Combatant.LocalPlayer;
            player.ServerTakeDamage(new DamageInfo { Amount = 10000f, Cause = DamageCause.Zone, Zone = HitZone.Body });
            Assert.IsTrue(player.Dead.Value, "the player should be out of the match");

            Time.timeScale = TimeScale;
            float realEnd = Time.realtimeSinceStartup + RealSecondsLimit;
            int finalRegion = plan.FinalRegion;
            int alive = Bots;
            Combatant last = null;
            int aliveWhenOver = -1;
            while (true)
            {
                Assert.AreEqual(ZoneState.Safe, zones.StateOf(finalRegion), "the final region must never start closing");
                alive = AliveCount(out last);
                if (state.IsOver || alive <= 1)
                {
                    aliveWhenOver = alive;
                    break;
                }
                if (Time.realtimeSinceStartup > realEnd) break;
                yield return null;
            }
            Time.timeScale = 1f;
            // The host's own client hears the result a tick later.
            float heardBy = Time.realtimeSinceStartup + 5f;
            while (!state.IsOver && Time.realtimeSinceStartup < heardBy) yield return null;

            var log = recorder.Finish();
            var path = recorder.Write(MatchRecorder.DefaultFolder);
            Debug.Log($"[MatchSimulation] over {log.over} in {log.durationSeconds:0} s match time ({log.durationRealSeconds:0} s real), " +
                $"winner {log.winner}, final region {log.finalRegion}, kills {log.kills.Count}, stalls {log.stallTotal}, log {path}");

            Assert.IsTrue(state.IsOver, $"the match did not end within {RealSecondsLimit:0} s real time ({alive} still alive; log {path})");
            Assert.AreEqual(1, aliveWhenOver, "exactly one survivor when the match ended");
            Assert.AreEqual(1, AliveCount(out last), "exactly one survivor");
            Assert.GreaterOrEqual(state.WinnerId, 0, "a winner is named");
            Assert.AreEqual(last.ObjectId, state.WinnerId, "the winner is the one left standing");
            Assert.IsFalse(last.IsPlayer, "the player was out from the start");
            Assert.AreEqual(0, log.aliveInClosedRegionAtEnd, "nobody alive in a closed region");
            Assert.AreNotEqual(ZoneState.Closed, zones.StateAt(last.transform.position), "the winner stands in a region that is not closed");
            Assert.IsFalse(log.finalInClosingOrder);
            Assert.AreEqual(Bots + 1, log.participants);
        }
    }
}
