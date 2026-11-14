using Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // A wheel of equal parts the mouse picks from (ADR-130 marker wheel, ADR-131 emote wheel): held key opens it, the
    // mouse moves a cursor from the centre (the view stops turning meanwhile, PlayerController feeds the look into
    // it), letting go takes the part under the cursor. Our own layout: chips on a ring, the chosen one lit in its colour.
    public sealed class RadialWheel
    {
        public const float Radius = 168f, ChipWidth = 134f, ChipHeight = 46f, DeadZone = 26f, CursorMax = 120f, CursorGain = 7f;

        static RadialWheel _open;
        public static bool AnyOpen => _open != null;
        // Mouse look while a wheel is open.
        public static void FeedOpen(Vector2 look) => _open?.Feed(look);

        public bool IsOpen { get; private set; }
        // The part under the cursor, -1 inside the dead zone.
        public int Selected { get; private set; } = -1;
        public int Count => _names.Length;
        public Vector2 Cursor { get; private set; }
        public string CentreText => _centre != null ? _centre.text : "";

        GameObject _root;
        Image[] _chips;
        Text[] _texts;
        Text _centre;
        RectTransform _dot;
        string[] _names;
        Color[] _colours;
        string _title;
        int _shown = -2;

        public static RadialWheel Build(Transform parent, string name, string title, string[] names, Color[] colours)
        {
            var w = new RadialWheel { _names = names, _colours = colours, _title = title };
            var centre = new Vector2(0.5f, 0.5f);
            var root = UiKit.Rect(name, parent, centre, centre, Vector2.zero, new Vector2(Radius * 2f + ChipWidth + 40f, Radius * 2f + ChipHeight + 40f));
            w._root = root.gameObject;
            w._centre = UiKit.Label(UiKit.Rect("Centre", root, centre, centre, new Vector2(0f, 30f), new Vector2(240f, 34f)), title, 24, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            w._chips = new Image[names.Length];
            w._texts = new Text[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                float a = i * 2f * Mathf.PI / names.Length;
                var pos = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * Radius;
                var chip = UiKit.Rect("Part" + i, root, centre, centre, pos, new Vector2(ChipWidth, ChipHeight));
                w._chips[i] = UiKit.Box(chip, UiKit.Panel);
                UiKit.Box(UiKit.Rect("Stripe", chip, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(6f, ChipHeight)), colours[i]);
                w._texts[i] = UiKit.Label(UiKit.Fill("Text", chip), names[i], 22, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            }
            w._dot = UiKit.Rect("Cursor", root, centre, centre, Vector2.zero, new Vector2(12f, 12f));
            UiKit.Box(w._dot, UiKit.Text);
            w._root.SetActive(false);
            return w;
        }

        public void Show()
        {
            if (IsOpen) return;
            IsOpen = true;
            _open = this;
            Cursor = Vector2.zero;
            Selected = -1;
            _shown = -2;
            _dot.anchoredPosition = Vector2.zero;
            Refresh();
            _root.SetActive(true);
        }

        public void Hide()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (_open == this) _open = null;
            _root.SetActive(false);
        }

        // Test and script input: puts the cursor on part `index` as the mouse would.
        public void Point(int index)
        {
            float a = index * 2f * Mathf.PI / Count;
            Cursor = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (CursorMax * 0.8f);
            Selected = Pings.SegmentAt(Cursor.x, Cursor.y, Count, DeadZone);
            _dot.anchoredPosition = Cursor;
            Refresh();
        }

        void Feed(Vector2 look)
        {
            Cursor = Vector2.ClampMagnitude(Cursor + look * CursorGain, CursorMax);
            _dot.anchoredPosition = Cursor;
            Selected = Pings.SegmentAt(Cursor.x, Cursor.y, Count, DeadZone);
            Refresh();
        }

        void Refresh()
        {
            if (_shown == Selected) return;
            _shown = Selected;
            for (int i = 0; i < _chips.Length; i++)
            {
                bool on = i == Selected;
                _chips[i].color = on ? _colours[i] : UiKit.Panel;
                _texts[i].color = on ? new Color(0.04f, 0.05f, 0.06f, 1f) : UiKit.Text;
                _chips[i].rectTransform.localScale = on ? new Vector3(1.12f, 1.12f, 1f) : Vector3.one;
            }
            _centre.text = Selected >= 0 ? _names[Selected] : _title;
        }
    }
}
