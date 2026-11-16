using System;
using Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // 커스텀 모드 (adopted mockup, Resources/Ui/Screens/custom.json): friends' room rules, 방 만들기 and 코드로 참가.
    // Chrome and geometry are the measured mockup; the three dropdowns cycle on click, the 시점 switch toggles, 방 만들기
    // opens the hosted room (LobbyScreen) and 참가하기 joins the host address typed as the invite code (IP:포트).
    public class CustomScreen
    {
        static readonly string[][] Options =
        {
            new[] { "50명", "20명" }, new[] { "부족한 인원만", "없음" }, new[] { "기본", "느림", "빠름" },
        };
        static readonly float[] DropY = { 301.6f, 369.6f, 437.6f };

        public MockScreen Screen { get; private set; }
        public int[] Choice { get; } = new int[3];
        public bool FirstPerson { get; private set; }
        public string Code => _input != null ? _input.text : "";
        public Text[] DropText { get; } = new Text[3];

        InputField _input;
        Image _tppFill, _fppFill;
        RectTransform _tppBar;

        public RectTransform Build(Transform parent, MainMenu menu)
        {
            Screen = MockScreen.Build(parent, "custom", it => OnClick(it, menu));
            foreach (var kv in Screen.Rects)
            {
                var it = kv.Key; var r = kv.Value;
                if (it.k == "box" && it.r[2] == 300f && it.r[3] > 47f && it.r[3] < 49f)
                {
                    int d = Array.FindIndex(DropY, y => Mathf.Abs(y - it.r[1]) < 1f);
                    if (d < 0) continue;
                    var dropImg = r.GetComponent<Image>() ?? r.gameObject.AddComponent<Image>();
                    dropImg.raycastTarget = true;
                    var b = r.gameObject.AddComponent<Button>();
                    int idx = d;
                    b.onClick.AddListener(() => Cycle(idx));
                    DropText[d] = UiPaint.Label(r, "Value", 16, 0, 240, 48, Options[d][0], 20, UiPaint.Tx, TextAnchor.MiddleLeft);
                    UiPaint.Label(r, "Chev", 266, 0, 24, 48, "▾", 18, UiPaint.T2, TextAnchor.MiddleCenter);
                }
                else if (it.k == "box" && it.id == "cc")
                {
                    var img = r.GetComponent<Image>();
                    img.raycastTarget = true;
                    var text = UiPaint.Label(r, "Text", 16, 0, 538, 48, "", 20, UiPaint.Tx, TextAnchor.MiddleLeft);
                    _input = r.gameObject.AddComponent<InputField>();
                    _input.targetGraphic = img;
                    _input.textComponent = text;
                    _input.characterLimit = 64;
                    _input.text = "127.0.0.1:" + Game.Net.LobbyNet.DefaultPort;
                }
                else if (it.k == "box" && Mathf.Abs(it.r[1] - 505.6f) < 1f && Mathf.Abs(it.r[3] - 48f) < 1f)
                {
                    bool first = it.r[0] < 400f;
                    var segImg = r.GetComponent<Image>() ?? r.gameObject.AddComponent<Image>();
                    segImg.raycastTarget = true;
                    var b = r.GetComponent<Button>() ?? r.gameObject.AddComponent<Button>();
                    bool fp = !first;
                    b.onClick.AddListener(() => SetView(fp));
                    if (first) { _tppFill = segImg; _tppBar = r.Find("Bar") as RectTransform; }
                    else _fppFill = segImg;
                }
            }
            return Screen.Root;
        }

        void Cycle(int d)
        {
            Choice[d] = (Choice[d] + 1) % Options[d].Length;
            DropText[d].text = Options[d][Choice[d]];
        }

        public void SetView(bool firstPerson)
        {
            FirstPerson = firstPerson;
            _tppFill.color = firstPerson ? Color.clear : UiPaint.AcS;
            _fppFill.color = firstPerson ? UiPaint.AcS : Color.clear;
            if (_tppBar != null) _tppBar.gameObject.SetActive(!firstPerson);
        }

        void OnClick(MockScreen.Item it, MainMenu menu)
        {
            switch (it.id)
            {
                case "mk": menu.OpenHost(); break;
                case "jn":
                    menu.OpenJoin();
                    menu.Lobby.SetAddress(Code);
                    menu.Lobby.Join();
                    break;
                case "subn": menu.Back(); break;
                default: menu.OnMock(it); break;
            }
        }
    }
}
