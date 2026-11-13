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
    // Match score keeping in the real TestBox scene (one player, three dummies): alive count, kills, placement,
    // the winner, and the kill feed that shows it.
    public class MatchStateTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = false;
                return f;
            }
        }

        ScriptedInput _input;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        IEnumerator LoadTestBox()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            yield return WaitUntil(() => MatchState.Instance != null && MatchState.Instance.TotalCount == 4, 5f, "all four combatants counted");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UiFocus.Close();
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        // In spawn order, which is also the order of their names.
        static List<Combatant> Dummies()
        {
            var dummies = new List<Combatant>();
            foreach (var c in Combatant.All)
                if (!c.IsPlayer) dummies.Add(c);
            return dummies;
        }

        static void Kill(Combatant victim, Combatant attacker, int weaponId, HitZone zone = HitZone.Body, DamageCause cause = DamageCause.Shot)
        {
            var hit = new DamageInfo { Amount = 1000f, Attacker = attacker, Zone = zone, WeaponId = weaponId, Cause = cause };
            victim.ServerTakeDamage(in hit);
        }

        [UnityTest]
        public IEnumerator MatchStartsWithEveryoneAlive_AndNoResult()
        {
            yield return LoadTestBox();
            var state = MatchState.Instance;
            Assert.AreSame(state, MatchDirector.Instance.State);
            Assert.AreEqual(4, state.AliveCount);
            Assert.AreEqual(4, state.TotalCount);
            Assert.IsFalse(state.IsOver);
            Assert.AreEqual(0, state.KillsPosted);
            Assert.AreEqual(0, state.LocalKills);
            Assert.AreEqual(0, state.LocalPlacement);
            Assert.AreEqual(MatchOutcome.Playing, state.LocalOutcome);
            Assert.AreEqual("플레이어", state.ServerNameOf(Combatant.LocalPlayer));
            Assert.AreEqual("훈련 더미 1", state.ServerNameOf(Dummies()[0]));
            Assert.IsNotNull(KillFeed.Instance, "the match brings its kill feed");
            Assert.AreEqual(0, KillFeed.Instance.VisibleCount);
        }

        [UnityTest]
        public IEnumerator KillingEveryDummy_CountsKills_FillsTheFeed_AndDeclaresTheWinner()
        {
            yield return LoadTestBox();
            var state = MatchState.Instance;
            var me = Combatant.LocalPlayer;
            var dummies = Dummies();
            Assert.AreEqual(3, dummies.Count);

            Kill(dummies[0], me, 3, HitZone.Head);
            yield return WaitUntil(() => state.KillsPosted == 1, 3f, "first kill replicated");
            Assert.AreEqual(1, state.LocalKills);
            Assert.AreEqual(3, state.AliveCount);
            Assert.IsFalse(state.IsOver);
            Assert.AreEqual(4, state.PlacementOf(dummies[0]), "the first to die is last");

            Kill(dummies[1], me, 3);
            Kill(dummies[2], me, 1);
            yield return WaitUntil(() => state.KillsPosted == 3 && state.IsOver, 3f, "three kills and the match decided");

            Assert.AreEqual(3, state.LocalKills);
            Assert.AreEqual(1, state.AliveCount);
            Assert.AreEqual(4, state.TotalCount);
            Assert.AreEqual(3, state.PlacementOf(dummies[1]));
            Assert.AreEqual(2, state.PlacementOf(dummies[2]));
            Assert.AreEqual(1, state.LocalPlacement);
            Assert.AreEqual(MatchOutcome.Won, state.LocalOutcome);
            Assert.AreEqual(me.ObjectId, state.WinnerId);
            Assert.AreEqual("플레이어", state.WinnerName);

            Assert.AreEqual(3, state.Feed.Count);
            var first = state.Feed[0];
            Assert.AreEqual("플레이어", first.KillerName);
            Assert.AreEqual("훈련 더미 1", first.VictimName);
            Assert.AreEqual(3, first.WeaponId);
            Assert.IsTrue(first.Headshot);
            Assert.IsFalse(state.Feed[1].Headshot);
            Assert.AreEqual((byte)DamageCause.Shot, state.Feed[2].Cause);

            var feed = KillFeed.Instance;
            Assert.AreEqual(3, feed.VisibleCount);
            string rifle = GameData.Weapons.Find(3).Name, pistol = GameData.Weapons.Find(1).Name;
            Assert.AreEqual("플레이어  [" + pistol + "]  훈련 더미 3", feed.TextAt(0), "newest on top");
            Assert.AreEqual("플레이어  [" + rifle + "]  훈련 더미 2", feed.TextAt(1));
            Assert.AreEqual("플레이어  [" + rifle + " · 헤드샷]  훈련 더미 1", feed.TextAt(2));
        }

        [UnityTest]
        public IEnumerator PlayerDeath_IsPlacedLast_AndTheMatchGoesOn()
        {
            yield return LoadTestBox();
            var state = MatchState.Instance;
            var me = Combatant.LocalPlayer;
            var dummy = Dummies()[1];

            Kill(me, dummy, 1);
            yield return WaitUntil(() => state.LocalOutcome == MatchOutcome.Eliminated, 3f, "local player eliminated");
            Assert.AreEqual(4, state.LocalPlacement);
            Assert.AreEqual(4, state.TotalCount);
            Assert.AreEqual(3, state.AliveCount);
            Assert.AreEqual(0, state.LocalKills);
            Assert.AreEqual(1, state.KillsOf(dummy));
            Assert.IsFalse(state.IsOver, "three dummies are still standing");
            Assert.AreEqual("훈련 더미 2  [" + GameData.Weapons.Find(1).Name + "]  플레이어", KillFeed.Instance.TextAt(0));
        }

        [UnityTest]
        public IEnumerator DeathWithoutAKiller_NamesTheCause_AndGivesNobodyAKill()
        {
            yield return LoadTestBox();
            var state = MatchState.Instance;
            var dummies = Dummies();

            Kill(dummies[0], null, 0, HitZone.Body, DamageCause.Fall);
            yield return WaitUntil(() => state.KillsPosted == 1, 3f, "death replicated");
            Assert.AreEqual(-1, state.Feed[0].KillerId);
            Assert.AreEqual(0, state.LocalKills);
            Assert.AreEqual(3, state.AliveCount);
            Assert.AreEqual("훈련 더미 1  [낙하]", KillFeed.Instance.TextAt(0));

            Assert.AreEqual("가  [폭발]  나", KillFeed.Format(new KillEntry { KillerId = 1, VictimId = 2, KillerName = "가", VictimName = "나", Cause = (byte)DamageCause.Blast }));
            Assert.AreEqual("나  [방사능]", KillFeed.Format(new KillEntry { KillerId = -1, VictimId = 2, VictimName = "나", Cause = (byte)DamageCause.Zone }));
        }

        [UnityTest]
        public IEnumerator FeedLines_FadeAway()
        {
            yield return LoadTestBox();
            Kill(Dummies()[0], Combatant.LocalPlayer, 3);
            yield return WaitUntil(() => KillFeed.Instance.VisibleCount == 1, 3f, "line shown");
            yield return new WaitForSeconds(KillFeed.ShowSeconds + 0.5f);
            Assert.AreEqual(0, KillFeed.Instance.VisibleCount);
            Assert.AreEqual(1, MatchState.Instance.KillsPosted, "the state keeps the kill");
        }
    }
}
