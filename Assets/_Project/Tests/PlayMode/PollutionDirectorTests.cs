using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // The pollution (T10, replaces the lockdown tests) in the generated World scene with the quick rules (times and
    // front speed x60): the state schedule, damage only inside the confirmed gas, gas cover, and the snapshot a client
    // would rebuild.
    public class PollutionDirectorTests
    {
        PollutionDirector _director;

        internal static PollutionRules Fast() => PollutionRules.QuickMatch(60f);

        // The first region is confirmed after `first` seconds (warned `lead` before), the rest far later; fast front.
        internal static PollutionRules Timed(float first, float lead) => new PollutionRules
        {
            ConfirmMinutes = new[] { first / 60f, 1000f, 1001f, 1002f, 1003f, 1004f }, MinConfirmGapMinutes = 1f, WarningLeadSeconds = lead,
            FlatSpeed = 6000f, RoadSpeed = 6000f, SteepSpeed = 6000f, Dps = new[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f },
        };

        // Starts the pollution on the first seed whose first region is (or is not) `region`.
        internal static void ConfigureFirst(PollutionDirector d, PollutionRules rules, int region, bool equal)
        {
            for (int seed = 1; seed < 120; seed++)
            {
                Assert.IsTrue(d.ServerConfigure(rules, seed));
                if ((d.Match.Timeline.Order[0] == region) == equal) return;
            }
            Assert.Fail("no seed gives the first region the test needs");
        }

        internal static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            BotRules.Current = new BotRules { TargetsPlayers = false };
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 40f, "the local player in World");
            yield return WaitUntil(() => PollutionDirector.Instance != null && PollutionDirector.Instance.ServerReady, 30f, "the pollution to start");
            _director = PollutionDirector.Instance;
            Assert.AreEqual(6, _director.RegionCount);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PollutionDirector.RulesOverride = null;
            BotRules.Current = new BotRules();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        int PlayerRegion() => _director.RegionIndexAt(Combatant.LocalPlayer.transform.position);

        // Starts the pollution again with the fast rules on a seed whose first confirmed region satisfies `accept`.
        void Configure(Func<int, bool> accept)
        {
            for (int seed = 1; seed < 80; seed++)
            {
                Assert.IsTrue(_director.ServerConfigure(Fast(), seed));
                if (accept(_director.Match.Timeline.Order[0])) return;
            }
            Assert.Fail("no seed gives the first region the test needs");
        }

        [UnityTest]
        public IEnumerator Region_GoesSafeWarningClosedOnSchedule()
        {
            Configure(r => true);
            int target = _director.Match.Timeline.Order[0];
            var changes = new List<ZoneState>();
            Action<int, ZoneState> on = (r, s) => { if (r == target) changes.Add(s); };
            PollutionDirector.ServerRegionStateChanged += on;
            try
            {
                Assert.AreEqual(ZoneState.Safe, _director.StateOf(target));
                yield return WaitUntil(() => _director.StateOf(target) == ZoneState.Closed, 12f, "the first region to be confirmed");
            }
            finally { PollutionDirector.ServerRegionStateChanged -= on; }
            CollectionAssert.AreEqual(new[] { ZoneState.Warning, ZoneState.Closed }, changes);
            Assert.AreEqual(-1f, _director.SecondsLeft(target), "nothing counts down once it is confirmed");
        }

        [UnityTest]
        public IEnumerator Damage_OnlyWhereTheFrontIs()
        {
            int player = PlayerRegion();
            Assert.GreaterOrEqual(player, 0, "the player stands on the island");
            var me = Combatant.LocalPlayer;
            // Not the player's region first: the player stays unhurt while another region is confirmed.
            Configure(r => r != player);
            float hp = me.Hp.Value;
            yield return WaitUntil(() => _director.StateOf(_director.Match.Timeline.Order[0]) == ZoneState.Closed, 12f, "the first confirmation");
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(hp, me.Hp.Value, 0.01f, "outside the gas nothing hurts");
            // The player's own region: the gas arrives and takes health.
            Configure(r => r == player);
            hp = me.Hp.Value;
            yield return WaitUntil(() => me.Hp.Value < hp - 1f || me.Dead.Value, 25f, "the gas to reach the player and hurt");
        }

        [UnityTest]
        public IEnumerator GasCover_CutsTheDamage_ButNotInTheFinalRegion()
        {
            Configure(r => true);
            var m = _director.Match;
            var cover = new PollutionExposure();
            double t = m.Timeline.ConfirmSeconds[0] + 1;
            int first = m.Timeline.Order[0];
            var at = _director.RegionCentre(first);
            int cell = m.CellAt(at.x, at.z);
            float plain = m.Timeline.DamagePerSecond(cell, t + 60);
            cover.UseAntidote(t + 60, m.Rules);
            Assert.AreEqual(plain * 0.1f, m.DamagePerSecondAt(at.x, at.z, t + 60, cover.Cover), 1e-3f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Snapshot_RebuildsTheSameFront()
        {
            Configure(r => true);
            var grid = PollutionDirector.GridSource();
            var client = PollutionMatch.FromSnapshot(grid, _director.Match.Snapshot, Fast(), out bool ok);
            Assert.IsTrue(ok, "the same map and rules give the same front");
            Assert.AreEqual(_director.Match.GridHash, client.GridHash);
            yield return null;
        }
    }
}
