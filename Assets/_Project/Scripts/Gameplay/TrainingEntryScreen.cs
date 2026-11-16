using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // 훈련장 진입 (adopted mockup, Resources/Ui/Screens/tutorial.json): what to play before a match. Two modes exist in the game,
    // so only those two cards are drawn: 사격장 (the training range) and 봇 연습 매치 (a solo match against bots). The
    // mockup's 기본 조작 안내 card and its step-progress panel have no game behind them yet and are not drawn (no sample data).
    public class TrainingEntryScreen
    {
        public MockScreen Screen { get; private set; }
        public int Selected { get; private set; } // 0 = 사격장, 2 = 봇 연습 매치 (the mockup's card numbers)
        MainMenu _menu;
        MockScreen.Item _card0, _card2, _badgeBox, _badgeText;

        static bool Removed(MockScreen.Item it)
        {
            if (it.r[0] >= 60f && it.r[0] < 830f && it.r[1] >= 310f && it.r[1] < 425f) return true; // 기본 조작 안내 card
            if (it.r[0] >= 60f && it.r[0] < 830f && it.r[1] >= 550f && it.r[1] < 820f) return true;  // the empty box under the cards
            if (it.r[0] >= 855f && it.r[1] >= 120f && it.r[1] < 670f) return true;                     // step-progress panel
            if (it.r[0] >= 855f && it.r[1] >= 745f && it.r[1] < 800f) return true;                     // 처음 실행할 때 … toggle
            return false;
        }

        public RectTransform Build(Transform parent, MainMenu menu)
        {
            _menu = menu;
            Screen = MockScreen.Build(parent, "tutorial", OnClick, Removed);
            foreach (var it in Screen.Items)
            {
                if (it.id == "tut0") _card0 = it;
                else if (it.id == "tut2") _card2 = it;
                else if (it.k == "box" && it.r[0] > 730f && it.r[0] < 740f && it.r[1] > 215f && it.r[1] < 222f) _badgeBox = it;
                else if (it.k == "text" && it.t == "선택됨") _badgeText = it;
            }
            // 봇 연습 매치 moves up into the freed slot; 시작하기 / 뒤로 가기 sit under the cards.
            foreach (var it in Screen.Items)
            {
                var r = Screen.Rects[it];
                if (it.r[0] < 830f && it.r[1] >= 430f && it.r[1] < 545f) Shift(r, 0f, -121f);
                else if (it.r[1] >= 670f && it.r[1] < 740f)
                {
                    bool start = it.r[0] < 1640f;
                    float dx = start ? (64f - 860f) : (644f - 1652f);
                    Shift(r, dx, 450f - 674f);
                    if (start && it.k == "box") r.sizeDelta = new Vector2(560f, r.sizeDelta.y); // the mockup's wide button, narrowed to sit beside 뒤로 가기
                    else if (start && it.k == "text") r.anchoredPosition = new Vector2(64f + 280f - it.r[2] * 0.5f, r.anchoredPosition.y);
                }
            }
            Select(0);
            return Screen.Root;
        }

        static void Shift(RectTransform r, float dx, float dy) => r.anchoredPosition += new Vector2(dx, -dy);

        public void Select(int card)
        {
            Selected = card;
            bool first = card == 0;
            foreach (var c in new[] { _card0, _card2 })
            {
                if (c == null || !Screen.Rects.TryGetValue(c, out var rect)) continue;
                var img = rect.GetComponent<Image>();
                bool on = (c == _card0) == first;
                if (img != null) img.color = on ? UiPaint.AcS : UiPaint.Pn;
            }
            // The 선택됨 badge follows the selected card.
            float y = first ? 218f : 218f + 121f;
            foreach (var b in new[] { _badgeBox, _badgeText })
            {
                if (b == null || !Screen.Rects.TryGetValue(b, out var rect)) continue;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -(b.r[1] - 218f + y) + (b == _badgeText ? 0f : 0f));
            }
        }

        public void Start()
        {
            if (Selected == 0) _menu.StartTraining(); else _menu.StartGame();
        }

        void OnClick(MockScreen.Item it)
        {
            if (it.id == "tut0") { Select(0); return; }
            if (it.id == "tut2") { Select(2); return; }
            if (it.id == "go") { Start(); return; }
            if (it.id == "back") { _menu.Back(); return; }
            _menu.OnMock(it);
        }
    }
}
