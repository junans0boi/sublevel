using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The race's results dialog (#516, spec #505 story 38, ADR-562), after the approved mockup docs/research/ui_mockups/race_room.html
    // (resultHTML: a 560 px dialog at 1.5x over a dimmed screen): "결과 · course", a row per car (rank, name, the winner's time or the gap
    // to it, best lap or a sprint's average speed; my row blue) and 방으로 돌아가기 (Enter). RaceHudView fills and shows it and leaves the
    // match when it is pressed or the results time runs out; the menu then opens the room again (RaceLobbyScreen.ReturnPending).
    public sealed class RaceResultsView : MonoBehaviour
    {
        public static bool Shown { get; private set; }   // the mouse is a pointer while it is up (PlayerController)
        public bool BackPressed { get; private set; }
        public Button BackButton { get; private set; }
        public int RowCount { get; private set; }
        public string Title => _title.text;

        const int MaxRows = 12;
        const float W = 840f, Head = 72f, Pad = 24f, RowH = 42f, Foot = 82f;
        static readonly Color Line = H("#2f3a48"), Txt = H("#e9edf1"), Dim = H("#9aa5b1"), Acc = H("#f29e26"), AccInk = H("#2b1a02"), MeRow = H("#1f6fd1");

        GameObject _root;
        RectTransform _dialog, _foot;
        Text _title, _auto, _backLabel;
        readonly Image[] _rowBg = new Image[MaxRows];
        readonly Text[,] _cells = new Text[MaxRows, 4];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Shown = false;

        static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        void Awake()
        {
            var canvas = UiKit.Canvas("RaceResultsCanvas", 45, true);
            canvas.transform.SetParent(transform, false);
            _root = canvas.gameObject;
            var ov = UiKit.Fill("Dim", canvas.transform).gameObject.AddComponent<Image>();
            ov.color = new Color(5 / 255f, 7 / 255f, 10 / 255f, 0.66f);   // blocks clicks to anything under it
            _dialog = UiKit.Rect("Dialog", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, 400f));
            UiPaint.Box(_dialog, H("#141a22"), 15, Line);
            _title = UiPaint.Label(_dialog, "Title", 27, 0, W - 54, Head, "", 28, Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            Rule(_dialog, "HeadLine", Head);
            for (int i = 0; i < MaxRows; i++)
            {
                var row = UiPaint.At(_dialog, "Row" + i, 27, Head + Pad + i * RowH, W - 54, RowH);
                _rowBg[i] = UiPaint.Box(row, MeRow, 6);
                float[] x = { 15, 54, 420, 600 }, w = { 39, 360, 150, 156 };
                for (int c = 0; c < 4; c++)
                    _cells[i, c] = UiPaint.Label(row, "C" + c, x[c], 0, w[c], RowH, "", 22, Txt, c >= 2 ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, c == 0 ? FontStyle.Bold : FontStyle.Normal);
            }
            _foot = UiPaint.At(_dialog, "Foot", 0, 0, W, Foot);
            Rule(_foot, "FootLine", 0);
            _auto = UiPaint.Label(_foot, "Auto", 27, 0, 420, Foot, "", 20, Dim);
            BackButton = UiPaint.Btn(_foot, "Back", W - 27 - 270, 14, 270, 54, "", 21, 9, Acc, Acc, AccInk, Back);
            _backLabel = BackButton.GetComponentInChildren<Text>();
            _root.SetActive(false);
        }

        static void Rule(RectTransform parent, string name, float y)
        {
            var r = UiPaint.At(parent, name, 0, y, W, 1.5f);
            var img = r.gameObject.AddComponent<Image>(); img.color = Line; img.raycastTarget = false;
        }

        // `sprint`: the sprint's length (the last column is the average speed instead of the best lap), 0 on a circuit.
        public void Fill(RaceStandings m, int me, string course, float sprint)
        {
            _title.text = string.IsNullOrEmpty(course) ? "결과" : "결과 · " + course;
            RowCount = Mathf.Min(m.Keys.Length, MaxRows);
            for (int i = 0; i < MaxRows; i++)
            {
                bool on = i < RowCount;
                _rowBg[i].gameObject.SetActive(on);
                if (!on) continue;
                _rowBg[i].enabled = i == me;
                string time = !m.Done[i] ? "DNF" : i == 0 || !m.Done[0] ? RaceBoard.Time(m.Times[i])
                    : "+" + (m.Times[i] - m.Times[0]).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                string last = sprint > 0f ? (m.Done[i] ? RaceEvent.AverageKmh(sprint, m.Times[i]) : "--") : m.Best[i] > 0f ? "베스트 " + RaceBoard.Time(m.Best[i]) : "--";
                _cells[i, 0].text = (i + 1).ToString();
                _cells[i, 1].text = m.Names[i];
                _cells[i, 2].text = time;
                _cells[i, 3].text = last;
                for (int c = 0; c < 4; c++) _cells[i, c].color = i == me || c < 2 ? Txt : Dim;   // the mockup's dim gap, white on my row
            }
            float h = Head + Pad + RowCount * RowH + Pad;
            _foot.anchoredPosition = new Vector2(0f, -h);
            _dialog.sizeDelta = new Vector2(W, h + Foot);
        }

        // Shown each frame while the race is over; `secondsLeft` until it goes back by itself.
        public void Tick(bool show, float secondsLeft)
        {
            if (_root.activeSelf != show) _root.SetActive(show);
            Shown = show;
            if (!show) return;
            if (!BackPressed)
            {
                _backLabel.text = RaceLobbyScreen.ReturnPending ? "방으로 돌아가기" : "메뉴로 돌아가기";
                _auto.text = $"{Mathf.CeilToInt(Mathf.Max(0f, secondsLeft))}초 뒤 자동으로 돌아갑니다";
            }
            var k = Keyboard.current;
            if (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) Back();
        }

        public void Back()
        {
            if (BackPressed) return;
            BackPressed = true;
            BackButton.interactable = false;
            _backLabel.text = "돌아가는 중…";
            _auto.text = "";
        }

        void OnDestroy() => Shown = false;
    }
}
