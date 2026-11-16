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
        static readonly int[][] Sizes = { new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 1366, 768 }, new[] { 1920, 1200 }, new[] { 3440, 1440 } };

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

        // Every element's rect in stage pixels (top-left origin) with its text, to compare against the measured mockup.
        static void DumpRects(LobbyPanel panel, string path)
        {
            var sb = new System.Text.StringBuilder();
            var stage = panel.Stage;
            var corners = new Vector3[4];
            foreach (var r in stage.GetComponentsInChildren<RectTransform>(false))
            {
                if (r == stage) continue;
                r.GetWorldCorners(corners);
                var tl = stage.InverseTransformPoint(corners[1]); var br = stage.InverseTransformPoint(corners[3]);
                float x = tl.x + 960f, y = 540f - tl.y, w = br.x - tl.x, h = tl.y - br.y;
                var text = r.GetComponent<UnityEngine.UI.Text>();
                var img = r.GetComponent<UnityEngine.UI.Image>();
                string col = text != null ? ColorUtility.ToHtmlStringRGBA(text.color) : img != null ? ColorUtility.ToHtmlStringRGBA(img.color) : "";
                // Text: the glyph box (preferred size placed by the alignment), which is what the mockup's text rects measure.
                if (text != null)
                {
                    float pw = Mathf.Min(text.preferredWidth, text.horizontalOverflow == HorizontalWrapMode.Wrap ? w : float.MaxValue), ph = text.preferredHeight;
                    string a = text.alignment.ToString();
                    float gx = a.EndsWith("Left") ? x : a.EndsWith("Right") ? x + w - pw : x + (w - pw) * 0.5f;
                    float gy = a.StartsWith("Upper") ? y : a.StartsWith("Lower") ? y + h - ph : y + (h - ph) * 0.5f;
                    x = gx; y = gy; w = pw; h = ph;
                }
                sb.AppendLine(string.Join("|", r.name, Mathf.Round(x), Mathf.Round(y), Mathf.Round(w), Mathf.Round(h), text != null ? text.text.Replace("\n", " ") : "", text != null ? text.fontSize + "/" + text.fontStyle : "", col));
            }
            File.WriteAllText(path, sb.ToString());
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

            // Squad as leader: 도윤 ready, 서아 not yet (the mockup's sample party).
            var squad = new PartyLobby(0, "해무"); squad.Join(2, "도윤"); squad.Join(3, "서아"); squad.SetReady(2, true);
            panel.SetParty(squad, 0);
            StringAssert.Contains("스쿼드", panel.LedText);
            Assert.AreEqual("파티 시작", panel.StatusLabel);
            StringAssert.Contains("서아 님이 준비 중입니다", panel.ReasonText);
            DumpRects(panel, Path.Combine(folder, "lobby_squad_rects.txt"));
            yield return Shots(folder, "lobby_squad");

            // Matching.
            squad.SetReady(3, true); squad.Start(0);
            panel.SetParty(squad, 0);
            StringAssert.Contains("매칭 취소", panel.StatusLabel);
            yield return Shots(folder, "lobby_search");

            // Member view: 서아 leads, I (해무) have not readied.
            var member = new PartyLobby(10, "서아"); member.Join(0, "해무"); member.Join(2, "도윤"); member.SetReady(2, true);
            panel.SetParty(member, 0);
            Assert.AreEqual("준비", panel.StatusLabel);
            yield return Shots(folder, "lobby_member");

            panel.SetParty(new PartyLobby(0, "해무"), 0);
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
