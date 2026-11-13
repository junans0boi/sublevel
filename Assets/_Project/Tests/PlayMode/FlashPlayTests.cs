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
    // ADR-105 in the real TestBox as a host: a flash goes off after its fuse, blinds the local player by where they
    // look and whether a wall is between, rings their ears (the master volume ducks and comes back).
    public class FlashPlayTests
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

        int _bangs;

        IEnumerator Load()
        {
            PlayerController.InputOverride = new ScriptedInput();
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return WaitUntil(() => FlashOverlay.Instance != null, 5f, "the flash overlay installed itself");
            // The dummies shoot at the player: switch them off (by name: the test assembly does not reference the AI assembly).
            foreach (var c in Combatant.All)
                if (!c.IsPlayer)
                    foreach (var part in c.GetComponents<MonoBehaviour>())
                        if (part.GetType().Name == "TrainingDummy") part.enabled = false;
            yield return new WaitForSeconds(0.5f);
            _bangs = 0;
            ThrownItem.ServerFlashed += OnBang;
        }

        void OnBang(Vector3 centre, Combatant thrower) => _bangs++;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            ThrownItem.ServerFlashed -= OnBang;
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static Vector3 Forward()
        {
            var f = PlayerController.Local.Camera.transform.forward;
            f.y = 0f;
            return f.normalized;
        }

        // A flash dropped where it lies still, `at` away from the player's eyes.
        IEnumerator DropFlash(Vector3 at)
        {
            var user = Combatant.LocalPlayer.GetComponent<ThrowableUser>();
            Assert.IsNotNull(user);
            Assert.AreEqual(1, user.ServerAdd(ThrowableKind.Flash, 1));
            Assert.AreEqual(1, user.Flashes.Value);
            Assert.IsTrue(user.ServerThrow(at, Vector3.zero, ThrowableKind.Flash));
            Assert.AreEqual(0, user.Flashes.Value);
            int before = _bangs;
            yield return WaitUntil(() => _bangs > before, 5f, "the flash went off");
            yield return null;
        }

        [UnityTest]
        public IEnumerator InFront_AtFiveMetres_BlindsLong_AndTheEarsRing()
        {
            yield return Load();
            var me = Combatant.LocalPlayer;
            var overlay = FlashOverlay.Instance;
            float master = GameSettings.MasterVolume;
            int felt = overlay.FlashesFelt;
            yield return DropFlash(me.EyePosition + Forward() * 5f - Vector3.up * 1.2f);
            Assert.AreEqual(felt + 1, overlay.FlashesFelt);
            Assert.Greater(overlay.LastBlindSeconds, 4f, "facing it from five metres");
            Assert.AreEqual(1f, overlay.Whiteness, 1e-3f, "solid white at first");
            Assert.Less(AudioListener.volume, master * 0.5f, "the master volume ducks while the ears ring");
            Assert.Less(overlay.Duck, 0.5f);

            // Ringing ends after the blindness (+2 s): wait it out, then everything is back.
            yield return WaitUntil(() => overlay.Whiteness < 0.01f && overlay.Duck >= 1f, overlay.LastRingSeconds + 3f, "the flash wore off");
            Assert.AreEqual(master, AudioListener.volume, 1e-3f, "the volume is back");
        }

        [UnityTest]
        public IEnumerator Behind_AtFiveMetres_IsShort()
        {
            yield return Load();
            var me = Combatant.LocalPlayer;
            var overlay = FlashOverlay.Instance;
            yield return DropFlash(me.EyePosition - Forward() * 5f - Vector3.up * 1.2f);
            Assert.Greater(overlay.LastBlindSeconds, 0.9f, "still a flash");
            Assert.Less(overlay.LastBlindSeconds, 2f, "but turned away");
        }

        [UnityTest]
        public IEnumerator BehindAWall_NoBlindness_OnlyARing()
        {
            yield return Load();
            var overlay = FlashOverlay.Instance;
            // "BlockSpawn" is a 3 m high, 1 m thick wall along z = -14. Stand 1 m behind its far face, flash 1 m before its near one.
            PlayerController.Local.Teleport(new Vector3(3f, 0.1f, -12.5f));
            yield return new WaitForSeconds(0.4f);
            Assert.Less(Vector3.Distance(Combatant.LocalPlayer.transform.position, new Vector3(3f, 0.1f, -12.5f)), 1f, "the player stands behind the wall");
            int felt = overlay.FlashesFelt;
            yield return DropFlash(new Vector3(3f, 0.5f, -15.5f));
            Assert.AreEqual(felt + 1, overlay.FlashesFelt);
            Assert.AreEqual(0f, overlay.LastBlindSeconds, "the wall hides it");
            Assert.Greater(overlay.LastRingSeconds, 0.5f, "but the bang is heard close by");
            Assert.AreEqual(0f, overlay.Whiteness, 1e-3f);
        }
    }
}
