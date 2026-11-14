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
    // Review shots of the emote wheel and the six emotes (#169, ADR-133) in the World scene, seen from behind. Run on purpose:
    //   EMOTE_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.EmoteCaptureTests
    [Explicit, Category("Capture")]
    public class EmoteCaptureTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

        [UnityTest, Timeout(420000)]
        public IEnumerator CaptureEmotes()
        {
            string folder = Environment.GetEnvironmentVariable("EMOTE_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/emoteshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            var input = new HarnessInput();
            PlayerController.InputOverride = input;
            SceneManager.LoadScene("World");
            yield return NetHarness.WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null && WheelHud.Instance != null, "player", null, 60f);
            yield return NetHarness.WaitUntil(() => !SpawnProtection.InputBlocked, "doors open", null, 90f);
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            foreach (var c in Combatant.All) { var p = c != null ? c.GetComponent<SpawnProtection>() : null; if (p != null) p.ServerEnd(); }

            // Walk a few metres out of the station into the open, then turn to a view over the street.
            var origin = pc.transform.position;
            pc.LookAt(origin + pc.transform.forward * 20f + Vector3.up * 1.2f);
            yield return Seconds(0.5f);

            // The wheel the real way: hold the emote key, move the mouse to a part, shot while it is held.
            input.Frame.EmoteHeld = true;
            yield return NetHarness.WaitUntil(() => WheelHud.Instance.EmoteWheel.IsOpen, "the emote wheel", null, 3f);
            input.Frame.Look = new Vector2(-5f, -3f); // down and to the left: the fifth part, "춤"
            yield return null;
            input.Frame.Look = Vector2.zero;
            yield return Seconds(0.3f);
            Debug.Log("[EmoteCapture] wheel centre text: " + WheelHud.Instance.EmoteWheel.CentreText);
            yield return UiShot.Save(Path.Combine(folder, "emote_wheel.png"), 1920, 1080);
            input.Frame.EmoteHeld = false; // let go: the chosen emote plays
            yield return NetHarness.WaitUntil(() => me.EmoteId.Value != 0, "the chosen emote plays", null, 3f);
            Debug.Log("[EmoteCapture] chosen emote id " + me.EmoteId.Value);
            yield return Seconds(1.0f);
            if (pc.Rig.Mode == ViewMode.FirstPerson) pc.Rig.ToggleMode();
            yield return Seconds(0.6f);
            yield return UiShot.Save(Path.Combine(folder, "emote_wheel_result_third.png"), 1920, 1080);
            me.ServerEndEmote();

            for (int e = 1; e <= Emotes.Count; e++)
            {
                yield return Seconds(0.6f);
                Assert.IsTrue(me.ServerEmote((Emote)e), "emote " + e);
                yield return Seconds(Emotes.Duration((Emote)e) * (e == (int)Emote.Bow ? 0.5f : 0.42f));
                yield return UiShot.Save(Path.Combine(folder, $"emote_{e}_{(Emote)e}.png"), 1920, 1080);
                // A second frame of the swing for the moving ones.
                if (e == (int)Emote.Wave || e == (int)Emote.Dance || e == (int)Emote.Clap)
                {
                    yield return Seconds(0.3f);
                    yield return UiShot.Save(Path.Combine(folder, $"emote_{e}_{(Emote)e}_b.png"), 1920, 1080);
                }
                me.ServerEndEmote();
            }
        }
    }
}
