using Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // 이벤트 (ADR-512, #445): the event mode's entry screen. The first event is the car race: one card per course (RaceEvent.Courses whose
    // scene is in the build), 시작하기 starts a local race against the event's bots on the chosen course, 뒤로 가기 goes back to the lobby.
    public class EventEntryScreen
    {
        MainMenu _menu;
        RectTransform _root;
        int _selected;
        readonly System.Collections.Generic.List<Image> _cards = new System.Collections.Generic.List<Image>();

        public int Selected => _selected;
        public RaceEvent.CourseInfo SelectedCourse => RaceEvent.Courses[_selected];

        public RectTransform Build(Transform parent, MainMenu menu)
        {
            _menu = menu;
            _root = UiKit.Fill("EventEntry", parent);
            var stage = UiKit.Rect("Stage", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920f, 1080f));
            UiPaint.Box(UiPaint.At(stage, "Back", 0, 0, 1920, 1080), UiPaint.Bg, 0);
            UiPaint.Label(stage, "Title", 64, 56, 900, 60, "이벤트", 44, UiPaint.Tx, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiPaint.Label(stage, "Event", 64, 128, 900, 36, "자동차 경주 — 차에 탄 채 출발, 3·2·1·GO, 먼저 들어오는 사람이 1위", 22, UiPaint.T2);
            for (int i = 0; i < RaceEvent.Courses.Length; i++)
            {
                var c = RaceEvent.Courses[i];
                if (!Application.CanStreamedLevelBeLoaded(c.Scene)) { _cards.Add(null); continue; }   // not built yet
                int index = i;
                float y = 200f + i * 150f;
                var card = UiPaint.Btn(stage, "Course" + i, 64, y, 760, 130, "", 18, 12, UiPaint.Pn, UiPaint.Ln, UiPaint.Tx, () => Select(index));
                foreach (var t in card.GetComponentsInChildren<Text>()) Object.Destroy(t.gameObject);
                UiPaint.Label(card.transform, "Name", 28, 20, 700, 44, c.Name, 30, UiPaint.Tx, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiPaint.Label(card.transform, "Info", 28, 72, 700, 32,
                    c.Course.Open ? $"출발→결승 {c.Course.LapLength / 1000f:0.0}km · 고저차 {Mathf.Abs(c.Course.HeightAt(c.Course.LapLength) - c.Course.HeightAt(0f)):0}m · 최대 {RaceEvent.GridSlots}대"
                    : $"한 바퀴 {c.Course.LapLength / 1000f:0.0}km · {c.Laps}바퀴 · 최대 {RaceEvent.GridSlots}대{(c.Course.BranchPath.Count > 0 ? " · 갈림길" : "")}", 20, UiPaint.T2);
                _cards.Add(card.GetComponent<Image>());
            }
            float by = 200f + RaceEvent.Courses.Length * 150f + 20f;
            UiPaint.Btn(stage, "Go", 64, by, 360, 64, "시작하기", 24, 12, UiPaint.Ac, UiPaint.Ac, UiPaint.Bg, Start);
            UiPaint.Btn(stage, "BackBtn", 444, by, 240, 64, "뒤로 가기", 22, 12, UiPaint.Pn, UiPaint.Ln, UiPaint.Tx, () => _menu.Back());
            Select(0);
            return _root;
        }

        public void Select(int index)
        {
            _selected = Mathf.Clamp(index, 0, RaceEvent.Courses.Length - 1);
            for (int i = 0; i < _cards.Count; i++) if (_cards[i] != null) _cards[i].color = i == _selected ? UiPaint.AcS : UiPaint.Pn;
        }

        public void Start() => _menu.StartEvent(SelectedCourse);
    }
}
