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

        // Mockup-screen elements (rect, text, size) for the numeric comparison.
        static void DumpScreen(MockScreen s, string path)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in s.Rects)
            {
                var it = kv.Key;
                sb.AppendLine(string.Join("|", it.k, Mathf.Round(it.r[0]), Mathf.Round(it.r[1]), Mathf.Round(it.r[2]), Mathf.Round(it.r[3]), it.t ?? it.src ?? "", it.fs, it.fw));
            }
            File.WriteAllText(path, sb.ToString());
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
            PlayerAppearanceStore.Save(PlayerAppearance.FromIndex(2, 1)); // the player's chosen look (SwatGuy, variant 1)
            yield return Menu();
            var menu = MainMenu.Instance;
            var panel = menu.Panel;
            Assert.IsNotNull(menu.Stage, "the lobby has its 3D stage");
            Assert.IsNotNull(menu.Stage.CharacterOf(0), "the local player stands in the car");
            Assert.AreEqual("Lobby_MixamoSwatGuy", menu.Stage.CharacterOf(0).name, "slot 0 is the saved PlayerAppearance");
            Assert.AreEqual(1, menu.Stage.LookOf(0).Value.Variant);
            File.WriteAllText(Path.Combine(folder, "lobby_look.txt"), PlayerAppearanceStore.Load().Serialize());
            {
                var sbd = new System.Text.StringBuilder();
                var ch = menu.Stage.CharacterOf(0);
                sbd.AppendLine("pos " + ch.transform.position + " active " + ch.activeInHierarchy + " cam " + Camera.main.transform.position + " " + Camera.main.transform.rotation.eulerAngles);
                foreach (var r in ch.GetComponentsInChildren<Renderer>(true))
                    sbd.AppendLine(r.name + " enabled=" + r.enabled + " vis=" + r.isVisible + " bounds=" + r.bounds.center + " " + r.bounds.size + " mat=" + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "null") + " layer=" + r.gameObject.layer);
                var lg = ch.GetComponentInChildren<LODGroup>(true);
                sbd.AppendLine("lod " + (lg != null ? lg.lodCount + " size " + lg.size + " enabled " + lg.enabled : "none"));
                File.WriteAllText(Path.Combine(folder, "lobby_char_debug.txt"), sbd.ToString());
            }
            Assert.IsNotNull(panel, "the lobby is the entry screen");
            Assert.AreEqual("혼자 시작", panel.StatusLabel);
            StringAssert.Contains("솔로", panel.LedText);
            yield return Shots(folder, "lobby_solo");

            // Squad as leader: 도윤 ready, 서아 not yet (the mockup's sample party).
            var squad = new PartyLobby(0, "해무", PlayerAppearanceStore.Load()); squad.Join(2, "도윤", PlayerAppearance.FromIndex(5, 0)); squad.Join(3, "서아", PlayerAppearance.FromIndex(8, 2)); squad.SetReady(2, true);
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
            var member = new PartyLobby(10, "서아", PlayerAppearance.FromIndex(8, 2)); member.Join(0, "해무", PlayerAppearanceStore.Load()); member.Join(2, "도윤", PlayerAppearance.FromIndex(5, 0)); member.SetReady(2, true);
            panel.SetParty(member, 0);
            Assert.AreEqual("준비", panel.StatusLabel);
            yield return Shots(folder, "lobby_member");

            panel.SetParty(new PartyLobby(0, "해무"), 0);
            menu.ShowCustom();
            Assert.IsTrue(menu.CustomShown);
            Assert.AreEqual("50명", menu.CustomPanel.DropText[0].text);
            StringAssert.Contains(":", menu.CustomPanel.Code);
            menu.CustomPanel.SetView(true);
            Assert.IsTrue(menu.CustomPanel.FirstPerson);
            menu.CustomPanel.SetView(false);
            DumpScreen(menu.CustomPanel.Screen, Path.Combine(folder, "custom_rects.txt"));
            yield return Shots(folder, "custom");
            menu.Back();
            Assert.IsFalse(menu.CustomShown);

            PlayerAppearanceStore.Clear();
            menu.ShowCharacter();
            Assert.IsTrue(menu.CharacterShown);
            Assert.IsTrue(menu.Stage.Closeup, "the 3D camera is in close on the local character");
            Assert.AreEqual("외형 01 / 15", menu.Character.HeaderText);
            Assert.AreEqual("특수부대원", menu.Character.NameText);
            DumpScreen(menu.Character.Screen, Path.Combine(folder, "char_rects.txt"));
            yield return Shots(folder, "char");
            menu.Character.SelectModel(4);
            Assert.AreEqual("MixamoBrian", PlayerAppearanceStore.Load().ModelId, "the choice is saved at once");
            Assert.AreEqual("Lobby_MixamoBrian", menu.Stage.CharacterOf(menu.Stage.LocalSlot).name, "and shown on the 3D character at once");
            if (menu.Character.VariantCount > 1) { menu.Character.SetVariant(1); Assert.AreEqual(1, PlayerAppearanceStore.Load().Variant); }
            menu.Character.SetGear(0, 2); menu.Character.SetGear(1, 2); menu.Character.SetGear(2, 1);
            Assert.AreEqual(2, PlayerAppearanceStore.Load().Helmet);
            File.WriteAllText(Path.Combine(folder, "char_look.txt"), PlayerAppearanceStore.Load().Serialize());
            yield return Shots(folder, "char_changed");
            menu.Character.SelectModel(14);
            Assert.AreEqual("UbcFemale", PlayerAppearanceStore.Load().ModelId, "all 15 models are selectable");
            menu.Character.Apply();
            Assert.IsFalse(menu.CharacterShown);
            Assert.IsFalse(menu.Stage.Closeup);
            PlayerAppearanceStore.Clear();
            menu.ShowQuit();
            Assert.IsTrue(menu.ModalShown);
            DumpScreen(menu.ModalScreen, Path.Combine(folder, "modal_quit_rects.txt"));
            yield return Shots(folder, "modal_quit");
            menu.CloseModal();
            Assert.IsFalse(menu.ModalShown);
            menu.ShowModeModal();
            DumpScreen(menu.ModalScreen, Path.Combine(folder, "modal_mode_rects.txt"));
            yield return Shots(folder, "modal_mode");
            menu.OnMock(new MockScreen.Item { act = "mode:squad", id = "mlq" });
            Assert.AreEqual(TeamMode.Squad, panel.Party.Mode, "the mode window changes the lobby");
            Assert.IsTrue(menu.ModalShown);
            menu.OnMock(new MockScreen.Item { act = "modal:", id = "mc" });
            menu.OnMock(new MockScreen.Item { act = "mode:solo", id = "mls" });
            menu.CloseModal();
            menu.Invite();
            StringAssert.Contains("CH-7K2Q", menu.ToastText);
            Assert.AreEqual("CH-7K2Q", GUIUtility.systemCopyBuffer);
            yield return Shots(folder, "toast_invite");
            menu.ShowSettings();
            yield return Shots(folder, "settings");
            menu.Back();

            menu.ShowCredits();
            Assert.IsTrue(menu.CreditsShown);
            Assert.AreEqual(Credits.Entries.Length, menu.CreditsRows, "one live row per Credits.Entries item");
            DumpScreen(menu.CreditsScreen.Screen, Path.Combine(folder, "credits_rects.txt"));
            yield return Shots(folder, "credits");
            menu.Back();
            Assert.IsFalse(menu.CreditsShown);
        }

        // PERF: the lobby's frame time with the 3D party standing (the editor runs with graphics, no vsync).
        [UnityTest, Timeout(120000)]
        public IEnumerator Lobby_FrameTime()
        {
            PlayerAppearanceStore.Save(PlayerAppearance.FromIndex(2, 1));
            yield return Menu();
            var panel = MainMenu.Instance.Panel;
            var squad = new PartyLobby(0, "a", PlayerAppearanceStore.Load()); squad.Join(2, "b", PlayerAppearance.FromIndex(5, 0)); squad.Join(3, "c", PlayerAppearance.FromIndex(8, 2)); squad.Join(4, "d", PlayerAppearance.FromIndex(12, 1));
            panel.SetParty(squad, 0);
            for (int i = 0; i < 30; i++) yield return null;
            QualitySettings.vSyncCount = 0;
            float t0 = Time.realtimeSinceStartup; int n = 120;
            for (int i = 0; i < n; i++) yield return null;
            float ms = (Time.realtimeSinceStartup - t0) / n * 1000f;
            string folder = Folder();
            File.WriteAllText(Path.Combine(folder, "lobby_perf.txt"), "avg frame " + ms.ToString("0.00") + " ms over " + n + " frames, " + Screen.width + "x" + Screen.height + ", 4 characters\n");
            Assert.Less(ms, 33f, "the lobby stays above 30 fps");
            PlayerAppearanceStore.Clear();
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
