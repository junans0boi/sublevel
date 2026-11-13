using System;
using System.Collections;
using System.IO;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Review shots of the boost HUD, the flash overlay and the fire (#131 #133 #134). Not part of the suite: run on
    // purpose with   tools/unity.sh test playmode Game.Tests.HealThrowCaptureTests   -> Logs/healthrow/*.png
    [Explicit, Category("Capture")]
    public class HealThrowCaptureTests
    {
        const string Folder = "Logs/healthrow";

        static IEnumerator Load()
        {
            Directory.CreateDirectory(Folder);
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
        }

        static IEnumerator Shot(string name) => UiShot.Save(Path.Combine(Folder, name + ".png"), 1920, 1080);

        [UnityTest, Timeout(180000)]
        public IEnumerator BoostHud()
        {
            yield return Load();
            var me = Combatant.LocalPlayer;
            var healing = me.GetComponent<CombatantHealing>();
            me.ServerTakeDamage(50f);
            me.ServerGive(new LootEntry { Kind = LootKind.Drink, Amount = 2 });
            me.ServerGive(new LootEntry { Kind = LootKind.Pill, Amount = 1 });
            healing.ServerAddGauge(70f);
            yield return new WaitForSeconds(1f);
            Assert.IsTrue(healing.ServerBegin(HealKind.Pill));
            yield return new WaitForSeconds(2f);
            yield return Shot("boost_hud_pill_in_use");
            Debug.Log($"[HealThrowCapture] gauge {healing.Gauge.Value:F1} step {healing.BoostStep} hp {me.Hp.Value:F0} speed x{CombatantHealing.BoostSpeedOf(me):F3}");
        }

        static void QuietDummies()
        {
            foreach (var c in Combatant.All)
                if (!c.IsPlayer)
                    foreach (var part in c.GetComponents<MonoBehaviour>())
                        if (part.GetType().Name == "TrainingDummy") part.enabled = false;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator FlashOverlayShots()
        {
            yield return Load();
            QuietDummies();
            var me = Combatant.LocalPlayer;
            var user = me.GetComponent<ThrowableUser>();
            var fwd = PlayerController.Local.Camera.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            yield return Shot("flash_0_before");
            user.ServerAdd(ThrowableKind.Flash, 1);
            Assert.IsTrue(user.ServerThrow(me.EyePosition + fwd * 5f - Vector3.up * 1.2f, Vector3.zero, ThrowableKind.Flash));
            bool bang = false;
            ThrownItem.ServerFlashed += (c, t) => bang = true;
            float end = Time.realtimeSinceStartup + 6f;
            while (!bang && Time.realtimeSinceStartup < end) yield return null;
            yield return new WaitForSeconds(0.12f);
            yield return Shot("flash_1_burst");
            var overlay = FlashOverlay.Instance;
            Debug.Log($"[HealThrowCapture] flash blind {overlay.LastBlindSeconds:F2}s ring {overlay.LastRingSeconds:F2}s whiteness {overlay.Whiteness:F2} volume x{AudioListener.volume:F2}");
            yield return new WaitForSeconds(2.0f);
            yield return Shot("flash_2_white");
            yield return new WaitForSeconds(1.6f);
            Debug.Log($"[HealThrowCapture] flash fading whiteness {overlay.Whiteness:F2} volume x{AudioListener.volume:F2}");
            yield return Shot("flash_3_fade");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator FireShots()
        {
            yield return Load();
            QuietDummies();
            var me = Combatant.LocalPlayer;
            var user = me.GetComponent<ThrowableUser>();
            var fwd = PlayerController.Local.Camera.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            user.ServerAdd(ThrowableKind.Molotov, 1);
            bool lit = false;
            ThrownItem.ServerFired += (c, t) => lit = true;
            // Dropped 5 m ahead so the fire is in view.
            Assert.IsTrue(user.ServerThrow(me.transform.position + fwd * 5f + Vector3.up * 0.4f, Vector3.zero, ThrowableKind.Molotov));
            float end = Time.realtimeSinceStartup + 6f;
            while (!lit && Time.realtimeSinceStartup < end) yield return null;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("fire_1_ahead");
            // Step in: burning HUD label, damage numbers.
            PlayerController.Local.Teleport(me.transform.position + fwd * 5f);
            yield return new WaitForSeconds(2.0f);
            yield return Shot("fire_2_inside");
            Debug.Log($"[HealThrowCapture] fire inside: hp {me.Hp.Value:F0} burning {me.Burning.Value}");
        }
    }
}
