using System;
using System.Collections;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    // #106 / ADR-086 in the real TestBox scene: one listener on the player's camera, every world source 3D with a
    // falloff where 200 m is far quieter than 5 m. (Batchmode has no audio output, so the mixer's actual level is
    // read from the configured rolloff curve; a human must still listen.)
    public class SpatialAudioPlayTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 15f;
            while (Combatant.LocalPlayer == null || PlayerController.Local == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for the local player");
                yield return null;
            }
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator World_sounds_are_3D_and_fall_off_with_distance()
        {
            var listeners = Array.FindAll(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None), l => l.isActiveAndEnabled);
            Assert.AreEqual(1, listeners.Length, "exactly one active AudioListener");
            Assert.AreSame(PlayerController.Local.Camera.gameObject, listeners[0].gameObject, "the listener is the local player's camera");

            foreach (var source in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                // UI-like cues the local player hears in the head: hit confirmation, zone banner.
                if (source.name == "ConfirmAudio" || source.GetComponent<ZoneBanner>() != null) continue;
                Assert.AreEqual(1f, source.spatialBlend, $"{source.name} is a 3D world sound");
                Assert.AreEqual(AudioRolloffMode.Custom, source.rolloffMode, $"{source.name} uses the shared falloff");
            }

            var weapon = Combatant.LocalPlayer.transform.Find("WeaponAudio").GetComponent<AudioSource>();
            var curve = weapon.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            float near = curve.Evaluate(5f / weapon.maxDistance), far = curve.Evaluate(200f / weapon.maxDistance);
            Assert.Less(far, near * 0.1f, $"gunshot gain near {near:F3} far {far:F3}");
            Assert.Greater(far, 0f, "a shot 200 m away is still heard");
            Assert.IsNotNull(weapon.GetComponent<AudioLowPassFilter>(), "far shots are muffled");
            yield break;
        }
    }
}
