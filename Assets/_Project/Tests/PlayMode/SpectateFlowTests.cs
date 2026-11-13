using System;
using System.Collections;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // After death (ADR-094) in the real TestBox scene (one player, three dummies): death cam on the killer, the
    // result panel with the server's numbers, then spectating the killer, switching, and following the killer's killer.
    public class SpectateFlowTests
    {
        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
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

        sealed class NoInput : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        static void Hit(Combatant victim, Combatant attacker, float amount, int weaponId = 3) =>
            victim.ServerTakeDamage(new DamageInfo { Amount = amount, Attacker = attacker, Zone = HitZone.Body, WeaponId = weaponId, Cause = DamageCause.Shot });

        static float AngleTo(Camera cam, Combatant c) =>
            Vector3.Angle(cam.transform.forward, c.transform.position + Vector3.up * 1.2f - cam.transform.position);

        [UnityTest]
        public IEnumerator KilledByABot_DeathCamOnKiller_ResultNumbers_ThenSpectateCycles()
        {
            PlayerController.InputOverride = new NoInput();
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
            yield return WaitUntil(() => MatchState.Instance != null && MatchState.Instance.TotalCount == 4, 5f, "all four counted");
            var me = Combatant.LocalPlayer;
            var dummies = Combatant.All.Where(c => c != null && !c.IsPlayer).ToList();
            Combatant killer = dummies[0], other = dummies[1], first = dummies[2];

            // My damage: 40 on one, a kill (100 of health) on another.
            Hit(other, me, 40f);
            Hit(first, me, 1000f);
            Assert.AreEqual(140f, MatchState.Instance.ServerDamageOf(me), 0.01f, "server total");
            yield return WaitUntil(() => MatchState.Instance.KillsPosted == 1, 3f, "the first kill");

            Hit(me, killer, 1000f);
            var screen = ResultScreen.Instance;
            yield return WaitUntil(() => screen.Current == ResultScreen.Phase.DeathCam, 2f, "the death cam");
            yield return WaitUntil(() => screen.Spectate.Killer == killer, 2f, "the death cam on the killer");
            Assert.IsFalse(screen.IsShown, "no panel during the death cam");
            Assert.IsFalse(Hud.Instance.Visible, "no own HUD over the death cam");
            string killerName = MatchState.Instance.NameOfId(killer.ObjectId);
            StringAssert.Contains(killerName, screen.DeathCamText);
            StringAssert.Contains("m", screen.DeathCamText);
            yield return new WaitForSeconds(2f);
            var cam = Camera.main;
            screen.Spectate.Place();
            Assert.Less(AngleTo(cam, killer), 5f, "the camera turned on the killer");
            Assert.Less(Vector3.Distance(cam.transform.position, killer.transform.position), 7f, "close enough to see them");
            screen.Spectate.Place();
            yield return UiShot.Save("Logs/shots/g6_deathcam.png", 1920, 1080);

            yield return WaitUntil(() => screen.IsShown, ResultScreen.DeathCamSeconds + 2f, "the result panel after the death cam");
            yield return null;
            Assert.AreEqual(ResultScreen.Phase.Panel, screen.Current);
            Assert.AreEqual("탈락", screen.TitleText);
            Assert.AreEqual("#3 / 전체 4", screen.PlacementText);
            Assert.AreEqual("1", screen.KillsText);
            Assert.AreEqual(Mathf.RoundToInt(MatchState.Instance.ServerDamageOf(me)).ToString(), screen.DamageText, "damage = the server total");
            Assert.AreEqual("140", screen.DamageText);
            StringAssert.StartsWith("0:", screen.TimeText);
            StringAssert.Contains(killerName, screen.KilledByText);
            screen.Spectate.Place();
            yield return UiShot.Save("Logs/shots/g6_result.png", 1920, 1080);

            screen.StartSpectating();
            Assert.AreEqual(ResultScreen.Phase.Spectate, screen.Current);
            Assert.AreEqual(killer, screen.Spectate.Target, "solo: the killer first");
            yield return null;
            yield return null;
            Assert.IsFalse(screen.IsShown);
            Assert.IsTrue(screen.Spectate.BarShown);
            Assert.AreEqual(killerName, screen.Spectate.TargetText);
            screen.Spectate.Place();
            Assert.Less(Vector3.Distance(cam.transform.position, killer.EyePosition), 4f, "the camera over the killer's shoulder");
            screen.Spectate.Place();
            yield return UiShot.Save("Logs/shots/g6_spectate.png", 1920, 1080);

            screen.Spectate.Next();
            Assert.AreEqual(other, screen.Spectate.Target, "the next one alive, skipping the dead and me");
            screen.Spectate.Next();
            Assert.AreEqual(killer, screen.Spectate.Target, "wraps");
            screen.Spectate.Previous();
            Assert.AreEqual(other, screen.Spectate.Target);
            screen.Spectate.Previous();
            Assert.AreEqual(killer, screen.Spectate.Target);

            // The killer dies: the camera goes on to whoever killed them.
            Hit(killer, other, 1000f);
            yield return WaitUntil(() => screen.Spectate.Target == other, 3f, "the killer's killer");
            Assert.AreEqual(ResultScreen.Phase.Spectate, screen.Current);
        }
    }
}
