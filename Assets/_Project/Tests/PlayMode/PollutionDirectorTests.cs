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

        // #267: a filter used the way the Y key uses it (KeyHolder.RequestGasCover) halves the damage the gas really deals.
        [UnityTest]
        public IEnumerator Filter_UsedInTheGas_HalvesTheDamageTaken()
        {
            var me = Combatant.LocalPlayer;
            var keys = me.GetComponent<KeyHolder>();
            // Not every region can close first: take the seed's first one and stand in one of its stations.
            Configure(r => true);
            int first = _director.Match.Timeline.Order[0];
            var sites = ElevatorDoors.Instance.Sites;
            int site = -1;
            for (int i = 0; i < sites.Count && site < 0; i++) if (sites[i].Region == first) site = i;
            Assert.GreaterOrEqual(site, 0, "a station in the first region");
            PlayerController.Local.Teleport(sites[site].Spawn + Vector3.up * 0.05f);
            yield return null;
            var at = me.transform.position;
            yield return WaitUntil(() => _director.Match.DamagePerSecondAt(at.x, at.z, _director.Now, default) > 0f, 25f, "the gas to reach the player");
            Assert.IsTrue(keys.ServerTryTake(new LootEntry { Kind = LootKind.GasFilter, Amount = 1 }));
            yield return null;
            keys.RequestGasCover();
            yield return WaitUntil(() => keys.CoverUntil.Value > _director.Now, 3f, "the filter in use");
            Assert.AreEqual(0, keys.Filters.Value, "the filter is used up");
            Assert.AreEqual(_director.Rules.FilterFactor, keys.CoverFactor.Value, 1e-4f);

            // Health lost over 3 s against what the gas would deal uncovered over the same time (it may rise with a confirmation).
            float hp = me.Hp.Value, plain = 0f, end = Time.time + 3f;
            while (Time.time < end)
            {
                plain += _director.Match.DamagePerSecondAt(at.x, at.z, _director.Now, default) * Time.deltaTime;
                yield return null;
            }
            float lost = hp - me.Hp.Value;
            Debug.Log($"[GasCover] lost {lost:0.00} uncovered would be {plain:0.00}");
            Assert.Greater(lost, 0f, "the gas still hurts through a filter");
            Assert.That(lost / plain, Is.InRange(0.3f, 0.7f), "about half the damage (FilterFactor 0.5; 0.5 s ticks)");
        }

        // #270 in the game: the player's region confirmed (gas, red tint, damage line), the next region warned (it blinks
        // on the minimap and the map), the gas front drawn, a filter in use (badge with its icon), siren and Geiger hooks.
        //   POLL_SHOT_DIR=<dir> saves pollution_hud.png and pollution_map.png.
        [UnityTest]
        public IEnumerator Hud_GasFrontBlinkCoverBadgeAndSoundHooks()
        {
            var me = Combatant.LocalPlayer;
            var keys = me.GetComponent<KeyHolder>();
            me.ServerDamageFilter = _ => 0f; // the look, not the damage
            var rules = Timed(3f, 2f);
            rules.ConfirmMinutes[1] = 40f / 60f;
            rules.WarningLeadSeconds = 75f; // the second confirmation is pushed to >= 63 s (1 min gap): warned from the start
            int sirens = ZoneBanner.SirenCount;
            // Not every region can close first: take the seed's first one and stand in one of its stations.
            Assert.IsTrue(_director.ServerConfigure(rules, 1));
            int player = _director.Match.Timeline.Order[0];
            int second = _director.Match.Timeline.Order[1];
            var sites = ElevatorDoors.Instance.Sites;
            int site = -1;
            for (int i = 0; i < sites.Count && site < 0; i++) if (sites[i].Region == player) site = i;
            Assert.GreaterOrEqual(site, 0, "a station in the first region");
            PlayerController.Local.Teleport(sites[site].Spawn + Vector3.up * 0.05f);
            yield return null;
            Assert.AreEqual(player, PlayerRegion());
            yield return WaitUntil(() => PollutionHudView.Model.InGas, 15f, "the gas at the player");
            yield return WaitUntil(() => _director.StateOf(second) == ZoneState.Warning, 10f, "the second region warned");
            Assert.IsTrue(keys.ServerTryTake(new LootEntry { Kind = LootKind.GasFilter, Amount = 1 }));
            yield return null;
            keys.RequestGasCover();
            yield return new WaitForSeconds(1.5f);

            var view = UnityEngine.Object.FindFirstObjectByType<PollutionHudView>();
            Assert.IsNotNull(view);
            StringAssert.Contains("방독", view.GasText);
            Assert.IsTrue(view.CoverBadgeShown, "the cover badge shows");
            Assert.AreEqual("gas_filter", view.CoverIconKey);
            Assert.IsNotNull(ItemIcons.Get("gas_filter"), "the filter has its own icon");
            Assert.Greater(view.TintAlpha, 0f, "the red tint");
            StringAssert.Contains(_director.RegionNameOf(player), Hud.Instance.Hazard.LineText, "sealed names the region the player is in");
            // HUD v2 zone panel over the minimap: the warned region and its clock, the sealed count, the gas outline and line.
            var zone = Hud.Instance.Zone;
            Assert.IsTrue(zone.Shown && zone.Hot, "the zone panel shows, outlined in the gas");
            StringAssert.Contains(_director.RegionNameOf(second) + " 봉쇄까지", zone.TitleText);
            Assert.AreEqual("봉쇄된 구역 1 / 6", zone.CountText);
            StringAssert.Contains("방독", zone.GasText);
            Assert.That(zone.Bar, Is.InRange(0f, 1f));
            Assert.Greater(ZoneBanner.SirenCount, sirens, "the siren hook fired on the warnings");
            Assert.Greater(PollutionHudView.GeigerClicksPerSecond, 2f, "the Geiger hook ticks in the gas");

            var minimap = Hud.Instance.Minimap;
            Assert.Greater(minimap.FrontTexels, 0, "the gas front is drawn");
            bool sawBright = false, sawDim = false;
            float until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until) { sawBright |= minimap.WarningBright; sawDim |= !minimap.WarningBright; yield return null; }
            Assert.IsTrue(sawBright && sawDim, "the warned region blinks");

            string dir = Environment.GetEnvironmentVariable("POLL_SHOT_DIR");
            if (!string.IsNullOrEmpty(dir)) yield return UiShot.Save(System.IO.Path.Combine(dir, "pollution_hud.png"), 1600, 900);
            UiFocus.Set(UiScreen.Map);
            yield return null;
            yield return null;
            Assert.IsTrue(MapScreen.Instance.ZonesShown, "the map shows the region states and the front");
            if (!string.IsNullOrEmpty(dir)) yield return UiShot.Save(System.IO.Path.Combine(dir, "pollution_map.png"), 1600, 900);
            UiFocus.Close();
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
