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
    // The end-of-match screen in the real TestBox scene (one player, three dummies): hidden while playing,
    // "승리" for the last one standing, "탈락" with the placement for the dead, and a restart into a fresh match.
    public class ResultScreenTests
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

        static void Kill(Combatant victim, Combatant attacker, int weaponId)
        {
            var hit = new DamageInfo { Amount = 1000f, Attacker = attacker, Zone = HitZone.Body, WeaponId = weaponId, Cause = DamageCause.Shot };
            victim.ServerTakeDamage(in hit);
        }

        static void KillEveryDummy(Combatant attacker)
        {
            // Damage does not change the list, only spawning does.
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
                if (!all[i].IsPlayer) Kill(all[i], attacker, 3);
        }

        static Combatant AnyDummy()
        {
            foreach (var c in Combatant.All)
                if (!c.IsPlayer) return c;
            return null;
        }

        [UnityTest]
        public IEnumerator Result_IsHiddenWhilePlaying()
        {
            yield return LoadTestBox();
            Assert.IsNotNull(ResultScreen.Instance, "the match brings its result screen");
            Assert.IsFalse(ResultScreen.Instance.IsShown);
            Assert.IsFalse(ResultScreen.BlocksGameplay);
            Assert.AreEqual(MatchOutcome.Playing, ResultScreen.Instance.Outcome);
        }

        [UnityTest]
        public IEnumerator LastOneStanding_SeesTheWinScreen_WithKills()
        {
            yield return LoadTestBox();
            var screen = ResultScreen.Instance;

            KillEveryDummy(Combatant.LocalPlayer);
            yield return WaitUntil(() => screen.IsShown, 3f, "result screen");
            yield return WaitUntil(() => MatchState.Instance.KillsPosted == 3, 3f, "all kills replicated");
            yield return null;

            Assert.AreEqual(MatchOutcome.Won, screen.Outcome);
            Assert.AreEqual("승리", screen.TitleText);
            Assert.AreEqual("#1 / 전체 4", screen.PlacementText);
            Assert.AreEqual("3", screen.KillsText);
            Assert.IsTrue(ResultScreen.BlocksGameplay);
            Assert.IsFalse(Combatant.LocalPlayer.Dead.Value);
        }

        [UnityTest]
        public IEnumerator DeadPlayer_SeesEliminated_WithPlacement_AndCannotActAnyMore()
        {
            yield return LoadTestBox();
            var screen = ResultScreen.Instance;
            var me = Combatant.LocalPlayer;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == 3, 2f, "rifle in hand");

            Kill(me, AnyDummy(), 1);
            yield return WaitUntil(() => screen.IsShown, 3f, "result screen");
            yield return null;

            Assert.AreEqual(MatchOutcome.Eliminated, screen.Outcome);
            Assert.AreEqual("탈락", screen.TitleText);
            Assert.AreEqual("#4 / 전체 4", screen.PlacementText);
            Assert.AreEqual("0", screen.KillsText);
            Assert.IsTrue(ResultScreen.BlocksGameplay);

            // Tab and the trigger do nothing for the dead.
            int mag = me.ActiveMag;
            _input.Frame.Inventory = true;
            yield return null;
            yield return null;
            _input.Frame.Inventory = false;
            Assert.AreEqual(UiScreen.None, UiFocus.Current, "no inventory after death");
            _input.Frame.FireHeld = true;
            _input.Frame.FirePressed = true;
            yield return new WaitForSeconds(0.4f);
            _input.Frame.FireHeld = false;
            Assert.AreEqual(mag, me.ActiveMag, "no shots after death");
            Assert.IsTrue(screen.IsShown);
        }

        [UnityTest]
        public IEnumerator RestartButton_StartsAFreshMatch_WithTheScreenHidden()
        {
            yield return LoadTestBox();
            var before = Combatant.LocalPlayer;
            KillEveryDummy(before);
            yield return WaitUntil(() => ResultScreen.Instance.IsShown, 3f, "result screen");

            ResultScreen.Instance.Restart();

            // The old player is destroyed with the scene (and compares equal to null from then on).
            yield return WaitUntil(() => Combatant.LocalPlayer != null && !ReferenceEquals(Combatant.LocalPlayer, before)
                && PlayerController.Local != null && ResultScreen.Instance != null && MatchState.Instance != null, 15f, "a new local player after the restart");
            yield return WaitUntil(() => MatchState.Instance.TotalCount == 4, 5f, "all four combatants counted again");
            yield return null;
            Assert.IsFalse(ResultScreen.Instance.IsShown, "the new match starts without a result");
            Assert.IsFalse(ResultScreen.BlocksGameplay);
            Assert.AreEqual(4, MatchState.Instance.AliveCount);
            Assert.AreEqual(0, MatchState.Instance.KillsPosted);
            Assert.AreEqual(0, MatchState.Instance.LocalKills);
            Assert.AreEqual(0, KillFeed.Instance.VisibleCount);
        }

        [UnityTest]
        public IEnumerator EnterStillRestarts_AfterDeath()
        {
            yield return LoadTestBox();
            var before = Combatant.LocalPlayer;
            Kill(before, AnyDummy(), 1);
            yield return WaitUntil(() => ResultScreen.Instance.IsShown && before.Dead.Value, 3f, "dead with the result up");

            _input.Frame.Restart = true;
            yield return WaitUntil(() => Combatant.LocalPlayer != null && !ReferenceEquals(Combatant.LocalPlayer, before)
                && PlayerController.Local != null, 15f, "a new match after Enter");
            _input.Frame.Restart = false;
            Assert.AreEqual(100f, Combatant.LocalPlayer.Hp.Value);
        }
    }
}
