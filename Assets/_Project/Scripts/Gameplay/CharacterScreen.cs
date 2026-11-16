using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // 외형 (adopted mockup, Resources/Ui/Screens/char.json): the 24-slot roster, colour variants and the preview.
    // Geometry and chrome are the measured mockup; selection, colour and 적용/취소 act on it. The choice is kept in
    // PlayerPrefs ("ui.char.index", "ui.char.color") for the character track to read; locked slots (13..24) say so.
    public class CharacterScreen
    {
        public const string IndexKey = "ui.char.index", ColorKey = "ui.char.color";
        public const int Open = 12, Total = 24;
        static readonly string[] ColorNames = { "먹빛", "올리브", "황토", "청회색", "적갈색", "설백" };

        public MockScreen Screen { get; private set; }
        public int Index { get; private set; }
        public int ColorIndex { get; private set; }
        public string HeaderText => _header != null ? _header.text : "";
        public string NameText => _name != null ? _name.text : "";
        public string ColorText => _colorLabel != null ? _colorLabel.text : "";

        MainMenu _menu;
        Text _header, _name, _colorLabel, _check;
        Image _preview;
        readonly List<Cell> _cells = new List<Cell>();
        readonly List<Image> _colorRings = new List<Image>();
        readonly Sprite[] _previews = new Sprite[6];
        int _savedIndex, _savedColor;

        class Cell { public Image Fill, Ring; public RectTransform Rect; public string Name; public float X, Y; }

        public RectTransform Build(Transform parent, MainMenu menu)
        {
            _menu = menu;
            Screen = MockScreen.Build(parent, "char", it => OnClick(it));
            // Find the cells (ro0..ro23), the colour buttons and the right-hand texts from the measured items.
            MockScreen.Item lastCell = null;
            foreach (var kv in Screen.Rects)
            {
                var it = kv.Key; var r = kv.Value;
                if (it.k == "box" && it.id != null && it.id.StartsWith("ro"))
                {
                    var c = new Cell { Rect = r, Fill = r.GetComponent<Image>(), X = it.r[0], Y = it.r[1] };
                    c.Ring = r.Find("Ring") != null ? r.Find("Ring").GetComponent<Image>() : null;
                    _cells.Add(c); lastCell = it;
                }
                else if (it.k == "text" && lastCell != null && it.r[0] >= 48f && it.r[0] < 600f && it.fs == 16f && it.r[1] > 150f)
                {
                    if (_cells.Count > 0) _cells[_cells.Count - 1].Name = it.t; // the last 16px text of a cell is its name
                }
                else if (it.k == "text" && it.t != null && it.t.StartsWith("외형 ") && it.r[0] > 1300f) _header = r.GetComponent<Text>();
                else if (it.k == "text" && it.fs == 21f && it.r[0] > 1300f && it.r[1] > 150f) _name = r.GetComponent<Text>();
                else if (it.k == "text" && it.t != null && it.t.StartsWith("색 변형") ) _colorLabel = r.GetComponent<Text>();
                else if (it.k == "box" && it.id != null && it.id.StartsWith("col")) { }
                else if (it.k == "box" && it.r[2] == 54f && it.r[3] == 54f) _colorRings.Add(r.GetComponent<Image>());
                else if (it.k == "text" && it.t == "✓" && it.r[0] < 600f) _check = r.GetComponent<Text>();
                else if (it.k == "img" && Mathf.Abs(it.r[0] - 830f) < 2f && Mathf.Abs(it.r[2] - 260f) < 2f) _preview = r.GetComponent<Image>();
            }
            for (int c = 0; c < 6; c++)
            {
                var tex = Resources.Load<Texture2D>("Ui/Screens/char_preview" + c);
                if (tex != null) _previews[c] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            _savedIndex = Mathf.Clamp(PlayerPrefs.GetInt(IndexKey, 5), 0, Open - 1);
            _savedColor = Mathf.Clamp(PlayerPrefs.GetInt(ColorKey, 1), 0, 5);
            Select(_savedIndex); SetColor(_savedColor);
            return Screen.Root;
        }

        public void Reload()
        {
            _savedIndex = Mathf.Clamp(PlayerPrefs.GetInt(IndexKey, 5), 0, Open - 1);
            _savedColor = Mathf.Clamp(PlayerPrefs.GetInt(ColorKey, 1), 0, 5);
            Select(_savedIndex); SetColor(_savedColor);
        }

        public void Select(int i)
        {
            if (i < 0 || i >= Open) return;
            Index = i;
            for (int k = 0; k < _cells.Count && k < Total; k++)
            {
                bool on = k == i;
                if (_cells[k].Fill != null) _cells[k].Fill.color = on ? UiPaint.AcS : UiPaint.Bg;
                if (_cells[k].Ring != null) { _cells[k].Ring.color = on ? UiPaint.Ac : UiPaint.Ln; _cells[k].Ring.sprite = UiPaint.Round(16, on ? 2 : 1); }
            }
            if (_check != null)
            {
                var rt = (RectTransform)_check.transform;
                rt.anchoredPosition = new Vector2(_cells[i].X + 47f, -(_cells[i].Y + 32f));
            }
            if (_header != null) _header.text = "외형 " + (i + 1).ToString("00") + " / 24";
            if (_name != null && _cells[i].Name != null) _name.text = _cells[i].Name;
        }

        public void SetColor(int c)
        {
            if (c < 0 || c > 5) return;
            ColorIndex = c;
            for (int k = 0; k < _colorRings.Count && k < 6; k++)
            {
                var ring = _colorRings[k].transform.Find("Ring");
                if (ring != null) ring.GetComponent<Image>().color = k == c ? UiPaint.Tx : UiPaint.Ln;
            }
            if (_colorLabel != null) _colorLabel.text = "색 변형 · " + ColorNames[c];
            if (_preview != null && _previews[c] != null) _preview.sprite = _previews[c];
        }

        public void Apply()
        {
            PlayerPrefs.SetInt(IndexKey, Index);
            PlayerPrefs.SetInt(ColorKey, ColorIndex);
            PlayerPrefs.Save();
            _savedIndex = Index; _savedColor = ColorIndex;
            _menu.Toast("외형을 적용했습니다.");
            _menu.Back();
        }

        public void Cancel()
        {
            Select(_savedIndex); SetColor(_savedColor);
            _menu.Back();
        }

        public void Step(int delta)
        {
            int i = Mathf.Clamp(Index + delta, 0, Open - 1);
            Select(i);
        }

        void OnClick(MockScreen.Item it)
        {
            string a = !string.IsNullOrEmpty(it.go) ? it.go : it.act;
            if (a != null && a.StartsWith("ch:")) { Select(int.Parse(a.Substring(3))); return; }
            if (a != null && a.StartsWith("col:")) { SetColor(int.Parse(a.Substring(4))); return; }
            if (it.id == "apply") { Apply(); return; }
            if (it.id == "cancel") { Cancel(); return; }
            if (it.id == "topchar") return; // already here
            _menu.OnMock(it);
        }

        public void Tick()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.leftArrowKey.wasPressedThisFrame) Step(-1);
            if (kb.rightArrowKey.wasPressedThisFrame) Step(1);
            if (kb.enterKey.wasPressedThisFrame) Apply();
            if (kb.escapeKey.wasPressedThisFrame) Cancel();
        }
    }
}
