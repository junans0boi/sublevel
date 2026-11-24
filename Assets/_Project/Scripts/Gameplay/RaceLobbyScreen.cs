using System;
using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Gameplay
{
    // 이벤트 → the race room (레이스 방, #511, spec #505, ADR-562), laid out after the approved mockup
    // docs/research/ui_mockups/race_room.html (its 1280x720 screen at 1.5x). Reads the RaceLobby model and calls its commands; the solo
    // path keeps the room in PlayerPrefs (course, laps, settings, closed slots, my car and paint). 시작 (F5) starts the room's course with
    // bots in the open empty slots; after the race the menu opens on the room again (ReturnPending). Code-built like EventEntryScreen was;
    // the whole stage is rebuilt on every change, as the mockup's render() does.
    public partial class RaceLobbyScreen
    {
        public const string PrefsKey = "RaceRoom";
        public static bool ReturnPending;   // a race was started from the room: back in the menu, open the room

        // The garage's cars (mockup CARS): the model id (VehicleHandling row), display name, kind, silhouette.
        public static readonly (string id, string name, string kind, string shape)[] Cars =
        {
            ("GoblinV12", "GoblinV12", "스포츠카", "sport"),
            ("FairheavenLT80", "Fairheaven LT80", "세단", "sedan"),
            ("FairheavenSW84", "Fairheaven SW84", "왜건", "wagon"),
            ("LightbodyLifted85", "Lightbody Lifted85", "픽업", "pickup"),
            ("Shvan92", "Shvan92", "밴", "van"),
        };
        // The garage's 6 paints (mockup PAINTS) and the race tint (VehicleColors index) each one gives the real car (#514).
        public static readonly int[] RaceTint = { 5, 0, 1, 3, 4, 2 };
        static readonly Color[] PaintColors = { H("#c8342b"), H("#e9edf1"), H("#2d6fd6"), H("#f2c230"), H("#2f9e6a"), H("#1c1f24") };
        static readonly Dictionary<string, int> Difficulty = new Dictionary<string, int> { { "snake", 2 }, { "pass_up", 4 }, { "pass_down", 4 }, { "pass_loop", 5 } };

        // The mockup's palette (its :root).
        static readonly Color Pnl = new Color(16 / 255f, 20 / 255f, 26 / 255f, 0.92f), Pn2 = H("#1a212b"), Pn3 = H("#232c38"), Line = H("#2f3a48"),
            Txt = H("#e9edf1"), Dim = H("#9aa5b1"), Mute = H("#66717e"), Soft = H("#cdd6df"), Acc = H("#f29e26"), AccInk = H("#2b1a02"),
            Ready = H("#f2c230"), ReadyInk = H("#3a2a00"), Host = H("#3fb8c9"), HostInk = H("#05343b"), Danger = H("#d2382e");

        MainMenu _menu;
        RectTransform _stage;
        RaceLobby _room;
        RaceParty _party;   // the room on the backend party when it has 2+ members (#519), else local
        string _toast;
        float _toastUntil;

        public RaceLobby Room => _room;
        public bool Online => _party != null && _party.Online;
        public int Me { get; private set; }
        public string Dialog { get; private set; }   // null, "leave", "garage", "course", "invite", "settings"
        public string ToastText => _toast;

        public RectTransform Build(Transform parent, MainMenu menu)
        {
            _menu = menu;
            var root = UiKit.Fill("RaceRoom", parent);
            UiPaint.GradientOpaque(UiKit.Fill("Back", root), H("#1d2633"), H("#141a22"));
            _stage = UiKit.Rect("Stage", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(LobbyPanel.StageW, LobbyPanel.StageH));
            root.gameObject.AddComponent<StageFit>().Stage = _stage;
            Load();
            Render();
            return root;
        }

        // ---- state ----

        void Load()
        {
            var me = new RaceLobbyMember { Id = LobbyNet.AccountId != 0 ? (int)LobbyNet.AccountId : 1, Name = string.IsNullOrEmpty(LobbyNet.AuthNickname) ? "플레이어" : LobbyNet.AuthNickname, Car = Cars[1].id };
            Me = me.Id;
            _room = new RaceLobby(me) { CarName = id => Cars[CarIndex(id)].name };
            _party = new RaceParty(() => _menu != null ? _menu.Social : null, _room, Me);
            RaceLobbyState saved = null;
            try { saved = JsonUtility.FromJson<RaceLobbyState>(PlayerPrefs.GetString(PrefsKey, "")); } catch (ArgumentException) { }
            if (saved != null && RaceEvent.ById(saved.Course) != null && !string.IsNullOrEmpty(saved.Name))
            {
                var mine = saved.Members != null && saved.Members.Count > 0 ? saved.Members[0] : null;
                if (mine != null && Array.FindIndex(Cars, c => c.id == mine.Car) >= 0) { me.Car = mine.Car; me.Paint = Mathf.Clamp(mine.Paint, 0, RaceLobby.Paints - 1); }
                saved.Members = new List<RaceLobbyMember> { me };   // solo: only me, by my current name
                saved.Host = me.Id;
                saved.Closed &= ~1;                                  // my own seat
                _room.Load(saved);
            }
            _room.Lines.Clear();
            _room.Lines.Add($"{me.Name} 님이 방을 만들었습니다.");
        }

        void Save() => PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(_room.Snapshot()));

        void Changed() { if (!Online) Save(); Render(); }   // the party's room lives on the backend, not in my prefs

        static int CarIndex(string id) => Math.Max(0, Array.FindIndex(Cars, c => c.id == id));

        // ---- commands (buttons, keys, tests) ----

        public void Refresh() => Render();

        public void CloseSlot(int slot) { if (_party.CloseSlot(slot)) Changed(); }

        public void OpenSlot(int slot)
        {
            if (_party.OpenSlot(slot)) Changed();
            else Toast("방장만 자리를 열 수 있습니다.");
        }

        public void Kick(int id) { if (_party.Kick(id)) Changed(); }

        // The big button (and F5): the host's 시작, a guest's 준비 / 준비 취소. Solo: build the race start and load the course.
        public void Go()
        {
            if (Dialog != null || _menu.IsLoading) return;
            var go = _room.GoFor(Me);
            if (!go.Start) { _party.ToggleReady(); Render(); return; }
            if (!go.Enabled) { Toast($"{string.Join(", ", go.NotReady)} 님이 아직 준비하지 않았습니다."); return; }
            if (Online) { Toast("여럿이 출발하기는 다음 업데이트에서 열립니다."); return; }   // TODO(#521): the race ticket on the dedicated server
            var me = _room.Member(Me);
            Save();
            ReturnPending = true;
            _menu.StartEvent(RaceEvent.ById(_room.Course), _room.Laps, _room.BotCount, me.Car, RaceTint[Mathf.Clamp(me.Paint, 0, RaceTint.Length - 1)]);
        }

        // Back from the race: everyone unready, the rest kept.
        public void BackToRoom() { _room.BackToRoom(); Render(); }

        public void OpenDialog(string kind)
        {
            if (kind == "invite" && _room.Members.Count >= _room.Capacity) { Toast("빈 자리가 없습니다."); return; }
            if (!DraftFor(kind)) return;
            Dialog = kind;
            Render();
        }

        public void CloseDialog() { Dialog = null; Render(); }

        // Esc: closes a dialog, else asks to leave.
        public void Escape() { if (Dialog != null) CloseDialog(); else OpenDialog("leave"); }

        public void ConfirmLeave()
        {
            Dialog = null;
            _party.Leave();
            Render();
            _menu.Back();
        }

        public void Toast(string text)
        {
            _toast = text;
            _toastUntil = Time.unscaledTime + 1.8f;
            Render();
        }

        // Every frame while the room is shown.
        public void Tick()
        {
            var k = Keyboard.current;
            if (k != null && k.f5Key.wasPressedThisFrame) Go();
            // The login's account id can arrive after the room was built (LoginEnv asks /api/me): rebuild the local room under it.
            if (!Online && LobbyNet.AccountId != 0 && Me != (int)LobbyNet.AccountId) { Load(); Render(); }
            if (_party.Sync()) Render();
            var refused = _party.TakeToast();
            if (refused != null) Toast(refused);
            if (_toast != null && Time.unscaledTime > _toastUntil) { _toast = null; Render(); }
        }

        // ---- drawing ----

        void Render()
        {
            if (_stage == null) return;
            foreach (Transform c in _stage) Object.Destroy(c.gameObject);
            var info = RaceEvent.ById(_room.Course) ?? RaceEvent.Courses[0];
            var me = _room.Member(Me);
            bool host = Me == _room.HostId;
            TopBar(info, host);
            CourseCard(info);
            Chat();
            for (int i = 0; i < RaceLobby.Slots; i++) Slot(i, host);
            BottomBar(me, host);
            if (Dialog != null) DialogBox(host);
            if (_toast != null)
            {
                var t = L(_stage, 0, 0, 10, 10, _toast, 21, Txt, TextAnchor.MiddleCenter);
                float w = t.preferredWidth + 42f;
                var box = Panel(_stage, "Toast", (LobbyPanel.StageW - w) / 2f, 105, w, 48, Pn3, Line, 9);
                t.transform.SetParent(box, false);
                Place((RectTransform)t.transform, 0, 0, w, 48);
            }
        }

        void TopBar(RaceEvent.CourseInfo info, bool host)
        {
            var top = Panel(_stage, "Top", 24, 21, 1872, 69, Pnl, Line, 12);
            var title = L(top, 18, 0, 800, 69, _room.Name, 27, Txt, TextAnchor.MiddleLeft, true);
            float x = 18 + title.preferredWidth + 15;
            x = Pill(top, x, info.Course.Open ? "스프린트" : "순환", Acc, new Color(Acc.r, Acc.g, Acc.b, 0.12f));
            x = Pill(top, x, info.Course.Open ? "스프린트" : _room.Laps + "랩", Soft, Pn3);
            x = Pill(top, x, $"사람 {_room.Members.Count} · 봇 {_room.BotCount}", Soft, Pn3);
            if (_room.Private) Pill(top, x, "비공개", Soft, Pn3);
            var leave = Btn(top, "Leave", 1752, 12, 108, 45, Color.clear, null, () => OpenDialog("leave"), 9);
            L(leave.transform, 0, 0, 108, 45, "나가기", 21, Dim, TextAnchor.MiddleCenter);
            var set = Btn(top, "Settings", 1626, 12, 120, 45, Color.clear, null, () => OpenDialog("settings"), 9);
            L(set.transform, 0, 0, 120, 45, "방 설정", 21, Dim, TextAnchor.MiddleCenter);
            Enable(set, host);
            var hostName = _room.Member(_room.HostId)?.Name ?? "";
            L(top, 900, 0, 711, 69, host ? "코스와 설정은 방장만 바꿀 수 있습니다" : "방장: " + hostName, 18, Mute, TextAnchor.MiddleRight);
        }

        void CourseCard(RaceEvent.CourseInfo info)
        {
            var card = Panel(_stage, "Course", 24, 108, 408, 330, Pnl, Line, 12);
            var map = Panel(card, "Map", 15, 15, 378, 198, H("#1f2e24"), null, 9);
            var img = UiPaint.At(map, "Line", 0, 0, 378, 198).gameObject.AddComponent<Image>();
            img.sprite = MapSprite(info, 378, 198);
            img.raycastTarget = false;
            L(card, 15, 222, 378, 33, info.Name, 25, Txt, TextAnchor.MiddleLeft, true);
            var (len, elev) = CourseMeta(info);
            L(card, 15, 258, 378, 27, len + " · " + elev, 19, Dim);
            L(card, 15, 288, 90, 27, "난이도", 18, Dim);
            Difficulty.TryGetValue(info.Id, out int diff);
            for (int i = 0; i < 5; i++) Panel(card, "Diff" + i, 75 + i * 25.5f, 297, 21, 9, i < diff ? Danger : Pn3, null, 3);
        }

        // The course's length and elevation as the cards read them: "2.3 km", "고저차 152 m" / "평지".
        static (string len, string elev) CourseMeta(RaceEvent.CourseInfo info)
        {
            var c = info.Course;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in c.Path) { lo = Mathf.Min(lo, p.Y); hi = Mathf.Max(hi, p.Y); }
            return (c.LapLength < 1000f ? $"{c.LapLength:0} m" : $"{c.LapLength / 1000f:0.0} km", hi - lo < 1f ? "평지" : $"고저차 {hi - lo:0} m");
        }

        void Chat()
        {
            var card = Panel(_stage, "Chat", 24, 453, 408, 603, Pnl, Line, 12);
            var log = UiPaint.At(card, "Log", 15, 12, 378, 513);
            log.gameObject.AddComponent<RectMask2D>();
            var lines = _room.Lines;
            var sb = new System.Text.StringBuilder();
            for (int i = Math.Max(0, lines.Count - 30); i < lines.Count; i++) sb.Append(sb.Length > 0 ? "\n" : "").Append(lines[i].Replace("<", "‹"));
            var t = L(log, 0, 0, 378, 513, sb.ToString(), 19, Acc, lines.Count > 14 ? TextAnchor.LowerLeft : TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.3f;
            // Player chat is out of scope in v1 (spec #505): the input row stays, saying so.
            Panel(card, "Sep", 0, 534, 408, 1, Line, null, 1);
            var input = Panel(card, "Input", 12, 546, 384, 45, Pn3, Line, 9);
            L(input, 12, 0, 360, 45, "시스템 알림만 표시됩니다", 19, Mute);
        }

        void Slot(int i, bool host)
        {
            const float W = 352.5f, Hh = 399f;
            float x = 450 + (i % 4) * (W + 12), y = 108 + (i / 4) * (Hh + 12);
            int slot = i;
            if (_room.IsClosed(i))
            {
                var b = Btn(_stage, "Slot" + i, x, y, W, Hh, Color.clear, Line, () => OpenSlot(slot), 12);
                var stripes = UiPaint.At(b.transform, "Stripes", 3, 3, W - 6, Hh - 6).gameObject.AddComponent<Image>();
                stripes.sprite = Stripes();
                stripes.type = Image.Type.Tiled;
                stripes.raycastTarget = false;
                L(b.transform, 0, Hh / 2 - 30, W, 30, "닫힌 자리", 20, Mute, TextAnchor.MiddleCenter);
                if (host) L(b.transform, 0, Hh / 2 + 3, W, 27, "눌러서 열기", 18, Mute, TextAnchor.MiddleCenter);
                return;
            }
            var m = _room.SeatAt(i);
            if (m == null)
            {
                var b = Btn(_stage, "Slot" + i, x, y, W, Hh, new Color(26 / 255f, 33 / 255f, 43 / 255f, 0.55f), Line, () => OpenDialog("invite"), 12);
                L(b.transform, 0, (Hh - 51) / 2 - 48, W, 60, "+", 54, Mute, TextAnchor.MiddleCenter);
                L(b.transform, 0, (Hh - 51) / 2 + 12, W, 30, "친구 초대", 20, Mute, TextAnchor.MiddleCenter);
                Panel(b.transform, "Dash", 0, Hh - 51, W, 1, Line, null, 1);
                L(b.transform, 0, Hh - 51, W, 51, _room.FillBots ? "출발 때 봇이 채움" : "비워 둠", 21, Mute, TextAnchor.MiddleCenter);
                if (host) Close(b.transform, W, () => CloseSlot(slot));
                return;
            }
            bool mine = m.Id == Me, isHost = m.Id == _room.HostId;
            var card = Panel(_stage, "Slot" + i, x, y, W, Hh, Pn2, mine ? Host : Line, 12, mine ? 2 : 1);
            L(card, 15, 12, W - 90, 33, m.Name, 22, Txt);
            bool kick = host && !mine;
            if (m.Level > 0) L(card, W - (kick ? 147 : 105), 12, 90, 33, "Lv." + m.Level, 18, Mute, TextAnchor.MiddleRight);   // left of the kick x
            if (kick) { int id = m.Id; Close(card, W, () => Kick(id)); }
            float cw = W * 0.86f, ch = cw * 78f / 160f, top = 45 + (Hh - 51 - 45 - ch - 33) / 2f;
            Car(card, (W - cw) / 2f, top, cw, m.Car, m.Paint);
            L(card, 0, top + ch + 3, W, 30, Cars[CarIndex(m.Car)].name, 19, Dim, TextAnchor.MiddleCenter);
            Color fill = isHost ? Host : m.Ready ? Ready : Pn3, ink = isHost ? HostInk : m.Ready ? ReadyInk : Dim;
            Panel(card, "State", 0, Hh - 51, W, 51, fill, null, 12);
            Panel(card, "StateTop", 0, Hh - 51, W, 20, fill, null, 1);   // square top corners on the rounded bottom
            if (isHost)
            {
                var crown = UiPaint.At(card, "Crown", W / 2 - 54, Hh - 51 + 13.5f, 24, 24).gameObject.AddComponent<Image>();
                crown.sprite = CrownSprite();
                crown.raycastTarget = false;
                L(card, W / 2 - 24, Hh - 51, 90, 51, "방장", 22, ink, TextAnchor.MiddleLeft, true);
            }
            else L(card, 0, Hh - 51, W, 51, m.Ready ? "준비 완료" : "대기 중", 22, ink, TextAnchor.MiddleCenter, m.Ready);
        }

        void Close(Transform parent, float w, UnityEngine.Events.UnityAction act)
        {
            var x = Btn(parent, "X", w - 45, 9, 36, 36, Color.clear, null, act, 6);
            L(x.transform, 0, 0, 36, 36, "×", 26, Mute, TextAnchor.MiddleCenter);
        }

        void BottomBar(RaceLobbyMember me, bool host)
        {
            const float Y = 942, Hb = 114;
            bool lockedGarage = me.Ready && !host;
            var garage = BarButton(450, "차고", () => OpenDialog("garage"));
            Enable(garage, !lockedGarage);
            var course = BarButton(600, "코스", () => OpenDialog("course"));
            Enable(course, host);
            BarButton(750, "초대", () => OpenDialog("invite"));
            // The mockup's hover reasons (title), as a line in the gap above the bar.
            if (lockedGarage) L(_stage, 450, Y - 24, 600, 24, "준비를 취소해야 차를 바꿀 수 있습니다", 16, Mute);
            else if (!host) L(_stage, 600, Y - 24, 450, 24, "코스·방 설정: 방장만", 16, Mute);

            var car = Panel(_stage, "MyCar", 900, Y, 624, Hb, Pnl, Line, 12);
            Car(car, 18, (Hb - 80) / 2f, 165, me.Car, me.Paint);
            // The top speed on the first line: placed after the bold name it overlapped it on the first draw (width measured short).
            var row = VehicleHandling.RaceFor(me.Car);
            L(car, 201, 24, 400, 27, row != null ? $"내 차 · 최고 {row.MaxSpeedKmh:0} km/h" : "내 차", 19, Dim);
            L(car, 201, 54, 400, 33, Cars[CarIndex(me.Car)].name, 22, Txt, TextAnchor.MiddleLeft, true);

            var g = _room.GoFor(Me);
            Color fill = !g.Start ? (_room.Member(Me).Ready ? Pn3 : Ready) : g.Enabled ? Acc : Pn3;
            Color ink = !g.Start ? (_room.Member(Me).Ready ? Txt : ReadyInk) : g.Enabled ? AccInk : Dim;
            var go = Btn(_stage, "Go", 1536, Y, 360, Hb, fill, !g.Start && _room.Member(Me).Ready ? Ready : (Color?)null, Go, 12);
            L(go.transform, 0, 21, 360, 45, g.Label, 33, ink, TextAnchor.MiddleCenter, true);
            L(go.transform, 0, 66, 360, 27, g.Sub, 18, new Color(ink.r, ink.g, ink.b, 0.8f), TextAnchor.MiddleCenter);
        }

        // A disabled button fades whole, label included (mockup :disabled).
        static void Enable(Button b, bool on)
        {
            b.interactable = on;
            if (!on) b.gameObject.AddComponent<CanvasGroup>().alpha = 0.45f;
        }

        Button BarButton(float x, string text, UnityEngine.Events.UnityAction act)
        {
            var b = Btn(_stage, "Bar" + text, x, 942, 138, 114, Pnl, Line, act, 12);
            L(b.transform, 0, 0, 138, 114, text, 21, Soft, TextAnchor.MiddleCenter);
            return b;
        }

        void DialogBox(bool host)
        {
            var scrim = Btn(_stage, "Scrim", 0, 0, LobbyPanel.StageW, LobbyPanel.StageH, new Color(5 / 255f, 7 / 255f, 10 / 255f, 0.66f), null, CloseDialog, 1);
            scrim.transition = Selectable.Transition.None;
            if (Dialog == "garage") { GarageBox(); return; }
            if (Dialog == "course") { CourseBox(); return; }
            if (Dialog == "settings") { SettingsBox(); return; }
            if (Dialog == "invite") { InviteBox(); return; }
            const float W = 690, Hd = 240;   // the leave confirmation
            var box = Panel(_stage, "Dialog", (LobbyPanel.StageW - W) / 2f, (LobbyPanel.StageH - Hd) / 2f, W, Hd, H("#141a22"), Line, 15);
            box.GetComponent<Image>().raycastTarget = true;   // clicks on the card do not reach the scrim
            L(box, 27, 0, W - 54, 72, "방에서 나갈까요?", 28, Txt, TextAnchor.MiddleLeft, true);
            Panel(box, "Hr1", 0, 72, W, 1, Line, null, 1);
            L(box, 27, 72, W - 54, 96, host && _room.Members.Count > 1 ? "방장이 나가면 다음 사람이 방장이 됩니다." : "나가면 메인 메뉴로 돌아갑니다.", 21, Dim);
            Panel(box, "Hr2", 0, 168, W, 1, Line, null, 1);
            var stay = Btn(box, "Stay", W - 27 - 138 - 12 - 108, 183, 108, 42, Color.clear, Line, CloseDialog, 9);
            L(stay.transform, 0, 0, 108, 42, "남기", 21, Txt, TextAnchor.MiddleCenter);
            var go = Btn(box, "DoLeave", W - 27 - 138, 183, 138, 42, Danger, Danger, ConfirmLeave, 9);
            L(go.transform, 0, 0, 138, 42, "나가기", 21, Color.white, TextAnchor.MiddleCenter);
        }

        // ---- small builders ----

        static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        static Text L(Transform p, float x, float y, float w, float h, string text, int size, Color c, TextAnchor a = TextAnchor.MiddleLeft, bool bold = false) =>
            UiPaint.Label(p, "T", x, y, w, h, text, size, c, a, bold ? FontStyle.Bold : FontStyle.Normal);

        static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        static RectTransform Panel(Transform p, string name, float x, float y, float w, float h, Color fill, Color? ring, int radius, int ringWidth = 1)
        {
            var r = UiPaint.At(p, name, x, y, w, h);
            UiPaint.Box(r, fill, radius, ring, ringWidth);
            return r;
        }

        static Button Btn(Transform p, string name, float x, float y, float w, float h, Color fill, Color? ring, UnityEngine.Events.UnityAction act, int radius)
        {
            var r = UiPaint.At(p, name, x, y, w, h);
            var img = UiPaint.Box(r, fill == Color.clear ? UiPaint.Off : fill, radius, ring);
            img.raycastTarget = true;
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            b.colors = colors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(act);
            return b;
        }

        static float Pill(Transform p, float x, string text, Color ink, Color fill)
        {
            var r = UiPaint.At(p, "Pill", x, 18, 10, 33);
            UiPaint.Box(r, fill, 6);
            var t = UiKit.Label(UiKit.Fill("T", r), text, 19, ink, TextAnchor.MiddleCenter);
            float w = t.preferredWidth + 27;
            r.sizeDelta = new Vector2(w, 33);
            return x + w + 15;
        }

        // The car in its paint: the race model's portrait (CarPortrait, #514), else the mockup's flat silhouette (carSVG) when the scene
        // has no car prefabs.
        static void Car(Transform p, float x, float y, float w, string car, int paint)
        {
            float h = w * 78f / 160f;
            var portrait = CarPortrait.Get(Cars[CarIndex(car)].id, RaceTint[Mathf.Clamp(paint, 0, RaceTint.Length - 1)]);
            if (portrait != null)
            {
                var ri = UiPaint.At(p, "Car", x, y, w, h).gameObject.AddComponent<RawImage>();
                ri.texture = portrait;
                ri.raycastTarget = false;
                return;
            }
            string shape = Cars[CarIndex(car)].shape;
            var (body, details) = CarSprites(shape);
            var b = UiPaint.At(p, "Body", x, y, w, h).gameObject.AddComponent<Image>();
            b.sprite = body; b.color = PaintColors[Mathf.Clamp(paint, 0, PaintColors.Length - 1)]; b.raycastTarget = false;
            var d = UiPaint.At(p, "Details", x, y, w, h).gameObject.AddComponent<Image>();
            d.sprite = details; d.raycastTarget = false;
        }

        // ---- procedural sprites (cached) ----

        static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

        static readonly Dictionary<string, float[]> Bodies = new Dictionary<string, float[]>
        {
            { "sport", new float[] { 10, 52, 22, 40, 58, 30, 92, 30, 118, 40, 148, 44, 152, 54, 150, 60, 10, 60 } },
            { "sedan", new float[] { 8, 54, 16, 42, 44, 40, 60, 26, 104, 26, 122, 40, 148, 44, 152, 60, 8, 60 } },
            { "wagon", new float[] { 8, 54, 14, 40, 40, 38, 54, 24, 140, 24, 146, 40, 152, 46, 152, 60, 8, 60 } },
            { "pickup", new float[] { 6, 56, 12, 36, 52, 34, 60, 18, 94, 18, 100, 34, 152, 34, 154, 58, 6, 58 } },
            { "van", new float[] { 8, 58, 10, 30, 22, 14, 146, 14, 152, 22, 154, 58 } },
        };
        static readonly Dictionary<string, float[]> Windows = new Dictionary<string, float[]>
        {
            { "sport", new float[] { 60, 32, 90, 32, 110, 40, 48, 40 } },
            { "sedan", new float[] { 64, 29, 102, 29, 116, 40, 52, 40 } },
            { "wagon", new float[] { 58, 27, 136, 27, 140, 38, 46, 38 } },
            { "pickup", new float[] { 63, 21, 91, 21, 95, 33, 57, 33 } },
            { "van", new float[] { 26, 18, 60, 18, 60, 32, 16, 32 } },
        };

        static (Sprite body, Sprite details) CarSprites(string shape)
        {
            if (!Sprites.TryGetValue("body" + shape, out var body))
            {
                var poly = Bodies[shape];
                Sprites["body" + shape] = body = Raster(160, 78, 2f, (x, y) => Inside(poly, x, y) ? Color.white : Color.clear);
                var win = Windows[shape];
                float wy = shape == "pickup" || shape == "van" ? 58 : 60;
                Color glass = H("#9fb3c6"), tyre = H("#11151b"), hub = H("#5d6874");
                glass.a = 0.75f;
                Sprites["det" + shape] = Raster(160, 78, 2f, (x, y) =>
                {
                    float d1 = Dist(x, y, 38, wy), d2 = Dist(x, y, 124, wy);
                    if (d1 <= 5 || d2 <= 5) return hub;
                    if (d1 <= 11 || d2 <= 11) return tyre;
                    if (Inside(win, x, y)) return glass;
                    float ex = (x - 80) / 70f, ey = (y - 72) / 4f;
                    return ex * ex + ey * ey <= 1f ? new Color(0, 0, 0, 0.35f) : Color.clear;
                });
            }
            return (body, Sprites["det" + shape]);
        }

        static Sprite CrownSprite()
        {
            if (Sprites.TryGetValue("crown", out var s)) return s;
            var poly = new float[] { 3, 8, 7.5f, 12, 12, 5, 16.5f, 12, 21, 8, 19, 19, 5, 19 };
            var gold = H("#f2c230");
            return Sprites["crown"] = Raster(24, 24, 3f, (x, y) => Inside(poly, x, y) ? gold : Color.clear);
        }

        // The closed slot's 135° stripes (mockup .slot.closed), tiling seamlessly.
        static Sprite Stripes()
        {
            if (Sprites.TryGetValue("stripes", out var s)) return s;
            Color a = new Color(26 / 255f, 33 / 255f, 43 / 255f, 0.6f), b = new Color(20 / 255f, 26 / 255f, 34 / 255f, 0.6f);
            return Sprites["stripes"] = Raster(42, 42, 1f, (x, y) => ((int)((x + y) / 21f) & 1) == 0 ? a : b, 1);
        }

        // The course's line from its path (mockup trackSVG): a dark-green band under a light centre line, fitted into w x h.
        static Sprite MapSprite(RaceEvent.CourseInfo info, int w, int h)
        {
            string key = $"map{info.Id}{w}x{h}";
            if (Sprites.TryGetValue(key, out var s)) return s;
            var c = info.Course;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in c.Path) { minX = Mathf.Min(minX, p.X); maxX = Mathf.Max(maxX, p.X); minZ = Mathf.Min(minZ, p.Z); maxZ = Mathf.Max(maxZ, p.Z); }
            const float Margin = 18f, Outer = 7f, Inner = 2f;
            float k = Mathf.Min((w - 2 * Margin) / Mathf.Max(1f, maxX - minX), (h - 2 * Margin) / Mathf.Max(1f, maxZ - minZ));
            float ox = (w - (maxX - minX) * k) / 2f, oy = (h - (maxZ - minZ) * k) / 2f;
            var outer = new float[w * h];
            var inner = new float[w * h];
            void Stamp(float[] a, float cx, float cy, float r)
            {
                for (int y = Mathf.Max(0, (int)(cy - r - 1)); y <= Mathf.Min(h - 1, (int)(cy + r + 1)); y++)
                    for (int x = Mathf.Max(0, (int)(cx - r - 1)); x <= Mathf.Min(w - 1, (int)(cx + r + 1)); x++)
                    {
                        float cov = Mathf.Clamp01(r - Dist(x + 0.5f, y + 0.5f, cx, cy) + 0.5f);
                        int i = (h - 1 - y) * w + x;   // texture rows go up; z (south) goes down the picture
                        if (cov > a[i]) a[i] = cov;
                    }
            }
            void Run(IReadOnlyList<RaceCourse.Point> pts, bool closed)
            {
                int n = pts.Count;
                for (int i = 0; i < (closed ? n : n - 1); i++)
                {
                    var p0 = pts[i]; var p1 = pts[(i + 1) % n];
                    float x0 = ox + (p0.X - minX) * k, y0 = oy + (p0.Z - minZ) * k, x1 = ox + (p1.X - minX) * k, y1 = oy + (p1.Z - minZ) * k;
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Dist(x0, y0, x1, y1)));
                    for (int t = 0; t <= steps; t++)
                    {
                        float fx = Mathf.Lerp(x0, x1, t / (float)steps), fy = Mathf.Lerp(y0, y1, t / (float)steps);
                        Stamp(outer, fx, fy, Outer); Stamp(inner, fx, fy, Inner);
                    }
                }
            }
            Run(c.Path, !c.Open);
            if (c.BranchPath.Count > 1) Run(c.BranchPath, false);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            Color band = H("#3b4a3e"), lineC = H("#e9edf1");
            for (int i = 0; i < px.Length; i++) { var col = Color.Lerp(band, lineC, inner[i]); col.a = Mathf.Max(outer[i], inner[i]); px[i] = col; }
            tex.SetPixels(px);
            tex.Apply();
            return Sprites[key] = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        // Rasterises `at` (colour at a point in w x h units, y down) at `scale` texels per unit, 2x2 samples per texel (1 = none).
        static Sprite Raster(int w, int h, float scale, Func<float, float, Color> at, int samples = 2)
        {
            int tw = Mathf.CeilToInt(w * scale), th = Mathf.CeilToInt(h * scale);
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = samples == 1 ? TextureWrapMode.Repeat : TextureWrapMode.Clamp };
            var px = new Color[tw * th];
            for (int py = 0; py < th; py++)
                for (int qx = 0; qx < tw; qx++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                        {
                            var c = at((qx + (sx + 0.5f) / samples) / scale, (th - py - 1 + (sy + 0.5f) / samples) / scale);
                            r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                        }
                    px[py * tw + qx] = a > 0f ? new Color(r / a, g / a, b / a, a / (samples * samples)) : Color.clear;
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), 100f * scale);
        }

        static float Dist(float x0, float y0, float x1, float y1) => Mathf.Sqrt((x0 - x1) * (x0 - x1) + (y0 - y1) * (y0 - y1));

        // Even-odd point in polygon; poly is x0,y0,x1,y1,...
        static bool Inside(float[] poly, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 2; i < poly.Length; j = i, i += 2)
            {
                float xi = poly[i], yi = poly[i + 1], xj = poly[j], yj = poly[j + 1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }
    }
}
