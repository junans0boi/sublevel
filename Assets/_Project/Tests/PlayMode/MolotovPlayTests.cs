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
    // ADR-110 in the real TestBox as a host: a fire bottle breaks on the floor, standing in the fire for four
    // seconds costs about fifty health, the burn follows the body out and then goes out, and a burning body
    // cannot raise the sights.
    public class MolotovPlayTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read() => Frame;
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

        int _fires;
        Vector3 _fireAt;

        void OnFire(Vector3 centre, Combatant thrower) { _fires++; _fireAt = centre; }

        IEnumerator Load()
        {
            PlayerController.InputOverride = new ScriptedInput();
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            // The dummies shoot at the player: off (by name, the test assembly does not reference the AI assembly).
            foreach (var c in Combatant.All)
                if (!c.IsPlayer)
                    foreach (var part in c.GetComponents<MonoBehaviour>())
                        if (part.GetType().Name == "TrainingDummy") part.enabled = false;
            yield return new WaitForSeconds(0.5f);
            _fires = 0;
            ThrownItem.ServerFired += OnFire;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            ThrownItem.ServerFired -= OnFire;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator StandingInTheFire_ForFourSeconds_CostsAboutFifty_AndTheBurnFollowsThenGoesOut()
        {
            yield return Load();
            var me = Combatant.LocalPlayer;
            var user = me.GetComponent<ThrowableUser>();
            Assert.AreEqual(1, user.ServerAdd(ThrowableKind.Molotov, 1));
            Assert.AreEqual(1, user.Molotovs.Value);
            Assert.IsFalse(me.Burning.Value);
            // Dropped at the feet: it breaks on the floor at once.
            Assert.IsTrue(user.ServerThrow(me.transform.position + Vector3.up * 0.4f, Vector3.zero, ThrowableKind.Molotov));
            Assert.AreEqual(0, user.Molotovs.Value);
            yield return WaitUntil(() => _fires == 1, 5f, "the bottle broke");
            Assert.Less(Vector3.Distance(_fireAt, me.transform.position), 1.5f, "where it was dropped");
            yield return WaitUntil(() => me.Hp.Value < 100f, 2f, "the fire hurt");
            float hp0 = me.Hp.Value;
            float t0 = Time.time;
            yield return new WaitForSeconds(4f);
            float lost = hp0 - me.Hp.Value;
            Assert.AreEqual(50f, lost, 8f, "12.5 per second for four seconds");
            Assert.IsTrue(me.Burning.Value, "lit");
            Assert.Less(Time.time - t0, 6f);

            // Out of the fire the burn goes on for a while, then stops.
            PlayerController.Local.Teleport(me.transform.position + new Vector3(15f, 0f, 0f));
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(me.Burning.Value, "still burning outside the fire");
            float hp1 = me.Hp.Value;
            yield return new WaitForSeconds(1.5f);
            Assert.Greater(hp1 - me.Hp.Value, 8f, "10 per second");
            yield return WaitUntil(() => !me.Burning.Value, 6f, "the fire on the body went out");
            float hp2 = me.Hp.Value;
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(hp2, me.Hp.Value, 1e-3f, "nothing more once it is out");
        }

        [UnityTest]
        public IEnumerator ABurningBody_CannotUseTheSights()
        {
            yield return Load();
            var me = Combatant.LocalPlayer;
            // Bare hands cannot aim: take a rifle.
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 }));
            me.ServerSelectSlot(0);
            yield return null;
            me.ServerSetStance(AimMode.Sights, 0);
            Assert.AreEqual((int)AimMode.Sights, me.Aim.Value, "fine while not burning");
            me.ServerSetStance(AimMode.Hip, 0);
            me.ServerSetBurning(3f);
            yield return null;
            yield return null;
            Assert.IsTrue(me.Burning.Value);
            me.ServerSetStance(AimMode.Sights, 0);
            Assert.AreEqual((int)AimMode.Shoulder, me.Aim.Value, "on fire the sights drop to the shoulder");
            yield return WaitUntil(() => !me.Burning.Value, 6f, "the burn ended");
            me.ServerSetStance(AimMode.Sights, 0);
            Assert.AreEqual((int)AimMode.Sights, me.Aim.Value);
        }

        [UnityTest]
        public IEnumerator FireOnTheFloor_IsShown_ByTheThrowersView()
        {
            yield return Load();
            var me = Combatant.LocalPlayer;
            var user = me.GetComponent<ThrowableUser>();
            var view = me.GetComponent<ThrowableView>();
            Assert.AreEqual(1, user.ServerAdd(ThrowableKind.Molotov, 1));
            Assert.IsTrue(user.ServerThrow(me.transform.position + Vector3.forward * 6f + Vector3.up * 0.4f, Vector3.zero, ThrowableKind.Molotov));
            yield return WaitUntil(() => _fires == 1, 5f, "the bottle broke");
            yield return null;
            if (view != null)
            {
                Assert.AreEqual(1, view.FiresShown);
                Assert.AreEqual(_fireAt, view.LastFireCentre);
                Assert.AreEqual(1, view.ActiveFires);
            }
        }
    }
}
