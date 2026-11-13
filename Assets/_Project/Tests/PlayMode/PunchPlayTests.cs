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
    // ADR-059 (1) in the real TestBox scene as a host: bare hands punch with the fire button, the server checks
    // reach, cone, line and rate, the punch plays on the upper body, and a weapon in hand still fires instead.
    public class PunchPlayTests
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

        const int Rifle = 3;
        const float Chest = 1.1f, Head = 1.65f;

        ScriptedInput _input;
        int _punches;
        Combatant.PunchResult _lastPunch;

        void OnPunched(in Combatant.PunchResult punch)
        {
            _punches++;
            _lastPunch = punch;
        }

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
            _punches = 0;
            _lastPunch = default;
            Combatant.LocalPlayer.Punched += OnPunched;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Combatant.LocalPlayer != null) Combatant.LocalPlayer.Punched -= OnPunched;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        // The first dummy, no longer acting on its own; the player empty-handed, in the eye view (so the crosshair
        // is the eyes' line), standing `metres` in front of it and looking at `lookHeight` on it.
        IEnumerator FaceAQuietDummy(float metres, float lookHeight, Action<Combatant> found)
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
            var me = Combatant.LocalPlayer;
            me.ServerStripLoadout();
            var pc = PlayerController.Local;
            if (pc.Rig.Mode == ViewMode.ThirdPerson) pc.Rig.ToggleMode();
            pc.Teleport(dummy.transform.position + dummy.transform.forward * metres + Vector3.up * 0.1f);
            pc.LookAt(dummy.transform.position + Vector3.up * lookHeight);
            yield return new WaitForSeconds(0.6f); // on the floor, standing still, any earlier punch timed out
            Assert.AreEqual(0, me.ActiveWeaponId, "empty hands");
            pc.LookAt(dummy.transform.position + Vector3.up * lookHeight);
            yield return null;
            found(dummy);
        }

        IEnumerator Click()
        {
            _input.Frame.FirePressed = true;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator UnarmedFire_OneMetreFromADummy_PunchesForTwelve_AsMelee()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Combatant dummy = null;
            yield return FaceAQuietDummy(1.0f, Chest, d => dummy = d);
            float before = dummy.Hp.Value;
            DamageInfo seen = default;
            int damaged = 0;
            var previous = dummy.ServerDamageFilter; // armour, if any (it does not cover punches)
            dummy.ServerDamageFilter = info => { seen = info; damaged++; return previous != null ? previous(info) : info.Amount; };
            var view = me.GetComponent<CombatantView>();
            int confirms = view != null ? view.HitConfirms : 0;
            var audio = me.GetComponent<CombatantAudio>();
            int sounds = audio != null ? audio.PunchesPlayed : 0;

            _input.Frame.FirePressed = true;
            yield return WaitUntil(() => dummy.Hp.Value < before, 0.5f, "the punch landed");
            dummy.ServerDamageFilter = previous;

            Assert.AreEqual(before - 12f, dummy.Hp.Value, 1e-3f, "12 on the body");
            Assert.AreEqual(1, damaged);
            Assert.AreEqual(DamageCause.Melee, seen.Cause);
            Assert.AreEqual(me, seen.Attacker);
            Assert.AreEqual(0, seen.WeaponId);
            Assert.AreNotEqual(HitZone.Head, seen.Zone);
            Assert.AreEqual(1, _punches, "one punch event");
            Assert.AreEqual(dummy, _lastPunch.Victim);
            Assert.AreEqual(ShotHit.Combatant, _lastPunch.Hit);
            Assert.IsFalse(_lastPunch.Right, "the left hand goes first");
            if (view != null) Assert.AreEqual(confirms + 1, view.HitConfirms, "hit marker for the attacker");
            if (audio != null) Assert.AreEqual(sounds + 1, audio.PunchesPlayed, "the punch is heard");
            Assert.AreEqual("가  [주먹]  나", KillFeed.Format(new KillEntry { KillerId = 1, VictimId = 2, KillerName = "가", VictimName = "나", Cause = (byte)DamageCause.Melee }));
        }

        [UnityTest]
        public IEnumerator PunchOnTheHead_TakesTheHeadMultiplier_AndHandsAlternate()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Combatant dummy = null;
            yield return FaceAQuietDummy(1.0f, Head, d => dummy = d);
            float before = dummy.Hp.Value;
            var dir = dummy.transform.position + Vector3.up * Head - me.EyePosition;
            Assert.IsTrue(me.ServerPunch(dir));
            Assert.AreEqual(before - 18f, dummy.Hp.Value, 1e-3f, "12 x 1.5 on the head");
            Assert.AreEqual(HitZone.Head, _lastPunch.Zone);
            Assert.IsTrue(_lastPunch.Head);
            bool firstRight = _lastPunch.Right;
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(me.ServerPunch(dummy.transform.position + Vector3.up * Chest - me.EyePosition));
            Assert.AreEqual(before - 30f, dummy.Hp.Value, 1e-3f, "then 12 on the body");
            Assert.AreNotEqual(firstRight, _lastPunch.Right, "the other hand");
        }

        [UnityTest]
        public IEnumerator DummyBehind_IsNotPunched()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Combatant dummy = null;
            yield return FaceAQuietDummy(1.0f, Chest, d => dummy = d);
            // Turn round: the dummy is now behind.
            PlayerController.Local.LookAt(me.EyePosition + (me.EyePosition - dummy.transform.position - Vector3.up * Chest) * 5f);
            yield return null;
            yield return null;
            float before = dummy.Hp.Value;
            yield return Click();
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(before, dummy.Hp.Value, "nothing in front");
            Assert.AreEqual(1, _punches, "the punch is still thrown, at the air");
            Assert.IsNull(_lastPunch.Victim);
            Assert.AreEqual(ShotHit.None, _lastPunch.Hit);
        }

        [UnityTest]
        public IEnumerator TwoMetresAway_IsOutOfReach()
        {
            yield return LoadTestBox();
            Combatant dummy = null;
            yield return FaceAQuietDummy(2.0f, Chest, d => dummy = d);
            float before = dummy.Hp.Value;
            yield return Click();
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(before, dummy.Hp.Value);
            Assert.AreEqual(1, _punches);
            Assert.IsNull(_lastPunch.Victim);
        }

        [UnityTest]
        public IEnumerator ASecondPunchWithinTheInterval_IsIgnored()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Combatant dummy = null;
            yield return FaceAQuietDummy(1.0f, Chest, d => dummy = d);
            float before = dummy.Hp.Value;
            var dir = dummy.transform.position + Vector3.up * Chest - me.EyePosition;

            // The server's own limit.
            Assert.IsTrue(me.ServerPunch(dir));
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(me.ServerPunch(dir), "0.3 s later: too soon");
            Assert.AreEqual(before - 12f, dummy.Hp.Value, 1e-3f);
            Assert.AreEqual(1, _punches);

            // And the button: two clicks 0.2 s apart throw one punch.
            yield return new WaitForSeconds(0.6f);
            before = dummy.Hp.Value;
            yield return Click();
            yield return new WaitForSeconds(0.2f);
            yield return Click();
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(2, _punches, "one more punch, not two");
            Assert.AreEqual(before - 12f, dummy.Hp.Value, 1e-3f);
        }

        [UnityTest]
        public IEnumerator HoldingAWeapon_TheButtonFiresIt_AndDoesNotPunch()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Combatant dummy = null;
            yield return FaceAQuietDummy(1.0f, Chest, d => dummy = d);
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");
            yield return new WaitForSeconds(0.3f);
            int shots = 0;
            void OnShot(Vector3 a, Vector3 b) => shots++;
            me.ShotFired += OnShot;
            int mag = me.ActiveMag;
            // The rifle is automatic: it fires while the button is held.
            _input.Frame.FireHeld = true;
            _input.Frame.FirePressed = true;
            yield return WaitUntil(() => shots > 0, 1f, "a shot");
            _input.Frame.FireHeld = false;
            me.ShotFired -= OnShot;
            Assert.Less(me.ActiveMag, mag, "rounds were fired");
            Assert.AreEqual(0, _punches, "no punch with a weapon in hand");
            Assert.IsFalse(me.ServerPunch(Vector3.forward), "the server refuses a punch while armed");

            // Put away with X: the hands are empty again and the button punches.
            me.ServerSetHolstered(true);
            yield return new WaitForSeconds(0.6f);
            PlayerController.Local.LookAt(dummy.transform.position + Vector3.up * Chest);
            yield return null;
            yield return Click();
            yield return WaitUntil(() => _punches == 1, 0.5f, "a punch with the weapon put away");
        }

        [UnityTest]
        public IEnumerator Punch_PlaysThePunchStateOnTheAttackersUpperBody()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Combatant dummy = null;
            yield return FaceAQuietDummy(1.0f, Chest, d => dummy = d);
            var anim = me.GetComponent<CombatantAnimation>();
            var animator = me.GetComponentInChildren<Animator>();
            Assert.IsNotNull(anim);
            Assert.IsNotNull(animator);
            Assert.IsNull(anim.UpperState, "empty hands: the upper layer is off");
            int shown = anim.PunchesShown;

            yield return Click();
            yield return WaitUntil(() => anim.PunchesShown > shown, 0.5f, "the punch reached the animation");
            Assert.AreEqual(CombatantAnimation.PunchLeft, anim.UpperStateStarted);
            yield return WaitUntil(() =>
            {
                var current = animator.GetCurrentAnimatorStateInfo(1);
                var next = animator.GetNextAnimatorStateInfo(1);
                return current.IsName(CombatantAnimation.PunchLeft) || next.IsName(CombatantAnimation.PunchLeft);
            }, 0.5f, "the animator's upper layer in PunchLeft");
            yield return WaitUntil(() => animator.GetLayerWeight(1) > 0.5f, 0.3f, "the upper layer raised for the punch");
            Assert.AreEqual(CombatantAnimation.PunchLeft, anim.UpperState);

            // It lowers again afterwards, empty hands as before.
            yield return WaitUntil(() => animator.GetLayerWeight(1) < 0.01f, 2f, "the upper layer off again");
        }
    }
}
