using System.Collections.Generic;
using FishNet.Managing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Client: the countdown of the waiting island, as one line on the screen and as text on the big board of the island
    // (ADR-385, #418). The board turns through its pages (ADR-502, #422: countdown, bungee and range TOP3, the drop regions, tips) on
    // the countdown's own seconds, so every client shows the same page. The numbers are the server's broadcasts; nothing here decides anything.
    public sealed class WaitingHud : MonoBehaviour
    {
        WaitingBroadcast _last;
        float _at = -10f;
        NetworkManager _nm;
        static GUIStyle _style;
        readonly TextMesh[] _lines = new TextMesh[BoardText.Lines];
        readonly string[] _text = new string[BoardText.Lines];
        List<RecordRow> _bungeeTop = new List<RecordRow>(), _rangeTop = new List<RecordRow>();
        List<(string name, float seconds)> _lapTop = new List<(string, float)>();
        IslandLapBroadcast _lap; float _lapAt = -10f;   // the local driver's lap in the race hall (ADR-509)
        int _records;              // bumps with every records broadcast
        string[] _regions;
        (bool live, BoardPage page, int seconds, int total, int target, int bungee, int cycle, int records, int regions) _shown;
        bool _shownValid;
        float _regionsAskedAt = -10f;

        // The page the board shows now (tests compare clients).
        public BoardPage PageShown { get; private set; }
        public IReadOnlyList<RecordRow> RangeTop => _rangeTop;
        public IReadOnlyList<RecordRow> BungeeTop => _bungeeTop;
        public IReadOnlyList<(string name, float seconds)> LapTop => _lapTop;
        public string LineShown(int line) => _text[line] ?? "";

        public static void Attach(NetworkManager nm)
        {
            if (nm == null || nm.GetComponent<WaitingHud>() != null) return;
            var hud = nm.gameObject.AddComponent<WaitingHud>();
            hud._nm = nm;
            nm.ClientManager.RegisterBroadcast<WaitingBroadcast>(hud.OnBroadcast);
            nm.ClientManager.RegisterBroadcast<WaitingRecordsBroadcast>(hud.OnRecords);
            nm.ClientManager.RegisterBroadcast<IslandLapBroadcast>(hud.OnLap);
        }

        void OnLap(IslandLapBroadcast m, FishNet.Transporting.Channel channel) { _lap = m; _lapAt = Time.unscaledTime; }

        void OnDestroy()
        {
            OnIsland = false;
            if (_nm == null) return;
            _nm.ClientManager.UnregisterBroadcast<WaitingBroadcast>(OnBroadcast);
            _nm.ClientManager.UnregisterBroadcast<WaitingRecordsBroadcast>(OnRecords);
            _nm.ClientManager.UnregisterBroadcast<IslandLapBroadcast>(OnLap);
        }

        void OnRecords(WaitingRecordsBroadcast m, FishNet.Transporting.Channel channel)
        {
            _bungeeTop = Rows(m.BungeeNames, m.BungeeScores); _rangeTop = Rows(m.RangeNames, m.RangeScores);
            _lapTop = new List<(string, float)>();
            int laps = m.LapNames != null && m.LapSeconds != null ? Mathf.Min(m.LapNames.Length, m.LapSeconds.Length) : 0;
            for (int i = 0; i < laps; i++) _lapTop.Add((m.LapNames[i], m.LapSeconds[i]));
            _records++;
        }

        static List<RecordRow> Rows(string[] names, int[] scores)
        {
            var rows = new List<RecordRow>();
            int n = names != null && scores != null ? Mathf.Min(names.Length, scores.Length) : 0;
            for (int i = 0; i < n; i++) rows.Add(new RecordRow { Name = names[i], Score = scores[i] });
            return rows;
        }

        void OnBroadcast(WaitingBroadcast message, FishNet.Transporting.Channel channel)
        {
            int lit = message.LiftCalls & ~_last.LiftCalls;   // a button that just lit: its ding (the presser hears it, and who stands near)
            if (message.Active && lit != 0) { if ((lit & 1) != 0) Ding(false); if ((lit & 2) != 0) Ding(true); }
            _last = message; _at = Time.unscaledTime;
        }

        // True while the server says the room is open: the player stands on the island, not in an elevator.
        public static bool OnIsland { get; private set; }

        bool _preview;
        bool Live => _preview || (_last.Active && Time.unscaledTime - _at <= 3f);

        // Editor captures: the board as it reads with this broadcast, with no network behind it.
        public static WaitingHud Preview(WaitingBroadcast message, WaitingRecordsBroadcast records = default, string[] regions = null)
        {
            var hud = new GameObject("WaitingHudPreview").AddComponent<WaitingHud>();
            hud._last = message; hud._preview = true;
            hud.OnRecords(records, default);
            hud._regions = regions;
            hud.Update();
            return hud;
        }

        // Editor captures: show this page now, whatever the countdown says.
        public void PreviewPage(BoardPage page) { _forced = page; _shownValid = false; Update(); }
        BoardPage? _forced;

        void Update()
        {
            Refresh();
        }

        void Refresh()
        {
            bool live = Live;
            OnIsland = live;
            MoveLiftCar(live);
            if (_lines[0] == null && !TryBuildBoard()) return;
            var rules = IslandRules.Current ?? new IslandRules();
            int cycle = 0;
            var page = live ? _forced ?? BoardRotation.PageAt(_last.SecondsLeft, rules, out cycle) : BoardPage.Countdown;
            // The map's region names: built per call by the zone code, so asked at most once a second until it answers.
            if (page == BoardPage.Regions && (_regions == null || _regions.Length == 0) && Time.unscaledTime - _regionsAskedAt >= 1f)
            {
                _regionsAskedAt = Time.unscaledTime;
                _regions = PollutionDirector.GeometrySource?.Invoke()?.RegionName;
            }
            // Only when something on the board changed (about once a second): no garbage per frame.
            var shown = (live, page, Mathf.CeilToInt(_last.SecondsLeft), _last.Total, _last.Target, Mathf.RoundToInt(_last.BungeeBest * 10f), cycle, _records, _regions?.Length ?? 0);
            if (_shownValid && shown == _shown) return;
            _shown = shown; _shownValid = true;
            PageShown = page;
            if (!live) { for (int i = 0; i < _text.Length; i++) _text[i] = ""; _text[BoardText.Title] = "SUBLEVEL"; }
            else
            {
                var view = new BoardView { SecondsLeft = _last.SecondsLeft, Total = _last.Total, Target = _last.Target, BungeeBest = _last.BungeeBest,
                    BungeeTop = _bungeeTop, RangeTop = _rangeTop, LapTop = _lapTop, Regions = _regions, Tip = BoardRotation.TipAt(cycle, rules.Tips) };
                BoardText.Compose(page, view, _text);
            }
            for (int i = 0; i < _lines.Length; i++)
            {
                _lines[i].text = _text[i];
                _lines[i].gameObject.SetActive(_text[i].Length > 0);
            }
        }

        // The bungee lift's cage (ADR-387/490) follows the server's lift state (phase + seconds into it): the car, its shut doors and the
        // call buttons are drawn here, the riders' bodies are moved by their own BungeeRide. Before the first broadcast (or after the room
        // closed) the car waits at the bottom, doors open.
        Transform _car, _carDoors;
        CallButton _bottom, _top;
        LiftHoist _hoist;
        void MoveLiftCar(bool live)
        {
            if (_car == null)
            {
                var island = GameObject.Find("WaitingIsland");
                var car = island != null ? island.transform.Find("BungeeLiftCar") : null;
                if (car == null) return;
                _car = car; _carDoors = car.Find("Doors");
                Dress(island.transform);
                _hoist = new LiftHoist(island.transform, car, Look("WaitingSteel"), Look("WaitingAccent"));
            }
            var phase = live ? (Game.Core.LiftPhase)_last.LiftPhase : Game.Core.LiftPhase.OpenBottom;
            float into = live ? _last.LiftInto + (_preview ? 0f : Time.unscaledTime - _at) : 0f;
            float h = Game.Core.BungeeLift.HeightOf(phase, into);
            var p = _car.localPosition; p.y = h; _car.localPosition = p;
            _hoist.Move(h);
            if (_carDoors != null) _carDoors.gameObject.SetActive(!Game.Core.BungeeLift.DoorsOpenOf(phase, into));
            if (_bottom == null) { _bottom = new CallButton(_car.parent, false); _top = new CallButton(_car.parent, true); }
            bool moving = !Game.Core.BungeeLift.DoorsOpenOf(phase, into);
            string floor = (h > Game.Core.BungeeLift.Height * 0.5f ? "2" : "1") + (!moving ? "" : phase == Game.Core.LiftPhase.Rising ? "▲" : "▼");
            _bottom.Show(live && (_last.LiftCalls & 1) != 0, floor);
            _top.Show(live && (_last.LiftCalls & 2) != 0, floor);
        }

        static Material Look(string name) => Resources.Load<Material>("WaitingLook/" + name);

        // The island's own materials (ADR-495) on the hall, the tower and the lift car. shortcut: the World scene baked before ADR-495
        // still carries the town's materials, so they are swapped here by name; drop this once World.unity is rebuilt (the builder uses them).
        static void Dress(Transform island)
        {
            Material steel = Look("WaitingSteel"), panel = Look("WaitingPanel"), dome = Look("WaitingDome"), stainless = Look("WaitingStainless"), tread = Look("WaitingTread"), accent = Look("WaitingAccent");
            if (steel == null) return;
            foreach (var r in island.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.name; bool inCar = r.transform.parent != island;
                var m = !inCar && (n.StartsWith("Frame") || n == "Trim" || n == "BoardBack") ? steel : !inCar && n.StartsWith("Wall") ? panel : !inCar && n.StartsWith("Dome") ? dome : !inCar && n == "Band" ? accent
                    : inCar && (n == "Bars" || n == "Gates") ? stainless : inCar && n == "Floor" ? tread : null;
                if (m != null) r.sharedMaterial = m;
            }
        }

        // A call panel (ADR-490/495), built at runtime from WaitingIsland's numbers so the World scene need not be rebuilt: a brushed
        // steel faceplate (on a slim graphite pillar at the bottom, on the rail at the top) with a small dark window showing the car's
        // floor and one round push button with its arrow (▲ at the bottom, ▼ at the top) that glows while the call stands.
        sealed class CallButton
        {
            readonly Renderer _button; readonly Light _glow; readonly TextMesh _floor, _arrow;
            static readonly Color Off = new Color(0.78f, 0.80f, 0.82f), On = new Color(1f, 0.78f, 0.35f);
            static readonly Color ArrowOff = new Color(0.25f, 0.27f, 0.30f), ArrowOn = new Color(0.45f, 0.22f, 0.02f);

            public CallButton(Transform island, bool top)
            {
                var b = top ? Game.Core.WaitingIsland.CallTop : Game.Core.WaitingIsland.CallBottom;
                var root = new GameObject(top ? "LiftCallTop" : "LiftCallBottom").transform;
                root.SetParent(island, false);
                root.localPosition = new Vector3(b.x, b.y, b.z);
                root.localRotation = Quaternion.Euler(0f, top ? 180f : 0f, 0f);   // the face (+Z local) towards whoever presses it
                Material steel = Look("WaitingSteel"), stainless = Look("WaitingStainless");
                if (!top) Part(root, PrimitiveType.Cube, "Pillar", new Vector3(0f, -b.y * 0.5f + 0.3f, -0.05f), new Vector3(0.28f, b.y + 0.6f, 0.08f), steel);
                Part(root, PrimitiveType.Cube, "Plate", Vector3.zero, new Vector3(0.22f, 0.40f, 0.02f), stainless);
                Part(root, PrimitiveType.Cube, "Window", new Vector3(0f, 0.12f, 0.012f), new Vector3(0.15f, 0.08f, 0.006f), null).sharedMaterial.color = new Color(0.03f, 0.03f, 0.035f);
                Part(root, PrimitiveType.Cylinder, "Bezel", new Vector3(0f, -0.05f, 0.012f), new Vector3(0.13f, 0.004f, 0.13f), null).sharedMaterial.color = new Color(0.08f, 0.08f, 0.09f);
                _button = Part(root, PrimitiveType.Cylinder, "Button", new Vector3(0f, -0.05f, 0.02f), new Vector3(0.105f, 0.008f, 0.105f), null);
                _button.sharedMaterial.color = Off; _button.sharedMaterial.SetFloat("_Smoothness", 0.7f);
                var g = new GameObject("Glow"); g.transform.SetParent(root, false); g.transform.localPosition = new Vector3(0f, -0.05f, 0.2f);
                _glow = g.AddComponent<Light>(); _glow.type = LightType.Point; _glow.range = 1.2f; _glow.intensity = 1.5f; _glow.color = On; _glow.shadows = LightShadows.None; _glow.enabled = false;
                _floor = Label(root, new Vector3(0f, 0.12f, 0.016f), 0.016f, new Color(1f, 0.6f, 0.2f));
                _arrow = Label(root, new Vector3(0f, -0.05f, 0.029f), 0.014f, ArrowOff);
                _arrow.text = top ? "▼" : "▲";
            }

            static Renderer Part(Transform parent, PrimitiveType shape, string name, Vector3 at, Vector3 size, Material m)
            {
                var go = GameObject.CreatePrimitive(shape);
                go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());   // looks only: the server judges the reach
                go.transform.SetParent(parent, false); go.transform.localPosition = at; go.transform.localScale = size;
                if (shape == PrimitiveType.Cylinder) go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // its axis out of the plate
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = m != null ? m : new Material(r.sharedMaterial);   // a shared look, or its own copy to colour
                return r;
            }

            static TextMesh Label(Transform parent, Vector3 at, float size, Color c)
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var t = new GameObject("Label"); t.transform.SetParent(parent, false);
                t.transform.localPosition = at; t.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var text = t.AddComponent<TextMesh>();
                text.font = font; text.fontSize = 64; text.characterSize = size; text.anchor = TextAnchor.MiddleCenter; text.color = c;
                t.GetComponent<MeshRenderer>().sharedMaterial = BoardMaterial(font);
                return text;
            }

            bool? _lit;
            public void Show(bool lit, string floor)
            {
                if (_floor.text != floor) _floor.text = floor;
                if (_lit == lit) return;
                _lit = lit;
                var m = _button.sharedMaterial;
                m.color = lit ? On : Off;
                if (lit) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");   // URP Lit: the button glows, not just tints
                m.SetColor("_EmissionColor", lit ? On * 1.6f : Color.black);
                _arrow.color = lit ? ArrowOn : ArrowOff;
                _glow.enabled = lit;
            }
        }

        static AudioClip _chime;
        static void Ding(bool top)
        {
            if (_chime == null) _chime = Resources.Load<AudioClip>("Sfx/elev_arrive_chime");   // the elevators' arrival chime (ADR-387 reuses it too)
            if (_chime == null) return;
            var island = GameObject.Find("WaitingIsland");
            if (island == null) return;
            var b = top ? Game.Core.WaitingIsland.CallTop : Game.Core.WaitingIsland.CallBottom;
            AudioSource.PlayClipAtPoint(_chime, island.transform.TransformPoint(new Vector3(b.x, b.y, b.z)), 0.7f);   // shortcut: Net cannot read GameSettings' effects volume (Gameplay); move to a Gameplay view if it matters
        }

        // Client: the call button F would press from where `me` stands looking along `look` (the pickups' reach and view cone), on the island only.
        public static bool CallInReach(Combatant me, Vector3 look, out bool top)
        {
            top = false;
            if (!OnIsland || me == null || me.Dead.Value) return false;
            var island = GameObject.Find("WaitingIsland");
            if (island == null) return false;
            var feet = me.transform.position; var eye = me.EyePosition; var rules = Game.Core.InteractRules.Current;
            float best = float.MaxValue;
            for (int i = 0; i < 2; i++)
            {
                var b = i == 1 ? Game.Core.WaitingIsland.CallTop : Game.Core.WaitingIsland.CallBottom;
                var at = island.transform.TransformPoint(new Vector3(b.x, b.y, b.z));
                if (!Game.Core.Interact.Candidate(feet.x, feet.y, feet.z, eye.x, eye.z, look.x, look.z, at.x, at.y, at.z, rules, out float bearing)) continue;
                if (bearing < best) { best = bearing; top = i == 1; }
            }
            return best < float.MaxValue;
        }

        // Client: F at a call button. The host calls straight into its own server; a client asks it (the server checks the reach).
        public static bool Call(Combatant me, bool top)
        {
            if (me == null) return false;
            if (me.IsServerInitialized) { var room = WaitingRoom.Current; return room != null && room.ServerCall(me, top); }
            if (me.NetworkManager == null) return false;
            me.NetworkManager.ClientManager.Broadcast(new LiftCallRequest { Top = top });
            return true;
        }

        // The island is part of the World scene; its board spot is WaitingIsland.Board (Core). Built once the island object exists.
        bool TryBuildBoard()
        {
            var island = GameObject.Find("WaitingIsland");
            if (island == null) return false;
            // Korean pages (ADR-502): the game's Korean UI font, the built-in one only when it is missing.
            var font = Resources.Load<Font>("Fonts/NotoSansKR");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new GameObject("WaitingBoardText").transform;
            root.SetParent(island.transform, false);
            var b = Game.Core.WaitingIsland.Board;
            root.localPosition = new Vector3(b.x, 7f, b.z + 0.1f);
            root.localRotation = Quaternion.Euler(0f, 180f, 0f);          // the text faces the middle of the island (+Z)
            // Title, the big clock, three rows and a footer (BoardText's lines), top to bottom on the 9 m panel.
            _lines[BoardText.Title] = Line(root, font, "Title", 0f, 3.6f, 0.8f, new Color(0.85f, 0.9f, 1f));
            _lines[BoardText.Big] = Line(root, font, "Time", 0f, 0.9f, 1.7f, new Color(1f, 0.82f, 0.25f));
            _lines[BoardText.Row0] = Line(root, font, "Row0", 0f, 2.0f, 0.6f, new Color(1f, 0.95f, 0.8f));
            _lines[BoardText.Row0 + 1] = Line(root, font, "Row1", 0f, 0.25f, 0.6f, new Color(1f, 0.95f, 0.8f));
            _lines[BoardText.Row0 + 2] = Line(root, font, "Row2", 0f, -1.5f, 0.6f, new Color(0.8f, 1f, 0.85f));
            _lines[BoardText.Footer] = Line(root, font, "Footer", 0f, -3.3f, 0.5f, new Color(1f, 0.6f, 0.4f));
            return true;
        }

        static TextMesh Line(Transform parent, Font font, string name, float x, float y, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, y, 0f);
            var text = go.AddComponent<TextMesh>();
            text.font = font; text.fontSize = 64; text.characterSize = size * 0.35f; text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
            text.color = color; text.richText = false;
            go.GetComponent<MeshRenderer>().sharedMaterial = BoardMaterial(font);
            return text;
        }

        // The font's own material (GUI/Text Shader) draws with ZTest Always, so the board text showed through the dome, the tower and the
        // cage; a material property does not change that, so the text gets a small depth-tested shader of its own (Resources/Shaders).
        // One per font: the board (Noto Sans KR) and the lift panels (built-in font) must each sample their own glyph atlas, and a
        // dynamic font rebuilds its atlas when new letters come (a Korean page), so the material follows the rebuilt texture.
        static readonly Dictionary<Font, Material> _boardMaterials = new Dictionary<Font, Material>();
        static Material BoardMaterial(Font font)
        {
            if (!_boardMaterials.TryGetValue(font, out var m) || m == null)
            {
                var shader = Resources.Load<Shader>("Shaders/WaitingBoardText");
                m = shader != null ? new Material(shader) { mainTexture = font.material.mainTexture } : new Material(font.material);
                m.name = "WaitingBoardText_" + font.name;
                _boardMaterials[font] = m;
                if (_boardMaterials.Count == 1) Font.textureRebuilt += OnFontRebuilt;
            }
            return m;
        }

        static void OnFontRebuilt(Font font) { if (_boardMaterials.TryGetValue(font, out var m) && m != null) m.mainTexture = font.material.mainTexture; }

        // The same time on the screen, for a person who is not looking at the board.
        void OnGUI()
        {
            if (!Live) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            int s = Mathf.CeilToInt(_last.SecondsLeft);
            GUI.Label(new Rect(0, 20, Screen.width, 70), $"Departure in {s / 60}:{s % 60:00}    {_last.Total} / {_last.Target}", _style);
            // At the wheel in the race hall: the lap in progress, the last and the best (ADR-509).
            if (Time.unscaledTime - _lapAt <= 1.5f)
            {
                float run = _lap.Running >= 0f ? _lap.Running + (Time.unscaledTime - _lapAt) : -1f;
                GUI.Label(new Rect(0, 62, Screen.width, 40), $"LAP {(run >= 0f ? RaceBoard.Time(run) : "--")}   LAST {(_lap.Last > 0f ? RaceBoard.Time(_lap.Last) : "--")}   BEST {(_lap.Best > 0f ? RaceBoard.Time(_lap.Best) : "--")}", _style);
            }
        }
    }
}
