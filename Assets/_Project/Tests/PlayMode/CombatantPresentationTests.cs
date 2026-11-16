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
    // M1.5: the player in the real TestBox scene shows the held weapon, animates and triggers its sounds.
    public class CombatantPresentationTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Reload = false;
                Frame.SelectSlot = -1;
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

        [UnitySetUp]
        public IEnumerator SetUp()
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

        [UnityTest]
        public IEnumerator HeldWeaponModel_FollowsTheActiveWeapon()
        {
            var me = Combatant.LocalPlayer;
            var visual = me.GetComponent<WeaponVisual>();
            Assert.IsNotNull(visual, "player prefab should have a WeaponVisual");
            yield return null;
            Assert.AreEqual(0, visual.ShownWeaponId, "empty hands at spawn");

            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 4, Amount = 6 });
            yield return WaitUntil(() => visual.ShownWeaponId == 3, 2f, "rifle model shown");
            Assert.AreEqual(1, ActiveWeaponModels(me), "exactly one weapon model visible");

            _input.Frame.SelectSlot = 1;
            yield return WaitUntil(() => visual.ShownWeaponId == 4, 2f, "shotgun model shown after switching");
            Assert.AreEqual(1, ActiveWeaponModels(me));
        }

        static int ActiveWeaponModels(Combatant combatant)
        {
            int active = 0;
            foreach (var t in combatant.GetComponentsInChildren<Transform>(false))
                if (t.name.StartsWith("Weapon") && t.GetComponent<AudioSource>() == null && t.parent.name == "WeaponSocket") active++;
            return active;
        }

        [UnityTest]
        public IEnumerator Shooting_Reloading_Walking_AndBeingHit_TriggerSoundsAndAnimation()
        {
            var me = Combatant.LocalPlayer;
            var audio = me.GetComponent<CombatantAudio>();
            var animation = me.GetComponent<CombatantAnimation>();
            Assert.IsNotNull(audio, "player prefab should have CombatantAudio");
            Assert.IsNotNull(animation, "player prefab should have CombatantAnimation");
            Assert.AreEqual(CombatantAnimation.Locomotion, animation.BaseState);
            Assert.IsNull(animation.UpperState, "empty hands: no weapon pose over the body");

            // One shotgun shot is nine pellets but must sound once.
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 4, Amount = 2 });
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Shotgun12, Amount = 8 });
            yield return null;
            _input.Frame.FirePressed = true;
            yield return WaitUntil(() => me.Mag0.Value == 1, 2f, "shot fired");
            yield return WaitUntil(() => audio.ShotsPlayed > 0, 2f, "shot sound");
            yield return new WaitForSeconds(0.2f); // let every pellet's effect arrive
            Assert.AreEqual(1, audio.ShotsPlayed, "one sound per shot, not per pellet");

            _input.Frame.Reload = true;
            yield return WaitUntil(() => audio.ReloadsPlayed == 1, 2f, "reload sound");

            _input.Frame.Move = new Vector2(0, 1);
            yield return WaitUntil(() => animation.MoveDrive > 3f, 2f, "forward movement reaches the blend");
            yield return WaitUntil(() => audio.FootstepsPlayed >= 2, 3f, "footsteps while walking (feet touching down)");
            _input.Frame.Move = Vector2.zero;
            yield return WaitUntil(() => Mathf.Abs(animation.MoveDrive) < 0.2f, 2f, "blend back to standing");
            yield return new WaitForSeconds(0.3f); // let a foot that was mid-step come down
            int steps = audio.FootstepsPlayed;
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(steps, audio.FootstepsPlayed, "no footsteps while standing");

            me.ServerTakeDamage(10);
            yield return WaitUntil(() => audio.HitsPlayed == 1, 2f, "hit sound");
            me.ServerTakeDamage(1000);
            yield return WaitUntil(() => animation.BaseState == CombatantAnimation.Death, 2f, "death animation");
            yield return WaitUntil(() => animation.UpperState == null, 2f, "weapon pose released on death");
        }
    }
}
