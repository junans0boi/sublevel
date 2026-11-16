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
            PlayerPrefs.SetInt(UiFx.ReducedKey, 1); // captures are taken after the motion, so they show the settled screen
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
            menu.ShowTrainingEntry();
            Assert.IsTrue(menu.TrainingEntryShown);
            menu.TrainingEntry.Select(2);
            Assert.AreEqual(2, menu.TrainingEntry.Selected);
            menu.TrainingEntry.Select(0);
            yield return Shots(folder, "training");
            menu.Back();
            Assert.IsFalse(menu.TrainingEntryShown);

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

        // PERF: the lobby (3D party + UI) rendered into a 1920x1080 target frame by frame. Each configuration is timed two ways:
        // "sync" = render + a 1-pixel read-back that waits for the GPU (real work, includes the stall); "submit" = render only
        // (CPU time to build and submit the frame). Switching one cost off at a time gives the contribution table.
        sealed class PerfResult { public float Avg, P99, SubmitAvg; public long Draws, Tris, Batches; }

        IEnumerator MeasureLobby(Camera cam, RenderTexture rt, int frames, PerfResult r)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var times = new System.Collections.Generic.List<float>();
            float submit = 0f; int submitN = 0;
            for (int i = 0; i < frames + 20; i++)
            {
                yield return null;
                float t0 = Time.realtimeSinceStartup;
                cam.Render();
                float t1 = Time.realtimeSinceStartup;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); // waits for the GPU
                RenderTexture.active = null;
                if (i >= 20) { times.Add((Time.realtimeSinceStartup - t0) * 1000f); submit += (t1 - t0) * 1000f; submitN++; }
#if UNITY_EDITOR
                if (i == 60) { r.Draws = UnityEditor.UnityStats.drawCalls; r.Tris = UnityEditor.UnityStats.triangles; r.Batches = UnityEditor.UnityStats.batches; }
#endif
            }
            times.Sort();
            float avg = 0f; foreach (var x in times) avg += x;
            r.Avg = avg / times.Count; r.P99 = times[Mathf.Min(times.Count - 1, (int)(times.Count * 0.99f))]; r.SubmitAvg = submit / Mathf.Max(1, submitN);
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Lobby_FrameTime()
        {
            PlayerPrefs.SetInt(UiFx.ReducedKey, 1);
            PlayerAppearanceStore.Save(PlayerAppearance.FromIndex(2, 1));
            yield return Menu();
            var panel = MainMenu.Instance.Panel;
            var squad = new PartyLobby(0, "a", PlayerAppearanceStore.Load()); squad.Join(2, "b", PlayerAppearance.FromIndex(5, 0)); squad.Join(3, "c", PlayerAppearance.FromIndex(8, 2)); squad.Join(4, "d", PlayerAppearance.FromIndex(12, 1));
            panel.SetParty(squad, 0);
            for (int i = 0; i < 30; i++) yield return null;
            var cam = Camera.main;
            var rt = new RenderTexture(1920, 1080, 24);
            var canvases = new System.Collections.Generic.List<Canvas>();
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = cam.nearClipPlane + 0.05f; canvases.Add(c); }
            var oldTarget = cam.targetTexture;
            cam.targetTexture = rt;
            var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            var volumes = UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None);
            var sb = new System.Text.StringBuilder();
            string folder = Folder();

            var baseline = new PerfResult();
            yield return MeasureLobby(cam, rt, 160, baseline);
            // The frame itself, for the look-regression comparison between builds.
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var frame = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            frame.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); frame.Apply(); RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(folder, "lobby_perf_frame.png"), frame.EncodeToPNG());
            sb.AppendLine($"baseline: sync avg {baseline.Avg:0.00} ms, p99 {baseline.P99:0.00} ms | submit avg {baseline.SubmitAvg:0.00} ms | drawcalls {baseline.Draws}, batches {baseline.Batches}, triangles {baseline.Tris}");

            // One cost off at a time: (label, switch off, switch on).
            var configs = new System.Collections.Generic.List<(string, System.Action, System.Action)>
            {
                ("no UI", () => { foreach (var c in canvases) c.enabled = false; }, () => { foreach (var c in canvases) c.enabled = true; }),
                ("no post-processing", () => data.renderPostProcessing = false, () => data.renderPostProcessing = true),
                ("no volumes", () => { foreach (var v in volumes) v.enabled = false; }, () => { foreach (var v in volumes) v.enabled = true; }),
                ("no additional lights", () => { foreach (var l in lights) if (l != null) l.enabled = false; }, () => { foreach (var l in lights) if (l != null) l.enabled = true; }),
                ("no CarLight", () => Toggle("CarLight", false), () => Toggle("CarLight", true)),
                ("no WallWash", () => Toggle("WallWash", false), () => Toggle("WallWash", true)),
                ("no Key", () => Toggle("Key", false), () => Toggle("Key", true)),
                ("no rim lights", () => { Toggle("RimLeft", false); Toggle("RimRight", false); }, () => { Toggle("RimLeft", true); Toggle("RimRight", true); }),
                ("no characters", () => MainMenu.Instance.Stage.gameObject.SetActive(false), () => MainMenu.Instance.Stage.gameObject.SetActive(true)),
                ("MSAA off", () => { var a = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset; if (a != null) { _msaa = a.msaaSampleCount; a.msaaSampleCount = 1; } }, () => { var a = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset; if (a != null) a.msaaSampleCount = _msaa; }),
                ("shadows off", () => { var a = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset; if (a != null) { _shadowDist = a.shadowDistance; a.shadowDistance = 0f; } }, () => { var a = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset; if (a != null) a.shadowDistance = _shadowDist; }),
            };
            foreach (var (label, off, on) in configs)
            {
                off();
                var r = new PerfResult();
                yield return MeasureLobby(cam, rt, 100, r);
                on();
                sb.AppendLine($"{label}: sync avg {r.Avg:0.00} ms (-{baseline.Avg - r.Avg:0.00}), p99 {r.P99:0.00} ms (-{baseline.P99 - r.P99:0.00}) | submit avg {r.SubmitAvg:0.00} ms | drawcalls {r.Draws}, triangles {r.Tris}");
            }
            cam.targetTexture = oldTarget;
            foreach (var c in canvases) c.renderMode = RenderMode.ScreenSpaceOverlay;
            rt.Release();
            File.WriteAllText(Path.Combine(folder, "lobby_perf.txt"), "lobby 1920x1080, 4 characters + UI\n" + sb);
            PlayerAppearanceStore.Clear();
            Assert.Less(baseline.Avg, 33f, "the lobby stays above 30 fps");
        }

        static int _msaa; static float _shadowDist;
        static void Toggle(string name, bool on) { foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (l.name == name) l.enabled = on; }

        sealed class FakeQueue : IMatchQueue
        {
            public MatchQueueState State { get; set; }
            public bool Waiting { get; set; }
            public bool Connected = true;
            public bool Cancelled;
            public event System.Action<MatchQueueState> StateChanged;
            public bool RequestStart(TeamMode mode, int partySize) { Waiting = Connected; return Connected; }
            public bool Cancel() { Waiting = false; Cancelled = true; return true; }
            public void Push(MatchQueueState s) { State = s; StateChanged?.Invoke(s); }
        }

        // 파티 시작 through the server queue: request, the real queue words, cancel, and the error when not connected.
        [UnityTest, Timeout(120000)]
        public IEnumerator Lobby_QueueStates()
        {
            PlayerPrefs.SetInt(UiFx.ReducedKey, 1);
            string folder = Folder();
            yield return Menu();
            var menu = MainMenu.Instance;
            var panel = menu.Panel;
            var fake = new FakeQueue();
            panel.AttachQueue(fake);
            var squad = new PartyLobby(0, "해무"); squad.Join(2, "도윤"); squad.SetReady(2, true);
            panel.SetParty(squad, 0);
            Assert.AreEqual("파티 시작", panel.StatusLabel);

            fake.Connected = false;                                   // error: not connected
            panel.PressStatus();
            Assert.AreEqual(QueueText.NotConnected, menu.ToastText);
            Assert.IsFalse(panel.Matching);

            fake.Connected = true;                                    // waiting: asked, no window yet
            panel.PressStatus();
            Assert.IsTrue(panel.Matching);
            StringAssert.Contains("매칭 취소", panel.StatusLabel);
            Assert.AreEqual(QueueText.Requested, panel.ReasonText);

            fake.Push(new MatchQueueState { Open = true, Waiting = 12, Parties = 3, Target = 50, SecondsLeft = 24f });
            yield return null; panel.Tick(0.1f);                       // the server's real numbers
            Assert.AreEqual("매칭 중 12/50 · 남은 00:24", panel.ReasonText);
            yield return Shots(folder, "lobby_queue");

            panel.PressStatus();                                      // cancel
            Assert.IsTrue(fake.Cancelled);
            Assert.IsFalse(panel.Matching);

            panel.PressStatus();                                      // again, then the match starts
            fake.Push(new MatchQueueState { Open = true, Started = true, Waiting = 12, Target = 50 });
            yield return null; panel.Tick(0.1f);
            Assert.IsFalse(panel.Matching, "the match was made: the lobby goes to it");
            panel.AttachQueue(null);
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
