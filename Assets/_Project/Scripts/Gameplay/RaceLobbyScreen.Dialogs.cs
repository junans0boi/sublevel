using System;
using System.Linq;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The room's garage (#514), course and settings (#515) and invite (#520) dialogs, after the mockup's garageHTML / courseHTML /
    // settingsHTML / inviteHTML (880 / 880 / 520 / 560 px wide, at the stage's 1.5x). Each edits a draft; 이 차로 결정 / 이 코스로 / 저장 hand it to the RaceLobby
    // command (which refuses a ready guest's car, a guest's course or settings, an empty name). Redrawn whole like the rest of the room.
    public partial class RaceLobbyScreen
    {
        // Mockup CARS[].line, in Cars order; the cluster style names are the mockup's CLN.
        static readonly string[] CarLines = { "직선 최강, 헤어핀에서 뒤가 쉽게 빠짐", "무난한 균형형", "코너가 많은 고개에서 강함", "비포장 지름길에서 속도가 덜 떨어짐", "느리지만 부딪혀도 안 밀림" };
        static readonly Color Ok = H("#57c785"), Blue = H("#5cc8f0"), Err = H("#ff8a80"), Picked = H("#262522");   // the mockup's 8 % accent over the dialog, pre-mixed: UI blends in linear space, where 8 % reads far stronger
        const float DlgHead = 72, DlgFoot = 78, Pad = 27, BodyTop = DlgHead + 24;

        int _gCar, _gPaint, _cCourse, _cLaps;
        string _sName;
        bool _sPrivate, _sBots;
        Text _sErrorLabel;
        InputField _sInput;

        public int GarageCar => _gCar;
        public int GaragePaint => _gPaint;
        public int CourseDraft => _cCourse;
        public int LapsDraft => _cLaps;
        public string SettingsError { get; private set; }

        // Opening a dialog starts its draft from the room; false (with a toast) when this player may not open it.
        bool DraftFor(string kind)
        {
            var me = _room.Member(Me);
            bool host = Me == _room.HostId;
            if (kind == "garage")
            {
                if (me.Ready && !host) { Toast("준비를 취소해야 차를 바꿀 수 있습니다."); return false; }
                _gCar = CarIndex(me.Car); _gPaint = me.Paint;
            }
            else if (kind == "course" || kind == "settings")
            {
                if (!host) { Toast("방장만 바꿀 수 있습니다."); return false; }
                _cCourse = Math.Max(0, Array.FindIndex(RaceEvent.Courses, c => c.Id == _room.Course)); _cLaps = _room.Laps;
                _sName = _room.Name; _sPrivate = _room.Private; _sBots = _room.FillBots; SettingsError = null;
            }
            return true;
        }

        // ---- commands ----

        public void PickCar(int i) { _gCar = Mathf.Clamp(i, 0, Cars.Length - 1); Render(); }
        public void PickPaint(int i) { _gPaint = Mathf.Clamp(i, 0, RaceLobby.Paints - 1); Render(); }

        public void SaveCar()
        {
            Dialog = null;
            if (_party.SetCar(Cars[_gCar].id, _gPaint)) Changed();
            else Toast("준비를 취소해야 차를 바꿀 수 있습니다.");
        }

        public void PickCourse(int i) { _cCourse = Mathf.Clamp(i, 0, RaceEvent.Courses.Length - 1); Render(); }
        public void PickLaps(int laps) { if (Array.IndexOf(RaceLobby.LapChoices, laps) >= 0) { _cLaps = laps; Render(); } }

        public void SaveCourse()
        {
            Dialog = null;
            if (_party.SetCourse(RaceEvent.Courses[_cCourse].Id, _cLaps)) Changed();
            else Render();
        }

        // Typing clears the inline error without a redraw (that would rebuild the field under the caret).
        public void SetRoomName(string name)
        {
            _sName = name;
            SettingsError = null;
            if (_sErrorLabel != null) _sErrorLabel.text = "";
        }

        public void SetPrivate(bool on) { _sPrivate = on; Render(); }
        public void SetFillBots(bool on) { _sBots = on; Render(); }

        public void SaveSettings()
        {
            if (string.IsNullOrWhiteSpace(_sName))
            {
                SettingsError = "방 이름을 입력하세요.";
                Render();
                if (_sInput != null) _sInput.ActivateInputField();
                return;
            }
            Dialog = null;
            if (_party.SetSettings(_sName, _sPrivate, _sBots)) Changed();
            else Render();
        }

        // The invite dialog's friends: online first, then in a match, then offline (the mockup's order puts the invitable ones on top).
        public FriendDto[] InviteList
        {
            get
            {
                var all = _menu != null && _menu.Social != null ? _menu.Social.Friends.friends : null;
                return all == null ? new FriendDto[0] : all.OrderBy(f => f.status == "lobby" ? 0 : f.status == "in_match" ? 1 : 2).ToArray();
            }
        }

        public void SendInvite(long id)
        {
            var f = InviteList.FirstOrDefault(x => x.account_id == id);
            if (f == null) return;
            var why = _party.Invite(id, f.nickname);
            if (why != null) Toast(why);
            else Render();
        }

        // ---- drawing ----

        void InviteBox()
        {
            // shortcut: no scrolling, the first 8 friends (online ones first); a ScrollRect when someone has more friends than that.
            const float W = 840, RowH = 75, Gap = 9, BtnW = 135, BtnH = 51;
            var friends = InviteList.Take(8).ToArray();
            bool hostOnly = _room.Private && Me != _room.HostId;
            int rows = Math.Max(1, friends.Length);
            float noteY = BodyTop + rows * (RowH + Gap) - Gap + 18, Hd = noteY + 27 + 24 + DlgFoot;
            int empty = _room.Capacity - _room.Members.Count;
            var box = Frame(W, Hd, "친구 초대", "빈 자리 " + empty, 168);
            if (friends.Length == 0)
                L(box, Pad, BodyTop, W - 2 * Pad, RowH, _menu != null && _menu.Social != null ? "친구가 없습니다. 메인 메뉴의 친구 창에서 친구를 추가하세요." : "로그인하면 친구를 초대할 수 있습니다.", 21, Dim);
            for (int i = 0; i < friends.Length; i++)
            {
                var f = friends[i];
                long id = f.account_id;
                var row = Panel(box, "Friend" + id, Pad, BodyTop + i * (RowH + Gap), W - 2 * Pad, RowH, Pn2, Line, 12);
                Color dot = f.status == "lobby" ? Ok : f.status == "in_match" ? Acc : Mute;
                Panel(row, "Dot", 15, (RowH - 12) / 2f, 12, 12, dot, null, 6);
                L(row, 42, 0, 360, RowH, f.nickname, 21, Txt);
                float bx = W - 2 * Pad - 15 - BtnW;
                L(row, bx - 15 - 180, 0, 180, RowH, SocialRules.InviteStatus(f.status), 18, Dim, TextAnchor.MiddleRight);
                var (label, on) = SocialRules.InviteButton(f.status, _room.Member((int)id) != null, _party.Invited(id), hostOnly);
                if (on)
                {
                    var b = Btn(row, "Invite" + id, bx, (RowH - BtnH) / 2f, BtnW, BtnH, Acc, Acc, () => SendInvite(id), 9);
                    L(b.transform, 0, 0, BtnW, BtnH, label, 21, AccInk, TextAnchor.MiddleCenter, true);
                }
                else
                {
                    var b = Panel(row, "Invite" + id, bx, (RowH - BtnH) / 2f, BtnW, BtnH, Color.clear, Line, 9);
                    L(b, 0, 0, BtnW, BtnH, label, 21, Mute, TextAnchor.MiddleCenter);
                }
            }
            L(box, Pad, noteY, W - 2 * Pad, 27, hostOnly ? "비공개 방은 방장만 초대할 수 있습니다." : "초대받은 친구가 수락하면 빈 자리에 들어옵니다. 방이 꽉 차면 초대할 수 없습니다.", 18, hostOnly ? Err : Mute);
            var close = Btn(box, "Close", W - Pad - 96, Hd - DlgFoot + 15, 96, 48, Pn3, Line, CloseDialog, 9);
            L(close.transform, 0, 0, 96, 48, "닫기", 21, Txt, TextAnchor.MiddleCenter);
        }

        void GarageBox()
        {
            const float W = 1320, Hd = 837, LW = 360, PX = Pad + LW + 24, PW = W - PX - Pad;
            var box = Frame(W, Hd, "차고", "레이스에서는 차마다 성능이 다릅니다", 105);
            for (int i = 0; i < Cars.Length; i++)
            {
                int k = i;
                bool on = i == _gCar;
                var it = Btn(box, "Car" + i, Pad, BodyTop + i * 84, LW, 75, on ? Picked : Pn2, on ? Acc : Line, () => PickCar(k), 12);
                Car(it.transform, 15, (75 - 47) / 2f, 96, Cars[i].id, _gPaint);
                L(it.transform, 126, 10, LW - 136, 30, Cars[i].name, 21, Txt);
                L(it.transform, 126, 39, LW - 136, 24, $"{Cars[i].kind} · {TopSpeed(Cars[i].id)} km/h", 18, Dim);
            }

            var car = Cars[_gCar];
            var pv = UiPaint.At(box, "Preview", PX, BodyTop, PW, 300).gameObject.AddComponent<Image>();
            pv.sprite = PreviewBack();
            pv.raycastTarget = false;
            Car(pv.transform, (PW - 600) / 2f, (300 - 292) / 2f, 600, car.id, _gPaint);
            L(box, PX, BodyTop + 315, PW, 45, $"<size=33><b>{car.name}</b></size>   <color=#9aa5b1>{car.kind} · 최고 {TopSpeed(car.id)} km/h</color>", 21, Txt);
            var cl = VehicleHandling.ClusterFor(car.id, false);
            string style = cl == null ? "" : cl.Cluster == "A" ? "A 원형 쌍" : cl.Cluster == "B" ? "B 반원" : cl.Cluster == "D" ? "D 클래식 아날로그" : "C 디지털 스포츠";
            L(box, PX, BodyTop + 375, PW, 27, CarLines[_gCar] + (cl == null ? "" : $" · 계기판 <color={cl.ClusterAccent}>{style}</color>"), 19, Dim);

            var s = RaceStats.Of(VehicleHandling.RaceFor(car.id));
            var rows = new (string k, int v)[] { ("속도", s.Speed), ("가속", s.Accel), ("코너링", s.Cornering), ("접지", s.Grip), ("내구", s.Durability) };
            const float BarX = 123, BarW = PW - BarX - 66;
            for (int i = 0; i < rows.Length; i++)
            {
                float y = BodyTop + 420 + i * 33;
                L(box, PX, y, 108, 24, rows[i].k, 19, Dim);
                Panel(box, "Bar" + i, PX + BarX, y + 6, BarW, 12, Pn3, null, 6);
                Panel(box, "Fill" + i, PX + BarX, y + 6, BarW * rows[i].v / 10f, 12, Blue, null, 6);
                L(box, PX + PW - 51, y, 51, 24, rows[i].v.ToString(), 19, Dim);
            }

            float sy = BodyTop + 600;
            L(box, PX, sy, 60, 39, "색상", 19, Dim);
            for (int i = 0; i < RaceLobby.Paints; i++)
            {
                int k = i;
                var sw = Btn(box, "Paint" + i, PX + 60 + i * 51, sy, 39, 39, PaintColors[i], null, () => PickPaint(k), 19);
                if (i == _gPaint) Panel(sw.transform, "On", 0, 0, 39, 39, Color.clear, Txt, 19, 3);
            }
            Footer(box, W, Hd, "이 차로 결정", 174, SaveCar);
        }

        void CourseBox()
        {
            const float W = 1320, Hd = 609, CW = (W - 2 * Pad - 15) / 2f, CH = 165;
            var box = Frame(W, Hd, "코스 선택", null, 0);
            for (int i = 0; i < RaceEvent.Courses.Length; i++)
            {
                int k = i;
                var info = RaceEvent.Courses[i];
                bool on = i == _cCourse;
                var card = Btn(box, "Course" + i, Pad + (i % 2) * (CW + 15), BodyTop + (i / 2) * (CH + 15), CW, CH, on ? Picked : Pn2, on ? Acc : Line, () => PickCourse(k), 12);
                var map = Panel(card.transform, "Map", 15, 15, 225, 135, H("#1f2e24"), null, 9);
                var img = UiPaint.At(map, "Line", 0, 0, 225, 135).gameObject.AddComponent<Image>();
                img.sprite = MapSprite(info, 225, 135);
                img.raycastTarget = false;
                var (len, elev) = CourseMeta(info);
                L(card.transform, 258, 18, CW - 270, 30, info.Name, 24, Txt, TextAnchor.MiddleLeft, true);
                L(card.transform, 258, 51, CW - 270, 27, (info.Course.Open ? "스프린트" : "순환") + " · " + len, 19, Dim);
                L(card.transform, 258, 78, CW - 270, 27, elev, 19, Dim);
                L(card.transform, 258, 108, 60, 27, "난이도", 18, Dim);
                Difficulty.TryGetValue(info.Id, out int diff);
                for (int d = 0; d < 5; d++) Panel(card.transform, "Diff" + d, 318 + d * 25.5f, 117, 21, 9, d < diff ? Danger : Pn3, null, 3);
            }
            float ry = BodyTop + 2 * CH + 15 + 21;
            if (RaceLobby.IsLoopCourse(RaceEvent.Courses[_cCourse].Id))
            {
                L(box, Pad, ry, 66, 45, "랩 수", 21, Dim);
                var choices = RaceLobby.LapChoices;
                const float SW = 72;
                Panel(box, "Seg", Pad + 75, ry, SW * choices.Length, 45, Color.clear, Line, 9);
                for (int i = 0; i < choices.Length; i++)
                {
                    int n = choices[i];
                    bool on = n == _cLaps, end = i == 0 || i == choices.Length - 1;
                    var b = Btn(box, "Laps" + n, Pad + 75 + i * SW + (i == 0 ? 1 : 0), ry + 1, SW - (end ? 1 : 0), 43, on ? Pn3 : Color.clear, null, () => PickLaps(n), end ? 8 : 1);
                    L(b.transform, 0, 0, SW - (end ? 1 : 0), 43, n + "랩", 21, on ? Txt : Dim, TextAnchor.MiddleCenter);
                }
            }
            else L(box, Pad, ry, W - 2 * Pad, 45, "스프린트: 출발점에서 결승점까지 한 번", 18, Mute);
            Footer(box, W, Hd, "이 코스로", 138, SaveCourse);
        }

        void SettingsBox()
        {
            const float W = 780, Hd = 420, FW = W - 2 * Pad;
            var box = Frame(W, Hd, "방 설정", null, 0);
            L(box, Pad, BodyTop, FW, 27, "방 이름", 21, Dim);
            var field = Panel(box, "Name", Pad, BodyTop + 36, FW, 54, Pn3, SettingsError != null ? Err : Line, 9);
            var back = field.GetComponent<Image>();
            back.raycastTarget = true;
            var text = UiKit.Label(UiKit.Fill("Text", field, 15f), "", 21, Txt, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            _sInput = field.gameObject.AddComponent<InputField>();
            _sInput.targetGraphic = back; _sInput.textComponent = text; _sInput.characterLimit = RaceLobby.NameMax;
            _sInput.text = _sName;
            _sInput.onValueChanged.AddListener(SetRoomName);
            _sErrorLabel = L(box, Pad, BodyTop + 96, FW, 24, SettingsError ?? "", 18, Err);
            Check(box, BodyTop + 141, "비공개 (초대한 친구만 입장)", _sPrivate, () => SetPrivate(!_sPrivate));
            Check(box, BodyTop + 189, "빈 자리는 출발할 때 봇으로 채우기", _sBots, () => SetFillBots(!_sBots));
            Footer(box, W, Hd, "저장", 96, SaveSettings);
        }

        // The card, header (title, an optional note at noteX, ×) and the header/footer rules; the room's scrim is already drawn.
        RectTransform Frame(float w, float h, string title, string note, float noteX)
        {
            var box = Panel(_stage, "Dialog", (LobbyPanel.StageW - w) / 2f, (LobbyPanel.StageH - h) / 2f, w, h, H("#141a22"), Line, 15);
            box.GetComponent<Image>().raycastTarget = true;   // clicks on the card do not reach the scrim
            L(box, Pad, 0, w - 2 * Pad - 60, DlgHead, title, 28, Txt, TextAnchor.MiddleLeft, true);
            if (note != null) L(box, noteX, 0, w - noteX - 90, DlgHead, note, 18, Mute);
            var x = Btn(box, "X", w - Pad - 45, (DlgHead - 45) / 2f, 45, 45, Color.clear, null, CloseDialog, 9);
            L(x.transform, 0, 0, 45, 45, "×", 30, Dim, TextAnchor.MiddleCenter);
            Panel(box, "Hr1", 0, DlgHead, w, 1, Line, null, 1);
            Panel(box, "Hr2", 0, h - DlgFoot, w, 1, Line, null, 1);
            return box;
        }

        // 취소 (ghost) and the primary action, right-aligned in the footer.
        void Footer(RectTransform box, float w, float h, string ok, float okW, UnityAction act)
        {
            float y = h - DlgFoot + 15, x = w - Pad - okW;
            var pri = Btn(box, "Ok", x, y, okW, 48, Acc, Acc, act, 9);
            L(pri.transform, 0, 0, okW, 48, ok, 21, AccInk, TextAnchor.MiddleCenter, true);
            var cancel = Btn(box, "Cancel", x - 12 - 96, y, 96, 48, Color.clear, Line, CloseDialog, 9);
            L(cancel.transform, 0, 0, 96, 48, "취소", 21, Txt, TextAnchor.MiddleCenter);
        }

        // A checkbox row (mockup .chk): the whole row toggles.
        void Check(RectTransform box, float y, string text, bool on, UnityAction act)
        {
            var hit = UiPaint.At(box, "Chk", Pad, y, 600, 33);
            var area = hit.gameObject.AddComponent<Image>();
            area.color = Color.clear;   // clicks only: UiPaint.Off still shows as a band on this dark card
            var row = hit.gameObject.AddComponent<Button>();
            row.targetGraphic = area;
            row.transition = Selectable.Transition.None;
            row.navigation = new Navigation { mode = Navigation.Mode.None };
            row.onClick.AddListener(act);
            var b = Panel(row.transform, "Box", 0, 6, 21, 21, on ? Acc : Pn3, on ? Acc : Dim, 4);
            if (on)
            {
                var tick = UiPaint.At(b, "Tick", 0, 0, 21, 21).gameObject.AddComponent<Image>();
                tick.sprite = TickSprite();
                tick.color = AccInk;
                tick.raycastTarget = false;
            }
            L(row.transform, 33, 0, 560, 33, text, 21, Txt);
        }

        static string TopSpeed(string car) => (VehicleHandling.RaceFor(car)?.MaxSpeedKmh ?? 0f).ToString("0");

        static Sprite TickSprite()
        {
            if (Sprites.TryGetValue("tick", out var s)) return s;
            var poly = new float[] { 4, 12, 7, 9, 10, 12, 17, 5, 20, 8, 10, 18 };
            return Sprites["tick"] = Raster(24, 24, 3f, (x, y) => Inside(poly, x, y) ? Color.white : Color.clear);
        }

        // The preview's radial glow (mockup .pv: ellipse at 50% 80%, #2a3442 to #151b23 at 70%) with its 8 px corners.
        static Sprite PreviewBack()
        {
            if (Sprites.TryGetValue("pv", out var s)) return s;
            Color a = H("#2a3442"), b = H("#151b23");
            const float W = 147, Hh = 50, R = 2f;   // 882 x 300 stage px at 1/6
            return Sprites["pv"] = Raster((int)W, (int)Hh, 2f, (x, y) =>
            {
                float cx = Mathf.Clamp(x, R, W - R), cy = Mathf.Clamp(y, R, Hh - R);
                if (Dist(x, y, cx, cy) > R) return Color.clear;
                float dx = (x - W / 2f) / (W / 2f * 1.414f), dy = (y - Hh * 0.8f) / (Hh * 0.8f * 1.414f);
                return Color.Lerp(a, b, Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / 0.7f));
            });
        }
    }
}
