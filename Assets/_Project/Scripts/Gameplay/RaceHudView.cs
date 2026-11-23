using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The event race on the screen (ADR-519, #445, #446): the course name while waiting, five start lights that come on one by one with the
    // big countdown number, then GO; while racing the position (P3/8), the lap (2/2), the lap clock and the best lap, and the race order on
    // the right; at the end the results table, then back to the menu. It reads Net's RaceHud (what the server said); it decides nothing.
    // Put into any scene that has a RaceDirector.
    public sealed class RaceHudView : MonoBehaviour
    {
        Text _top, _big, _pos, _lap, _clock, _order, _resultsTitle;
        int _sprintLeft = int.MinValue;
        readonly Text[] _cols = new Text[4];   // results: rank, name, time, best lap (one Text per column keeps them lined up)
        GameObject _resultsPanel;
        bool _left;
        float _builtAt = -1f, _resultsSeen = -1f, _overSeen = -1f;
        readonly Image[] _lights = new Image[5];
        static readonly Color LightOff = new Color(0.13f, 0.13f, 0.15f, 0.95f), LightOn = new Color(1f, 0.12f, 0.08f, 1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
            OnLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Object.FindAnyObjectByType<RaceDirector>() == null || Object.FindAnyObjectByType<RaceHudView>() != null) return;
            new GameObject("RaceHudView").AddComponent<RaceHudView>();
        }

        void Awake()
        {
            var canvas = UiKit.Canvas("RaceHudCanvas", 40, false);
            canvas.transform.SetParent(transform, false);
            var root = (RectTransform)canvas.transform;
            _top = UiKit.Label(UiKit.Rect("Top", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(1400f, 50f)),
                "", 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _big = UiKit.Label(UiKit.Rect("Big", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(800f, 220f)),
                "", 180, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            // While racing: the position big at the top left, the lap and the clocks under it, the order down the right.
            _pos = UiKit.Label(UiKit.Rect("Pos", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(300f, 90f)), "", 72, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            _lap = UiKit.Label(UiKit.Rect("Lap", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f, -120f), new Vector2(400f, 40f)), "", 30, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            _clock = UiKit.Label(UiKit.Rect("Clock", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f, -162f), new Vector2(500f, 70f)), "", 24, new Color(0.85f, 0.9f, 1f), TextAnchor.UpperLeft);
            _order = UiKit.Label(UiKit.Rect("Order", root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -110f), new Vector2(380f, 520f)), "", 22, Color.white, TextAnchor.UpperLeft);
            // At the end: a dark table in the middle.
            var panel = UiKit.Rect("Results", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 640f));
            var bg = panel.gameObject.AddComponent<Image>(); bg.color = new Color(0.04f, 0.06f, 0.08f, 0.88f); bg.raycastTarget = false;
            _resultsTitle = UiKit.Label(UiKit.Rect("Title", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(900f, 60f)), "", 40, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            float[] colX = { -420f, -320f, 120f, 300f }, colW = { 90f, 420f, 170f, 170f };
            for (int i = 0; i < 4; i++)
                _cols[i] = UiKit.Label(UiKit.Rect("Col" + i, panel, new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(colX[i], -100f), new Vector2(colW[i], 520f)), "", 26, Color.white, TextAnchor.UpperLeft);
            _resultsPanel = panel.gameObject;
            _resultsPanel.SetActive(false);
            for (int i = 0; i < 5; i++)
            {
                var r = UiKit.Rect("Light" + i, root, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-120f + i * 60f, -150f), new Vector2(48f, 48f));
                _lights[i] = r.gameObject.AddComponent<Image>();
                _lights[i].raycastTarget = false;
            }
        }

        void Update()
        {
            var director = RaceDirector.Current;
            var phase = RaceHud.Phase;
            bool counting = phase == RacePhase.Countdown, waiting = phase == RacePhase.Waiting && director != null;
            _top.text = waiting ? $"{director.Info.Name}  ·  다른 참가자를 기다리는 중" : "";
            int lit = counting ? Mathf.Clamp(5 - Mathf.CeilToInt(RaceHud.CountdownNow / RaceEvent.CountdownSeconds * 5f), 0, 5) : 0;
            for (int i = 0; i < 5; i++)
            {
                _lights[i].enabled = counting || waiting;
                _lights[i].color = i < lit ? LightOn : LightOff;
            }
            if (counting) { _big.text = Mathf.Max(1, Mathf.CeilToInt(RaceHud.CountdownNow)).ToString(); _big.color = new Color(1f, 0.85f, 0.2f); }
            else if (RaceHud.GoShowing) { _big.text = "GO!"; _big.color = new Color(0.4f, 1f, 0.45f); }
            else _big.text = "";

            var m = RaceHud.Last;
            bool racing = phase == RacePhase.Racing || phase == RacePhase.Finishing, end = phase == RacePhase.Results || phase == RacePhase.Over;
            int me = MyRow(m);
            _pos.text = racing && me >= 0 ? $"P{me + 1}/{m.Keys.Length}" : "";
            var course = director != null ? director.Info.Course : null;
            if (racing && me >= 0 && course != null && course.Open)
            {
                // A sprint (#451): what is left to the finish from the car's own place, and the time since the start line.
                bool done = m.Done[me];
                var car = VehicleSeat.VehicleOf(Combatant.LocalPlayer);
                int left = car != null ? Mathf.CeilToInt(course.LapLength - Mathf.Clamp(course.Project(car.transform.position.x, car.transform.position.z).s, 0f, course.LapLength)) : -1;
                if (done) _lap.text = "완주!";
                else if (left != _sprintLeft) { _sprintLeft = left; _lap.text = car != null ? RaceEvent.SprintLine(course.LapLength - left, course.LapLength) : ""; }   // text only when the metres change
                float run = m.Running[me] >= 0f ? m.Running[me] + RaceHud.SinceLast : -1f;
                _clock.text = done ? $"기록 {RaceBoard.Time(m.Times[me])}" : $"시간 {(run >= 0f ? RaceBoard.Time(run) : "--")}";
                if (phase == RacePhase.Finishing) _clock.text += $"\n종료까지 {Mathf.CeilToInt(Mathf.Max(0f, m.SecondsLeft - RaceHud.SinceLast))}초";
            }
            else if (racing && me >= 0)
            {
                bool done = m.Done[me];
                int toGo = m.LapsToGo != null && m.LapsToGo.Length > me ? m.LapsToGo[me] : m.Laps;   // one more after a false start
                _lap.text = done ? "완주!" : $"LAP {Mathf.Min(m.LapsDone[me] + 1, toGo)}/{toGo}{(toGo > m.Laps ? "  (부정 출발 +1)" : "")}";
                float run = m.Running[me] >= 0f ? m.Running[me] + RaceHud.SinceLast : -1f;
                _clock.text = done ? $"기록 {RaceBoard.Time(m.Times[me])}" : $"랩 {(run >= 0f ? RaceBoard.Time(run) : "--")}   최고 {(m.Best[me] > 0f ? RaceBoard.Time(m.Best[me]) : "--")}";
                if (phase == RacePhase.Finishing) _clock.text += $"\n종료까지 {Mathf.CeilToInt(Mathf.Max(0f, m.SecondsLeft - RaceHud.SinceLast))}초";
            }
            else { _lap.text = ""; _clock.text = ""; }
            // The order and the results only change with a new word from the server (4 a second): no text built every frame (GC budget).
            bool fresh = RaceHud.LastAt != _builtAt;
            if (fresh)
            {
                _builtAt = RaceHud.LastAt;
                _order.text = racing ? OrderText(m, me) : "";
                if (end && m.Keys != null)
                {
                    _resultsTitle.text = director != null ? $"{director.Info.Name}  결과" : "결과";
                    ResultsText(m, me, _cols, director != null && director.Info.Course.Open ? director.Info.Course.LapLength : 0f);
                }
            }
            _resultsPanel.SetActive(end && m.Keys != null);
            // Back to the menu once the results have been up their while: a guest also by its own clock (the server's Over may be lost),
            // the host a moment after Over so its guests get that word before its leaving ends the match.
            if (end && _resultsSeen < 0f) _resultsSeen = Time.unscaledTime;
            if (phase == RacePhase.Over && _overSeen < 0f) _overSeen = Time.unscaledTime;
            bool host = FishNet.InstanceFinder.IsServerStarted;
            bool leave = host ? _overSeen >= 0f && Time.unscaledTime - _overSeen >= 1f
                              : _overSeen >= 0f || (_resultsSeen >= 0f && Time.unscaledTime - _resultsSeen >= RaceEvent.ResultsSeconds + 2f);
            if (leave && !_left) { _left = true; MainMenu.LeaveMatch(); }
        }

        static int MyRow(RaceStandings m)
        {
            var me = Combatant.LocalPlayer;
            if (me == null || m.Keys == null) return -1;
            for (int i = 0; i < m.Keys.Length; i++) if (m.Keys[i] == me.ObjectId) return i;
            return -1;
        }

        static string OrderText(RaceStandings m, int me)
        {
            if (m.Keys == null) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < m.Keys.Length && i < 12; i++)
            {
                string line = $"{i + 1,2}  {m.Names[i]}{(m.Done[i] ? "  완주" : "")}";
                sb.AppendLine(i == me ? $"<b><color=#FFB347>{line}</color></b>" : line);
            }
            return sb.ToString();
        }

        // `sprint`: a sprint's length (its last column is the average speed instead of the best lap), 0 on a circuit.
        static void ResultsText(RaceStandings m, int me, Text[] cols, float sprint)
        {
            var sb = new System.Text.StringBuilder[4];
            string[] head = { "순위", "이름", "기록", sprint > 0f ? "평균 속도" : "최고 랩" };
            for (int c = 0; c < 4; c++) { sb[c] = new System.Text.StringBuilder(); sb[c].AppendLine(head[c]); }
            for (int i = 0; i < m.Keys.Length; i++)
            {
                string last = sprint > 0f ? (m.Done[i] ? RaceEvent.AverageKmh(sprint, m.Times[i]) : "--") : m.Best[i] > 0f ? RaceBoard.Time(m.Best[i]) : "--";
                string[] cell = { (i + 1).ToString(), m.Names[i], m.Done[i] ? RaceBoard.Time(m.Times[i]) : "DNF", last };
                for (int c = 0; c < 4; c++) sb[c].AppendLine(i == me ? $"<b><color=#FFB347>{cell[c]}</color></b>" : cell[c]);
            }
            for (int c = 0; c < 4; c++) cols[c].text = sb[c].ToString();
        }
    }
}
