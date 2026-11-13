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
    // M3 region lockdown in the generated World scene, with a shortened LockdownRules so a whole closing runs in
    // seconds: the state machine, radiation (never on someone outside), missiles, bots as combatants, the pass
    // barrier that stops bodies but not bullets, and a key opening it for a while.
    public class ZoneDirectorTests
    {
        ZoneDirector _director;

        // A closing that starts after `first` seconds; later closings are far away (MinSecondsBetweenClosings).
        static LockdownRules Fast(float first = 0.3f, float warning = 0.4f, float lockdown = 60f, float bombardment = 60f) => new LockdownRules
        {
            FirstWarningSeconds = first, WarningSeconds = warning, LockdownSeconds = lockdown, BombardmentSeconds = bombardment,
            MinSecondsBetweenClosings = 1000f,
            RadiationDps = 8f, RadiationGraceSeconds = 0.2f, RadiationTickSeconds = 0.1f, ClosedDps = 8f,
            BlastIntervalSeconds = 0.3f, BlastWarningSeconds = 0.5f, BlastDamage = 1f, KeysPerRegion = 0,
            KeyOpenSeconds = 2f, SubZoneStartDelaySeconds = 1000f,
        };

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
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
            // Nobody in the scene is here to fight: only the zone hurts.
            BotRules.Current = new BotRules { TargetsPlayers = false };
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 40f, "the local player in World");
            _director = UnityEngine.Object.FindFirstObjectByType<ZoneDirector>();
            if (_director == null) _director = new GameObject("ZoneDirector").AddComponent<ZoneDirector>();
            yield return WaitUntil(() => _director.ServerReady, 15f, "the zone director to start");
            Assert.IsNotNull(_director.Geometry, "the director should know the map");
            Assert.AreEqual(4, _director.RegionCount);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            BotRules.Current = new BotRules();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        // A seed whose plan satisfies the condition, found the way the director plans (same inputs).
        int FindSeed(LockdownRules rules, Func<LockdownPlan, bool> accept)
        {
            var g = _director.Geometry;
            for (int seed = 1; seed < 2000; seed++)
            {
                var plan = Lockdown.Plan(g.RegionCount, g.PassA, g.PassB, seed, g.SubZoneCounts(), rules);
                if (accept(plan)) return seed;
            }
            Assert.Fail("no seed makes the plan the test needs");
            return 0;
        }

        int PlayerRegion() => _director.RegionIndexAt(Combatant.LocalPlayer.transform.position);

        static Combatant AnyBot()
        {
            foreach (var c in Combatant.All)
                if (c != null && !c.IsPlayer && c.IsServerInitialized && !c.Dead.Value) return c;
            return null;
        }

        // ---- State machine ----

        [UnityTest]
        public IEnumerator Region_GoesThroughEveryStateOnSchedule()
        {
            var rules = Fast(0.5f, 0.5f, 0.8f, 0.8f);
            int player = PlayerRegion();
            Assert.GreaterOrEqual(player, 0, "the player should stand on the island");
            // Not the player's region: it will be closed at the end.
            _director.ServerConfigure(rules, FindSeed(rules, p => p.Order[0] != player));

            int target = _director.ServerPlan.Order[0];
            var changes = new List<(int region, ZoneState state)>();
            Action<int, ZoneState> onChange = (r, s) => changes.Add((r, s));
            ZoneDirector.ServerRegionStateChanged += onChange;
            var seenAt = new Dictionary<ZoneState, float>();
            try
            {
                Assert.AreEqual(ZoneState.Safe, _director.StateOf(target));
                Assert.Greater(_director.SecondsToNextEvent, 0f);
                float end = Time.realtimeSinceStartup + 12f;
                while (_director.StateOf(target) != ZoneState.Closed)
                {
                    if (Time.realtimeSinceStartup > end) Assert.Fail("the region never closed; it was " + _director.StateOf(target));
                    var state = _director.StateOf(target);
                    if (!seenAt.ContainsKey(state)) seenAt[state] = _director.ServerElapsed;
                    yield return null;
                }
                seenAt[ZoneState.Closed] = _director.ServerElapsed;
            }
            finally
            {
                ZoneDirector.ServerRegionStateChanged -= onChange;
            }

            // On schedule: Warning at 0.5, Lockdown at 1.0, Bombardment at 1.8, Closed at 2.6 (frames make it a little late, never early).
            AssertAbout(seenAt, ZoneState.Warning, 0.5f);
            AssertAbout(seenAt, ZoneState.Lockdown, 1.0f);
            AssertAbout(seenAt, ZoneState.Bombardment, 1.8f);
            AssertAbout(seenAt, ZoneState.Closed, 2.6f);

            var order = new List<ZoneState>();
            foreach (var (region, state) in changes)
            {
                Assert.AreEqual(target, region, "only the closing region changes");
                order.Add(state);
            }
            CollectionAssert.AreEqual(new[] { ZoneState.Warning, ZoneState.Lockdown, ZoneState.Bombardment, ZoneState.Closed }, order);
            for (int r = 0; r < _director.RegionCount; r++)
                if (r != target) Assert.AreEqual(ZoneState.Safe, _director.StateOf(r), "region " + r);
            Assert.AreEqual(ZoneState.Closed, _director.StateOf(_director.RegionIdOf(target)), "StateOf(id) agrees with StateOf(index)");
        }

        static void AssertAbout(Dictionary<ZoneState, float> seenAt, ZoneState state, float scheduled)
        {
            Assert.IsTrue(seenAt.TryGetValue(state, out float at), "never saw " + state);
            Assert.GreaterOrEqual(at, scheduled - 0.001f, state + " came early");
            Assert.LessOrEqual(at, scheduled + 0.75f, state + " came late");
        }

        [UnityTest]
        public IEnumerator FinalRegion_IsNamedWhenTheLastClosingIsAnnounced()
        {
            // Closings 0.6 s apart; the last announcement (and so the reveal) comes at 0.2 + 2 x 0.6.
            var rules = Fast(0.2f, 0.2f, 0.3f, 0.3f);
            rules.MinSecondsBetweenClosings = 0.6f;
            rules.RadiationDps = 0.1f;
            rules.ClosedDps = 0.1f;
            _director.ServerConfigure(rules, FindSeed(rules, p => true));
            var plan = _director.ServerPlan;
            Assert.IsFalse(_director.FinalRegionRevealed);
            Assert.AreEqual(-1, _director.FinalRegion);
            yield return WaitUntil(() => _director.FinalRegionRevealed, 8f, "the final region to be named");
            Assert.GreaterOrEqual(_director.ServerElapsed, plan.FinalRevealAt - 0.001f);
            Assert.AreEqual(plan.FinalRegion, _director.FinalRegion);
            Assert.AreEqual(ZoneState.Safe, _director.StateOf(plan.FinalRegion));
        }

        // ---- Radiation ----

        [UnityTest]
        public IEnumerator Radiation_HurtsInsideALockedRegion_NotOutside_AndNeverAtOnce()
        {
            var rules = Fast(0.2f, 0.3f);
            var me = Combatant.LocalPlayer;
            int player = PlayerRegion();
            var outsider = FindOutsider(player);
            if (outsider == null) Assert.Ignore("no other combatant stands outside the player's region");

            var mine = new List<DamageCause>();
            var theirs = new List<DamageCause>();
            me.ServerDamageFilter = info => { mine.Add(info.Cause); return info.Amount; };
            outsider.ServerDamageFilter = info => { theirs.Add(info.Cause); return info.Amount; };
            float outsiderBefore = outsider.Hp.Value;

            _director.ServerConfigure(rules, FindSeed(rules, p => p.Order[0] == player));
            float start = Time.time;
            yield return WaitUntil(() => mine.Contains(DamageCause.Zone), 8f, "radiation damage on the player");
            Assert.IsFalse(me.Dead.Value, "radiation does not kill at once");
            Assert.Greater(Time.time - start, rules.FirstWarningSeconds + rules.WarningSeconds, "nothing hurts before the lockdown");
            Assert.Less(me.Hp.Value, 100f);

            // Let it run a little longer: the outsider must have taken nothing from the zone.
            yield return new WaitForSeconds(0.8f);
            if (_director.RegionIndexAt(outsider.transform.position) != player)
                CollectionAssert.DoesNotContain(theirs, DamageCause.Zone, "a combatant outside the region takes no radiation");
            Assert.GreaterOrEqual(outsider.Hp.Value, outsiderBefore - 0.01f);
        }

        Combatant FindOutsider(int region)
        {
            foreach (var c in Combatant.All)
            {
                if (c == null || c.IsPlayer || !c.IsServerInitialized || c.Dead.Value) continue;
                int r = _director.RegionIndexAt(c.transform.position);
                if (r >= 0 && r != region) return c;
            }
            return null;
        }

        [UnityTest]
        public IEnumerator Bots_CountAsCombatants_AndTakeRadiation()
        {
            var bot = AnyBot();
            if (bot == null) Assert.Ignore("the World scene has no bots");
            int region = _director.RegionIndexAt(bot.transform.position);
            Assert.GreaterOrEqual(region, 0);
            Assert.IsTrue(Combatant.All.Contains(bot));

            var causes = new List<DamageCause>();
            bot.ServerDamageFilter = info => { causes.Add(info.Cause); return info.Amount; };
            var rules = Fast(0.1f, 0.2f);
            _director.ServerConfigure(rules, FindSeed(rules, p => p.Order[0] == region));
            yield return WaitUntil(() => causes.Contains(DamageCause.Zone), 8f, "radiation damage on a bot");
        }

        // ---- Missiles ----

        [UnityTest]
        public IEnumerator Bombardment_ShowsAMarkerThenLands()
        {
            var rules = Fast(0.2f, 0.2f, 0.3f);
            int player = PlayerRegion();
            // The player's own region is not the target (a blast near them would only add noise).
            _director.ServerConfigure(rules, FindSeed(rules, p => p.Order[0] != player));

            var incoming = new List<(float time, float delay, float radius)>();
            var impacts = new List<float>();
            Action<Vector3, float, float> onIncoming = (pos, delay, radius) => incoming.Add((Time.time, delay, radius));
            Action<Vector3, float> onImpact = (pos, radius) => impacts.Add(Time.time);
            ZoneDirector.ClientBlastIncoming += onIncoming;
            ZoneDirector.ClientBlastImpact += onImpact;
            try
            {
                yield return WaitUntil(() => impacts.Count > 0, 10f, "a missile to land");
                Assert.Greater(incoming.Count, 0, "the marker comes first");
                Assert.AreEqual(rules.BlastWarningSeconds, incoming[0].delay, 1e-3f);
                Assert.AreEqual(rules.BlastOuterRadius, incoming[0].radius, 1e-3f);
                float gap = impacts[0] - incoming[0].time;
                Assert.GreaterOrEqual(gap, rules.BlastWarningSeconds - 0.05f, "the missile came before its warning ran out");
                Assert.LessOrEqual(gap, rules.BlastWarningSeconds + 0.75f);
            }
            finally
            {
                ZoneDirector.ClientBlastIncoming -= onIncoming;
                ZoneDirector.ClientBlastImpact -= onImpact;
            }
        }

        // ---- What the player sees ----

        [UnityTest]
        public IEnumerator PlayerInAClosingRegion_SeesBannerStatusAndFog()
        {
            var rules = Fast(0.2f, 2.5f);
            rules.RadiationDps = 0.1f;
            int player = PlayerRegion();
            _director.ServerConfigure(rules, FindSeed(rules, p => p.Order[0] == player));

            yield return WaitUntil(() => ZoneStatus.Current.Valid && ZoneStatus.Current.State == ZoneState.Warning, 6f, "the warning on the status card");
            Assert.AreEqual(_director.RegionIdOf(player), ZoneStatus.Current.RegionId);
            Assert.IsNotEmpty(ZoneStatus.Current.RegionName);
            Assert.Greater(ZoneStatus.Current.SecondsLeft, 0f);
            Assert.IsTrue(ZoneStatus.Current.HasExit, "a locked region shows the way out");
            Assert.AreEqual(1f, ZoneStatus.Current.ExitDirection.magnitude, 1e-3f);
            Assert.IsNotNull(ZoneBanner.Instance, "the banner installs itself next to the director");
            yield return WaitUntil(() => ZoneBanner.Instance.Visible, 2f, "the banner");
            StringAssert.Contains("지역 봉쇄 경고", ZoneBanner.Instance.Shown);
            StringAssert.Contains("초", ZoneBanner.Instance.Shown);

            yield return WaitUntil(() => ZoneStatus.Current.State == ZoneState.Lockdown, 6f, "the lockdown on the status card");
            yield return WaitUntil(() => ZoneFog.Current.Active, 6f, "the radiation fog");
            Assert.Greater(ZoneFog.Current.Density, ZoneFog.ActiveAbove);
        }

        [UnityTest]
        public IEnumerator LastRegionToClose_HurtsMoreThanTheFirst_AndTheBannerSaysSo()
        {
            var rules = Fast(0.2f, 1f);
            rules.MinSecondsBetweenClosings = 1.5f;
            rules.RadiationDps = 0.1f; // never kills while the test looks
            int player = PlayerRegion();
            _director.ServerConfigure(rules, FindSeed(rules, p => p.Order[p.Order.Length - 1] == player));
            Assert.AreEqual(1f, _director.StageOf(player), 1e-4f, "the last closing is stage 1");

            yield return WaitUntil(() => _director.StateOf(player) == ZoneState.Warning, 12f, "the late warning");
            Assert.AreEqual(0.1f * 5.5f, _director.RadiationDpsOf(player), 1e-4f, "what the lockdown will cost: 0.1 x 5.5");
            yield return WaitUntil(() => _director.StateOf(player) == ZoneState.Lockdown, 4f, "the lockdown");
            Assert.AreEqual(0.1f * 5.5f, _director.DamagePerSecondAt(Combatant.LocalPlayer.transform.position), 1e-3f);
            // Messages ("final region ...") take the banner first; then it names the radiation.
            yield return WaitUntil(() => ZoneBanner.Instance != null && ZoneBanner.Instance.Shown.Contains("방사능 초당 0.6"), 8f, "the banner naming the radiation");
            for (int r = 0; r < _director.RegionCount; r++)
                if (r != player && _director.StageOf(r) < 1f)
                    Assert.Less(_director.RadiationDpsOf(r), _director.RadiationDpsOf(player), "earlier closings hurt less");
        }

        // ---- Barriers ----

        PassBarrier BarrierOf(int pass)
        {
            foreach (var b in _director.GetComponentsInChildren<PassBarrier>(true))
                if (b.PassIndex == pass) return b;
            return null;
        }

        // Walks a player-sized capsule through a pass in one frame; returns how far past the pass line it got (metres).
        static float WalkThrough(Vector3 pass, Vector3 forward)
        {
            var go = new GameObject("Walker");
            try
            {
                var start = pass - forward * 4f;
                start.y = pass.y + 20f;
                if (Physics.Raycast(start, Vector3.down, out var down, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) start.y = down.point.y + 0.1f;
                else start.y = pass.y + 0.1f;
                var cc = go.AddComponent<CharacterController>();
                cc.radius = 0.35f;
                cc.height = 1.8f;
                cc.center = new Vector3(0f, 0.9f, 0f);
                go.transform.position = start;
                Physics.SyncTransforms();
                for (int i = 0; i < 40; i++) cc.Move(forward * 0.4f + Vector3.down * 0.1f);
                return Vector3.Dot(go.transform.position - pass, forward);
            }
            finally
            {
                UnityEngine.Object.Destroy(go);
            }
        }

        [UnityTest]
        public IEnumerator PassBarrier_BlocksBodiesButNotShots_AndOpensForAKey()
        {
            var g = _director.Geometry;
            Assert.Greater(g.PassCount, 0);
            int pass = 0;
            var rules = Fast(0.1f, 0.2f);
            rules.RadiationDps = 0.1f;
            int a = g.PassA[pass], b = g.PassB[pass];
            _director.ServerConfigure(rules, FindSeed(rules, p => p.Order[0] == a || p.Order[0] == b));

            var at = new Vector3(g.PassX[pass], g.PassY[pass], g.PassZ[pass]);
            float yaw = g.PassYawDeg[pass] * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));

            // Open before the lockdown: a body walks through.
            var barrier = BarrierOf(pass);
            Assert.IsNotNull(barrier, "a barrier stands on every pass");
            Assert.AreEqual(PassBarrier.IgnoreRaycastLayer, barrier.gameObject.layer, "shots and sight pass through it");
            Assert.IsFalse(barrier.Blocking);

            yield return WaitUntil(() => _director.IsPassLocked(pass), 6f, "the pass to lock");
            yield return null;
            yield return null;
            Assert.IsTrue(barrier.Blocking, "the barrier stands once a region beside it is in lockdown");
            Assert.IsTrue(barrier.Box.enabled);

            // Bullets and sight lines go through it.
            if (Physics.Raycast(at - forward * 6f + Vector3.up * 1.2f, forward, out var shot, 12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                Assert.IsNull(shot.collider.GetComponentInParent<PassBarrier>(), "a shot should not hit the barrier");

            // A body does not.
            Assert.Less(WalkThrough(at, forward), 0f, "a capsule should stop at the barrier");

            // A key opens it for a while.
            _director.ServerOpenPass(pass, rules.KeyOpenSeconds);
            Assert.IsTrue(_director.IsPassOpen(pass));
            yield return null;
            Assert.IsFalse(barrier.Blocking, "open for the key's seconds");
            Assert.Greater(WalkThrough(at, forward), 1.5f, "a capsule walks through the open pass");

            yield return new WaitForSeconds(rules.KeyOpenSeconds + 0.3f);
            yield return null;
            Assert.IsFalse(_director.IsPassOpen(pass));
            Assert.IsTrue(barrier.Blocking, "it closes again when the time is up");
            Assert.Less(WalkThrough(at, forward), 0f);
        }

        [UnityTest]
        public IEnumerator KeyHolder_UsesAKeyNextToALockedPassOnly()
        {
            var keys = KeyHolder.Local;
            if (keys == null) Assert.Ignore("KeyHolder is not on the player prefab yet (see the wiring notes)");
            var g = _director.Geometry;
            int pass = 0;
            var rules = Fast(0.1f, 0.2f);
            rules.RadiationDps = 0.1f;
            int a = g.PassA[pass], b = g.PassB[pass];
            _director.ServerConfigure(rules, FindSeed(rules, p => p.Order[0] == a || p.Order[0] == b));
            yield return WaitUntil(() => _director.IsPassLocked(pass), 6f, "the pass to lock");

            var at = new Vector3(g.PassX[pass], g.PassY[pass], g.PassZ[pass]);
            float yaw = g.PassYawDeg[pass] * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));

            // A pickup of the key kind is taken (and given back to nobody else).
            Assert.AreEqual(1, keys.ServerAdd(1));
            Assert.AreEqual(1, keys.Keys.Value);
            Assert.IsFalse(keys.ServerTryTake(new LootEntry { Kind = LootKind.Medkit }), "only key pickups");

            // Far from the pass: refused, the key is kept.
            PlayerController.Local.Teleport(at - forward * 60f + Vector3.up * 2f);
            yield return null;
            if (_director.PassDistance(pass, Combatant.LocalPlayer.transform.position) > rules.PassInteractRange)
            {
                Assert.IsFalse(keys.ServerUseKey(pass));
                Assert.AreEqual(1, keys.Keys.Value);
            }

            // Next to it: the key opens the pass for KeyOpenSeconds.
            PlayerController.Local.Teleport(at - forward * 2f + Vector3.up * 0.5f);
            yield return null;
            Assert.IsTrue(keys.ServerUseKey(pass));
            Assert.AreEqual(0, keys.Keys.Value);
            Assert.IsTrue(_director.IsPassOpen(pass));
            Assert.IsFalse(keys.ServerUseKey(pass), "no key left, and the pass is already open");
        }
    }
}
