using System;
using System.Collections;
using System.IO;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // Player-build performance capture of the lobby (PERF_BUDGET A and B/C for the main menu): launch the player with
    //   Sublevel.app/Contents/MacOS/Sublevel -perflobby <out.json> [-perfvsync]
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

        string _path;
        bool _vsync;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-perflobby");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("LobbyPerfCapture");
            DontDestroyOnLoad(go);
            var c = go.AddComponent<LobbyPerfCapture>();
            c._path = args[i + 1];
            c._vsync = Array.IndexOf(args, "-perfvsync") >= 0;
            c.StartCoroutine(c.Run());
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
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)));
            File.WriteAllText(_path, JsonUtility.ToJson(result, true));
            ScreenCapture.CaptureScreenshot(Path.ChangeExtension(Path.GetFullPath(_path), ".png"));
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
