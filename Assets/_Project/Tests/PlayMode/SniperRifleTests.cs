using System.Linq;
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
    // The Sentinel (weapon 5, 7.62mm) with a 15x optic (or the 8x) in the real TestBox scene as a host.
    public class SniperRifleTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = false;
                Frame.ZoomSteps = 0;
                Frame.SelectSlot = -1;
                return f;
            }
        }

        const int Sentinel = 5, X8 = 7, X16 = 8;

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
            Assert.IsTrue(me.ServerGive(Optics.ToLoot(X16)), "the Sentinel takes the 15x optic");
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
            // Any direction that keeps the shooter inside the box; props on the line are switched off (the scene is
            // reloaded for every test), so the layout of the box never decides the result.
            Vector3 spot = default, to = dummy.transform.position + Vector3.up * 1.1f;
            RaycastHit[] blockers = null;
            for (int i = 0; i < 16; i++)
            {
                var dir = Quaternion.Euler(0f, i * 22.5f, 0f) * dummy.transform.forward;
                var candidate = dummy.transform.position + dir * 40f + Vector3.up * 0.1f;
                if (Mathf.Abs(candidate.x) >= 29f || Mathf.Abs(candidate.z) >= 29f) continue;
                var hits = Physics.RaycastAll(candidate + Vector3.up * 1.5f, to - (candidate + Vector3.up * 1.5f), Vector3.Distance(candidate + Vector3.up * 1.5f, to))
                    .Where(h => h.collider.GetComponentInParent<Combatant>() == null).ToArray();
                if (blockers == null || hits.Length < blockers.Length) { spot = candidate; blockers = hits; }
                if (hits.Length == 0) break;
            }
            Assert.IsNotNull(blockers, "a spot 40 m from the dummy inside the test box");
            foreach (var hit in blockers) hit.collider.enabled = false;
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
            yield return new WaitForSeconds(0.25f); // 40 m is beyond the instant range (ADR-098): the bullet flies for 0.05 s
            Assert.AreEqual(18f, dummy.Hp.Value, 1e-3f, "82 at 40 m (no falloff before 100 m)");
            Assert.IsFalse(ShootAt(me, dummy.transform.position + Vector3.up * 1.65f), "bolt-action pace: 1.3 s between shots");
            Assert.AreEqual(18f, dummy.Hp.Value, 1e-3f);

            yield return new WaitForSeconds(1.4f);
            Assert.IsTrue(ShootAt(me, dummy.transform.position + Vector3.up * 1.65f), "head shot fired");
            yield return new WaitForSeconds(0.25f);
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
        public IEnumerator TapAim_With15xOptic_NarrowsTheViewToTheFifteenTimesValue()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            yield return GiveSentinelWithScope();
            float sights = GameData.Rules.Aim.Sights.Fov;
            float scoped = Optics.FovFor(sights, 15f);
            Assert.AreEqual(Optics.FovFor(38f, 15f), scoped, 0.01f, "15x of the 38 degree sights view");

            _input.Frame.AimHeld = true;
            yield return null;
            yield return null;
            _input.Frame.AimHeld = false;
            yield return null;
            yield return WaitUntil(() => me.AimMode == AimMode.Sights, 2f, "sights on");
            yield return WaitUntil(() => Mathf.Abs(pc.Camera.fieldOfView - scoped) < 0.5f, 3f, "field of view narrowed to the 15x value");
            Assert.AreEqual(scoped, ScopeView.FovOverride, 0.01f);
            Assert.AreEqual(Optics.SensitivityFor(15f), ScopeView.SensitivityFactor, 0.02f);
        }

        IEnumerator TapAim()
        {
            _input.Frame.AimHeld = true;
            yield return null;
            yield return null;
            _input.Frame.AimHeld = false;
            yield return null;
        }

        IEnumerator Wheel(int notches)
        {
            for (int i = 0; i < Mathf.Abs(notches); i++)
            {
                _input.Frame.ZoomSteps = notches > 0 ? 1 : -1;
                yield return null;
                yield return null;
            }
        }

        // ADR-063: the 8x on the Sentinel turns down to 6x with two wheel notches; leaving and re-entering sights keeps
        // 6x; switching weapons and back resets it to 8x.
        [UnityTest]
        public IEnumerator EightX_WheelDownTwice_Is6x_KeptAcrossSights_ResetByWeaponSwap()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Sentinel, Amount = 5 }));
            Assert.IsTrue(me.ServerGive(Optics.ToLoot(X8)), "the Sentinel takes the 8x optic");
            yield return WaitUntil(() => me.ActiveWeaponId == Sentinel, 2f, "Sentinel in hand");
            int sentinelSlot = me.ActiveSlot.Value;
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 }), "a second rifle");
            yield return null;
            yield return WaitUntil(() => me.GetComponent<CombatantOptics>().ActiveOptic == X8 || me.ActiveSlot.Value != sentinelSlot, 2f, "optic replicated");
            if (me.ActiveSlot.Value != sentinelSlot)
            {
                _input.Frame.SelectSlot = sentinelSlot;
                yield return WaitUntil(() => me.ActiveWeaponId == Sentinel, 2f, "back on the Sentinel");
            }
            float sights = GameData.Rules.Aim.Sights.Fov;

            yield return TapAim();
            yield return WaitUntil(() => me.AimMode == AimMode.Sights, 2f, "sights on");
            Assert.AreEqual(8f, ScopeView.CurrentZoom);
            yield return Wheel(-2);
            yield return null;
            Assert.AreEqual(6f, ScopeView.CurrentZoom, "two notches down");
            Assert.AreEqual(Optics.FovFor(sights, 6f), ScopeView.FovOverride, 0.01f);
            yield return WaitUntil(() => Mathf.Abs(pc.Camera.fieldOfView - Optics.FovFor(sights, 6f)) < 0.3f, 3f, "view at 6x");
            Assert.AreEqual(Optics.SensitivityFor(6f), ScopeView.SensitivityFactor, 0.02f);

            yield return TapAim();
            yield return WaitUntil(() => me.AimMode != AimMode.Sights, 2f, "sights off");
            yield return TapAim();
            yield return WaitUntil(() => me.AimMode == AimMode.Sights, 2f, "sights on again");
            yield return null;
            Assert.AreEqual(6f, ScopeView.CurrentZoom, "kept across sights");
            Assert.AreEqual(Optics.FovFor(sights, 6f), ScopeView.FovOverride, 0.01f);

            yield return TapAim();
            yield return WaitUntil(() => me.AimMode != AimMode.Sights, 2f, "sights off before the swap");
            int other = -1;
            for (int s = 0; s < 2; s++) if (s != sentinelSlot) other = s;
            _input.Frame.SelectSlot = other;
            yield return WaitUntil(() => me.ActiveWeaponId == 3, 2f, "the other rifle in hand");
            _input.Frame.SelectSlot = sentinelSlot;
            yield return WaitUntil(() => me.ActiveWeaponId == Sentinel, 2f, "the Sentinel again");
            yield return null;
            Assert.AreEqual(8f, ScopeView.CurrentZoom, "a weapon swap resets to the highest zoom");
        }
    }
}
