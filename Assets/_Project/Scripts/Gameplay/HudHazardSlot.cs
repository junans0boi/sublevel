using Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The HUD's one extension point for pollution warning, keys and chip (ADR-246). Self-contained: Hud only builds it
    // and exposes it; the pollution code calls Set(view). Hidden while there is nothing to show; text changes only on change.
    public class HudHazardSlot
    {
        public const float Width = 460f, Height = 64f, ItemsOnlyHeight = 34f;
        public float CurrentHeight => _root != null ? _root.sizeDelta.y : 0f;

        RectTransform _root;
        Text _line, _items;
        HudHazardView _shown;
        bool _has;

        public RectTransform Root => _root;
        public string LineText => _line != null ? _line.text : "";
        public string ItemsText => _items != null ? _items.text : "";
        public bool Visible => _root != null && _root.gameObject.activeSelf;

        public static HudHazardSlot Build(Transform parent, Vector2 anchor, Vector2 position)
        {
            var s = new HudHazardSlot();
            s._root = UiKit.Rect("HazardSlot", parent, anchor, anchor, position, new Vector2(Width, Height));
            UiKit.Box(s._root, UiKit.Panel);
            UiKit.Frame(s._root, 1f, UiKit.SteelDim);
            s._line = UiKit.Label(UiKit.Rect("Line", s._root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(Width - 16f, 32f)),
                "", 22, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            s._items = UiKit.Label(UiKit.Rect("Items", s._root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(Width - 16f, 26f)),
                "", 20, UiKit.Text, TextAnchor.MiddleCenter);
            s._root.gameObject.SetActive(false);
            return s;
        }

        public void Set(HudHazardView v)
        {
            if (_has && v.Equals(_shown)) return;
            _has = true; _shown = v;
            _line.text = v.Line();
            _items.text = v.Items();
            _line.color = v.Warn == HazardWarn.Sealed || v.Warn == HazardWarn.Imminent ? UiKit.Danger : UiKit.Accent;
            _root.gameObject.SetActive(_line.text.Length > 0 || _items.text.Length > 0);
            // One of the two rows only (keys without a warning, a warning without keys): a short box, no empty half.
            _root.sizeDelta = new Vector2(Width, _line.text.Length > 0 && _items.text.Length > 0 ? Height : ItemsOnlyHeight);
        }
    }
}
