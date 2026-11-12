using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Shared look and building blocks for the runtime-built UI (HUD, inventory, menus): dark industrial panels,
    // muted colours, one accent. Everything is created in code so no scene or prefab editing is needed.
    public static class UiKit
    {
        public static readonly Color Panel = new Color(0.07f, 0.08f, 0.09f, 0.82f);
        public static readonly Color PanelLight = new Color(0.16f, 0.18f, 0.20f, 0.9f);
        public static readonly Color Text = new Color(0.92f, 0.93f, 0.94f, 1f);
        public static readonly Color TextDim = new Color(0.62f, 0.65f, 0.68f, 1f);
        public static readonly Color Accent = new Color(0.95f, 0.62f, 0.15f, 1f);  // selection, warnings
        public static readonly Color Health = new Color(0.86f, 0.88f, 0.90f, 1f);
        public static readonly Color Danger = new Color(0.82f, 0.22f, 0.18f, 1f);

        static Font _font;
        static Sprite _white;

        // One place to swap the typeface later (an OFL font with Korean glyphs, recorded in the asset ledger).
        public static Font Font => _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Sprite White
        {
            get
            {
                if (_white == null) _white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                return _white;
            }
        }

        // A screen-space canvas scaled against 1920x1080. Higher `order` draws on top.
        public static Canvas Canvas(string name, int order, bool interactive)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            if (interactive)
            {
                go.AddComponent<GraphicRaycaster>();
                EnsureEventSystem();
            }
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        // A rect under `parent`. Anchors and pivot are in 0..1 of the parent; position and size in reference pixels.
        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        // Fills its parent, inset by `margin` on every side.
        public static RectTransform Fill(string name, Transform parent, float margin = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
            return rect;
        }

        public static Image Box(RectTransform rect, Color color, bool raycastTarget = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = White;
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        public static Text Label(RectTransform rect, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = align;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        // A clickable panel with a centred label. Highlights under the pointer.
        public static Button Button(RectTransform rect, string text, int size, UnityEngine.Events.UnityAction onClick)
        {
            var image = Box(rect, PanelLight, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.6f, 1.45f, 1.1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null) button.onClick.AddListener(onClick);
            Label(Fill("Label", rect, 8f), text, size, Text, TextAnchor.MiddleCenter);
            return button;
        }
    }
}
