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
    // #120 / ADR-092 in the real TestBox scene: which gunshot layer plays at 5 m and 300 m, and the whiz of a round
    // passing 1 m from the listener. The listener is moved by swapping in a test AudioListener. Batchmode has no audio
    // output, so this proves the right clips are triggered at the right volumes; a human must still listen.
    public class SoundCuePlayTests
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
            yield return new WaitForSeconds(0.3f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        // The camera's listener off, a free one in its place.
        static Transform TestListener()
        {
            PlayerController.Local.Camera.GetComponent<AudioListener>().enabled = false;
            return new GameObject("TestListener").AddComponent<AudioListener>().transform;
        }

        IEnumerator Fire(CombatantAudio audio)
        {
            int shots = audio.ShotsPlayed;
            _input.Frame.FirePressed = true;
            yield return WaitUntil(() => audio.ShotsPlayed > shots, 2f, "shot sound");
            yield return new WaitForSeconds(0.5f); // past the pistol's fire interval
        }

        [UnityTest]
        public IEnumerator A_near_shot_plays_the_near_layer_and_a_300m_shot_the_far_layer()
        {
            var me = Combatant.LocalPlayer;
            var audio = me.GetComponent<CombatantAudio>();
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 1, Amount = 12 });
            yield return WaitUntil(() => me.ActiveWeaponId == 1, 2f, "pistol in hand");
            var muzzle = me.transform.Find("WeaponAudio").position;
            var listener = TestListener();

            listener.position = muzzle + Vector3.right * 5f;
            int near = audio.NearShotsPlayed, far = audio.FarShotsPlayed;
            yield return Fire(audio);
            Assert.AreEqual(near + 1, audio.NearShotsPlayed, "5 m: the crack/body layer");
            Assert.AreEqual(far, audio.FarShotsPlayed, "5 m: no far layer");

            listener.position = muzzle + Vector3.right * 300f;
            near = audio.NearShotsPlayed;
            yield return Fire(audio);
            Assert.AreEqual(far + 1, audio.FarShotsPlayed, "300 m: the thump/echo layer");
            Assert.AreEqual(near, audio.NearShotsPlayed, "300 m: no near crack");
            Assert.IsNotNull(me.transform.Find("WeaponAudio/FarShotAudio")?.GetComponent<AudioLowPassFilter>(), "the far layer is muffled by distance too");
        }

        [UnityTest]
        public IEnumerator A_round_passing_1m_from_the_listener_whizzes_once()
        {
            var me = Combatant.LocalPlayer;
            var audio = me.GetComponent<CombatantAudio>();
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 1, Amount = 12 });
            yield return WaitUntil(() => me.ActiveWeaponId == 1, 2f, "pistol in hand");

            Vector3 origin = default, end = default;
            void Grab(Vector3 o, Vector3 e) { origin = o; end = e; }
            me.ShotFired += Grab;
            yield return Fire(audio);
            Assert.AreEqual(0, audio.WhizzesPlayed, "the shooter never hears their own round whiz");

            var path = end - origin;
            Assert.Greater(path.magnitude, 9f, "the round flew far enough to pass someone");
            var side = Vector3.Cross(path.normalized, Vector3.up).normalized;
            var listener = TestListener();
            listener.position = origin + path * 0.8f + side * 1f;
            yield return Fire(audio);
            me.ShotFired -= Grab;
            Assert.AreEqual(1, audio.WhizzesPlayed, $"one whiz for the round passing 1 m away (listener {listener.position}, first {origin}->{end})");

            listener.position = origin + path * 0.8f + side * 8f;
            yield return Fire(audio);
            Assert.AreEqual(1, audio.WhizzesPlayed, "no whiz 8 m from the path");
        }
    }
}
