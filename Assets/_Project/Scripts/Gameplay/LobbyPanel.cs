using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The main entry screen, built from the adopted mockup "② 승강기 조작반 + B 클린 모던" (docs/research/ui_mockups,
    // lobby2 in ui.js) in the mockup's own 1920x1080 stage coordinates: floor-indicator top bar, the elevator scene with the
    // party on the platform tiles, the two info plates, and the control console (mode switch, LED, view switch, map window,
    // elevator call, one state button). Numbers come from the measured mockup (docs/research/ui_mockups/LOBBY_MEASURES.md).
    // The state is PartyLobby (ADR-245); this only draws it.
    public class LobbyPanel
    {
        public const float StageW = 1920f, StageH = 1080f;
        static readonly float[] SlotX = { 640f, 820f, 1000f, 1180f };
        static readonly Color32[] PersonColors = { new Color32(111, 143, 122, 255), new Color32(169, 130, 80, 255), new Color32(95, 118, 134, 255), new Color32(120, 120, 140, 255) };
        const float TileY = 575f;

        public PartyLobby Party { get; private set; }
        public int Self { get; private set; } = 0;
        public RectTransform Root { get; private set; }
        public RectTransform Stage { get; private set; }
        public string LedText => _led != null ? _led.text : "";
        public string StatusLabel => _statusLabel != null ? _statusLabel.text : "";
        public string ReasonText => _reason != null ? _reason.text : "";
        public bool Matching => Party.Matching;

        MainMenu _menu;
        float _matchSeconds;
        Text _led, _statusLabel, _reason, _seven, _callLabel, _fillText, _lockText, _kbd, _ledCaption;
        Image _statusFill, _statusRing, _kbdBox, _lamp1, _lamp2;
        Image[] _lvrFill = new Image[2], _lvrRing = new Image[2];
        Text[] _lvrMs = new Text[2], _lvrName = new Text[2];
        Button _status, _solo, _squad, _tpp, _fpp;
        Image _tppFill, _fppFill, _tppBar, _fppBar;
        RectTransform _knob;
        Image[] _cells = new Image[50], _cellRing = new Image[50];
        readonly Slot[] _slots = new Slot[PartyLobby.SquadSize];

        class Slot
        {
            public GameObject Tile, Figure, Dashed, Name, Invite;
            public Image TileFill, TileStroke;
            public Sprite TileDash, TileSolid;
            public Text TileLabel, NameText;
            public Image[] Body;
        }

        public static LobbyPanel Build(MainMenu menu, Transform parent, System.Action training, System.Action custom, System.Action settings, System.Action credits, System.Action quit)
        {
            var p = new LobbyPanel { _menu = menu };
            p.Party = new PartyLobby(0, "플레이어");
            p.Party.SetView(0, GameSettings.DefaultViewFirstPerson ? Perspective.First : Perspective.Third);
            p.Root = UiKit.Fill("Lobby", parent);
            UiKit.Box(p.Root, UiPaint.Bg);
            p.Stage = UiKit.Rect("Stage", p.Root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(StageW, StageH));
            p.Root.gameObject.AddComponent<StageFit>().Stage = p.Stage;
            p.BuildScene();
            p.BuildTop(training, custom, settings, credits, quit);
            p.BuildPlates();
            p.BuildConsole();
            p.Refresh();
            return p;
        }

        // Replaces the party (capture and later the real party feed) and redraws.
        public void SetParty(PartyLobby party, int self)
        {
            Party = party; Self = self; _matchSeconds = party.Matching ? 24f : 0f;
            Refresh();
        }

        // ---------------------------------------------------------------- top bar
        void BuildTop(System.Action training, System.Action custom, System.Action settings, System.Action credits, System.Action quit)
        {
            var bar = UiPaint.At(Stage, "TopBar", 0, 0, StageW, 96);
            UiPaint.GradientOpaque(bar, UiPaint.Pn, new Color(UiPaint.Pn.r, UiPaint.Pn.g, UiPaint.Pn.b, 0f));
            string[] names = { "플레이", "외형", "훈련장", "설정", "크레딧" };
            System.Action[] acts = { null, null, training, settings, credits };
            for (int i = 0; i < 5; i++)
            {
                float x = 48f + i * 100f;
                bool on = i == 0;
                var b = UiPaint.Btn(Stage, "Floor" + i, x, 19, 92, 57, "", 20, 14, on ? UiPaint.AcS : Color.clear, on ? UiPaint.Ac : Color.clear, UiPaint.Tx,
                    acts[i] == null ? null : new UnityEngine.Events.UnityAction(acts[i]));
                foreach (var t in b.GetComponentsInChildren<Text>()) Object.Destroy(t.gameObject);
                if (on) UiPaint.Box(UiPaint.At(b.transform, "Bar", 8, 54, 76, 3), UiPaint.Ac, 1);
                UiPaint.Label(b.transform, "No", 0, 7, 92, 17, (on ? "▲ " : "") + (i + 1).ToString("00"), 15, on ? UiPaint.Ac : UiPaint.T2, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiPaint.Label(b.transform, "Name", 0, 24, 92, 26, names[i], 20, on ? UiPaint.Tx : UiPaint.T2, TextAnchor.MiddleCenter, FontStyle.Bold);
            }
            UiPaint.Label(Stage, "Chevron", 548, 32, 9, 32, "›", 22, UiPaint.T2, TextAnchor.MiddleCenter);
            // 일반 매치 | 커스텀 모드 segmented control.
            UiPaint.Box(UiPaint.At(Stage, "Seg", 565, 23, 252, 50), Color.clear, 25, UiPaint.Ln);
            var normal = UiPaint.Btn(Stage, "Normal", 566, 24, 128, 48, "✓ 일반 매치", 18, 24, UiPaint.AcS, Color.clear, UiPaint.Tx, null);
            UiPaint.Box(UiPaint.At(normal.transform, "Bar", 16, 44, 96, 4), UiPaint.Ac, 1);
            UiPaint.Btn(Stage, "Custom", 694, 24, 122, 48, "커스텀 모드", 18, 24, Color.clear, Color.clear, UiPaint.Tx, new UnityEngine.Events.UnityAction(custom));
            // Right: ping chip, three round icon buttons, logo placeholder with the game name.
            UiPaint.Box(UiPaint.At(Stage, "Ping", 1476, 34, 85, 26), UiPaint.Pn, 13, UiPaint.Ln);
            UiPaint.Label(Stage, "PingText", 1476, 34, 85, 26, "핑 18 ms", 14, UiPaint.Tx, TextAnchor.MiddleCenter);
            string[] icons = { "ico_ib_bell", "ico_ib_gear", "ico_ib_power" };
            System.Action[] iacts = { null, settings, quit };
            for (int i = 0; i < 3; i++)
            {
                var ib = UiPaint.Btn(Stage, "Ib" + i, 1569 + i * 52, 26, 44, 44, "", 12, 22, UiPaint.Pn, UiPaint.Ln, UiPaint.Tx, iacts[i] == null ? null : new UnityEngine.Events.UnityAction(iacts[i]));
                foreach (var t in ib.GetComponentsInChildren<Text>()) Object.Destroy(t.gameObject);
                UiPaint.Pic(ib.transform, "Icon", icons[i], 11, 11, 22, 22);
            }
            UiPaint.Pic(Stage, "Logo", "ico_logo", 1741, 29, 38, 38);
            UiPaint.Label(Stage, "Game", 1791, 38, 100, 20, "SUBLEVEL", 18, UiPaint.Tx, TextAnchor.MiddleLeft, FontStyle.Bold);
        }

        // ---------------------------------------------------------------- scene: wall, door, platform, party
        void BuildScene()
        {
            UiPaint.Pic(Stage, "Scene", "lobby_scene", 0, 0, StageW, StageH);
            for (int i = 0; i < PartyLobby.SquadSize; i++) _slots[i] = BuildSlot(SlotX[i], i);
        }

        Slot BuildSlot(float x, int i)
        {
            var s = new Slot();
            // Platform tile: trapezoid -70,40 .. 70,40 .. 86,96 .. -86,96 (centre x, base y 575); drawn as a rounded box stand-in
            // with the mockup's width at the front edge, 3 px stroke.
            var tile = UiPaint.At(Stage, "Tile" + i, x - 86, TileY + 40, 172, 56);
            s.Tile = tile.gameObject;
            s.TileFill = UiPaint.Shape(tile, UiPaint.Trapezoid(false), Color.clear);
            s.TileStroke = UiPaint.Shape(UiKit.Fill("Edge", tile), UiPaint.Trapezoid(true), UiPaint.Ac);
            s.TileDash = UiPaint.Trapezoid(true, true); s.TileSolid = s.TileStroke.sprite;
            s.TileLabel = UiPaint.Label(tile, "State", 0, 14, 172, 30, "", 24, UiPaint.Tx, TextAnchor.MiddleCenter, FontStyle.Bold);
            // Person figure (scale 1.25 for the local player, 1.05 for others; feet at y 605).
            var fig = UiKit.Rect("Figure" + i, Stage, new Vector2(0f, 1f), new Vector2(0.5f, 0f), new Vector2(x, -(TileY + 30f)), new Vector2(1f, 1f));
            s.Figure = fig.gameObject;
            s.Body = new Image[4];
            s.Body[0] = Part(fig, "Legs", -13, 0, 26, 72, 4);
            s.Body[1] = Part(fig, "Torso", -19, 68, 38, 74, 8);
            s.Body[2] = Part(fig, "Head", -16, 144, 32, 32, 16);
            var stripe = Part(fig, "Stripe", -19, 111, 38, 7, 1);
            stripe.color = UiPaint.Ac;
            s.Body[3] = stripe;
            var dashed = UiKit.Rect("Dashed" + i, Stage, new Vector2(0f, 1f), new Vector2(0.5f, 0f), new Vector2(x, -(TileY + 30f)), new Vector2(1f, 1f));
            s.Dashed = dashed.gameObject;
            Part(dashed, "Legs", -13, 0, 26, 72, 4, true); Part(dashed, "Torso", -19, 68, 38, 74, 8, true); Part(dashed, "Head", -16, 144, 32, 32, 16, true);
            s.Name = UiPaint.Label(Stage, "Name" + i, x - 100, TileY + 102, 200, 28, "", 22, UiPaint.Tx, TextAnchor.MiddleCenter, FontStyle.Bold).gameObject;
            s.NameText = s.Name.GetComponent<Text>();
            var inv = UiPaint.Btn(Stage, "Invite" + i, x - 60, TileY + 104, 120, 44, "+ 초대", 18, 22, UiPaint.Pn, UiPaint.Ln, UiPaint.Tx, null);
            s.Invite = inv.gameObject;
            return s;
        }

        // A body part of the figure: x from the centre, `bottom` = height of its bottom edge above the feet, y up.
        static Image Part(RectTransform fig, string name, float x, float bottom, float w, float h, int radius, bool dashedLook = false)
        {
            var r = UiKit.Rect(name, fig, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x, bottom), new Vector2(w, h));
            if (dashedLook) { UiPaint.Box(r, Color.clear, radius, UiPaint.T2, 2, true); return null; }
            return UiPaint.Box(r, UiPaint.T2, radius, UiPaint.Ln, 2);
        }

        // ---------------------------------------------------------------- plates
        void BuildPlates()
        {
            var a = UiPaint.At(Stage, "PlateRad", 48, 150, 340, 127);
            UiPaint.Box(a, UiPaint.Pn, 16, UiPaint.Ln);
            UiPaint.Pic(Stage, "Rad", "ico_rad", 63, 167, 24, 24);
            UiPaint.Label(Stage, "RadTitle", 95, 163, 200, 32, "오염 브리핑", 22, UiPaint.Tx, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiPaint.Label(Stage, "RadLine1", 63, 203, 330, 27, "<color=#6ccfa0><b>✓ 전 지역 안전</b></color> · 첫 경고 <b>05:00</b> 후", 18, UiPaint.Tx);
            UiPaint.Label(Stage, "RadLine2", 63, 238, 330, 26, "6개 지역 · 승강장 20곳 · 청람도", 18, UiPaint.T2);
            var b = UiPaint.At(Stage, "PlateKey", 1532, 150, 340, 118);
            UiPaint.Box(b, UiPaint.Pn, 16, UiPaint.Ln);
            UiPaint.Pic(Stage, "Key", "ico_key", 1547, 167, 24, 24);
            UiPaint.Label(Stage, "KeyTitle", 1579, 163, 200, 32, "열쇠 안내", 22, UiPaint.Tx, TextAnchor.MiddleLeft, FontStyle.Bold);
            var body = UiPaint.Label(Stage, "KeyBody", 1547, 203, 310, 52, "닫히는 지역에서 벗어나려면 <b>열쇠 승강기</b>를 찾으세요. 팀이 함께 타야 작동합니다.", 16, UiPaint.Tx, TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        // ---------------------------------------------------------------- console
        void BuildConsole()
        {
            var con = UiPaint.At(Stage, "Console", 0, 720, StageW, 360);
            UiPaint.GradientOpaque(con, UiPaint.Sf, UiPaint.Bg);
            UiKit.Box(UiPaint.At(Stage, "ConsoleEdge", 0, 720, StageW, 4), UiPaint.Ln);

            // Left: mode switch.
            UiPaint.Label(Stage, "ModeCap", 48, 742, 420, 22, "모드 스위치", 18, UiPaint.T2, TextAnchor.MiddleLeft, FontStyle.Bold);
            BuildLever(0, 96f, "솔로", () => { Party.SetMode(Self, TeamMode.Solo); Refresh(); });
            BuildLever(1, 300f, "스쿼드", () => { Party.SetMode(Self, TeamMode.Squad); Refresh(); });
            var track = UiPaint.At(Stage, "LeverTrack", 232 + 20.8f, 851 + 8.7f, 10.4f, 112.7f);
            UiPaint.Box(track, UiPaint.Sf, 5, UiPaint.Ln, 2);
            _knob = UiPaint.At(Stage, "LeverKnob", 232 + 12.1f, 851 + 71.1f, 27.7f, 39.9f);
            UiPaint.Box(_knob, UiPaint.Ac, 7, UiPaint.Tx, 2);
            UiKit.Box(UiPaint.At(_knob, "Slot", 3.5f, 15.6f, 20.8f, 3.5f), UiPaint.Bg);

            // Centre: LED, view switch, map window, reason line.
            _ledCaption = UiPaint.Label(Stage, "LedCap", 858, 742, 105, 22, "현재 투입 설정", 18, UiPaint.T2, TextAnchor.MiddleCenter, FontStyle.Bold);
            var led = UiPaint.At(Stage, "Led", 714, 772, 392, 88);
            UiPaint.Box(led, UiPaint.Bg, 20, UiPaint.Ac, 3);
            _led = UiPaint.Label(led, "Text", 0, 0, 392, 88, "", 64, UiPaint.Ac, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiPaint.Label(Stage, "ViewCap", 609, 878, 261, 17, "시점 스위치", 14, UiPaint.T2, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiPaint.Box(UiPaint.At(Stage, "ViewSeg", 609, 903, 261, 50), Color.clear, 25, UiPaint.Ln);
            _tpp = UiPaint.Btn(Stage, "Tpp", 610, 904, 142, 48, "TPP 3인칭", 18, 24, Color.clear, Color.clear, UiPaint.Tx, () => { Party.SetView(Self, Perspective.Third); Refresh(); });
            _fpp = UiPaint.Btn(Stage, "Fpp", 752, 904, 117, 48, "FPP 1인칭", 18, 24, Color.clear, Color.clear, UiPaint.Tx, () => { Party.SetView(Self, Perspective.First); Refresh(); });
            _tppFill = _tpp.targetGraphic as Image; _fppFill = _fpp.targetGraphic as Image;
            _tppBar = UiPaint.Box(UiPaint.At(_tpp.transform, "Bar", 14, 44, 114, 4), UiPaint.Ac, 1);
            _fppBar = UiPaint.Box(UiPaint.At(_fpp.transform, "Bar", 14, 44, 89, 4), UiPaint.Ac, 1);
            var map = UiPaint.At(Stage, "MapWin", 885, 868, 326, 94);
            UiPaint.Box(map, UiPaint.Bg, 12, UiPaint.Ln);
            UiPaint.Pic(Stage, "MapThumb", "ico_mapthumb", 897, 884, 118, 64);
            UiPaint.Label(Stage, "MapName", 1030, 877, 150, 28, "청람도", 22, UiPaint.Tx, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiPaint.Label(Stage, "MapSub", 1030, 905, 180, 23, "6개 지역 · 약 20~25분", 18, UiPaint.T2, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiPaint.Pic(Stage, "Lock", "ico_lock", 1030, 931, 20, 20);
            UiPaint.Label(Stage, "MapNext", 1054, 928, 150, 25, "다음 맵 준비 중", 18, UiPaint.T2, TextAnchor.MiddleLeft, FontStyle.Bold);
            _reason = UiPaint.Label(Stage, "Reason", 690, 970, 440, 26, "", 18, UiPaint.T2, TextAnchor.MiddleCenter);
            _reason.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Right: elevator call, matching timer, fill text and 50 cells, the state button.
            _callLabel = UiPaint.Label(Stage, "CallCap", 1352, 754, 200, 22, "승강기 호출", 18, UiPaint.T2, TextAnchor.MiddleLeft, FontStyle.Bold);
            var seven = UiPaint.At(Stage, "Seven", 1730, 742, 142, 46);
            UiPaint.Box(seven, UiPaint.Bg, 12, UiPaint.Ln, 2);
            _seven = UiPaint.Label(seven, "Text", 0, 0, 142, 46, "--:--", 38, UiPaint.Ac, TextAnchor.MiddleCenter, FontStyle.Bold);
            _fillText = UiPaint.Label(Stage, "FillText", 1352, 796, 420, 27, "", 18, UiPaint.Tx);
            UiPaint.Label(Stage, "Eta", 1793, 796, 79, 27, "예상 <b>0:45</b>", 18, UiPaint.T2, TextAnchor.MiddleRight);
            float cw = 9f, gap = (520f - 50f * cw) / 49f;
            for (int i = 0; i < 50; i++)
            {
                var c = UiPaint.At(Stage, "Cell" + i, 1352 + i * (cw + gap), 831, cw, 12);
                _cells[i] = UiPaint.Box(c, Color.clear, 2, UiPaint.Ln, 1);
                _cellRing[i] = c.Find("Ring").GetComponent<Image>();
            }
            _status = UiPaint.Btn(Stage, "State", 1352, 968, 520, 86, "", 30, 43, UiPaint.AcS, UiPaint.Ac, UiPaint.Tx, OnStatus, FontStyle.Bold, 2);
            _statusFill = _status.targetGraphic as Image;
            _statusRing = _status.transform.Find("Ring").GetComponent<Image>();
            _statusLabel = _status.GetComponentInChildren<Text>();
            _statusLabel.fontStyle = FontStyle.Bold;
            ((RectTransform)_statusLabel.transform).offsetMax = new Vector2(-250f, 0f); // text left of the key chip, as in the mockup
            var kbd = UiPaint.At(_status.transform, "Kbd", 285, 25, 73, 36);
            _kbdBox = UiPaint.Box(kbd, UiPaint.Sf, 8, UiPaint.Ln);
            _kbd = UiPaint.Label(kbd, "Key", 0, 0, 73, 36, "Enter", 17, UiPaint.Tx, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        void BuildLever(int i, float x, string name, UnityEngine.Events.UnityAction act)
        {
            var b = UiPaint.Btn(Stage, "Lvr" + i, x, 871, 120, 91, "", 24, 12, Color.clear, UiPaint.Ln, UiPaint.Tx, act);
            foreach (var t in b.GetComponentsInChildren<Text>()) Object.Destroy(t.gameObject);
            _lvrFill[i] = b.targetGraphic as Image;
            _lvrRing[i] = b.transform.Find("Ring").GetComponent<Image>();
            var lamp = UiPaint.Circle(b.transform, "Lamp", 60, 19, 16, UiPaint.Ok);
            lamp.sprite = UiPaint.Round(8);
            if (i == 0) _lamp1 = lamp; else _lamp2 = lamp;
            _lvrName[i] = UiPaint.Label(b.transform, "Name", 0, 29, 120, 30, name, 24, UiPaint.T2, TextAnchor.MiddleCenter, FontStyle.Bold);
            _lvrMs[i] = UiPaint.Label(b.transform, "Ms", 0, 61, 120, 19, "", 15, UiPaint.T2, TextAnchor.MiddleCenter);
        }

        // ---------------------------------------------------------------- behaviour
        // The state button's action. Squad matches wait for the team flow, so only solo starts today.
        void OnStatus()
        {
            var b = Party.ButtonFor(Self);
            if (!b.Enabled) return;
            switch (b.Action)
            {
                case LobbyAction.Start:
                    if (Party.Mode != TeamMode.Solo) return;
                    _menu.SetMode(TeamMode.Solo);
                    GameSettings.DefaultViewFirstPerson = Party.View == Perspective.First;
                    _menu.StartGame();
                    break;
                case LobbyAction.Ready: Party.SetReady(Self, true); break;
                case LobbyAction.Unready: Party.SetReady(Self, false); break;
                case LobbyAction.CancelMatch: Party.CancelMatch(Self); break;
                case LobbyAction.LeaveParty: Party.Leave(Self); break;
            }
            Refresh();
        }

        public void Tick(float dt)
        {
            if (Party.Matching) { _matchSeconds += dt; _seven.text = PartyLobby.Clock((int)_matchSeconds); }
            var kb = Keyboard.current;
            if (kb != null && Root.gameObject.activeInHierarchy && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) OnStatus();
        }

        public void Refresh()
        {
            bool solo = Party.Mode == TeamMode.Solo;
            bool member = !Party.IsLeader(Self);
            bool search = Party.Matching;
            int n = Party.Count;
            // Mode switch.
            _knob.anchoredPosition = new Vector2(_knob.anchoredPosition.x, -(851f + (solo ? 12.1f : 71.1f)));
            for (int i = 0; i < 2; i++)
            {
                bool on = (i == 0) == solo;
                _lvrFill[i].color = on ? UiPaint.AcS : Color.clear;
                _lvrRing[i].color = on ? UiPaint.Ac : UiPaint.Ln;
                _lvrName[i].color = on ? UiPaint.Tx : UiPaint.T2;
                _lvrMs[i].text = member ? "잠김" : on ? "✓ 선택됨" : i == 0 ? "1명" : "4인";
                _lvrMs[i].color = on ? UiPaint.Tx : UiPaint.T2;
            }
            _lamp1.color = solo ? UiPaint.Ac : UiPaint.Ok;
            _lamp2.color = solo ? UiPaint.Ok : UiPaint.Ac;
            _led.text = solo ? "솔로 · 1인" : "스쿼드 · 4인";
            bool tpp = Party.View == Perspective.Third;
            _tpp.GetComponentInChildren<Text>().text = (tpp ? "✓ " : "") + "TPP 3인칭";
            _fpp.GetComponentInChildren<Text>().text = (tpp ? "" : "✓ ") + "FPP 1인칭";
            _tppFill.color = tpp ? UiPaint.AcS : Color.clear; _tppBar.gameObject.SetActive(tpp);
            _fppFill.color = tpp ? Color.clear : UiPaint.AcS; _fppBar.gameObject.SetActive(!tpp);

            // Party on the platform.
            for (int i = 0; i < PartyLobby.SquadSize; i++)
            {
                var s = _slots[i];
                bool hasMember = i < n;
                bool empty = !solo && i == n && n < PartyLobby.SquadSize;
                bool shown = hasMember || empty;
                s.Tile.SetActive(shown); s.Figure.SetActive(hasMember); s.Dashed.SetActive(empty);
                s.Name.SetActive(hasMember); s.Invite.SetActive(empty);
                if (empty) { SetTile(s, Color.clear, UiPaint.T2, ""); s.TileStroke.sprite = s.TileDash; }
                else s.TileStroke.sprite = s.TileSolid;
                if (!hasMember) continue;
                var m = Party.Members[i];
                bool lead = Party.IsLeader(m.Id), ready = m.Ready;
                Color tint = lead ? UiPaint.Ac : ready ? UiPaint.Ok : UiPaint.Wr;
                SetTile(s, lead || ready ? UiPaint.AcS : UiPaint.Bg, tint, lead ? "★ 파티장" : ready ? "✓ 준비 완료" : "… 준비 중");
                s.NameText.text = m.Name + (m.Id == Self ? " (나)" : "");
                var color = (Color)PersonColors[i % PersonColors.Length];
                var fig = (RectTransform)s.Figure.transform;
                fig.localScale = Vector3.one * (m.Id == Self ? 1.25f : 1.05f);
                for (int k = 0; k < 3; k++) s.Body[k].color = color;
            }

            // Reason line, call panel, state button.
            string waiting = Party.WaitingLine();
            if (search) _reason.text = member ? "파티장이 매칭을 시작했습니다. 취소는 파티장만 할 수 있습니다." : "매칭 중입니다. 사람이 더 들어오면 봇이 줄어듭니다.";
            else if (solo) _reason.text = "바로 시작할 수 있습니다. 부족한 인원은 봇이 채웁니다.";
            else if (member) _reason.text = Party.Members[IndexOf(Self)].Ready ? "준비 완료. 파티장이 시작하면 매칭이 시작됩니다." : "준비를 누르면 파티장이 시작할 수 있습니다.";
            else if (waiting.Length > 0) _reason.text = "⚠ " + string.Join(", ", Party.NotReady()) + " 님이 준비 중입니다. 준비가 끝나면 시작할 수 있습니다.";
            else _reason.text = "전원 준비 완료. 시작할 수 있습니다.";
            _reason.color = !member && !solo && !search && waiting.Length > 0 ? UiPaint.Wr : UiPaint.T2;
            _callLabel.text = member ? "준비 호출" : "승강기 호출";
            _seven.text = search ? PartyLobby.Clock((int)_matchSeconds) : "--:--";
            int humans = Mathf.Min(50, 24 + (int)(_matchSeconds / 4f));
            _fillText.text = search ? "<b>사람 " + humans + " / 봇 " + (50 - humans) + " 채움</b>" : "시작하면 여기에 <b>사람 N / 봇 M 채움</b>이 표시됩니다";
            for (int i = 0; i < 50; i++)
            {
                bool h = search && i < humans;
                _cells[i].color = h ? UiPaint.AcS : Color.clear;
                _cellRing[i].color = h ? UiPaint.Ac : search ? UiPaint.Tx : UiPaint.Ln;
            }
            var btn = Party.ButtonFor(Self, (int)_matchSeconds);
            bool primary = btn.Action == LobbyAction.Start || btn.Action == LobbyAction.Ready;
            bool enabled = btn.Enabled;
            _statusLabel.text = btn.Label;
            _statusFill.color = primary && enabled ? UiPaint.AcS : UiPaint.Pn;
            _statusRing.color = primary && enabled ? UiPaint.Ac : UiPaint.Ln;
            _statusLabel.color = enabled ? UiPaint.Tx : UiPaint.T2;
            _kbd.text = btn.Action == LobbyAction.CancelMatch ? "Esc" : "Enter";
            _kbd.gameObject.transform.parent.gameObject.SetActive(btn.Action != LobbyAction.LeaveParty);
            _status.interactable = enabled;
            var kr = (RectTransform)_kbd.transform.parent;
            kr.anchoredPosition = new Vector2(btn.Action == LobbyAction.CancelMatch ? 300f : 285f, kr.anchoredPosition.y);
        }

        int IndexOf(int id)
        {
            for (int i = 0; i < Party.Count; i++) if (Party.Members[i].Id == id) return i;
            return 0;
        }

        static void SetTile(Slot s, Color fill, Color stroke, string label)
        {
            s.TileFill.color = fill;
            s.TileStroke.color = stroke;
            s.TileLabel.text = label;
            s.TileLabel.color = stroke;
        }
    }

    // Scales the 1920x1080 stage to fit the canvas: never above 1, so a taller canvas (16:10) letterboxes top and bottom
    // and a wider one (21:9) keeps the UI centred inside its safe area.
    public class StageFit : MonoBehaviour
    {
        public RectTransform Stage;

        void OnRectTransformDimensionsChange() => Fit();
        void LateUpdate() => Fit();

        void Fit()
        {
            var parent = (RectTransform)transform;
            float w = parent.rect.width, h = parent.rect.height;
            if (Stage == null || w <= 0f || h <= 0f) return;
            float s = Mathf.Min(1f, Mathf.Min(w / LobbyPanel.StageW, h / LobbyPanel.StageH));
            Stage.localScale = new Vector3(s, s, 1f);
        }
    }
}
