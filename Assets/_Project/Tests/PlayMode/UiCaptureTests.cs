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
    // Resolution shots of the non-game UI (lobby, custom mode, settings, credits) with the checks next to them (S9).
    // No sound is played. Run on purpose with
    //   UI_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.UiCaptureTests
    [Explicit, Category("Capture")]
    public class UiCaptureTests
    {
        static readonly int[][] Sizes = { new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 1920, 1200 }, new[] { 3440, 1440 } };

        static string Folder()
        {
            string folder = Environment.GetEnvironmentVariable("UI_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/uishots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            return folder;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static IEnumerator Menu()
        {
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return NetHarness.WaitUntil(() => MainMenu.Instance != null, "main menu", null, 20f);
            yield return null;
            Camera.main.cullingMask = -1;
        }

        static IEnumerator Shots(string folder, string name)
        {
            foreach (var s in Sizes)
                yield return UiShot.Save(Path.Combine(folder, name + "_" + s[0] + "x" + s[1] + ".png"), s[0], s[1]);
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Lobby_Solo_And_Squad_AndTheOtherScreens()
        {
            string folder = Folder();
            yield return Menu();
            var menu = MainMenu.Instance;
            var panel = menu.Panel;
            Assert.IsNotNull(panel, "the lobby is the entry screen");
            Assert.AreEqual("혼자 시작", panel.StatusLabel);
            StringAssert.Contains("솔로", panel.LedText);
            yield return Shots(folder, "lobby_solo");

            panel.Party.SetMode(LobbyPanel.SelfId, TeamMode.Squad);
            panel.Refresh();
            StringAssert.Contains("스쿼드", panel.LedText);
            StringAssert.Contains("팀 규칙", panel.ReasonText, "squad cannot start yet and says why");
            yield return Shots(folder, "lobby_squad");

            panel.Party.SetMode(LobbyPanel.SelfId, TeamMode.Solo);
            panel.Refresh();
            menu.ShowCustom();
            Assert.IsTrue(menu.CustomShown);
            yield return Shots(folder, "custom");
            menu.Back();
            Assert.IsFalse(menu.CustomShown);

            menu.ShowSettings();
            yield return Shots(folder, "settings");
            menu.Back();

            menu.ShowCredits();
            Assert.IsTrue(menu.CreditsShown);
            yield return Shots(folder, "credits");
            menu.Back();
            Assert.IsFalse(menu.CreditsShown);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator HazardSlot_ShowsLineAndItems_AndHides()
        {
            var root = new GameObject("c", typeof(Canvas)).transform;
            var slot = HudHazardSlot.Build(root, new Vector2(0.5f, 1f), Vector2.zero);
            Assert.IsFalse(slot.Visible);
            slot.Set(new HudHazardView { Warn = HazardWarn.Warning, SecondsToClose = 65, Region = "해안 구역", Keys = 1, Chip = HudChip.Held });
            Assert.IsTrue(slot.Visible);
            Assert.AreEqual("[경고] 해안 구역 · 봉쇄까지 01:05", slot.LineText);
            Assert.AreEqual("열쇠 ×1  ·  칩 보유", slot.ItemsText);
            slot.Set(default);
            Assert.IsFalse(slot.Visible);
            UnityEngine.Object.Destroy(root.gameObject);
            yield return null;
        }
    }
}
