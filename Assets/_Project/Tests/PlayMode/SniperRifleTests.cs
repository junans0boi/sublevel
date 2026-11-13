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
    // The Sentinel (weapon 5, 7.62mm) with a 16x optic in the real TestBox scene as a host.
    public class SniperRifleTests
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

        const int Sentinel = 5, X16 = 8;

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
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        IEnumerator GiveSentinelWithScope()
        {
            var me = Combatant.LocalPlayer;
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Sentinel, Amount = 5 }));
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle762, Amount = 20 }));
            Assert.IsTrue(me.ServerGive(Optics.ToLoot(X16)), "the Sentinel takes the 16x optic");
            yield return WaitUntil(() => me.ActiveWeaponId == Sentinel, 2f, "Sentinel in hand");
            yield return WaitUntil(() => me.ReserveRifle762.Value == 20, 2f, "7.62mm reserve replicated");
        }

        // A dummy with the player 40 m away on a clear line (the room is 60 m wide; the dummy's forward or backward).
        IEnumerator FaceDummyAt40m(Action<Combatant> found)
        {
            Combatant dummy = null;
            yield return WaitUntil(() =>
            {
                foreach (var c in Combatant.All)
                    if (!c.IsPlayer) { dummy = c; return true; }
                return false;
            }, 5f, "a training dummy in the test box");
            foreach (var c in Combatant.All)
            {
                if (c.IsPlayer) continue;
                // By name: the test assembly does not reference the AI assembly.
                foreach (var part in c.GetComponents<MonoBehaviour>())
                    if (part.GetType().Name == "TrainingDummy") part.enabled = false;
            }
            var pc = PlayerController.Local;
            Vector3 spot = default;
            bool clear = false;
            foreach (var dir in new[] { dummy.transform.forward, -dummy.transform.forward, dummy.transform.right, -dummy.transform.right })
            {
                spot = dummy.transform.position + dir * 40f + Vector3.up * 0.1f;
                var from = spot + Vector3.up * 1.5f;
                var to = dummy.transform.position + Vector3.up * 1.1f;
                if (Mathf.Abs(spot.x) < 29f && Mathf.Abs(spot.z) < 29f &&
                    (!Physics.Linecast(from, to, out var hit) || hit.collider.GetComponentInParent<Combatant>() == dummy))
                { clear = true; break; }
            }
            if (!clear) Assert.Inconclusive("no clear 40 m line to the dummy in this TestBox layout");
            pc.Teleport(spot);
            pc.LookAt(dummy.transform.position + Vector3.up * 1.1f);
            yield return new WaitForSeconds(0.5f);
            found(dummy);
        }

        static bool ShootAt(Combatant shooter, Vector3 point)
        {
            float before = shooter.StanceSpreadFactor;
            shooter.StanceSpreadFactor = 0f;
            bool fired = shooter.ServerFire(point - shooter.EyePosition);
            shooter.StanceSpreadFactor = before;
            return fired;
        }

        [UnityTest]
        public IEnumerator Sentinel_BodyShotAt40m_LeavesEighteen_HeadShotKills_AndTheNextShotWaitsTheInterval()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            yield return GiveSentinelWithScope();
            Combatant dummy = null;
            yield return FaceDummyAt40m(d => dummy = d);
            Assert.AreEqual(100f, dummy.Hp.Value);

            Assert.IsTrue(ShootAt(me, dummy.transform.position + Vector3.up * 1.1f), "body shot fired");
            Assert.AreEqual(18f, dummy.Hp.Value, 1e-3f, "82 at 40 m (no falloff before 100 m)");
            Assert.IsFalse(ShootAt(me, dummy.transform.position + Vector3.up * 1.65f), "bolt-action pace: 1.3 s between shots");
            Assert.AreEqual(18f, dummy.Hp.Value, 1e-3f);

            yield return new WaitForSeconds(1.4f);
            Assert.IsTrue(ShootAt(me, dummy.transform.position + Vector3.up * 1.65f), "head shot fired");
            Assert.AreEqual(0f, dummy.Hp.Value);
            Assert.IsTrue(dummy.Dead.Value);
        }

        [UnityTest]
        public IEnumerator Sentinel_HasAFiveRoundMagazine_AndNoAutomaticMode()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            yield return GiveSentinelWithScope();
            Assert.AreEqual(5, me.ActiveMag);
            Assert.IsFalse(GameData.Weapons.Find(Sentinel).Auto, "no automatic mode for a bolt-action rifle");
        }

        [UnityTest]
        public IEnumerator TapAim_With16xOptic_NarrowsTheViewToTheSixteenTimesValue()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            yield return GiveSentinelWithScope();
            float sights = GameData.Rules.Aim.Sights.Fov;
            float scoped = Optics.FovFor(sights, 16f);
            Assert.AreEqual(2.466f, scoped, 0.01f, "16x of the 38 degree sights view");

            _input.Frame.AimHeld = true;
            yield return null;
            yield return null;
            _input.Frame.AimHeld = false;
            yield return null;
            yield return WaitUntil(() => me.AimMode == AimMode.Sights, 2f, "sights on");
            yield return WaitUntil(() => Mathf.Abs(pc.Camera.fieldOfView - scoped) < 0.5f, 3f, "field of view narrowed to the 16x value");
            Assert.AreEqual(scoped, ScopeView.FovOverride, 0.01f);
            Assert.AreEqual(Optics.SensitivityFor(16f), ScopeView.SensitivityFactor, 0.02f);
        }
    }
}
