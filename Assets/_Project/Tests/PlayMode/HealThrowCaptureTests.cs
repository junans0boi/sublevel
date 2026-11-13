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
    }
}
