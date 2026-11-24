using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The event race on the screen (ADR-519, #445, #446): the course name while waiting, five start lights that come on one by one with the
    // big countdown number, then GO; at the end the results table, then back to the menu. While racing, as in the approved mockup
    // (docs/research/ui_mockups/race_room.html, ADR-562, #510; its 1280 px coordinates x1.5): the position and the lap (or the sprint's
    // metres left) top left, the standings with each car's gap to the leader under them, the lap clock top centre with the gap to the car
    // ahead under it, the course minimap with every car's dot top right and the best lap bottom left (the car's cluster, VehicleHud, keeps
    // the bottom centre). It reads Net's RaceHud (what the server said); it decides nothing. Put into any scene that has a RaceDirector.
    public sealed class RaceHudView : MonoBehaviour
    {
        Text _top, _big, _pos, _lap, _lapKey, _clock, _gap, _best, _resultsTitle;
        GameObject _racePanels, _bestPanel;
        const int MaxRows = 12;
        readonly Image[] _rowBg = new Image[MaxRows];
        readonly Text[] _rowRank = new Text[MaxRows], _rowName = new Text[MaxRows], _rowGap = new Text[MaxRows];
        RectTransform _board, _mapRect, _dotRoot;
        readonly System.Collections.Generic.List<Image> _dots = new System.Collections.Generic.List<Image>();
        RaceCourse _mapCourse;
        Vector3 _mapFit;   // world x and z at the map's bottom left corner, pixels per metre
        static readonly Color Dim = new Color(0.60f, 0.65f, 0.69f), Ready = new Color(0.95f, 0.76f, 0.19f), MeRow = new Color(0.12f, 0.44f, 0.82f),
            MyDot = new Color(0.95f, 0.62f, 0.15f), TrackEdge = new Color(0.23f, 0.29f, 0.24f), TrackLine = new Color(0.91f, 0.93f, 0.95f);
        const float MapW = 300f, MapH = 255f, MapPad = 18f;
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
            BuildRacePanels(root);
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

        static RectTransform Panel(string name, Transform parent, Vector2 corner, Vector2 position, Vector2 size)
        {
            var r = UiKit.Rect(name, parent, corner, corner, position, size);
            var bg = r.gameObject.AddComponent<Image>(); bg.color = UiKit.Panel; bg.raycastTarget = false;
            bg.sprite = UiPaint.Round(9); bg.type = Image.Type.Sliced;
            return r;
        }

        static Text At(RectTransform parent, string name, Vector2 position, Vector2 size, int fontSize, Color color, TextAnchor align, FontStyle style = FontStyle.Normal)
            => UiKit.Label(UiKit.Rect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), position, size), "", fontSize, color, align, style);

        void BuildRacePanels(RectTransform root)
        {
            var tl = new Vector2(0f, 1f);
            _racePanels = new GameObject("RacePanels", typeof(RectTransform));
            var rp = (RectTransform)_racePanels.transform; rp.SetParent(root, false);
            rp.anchorMin = Vector2.zero; rp.anchorMax = Vector2.one; rp.offsetMin = rp.offsetMax = Vector2.zero;
            // Position and lap: 220x84 at (16, 16).
            var pos = Panel("Pos", rp, tl, new Vector2(24f, -24f), new Vector2(330f, 126f));
            At(pos, "PosKey", new Vector2(24f, -12f), new Vector2(120f, 26f), 18, Dim, TextAnchor.UpperLeft).text = "순위";
            _pos = At(pos, "Pos", new Vector2(22f, -34f), new Vector2(150f, 84f), 74, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            _lapKey = At(pos, "LapKey", new Vector2(178f, -12f), new Vector2(150f, 26f), 18, Dim, TextAnchor.UpperLeft);
            _lap = At(pos, "Lap", new Vector2(176f, -34f), new Vector2(150f, 84f), 50, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            // Standings: 240 wide at (16, 108); rows of rank, name and gap.
            _board = Panel("Standings", rp, tl, new Vector2(24f, -162f), new Vector2(360f, 12f));
            for (int i = 0; i < MaxRows; i++)
            {
                var row = UiKit.Rect("Row" + i, _board, tl, tl, new Vector2(0f, -6f - i * 34f), new Vector2(360f, 34f));
                _rowBg[i] = row.gameObject.AddComponent<Image>(); _rowBg[i].raycastTarget = false; _rowBg[i].color = MeRow;
                _rowRank[i] = At(row, "Rank", new Vector2(15f, 0f), new Vector2(36f, 34f), 21, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
                _rowName[i] = At(row, "Name", new Vector2(54f, 0f), new Vector2(200f, 34f), 21, Color.white, TextAnchor.MiddleLeft);
                _rowName[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                _rowGap[i] = At(row, "Gap", new Vector2(246f, 0f), new Vector2(99f, 34f), 21, Dim, TextAnchor.MiddleRight);
                row.gameObject.SetActive(false);
            }
            // The lap clock top centre and the gap to the car ahead under it.
            var clock = Panel("Clock", rp, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(300f, 66f));
            _clock = UiKit.Label(UiKit.Rect("Text", clock, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 66f)), "", 46, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _gap = UiKit.Label(UiKit.Rect("GapAhead", rp, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(600f, 60f)), "", 21, Ready, TextAnchor.UpperCenter, FontStyle.Bold);
            _gap.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1.5f, -1.5f);   // no plate under it (the mockup's): readable on a bright sky
            // The minimap: 200x170 at the top right.
            var map = Panel("Minimap", rp, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(MapW, MapH));
            _mapRect = map;
            _dotRoot = UiKit.Rect("Dots", map, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(MapW, MapH));
            // The best lap bottom left.
            var best = Panel("Best", rp, Vector2.zero, new Vector2(24f, 24f), new Vector2(230f, 84f));
            _bestPanel = best.gameObject;
            At(best, "Key", new Vector2(21f, -10f), new Vector2(200f, 24f), 19, Dim, TextAnchor.UpperLeft).text = "베스트 랩";
            _best = At(best, "Time", new Vector2(20f, -34f), new Vector2(200f, 44f), 36, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            _racePanels.SetActive(false);
        }

        // The course line drawn once into a texture (the mockup's track: a wide dark edge under a thin light line), north up, fitted into
        // the panel; _mapFit keeps the mapping for the car dots.
        void BuildMap(RaceCourse course)
        {
            _mapCourse = course;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var p in course.Path) { minX = Mathf.Min(minX, p.X); maxX = Mathf.Max(maxX, p.X); minZ = Mathf.Min(minZ, p.Z); maxZ = Mathf.Max(maxZ, p.Z); }
            foreach (var p in course.BranchPath) { minX = Mathf.Min(minX, p.X); maxX = Mathf.Max(maxX, p.X); minZ = Mathf.Min(minZ, p.Z); maxZ = Mathf.Max(maxZ, p.Z); }
            float ppm = Mathf.Min((MapW - 2f * MapPad) / Mathf.Max(1f, maxX - minX), (MapH - 2f * MapPad) / Mathf.Max(1f, maxZ - minZ));
            float ox = (MapW - (maxX - minX) * ppm) * 0.5f, oz = (MapH - (maxZ - minZ) * ppm) * 0.5f;
            _mapFit = new Vector3(minX - ox / ppm, minZ - oz / ppm, ppm);
            int w = (int)MapW, h = (int)MapH;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color[w * h];
            foreach (var (r, c) in new[] { (7.5f, TrackEdge), (2.2f, TrackLine) })
            {
                var cover = new float[w * h];
                Stroke(cover, w, h, course.Path, !course.Open, r);
                Stroke(cover, w, h, course.BranchPath, false, r);
                for (int i = 0; i < px.Length; i++)
                    if (cover[i] > 0f) px[i] = new Color(Mathf.Lerp(px[i].r, c.r, cover[i]), Mathf.Lerp(px[i].g, c.g, cover[i]), Mathf.Lerp(px[i].b, c.b, cover[i]), Mathf.Max(px[i].a, cover[i]));
            }
            tex.SetPixels(px); tex.Apply();
            var img = UiKit.Rect("Course", _mapRect, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(MapW, MapH)).gameObject.AddComponent<RawImage>();
            img.texture = tex; img.raycastTarget = false;
            img.transform.SetSiblingIndex(0);
        }

        Vector2 MapPoint(float x, float z) => new Vector2((x - _mapFit.x) * _mapFit.z, (z - _mapFit.y) * _mapFit.z);

        // How much of each texel a line of radius `r` along the polyline covers (discs stamped every half pixel, a soft one-pixel edge).
        void Stroke(float[] cover, int w, int h, System.Collections.Generic.IReadOnlyList<RaceCourse.Point> path, bool closed, float r)
        {
            int n = path.Count;
            for (int i = 0; i < (closed ? n : n - 1); i++)
            {
                Vector2 a = MapPoint(path[i].X, path[i].Z), b = MapPoint(path[(i + 1) % n].X, path[(i + 1) % n].Z);
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) * 2f));
                for (int k = 0; k <= steps; k++)
                {
                    var q = Vector2.Lerp(a, b, k / (float)steps);
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(q.x - r - 1f)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(q.x + r + 1f));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(q.y - r - 1f)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(q.y + r + 1f));
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float c = Mathf.Clamp01(r + 0.5f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), q));
                            if (c > cover[y * w + x]) cover[y * w + x] = c;
                        }
                }
            }
        }

        // Every car's dot on the minimap, mine bigger and orange on top.
        void TickDots()
        {
            var mine = VehicleSeat.VehicleOf(Combatant.LocalPlayer);
            int n = 0; Image myDot = null;
            foreach (var v in Vehicle.All)
            {
                if (v == null || v.Kind != VehicleKind.Car) continue;
                if (n == _dots.Count)
                {
                    var d = UiKit.Rect("Dot", _dotRoot, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f)).gameObject.AddComponent<Image>();
                    d.sprite = UiPaint.Round(8); d.raycastTarget = false;
                    _dots.Add(d);
                }
                var dot = _dots[n++];
                dot.gameObject.SetActive(true);
                bool me = v == mine;
                dot.color = me ? MyDot : TrackLine;
                dot.rectTransform.sizeDelta = Vector2.one * (me ? 18f : 12f);
                dot.rectTransform.anchoredPosition = MapPoint(v.transform.position.x, v.transform.position.z);
                if (me) myDot = dot;
            }
            for (int i = n; i < _dots.Count; i++) _dots[i].gameObject.SetActive(false);
            if (myDot != null) myDot.transform.SetAsLastSibling();
        }

        static string Gap(float seconds) => seconds < 0f ? "" : "+" + seconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

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
            _racePanels.SetActive(racing && me >= 0);
            var course = director != null ? director.Info.Course : null;
            // Text that only changes with a new word from the server (4 a second) is built then, not every frame (GC budget).
            bool fresh = RaceHud.LastAt != _builtAt;
            if (racing && me >= 0)
            {
                if (course != null && course != _mapCourse) BuildMap(course);
                TickDots();
                bool done = m.Done[me];
                float run = m.Running[me] >= 0f ? m.Running[me] + RaceHud.SinceLast : -1f;
                if (fresh) _pos.text = $"{me + 1}<size=34><color=#9AA5B1>/{m.Keys.Length}</color></size>";
                _clock.text = done ? RaceBoard.Time(m.Times[me]) : run >= 0f ? RaceBoard.Time(run) : "--";
                if (fresh)
                {
                    string ahead = done ? "완주!" : me == 0 ? "선두" : m.Gaps != null && m.Gaps.Length > me && m.Gaps[me] >= 0f && m.Gaps[me - 1] >= 0f ? "앞 차와 " + Gap(m.Gaps[me] - m.Gaps[me - 1]) : "";
                    if (phase == RacePhase.Finishing) ahead += $"\n종료까지 {Mathf.CeilToInt(Mathf.Max(0f, m.SecondsLeft))}초";
                    _gap.text = ahead;
                }
                bool sprint = course != null && course.Open;
                _bestPanel.SetActive(!sprint);
                if (sprint)
                {
                    // A sprint (#451): what is left to the finish from the car's own place, in the lap's box.
                    _lapKey.text = "남은 거리";
                    var car = VehicleSeat.VehicleOf(Combatant.LocalPlayer);
                    int left = car != null ? Mathf.CeilToInt(course.LapLength - Mathf.Clamp(course.Project(car.transform.position.x, car.transform.position.z).s, 0f, course.LapLength)) : -1;
                    if (done) _lap.text = "완주";
                    else if (left != _sprintLeft) { _sprintLeft = left; _lap.text = car != null ? $"<size=26>{RaceEvent.SprintLine(course.LapLength - left, course.LapLength)}</size>" : ""; }   // text only when the metres change
                }
                else if (fresh)
                {
                    int toGo = m.LapsToGo != null && m.LapsToGo.Length > me ? m.LapsToGo[me] : m.Laps;   // one more after a false start
                    _lapKey.text = toGo > m.Laps ? "랩 (부정 출발 +1)" : "랩";
                    _lap.text = done ? "완주" : $"{Mathf.Min(m.LapsDone[me] + 1, toGo)}<size=26><color=#9AA5B1>/{toGo}</color></size>";
                    _best.text = m.Best[me] > 0f ? RaceBoard.Time(m.Best[me]) : "--";
                }
            }
            if (fresh)
            {
                _builtAt = RaceHud.LastAt;
                if (racing) Standings(m, me);
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

        // The standings rows (rank, name, +gap to the leader; my row on blue), the panel sized to them.
        void Standings(RaceStandings m, int me)
        {
            int n = m.Keys == null ? 0 : Mathf.Min(m.Keys.Length, MaxRows);
            for (int i = 0; i < MaxRows; i++)
            {
                bool on = i < n;
                _rowBg[i].transform.gameObject.SetActive(on);
                if (!on) continue;
                _rowBg[i].enabled = i == me;
                _rowRank[i].text = (i + 1).ToString();
                _rowName[i].text = m.Done[i] ? m.Names[i] + " <color=#9AA5B1>완주</color>" : m.Names[i];
                _rowGap[i].text = i == 0 || m.Gaps == null || m.Gaps.Length <= i ? "" : Gap(m.Gaps[i]);
                _rowGap[i].color = i == me ? Color.white : Dim;
            }
            _board.sizeDelta = new Vector2(_board.sizeDelta.x, 12f + n * 34f);
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
