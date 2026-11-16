using System;
using System.Collections;
using System.IO;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // Player-build performance capture of the lobby (PERF_BUDGET A and B/C for the main menu): launch the player with
    //   Sublevel.app/Contents/MacOS/Sublevel -perflobby <out.json> [-perfvsync]   and   -captureall <dir>  (every screen in turn, PNG + captureall.json)
    // It stands a four-person party in the lobby, warms up 3 s, then records
    //   Frame: 600 frames of Time.unscaledDeltaTime (frame budget; with -perfvsync a 60 Hz display is required), and
    //   Render: 600 frames of the camera rendered into a 1920x1080 target with a 1-pixel read-back (render budget A; UI included),
    // and writes both with FrameStats (avg / p99 / max), a screenshot beside the json, and quits.
    public class LobbyPerfCapture : MonoBehaviour
    {
        const int Frames = 600;
        const float WarmupSeconds = 3f;

        [Serializable]
        public struct Result
        {
            public string Scene;
            public int Width, Height;
            public float DisplayHz;
            public bool Vsync;
            public FrameStats Frame;   // natural frames
            public FrameStats Render;  // forced-complete 1080p renders, UI included
            public long DrawCalls, Triangles; // 0 when the build has no profiler counters
            public int Characters;
        }

        string _path, _allDir;
        bool _vsync;

        // Every step is logged ("[perflobby] ...") to Player.log and the failure reason is also written beside the output
        // (<path>.fail.txt), so a silent exit cannot happen. Relative paths resolve against the process cwd; if the target
        // cannot be written the result goes to Application.persistentDataPath instead.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            string perf = ArgValue(args, "-perflobby"), all = ArgValue(args, "-captureall");
            bool wantPerf = Array.IndexOf(args, "-perflobby") >= 0, wantAll = Array.IndexOf(args, "-captureall") >= 0;
            if (!wantPerf && !wantAll) return;
            Debug.Log("[perflobby] args: " + string.Join(" ", args) + " | cwd=" + Directory.GetCurrentDirectory() + " | scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            if ((wantPerf && perf == null) || (wantAll && all == null))
            {
                Debug.LogError("[perflobby] flag given without a path argument; nothing captured");
                return;
            }
            var go = new GameObject("LobbyPerfCapture");
            DontDestroyOnLoad(go);
            var c = go.AddComponent<LobbyPerfCapture>();
            c._path = perf != null ? Path.GetFullPath(perf) : null;
            c._allDir = all != null ? Path.GetFullPath(all) : null;
            c._vsync = Array.IndexOf(args, "-perfvsync") >= 0;
            c.StartCoroutine(c.Main());
        }

        static string ArgValue(string[] args, string key)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : null;
        }

        void Fail(string why)
        {
            Debug.LogError("[perflobby] FAIL: " + why);
            try
            {
                string target = (_path ?? Path.Combine(_allDir, "captureall.json")) + ".fail.txt";
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllText(target, why);
            }
            catch (Exception e) { Debug.LogError("[perflobby] could not write fail file: " + e.Message); }
            Application.Quit(1);
        }

        IEnumerator Main()
        {
            float give = Time.realtimeSinceStartup + 30f;
            while (MainMenu.Instance == null || MainMenu.Instance.Panel == null)
            {
                if (Time.realtimeSinceStartup > give) { Fail("MainMenu not ready after 30 s (scene " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + ")"); yield break; }
                yield return null;
            }
            Debug.Log("[perflobby] menu ready");
            if (_allDir != null) yield return CaptureAll();
            if (_path != null) yield return Run();
            else Application.Quit();
        }

        // -captureall <dir>: walks every screen in the order that used to leave two roots active, saving one PNG per step plus
        // captureall.json (screen, active-root count, whether the 3D stage still renders). Count must be 1 in every row.
        IEnumerator CaptureAll()
        {
            var menu = MainMenu.Instance;
            Directory.CreateDirectory(_allDir);
            var steps = new (string name, Action go)[]
            {
                ("lobby", menu.Back), ("training", menu.ShowTrainingEntry), ("character", menu.ShowCharacter), ("settings", menu.ShowSettings),
                ("credits", menu.ShowCredits), ("settings2", menu.ShowSettings), ("custom", menu.ShowCustom), ("credits2", menu.ShowCredits),
                ("character2", menu.ShowCharacter), ("training2", menu.ShowTrainingEntry), ("custom2", menu.ShowCustom), ("lobby2", menu.Back),
            };
            var rows = new System.Text.StringBuilder("[\n");
            int bad = 0;
            for (int i = 0; i < steps.Length; i++)
            {
                steps[i].go();
                float t = Time.realtimeSinceStartup + 0.8f;
                while (Time.realtimeSinceStartup < t) yield return null;
                int roots = menu.ActiveScreenRoots;
                if (roots != 1) bad++;
                string file = Path.Combine(_allDir, (i + 1).ToString("00") + "_" + steps[i].name + ".png");
                ScreenCapture.CaptureScreenshot(file);
                for (int k = 0; k < 3; k++) yield return null;
                rows.Append("  {\"step\":\"" + steps[i].name + "\",\"activeRoots\":" + roots + ",\"stage3D\":" + (menu.Stage != null ? "true" : "false") + ",\"png\":\"" + file.Replace("\\", "/") + "\"}" + (i < steps.Length - 1 ? "," : "") + "\n");
                Debug.Log("[perflobby] captureall " + steps[i].name + " roots=" + roots);
            }
            rows.Append("]");
            File.WriteAllText(Path.Combine(_allDir, "captureall.json"), "{\"violations\":" + bad + ",\"steps\":" + rows + "}");
            Debug.Log("[perflobby] captureall done violations=" + bad);
            menu.Back();
        }

        IEnumerator Run()
        {
            while (MainMenu.Instance == null || MainMenu.Instance.Panel == null) yield return null;
            var menu = MainMenu.Instance;
            // The same sample party as the editor test; the player's own saved look is put back afterwards.
            string saved = PlayerPrefs.GetString(PlayerAppearanceStore.Key, "");
            PlayerAppearanceStore.Save(PlayerAppearance.FromIndex(2, 1));
            var party = new PartyLobby(0, "a", PlayerAppearanceStore.Load());
            party.Join(2, "b", PlayerAppearance.FromIndex(5, 0));
            party.Join(3, "c", PlayerAppearance.FromIndex(8, 2));
            party.Join(4, "d", PlayerAppearance.FromIndex(12, 1));
            menu.Panel.SetParty(party, 0);
            QualitySettings.vSyncCount = _vsync ? 1 : 0;
            Application.targetFrameRate = -1;

            float until = Time.realtimeSinceStartup + WarmupSeconds;
            while (Time.realtimeSinceStartup < until) yield return null;

            var frame = new float[Frames];
            for (int i = 0; i < Frames; i++) { yield return null; frame[i] = Time.unscaledDeltaTime * 1000f; }

            // Render budget: the lobby camera into a 1080p target, each frame forced to finish.
            var cam = Camera.main;
            var rt = new RenderTexture(1920, 1080, 24);
            var canvases = new System.Collections.Generic.List<Canvas>();
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = cam.nearClipPlane + 0.05f; canvases.Add(c); }
            var oldTarget = cam.targetTexture;
            cam.targetTexture = rt;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            Unity.Profiling.ProfilerRecorder draws = default, tris = default;
            try
            {
                draws = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Draw Calls Count");
                tris = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Triangles Count");
            }
            catch (Exception) { }
            var render = new float[Frames];
            long drawCalls = 0, triangles = 0;
            for (int i = 0; i < Frames; i++)
            {
                yield return null;
                float t0 = Time.realtimeSinceStartup;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                RenderTexture.active = null;
                render[i] = (Time.realtimeSinceStartup - t0) * 1000f;
                if (i == Frames / 2) { if (draws.Valid) drawCalls = draws.LastValue; if (tris.Valid) triangles = tris.LastValue; }
            }
            cam.targetTexture = oldTarget;
            foreach (var c in canvases) c.renderMode = RenderMode.ScreenSpaceOverlay;
            if (draws.Valid) draws.Dispose();
            if (tris.Valid) tris.Dispose();
            rt.Release();

            var result = new Result
            {
                Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                Width = Screen.width, Height = Screen.height,
                DisplayHz = (float)Screen.currentResolution.refreshRateRatio.value,
                Vsync = _vsync,
                Frame = FrameStats.Compute(frame, 0),
                Render = FrameStats.Compute(render, 0),
                DrawCalls = drawCalls, Triangles = triangles,
                Characters = menu.Stage != null ? CountCharacters(menu.Stage) : 0,
            };
            string outPath = _path;
            try { Directory.CreateDirectory(Path.GetDirectoryName(outPath)); File.WriteAllText(outPath, JsonUtility.ToJson(result, true)); }
            catch (Exception e)
            {
                outPath = Path.Combine(Application.persistentDataPath, "perf_lobby.json");
                Debug.LogError("[perflobby] write failed (" + e.Message + "), using " + outPath);
                File.WriteAllText(outPath, JsonUtility.ToJson(result, true));
            }
            Debug.Log("[perflobby] wrote " + outPath);
            ScreenCapture.CaptureScreenshot(Path.ChangeExtension(outPath, ".png"));
            if (string.IsNullOrEmpty(saved)) PlayerAppearanceStore.Clear(); else PlayerPrefs.SetString(PlayerAppearanceStore.Key, saved);
            for (int i = 0; i < 4; i++) yield return null; // the screenshot is written at the end of a later frame
            Application.Quit();
        }

        static int CountCharacters(LobbyStage stage)
        {
            int n = 0;
            for (int i = 0; i < LobbyStage.Slots; i++) if (stage.CharacterOf(i) != null) n++;
            return n;
        }
    }
}
