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

        // "Field terminal" look of the in-game HUD: thin steel lines on dark plates (ADR-057).
        public static readonly Color Steel = new Color(0.50f, 0.58f, 0.64f, 1f);
        public static readonly Color SteelDim = new Color(0.30f, 0.36f, 0.41f, 1f);

        static Font _font;
        static Sprite _white;

        // Noto Sans KR (OFL, asset ledger F-0001) so Korean renders the same on every machine.
        static Font _bold;
        // Noto Sans KR Bold (same family and licence as the Regular, ledger F-0002): headings and emphasis use the real bold
        // cut instead of a synthesised one.
        public static Font BoldFont
        {
            get
            {
                if (_bold == null) _bold = Resources.Load<Font>("Fonts/NotoSansKRBold");
                return _bold != null ? _bold : Font;
            }
        }

        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.Load<Font>("Fonts/NotoSansKR");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

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
            bool bold = style == FontStyle.Bold || style == FontStyle.BoldAndItalic;
            label.font = bold ? BoldFont : Font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = bold && BoldFont != Font ? FontStyle.Normal : style; // the real bold cut replaces the synthetic one
            label.lineSpacing = 1.05f;
            label.color = color;
            label.alignment = align;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        // A thin outline just inside `parent`: top, bottom, left, right.
        public static Image[] Frame(RectTransform parent, float thickness, Color color)
        {
            var lines = new Image[4];
            lines[0] = Box(Edge("Top", parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, thickness)), color);
            lines[1] = Box(Edge("Bottom", parent, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, thickness)), color);
            lines[2] = Box(Edge("Left", parent, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(thickness, 0f)), color);
            lines[3] = Box(Edge("Right", parent, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(thickness, 0f)), color);
            return lines;
        }

        static RectTransform Edge(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            return rect;
        }

        // Four corner brackets (two arms each, `length` long) on `parent`; returns the eight arms.
        public static Image[] Brackets(RectTransform parent, float length, float thickness, Color color)
        {
            var arms = new Image[8];
            int n = 0;
            for (int cy = 0; cy < 2; cy++)
            {
                for (int cx = 0; cx < 2; cx++)
                {
                    var corner = new Vector2(cx, cy);
                    arms[n++] = Box(Rect("BracketH", parent, corner, corner, Vector2.zero, new Vector2(length, thickness)), color);
                    arms[n++] = Box(Rect("BracketV", parent, corner, corner, Vector2.zero, new Vector2(thickness, length)), color);
                }
            }
            return arms;
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

        // ---- Menu layout parts (shared by the main menu, the pause menu and the settings panel) ----

        public const float MenuPadding = 28f;
        public const float MenuButtonHeight = 56f;
        public const float MenuButtonGap = 12f;

        // A centred column panel; the caller sets its height once the content is laid out.
        public static RectTransform CenteredPanel(string name, Transform parent, float width)
        {
            var panel = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 100f));
            Box(panel, Panel, true);
            return panel;
        }

        // A row hung from the top of a panel, `y` reference pixels down, `x` from its centre.
        public static RectTransform HangRow(string name, Transform panel, float width, float height, float y, float x = 0f) =>
            Rect(name, panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, -y), new Vector2(width, height));

        // A full-width menu button; returns the y below it.
        public static float MenuButton(Transform panel, string text, float panelWidth, float y, UnityEngine.Events.UnityAction onClick)
        {
            Button(HangRow(text, panel, panelWidth - MenuPadding * 2f, MenuButtonHeight, y), text, 24, onClick);
            return y + MenuButtonHeight + MenuButtonGap;
        }

        // Caption on the left, current value on the right, slider underneath.
        public static Slider SliderRow(Transform panel, string caption, float width, ref float y, float min, float max, float value, out Text valueLabel)
        {
            var header = HangRow(caption, panel, width, 26f, y);
            Label(Fill("Caption", header), caption, 18, TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            valueLabel = Label(Fill("Value", header), "", 18, Text, TextAnchor.MiddleRight);
            y += 30f;
            var slider = BuildSlider(HangRow(caption + " Slider", panel, width, 22f, y), min, max, value);
            y += 22f + 24f;
            return slider;
        }

        // A plain uGUI slider from kit boxes: track, accent fill and a block handle.
        public static Slider BuildSlider(RectTransform rect, float min, float max, float value)
        {
            const float handleWidth = 14f;
            var track = Fill("Track", rect);
            track.offsetMin = new Vector2(0f, 6f);
            track.offsetMax = new Vector2(0f, -6f);
            Box(track, PanelLight, true);

            var fillArea = Fill("Fill Area", rect);
            fillArea.offsetMin = new Vector2(0f, 6f);
            fillArea.offsetMax = new Vector2(0f, -6f);
            var fill = Fill("Fill", fillArea);
            Box(fill, Accent);

            var handleArea = Fill("Handle Area", rect);
            handleArea.offsetMin = new Vector2(handleWidth * 0.5f, 0f);
            handleArea.offsetMax = new Vector2(-handleWidth * 0.5f, 0f);
            var handle = Fill("Handle", handleArea);
            handle.sizeDelta = new Vector2(handleWidth, 0f);
            var handleImage = Box(handle, Text, true);

            var slider = rect.gameObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.targetGraphic = handleImage;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.wholeNumbers = false;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(value);
            return slider;
        }

        public static readonly Color Selected = new Color(0.55f, 0.36f, 0.08f, 0.95f);

        // Marks a button made by Button() as the chosen option of a group.
        public static void SetSelected(Button button, bool selected)
        {
            if (button != null && button.targetGraphic != null) button.targetGraphic.color = selected ? Selected : PanelLight;
        }

        // ---- Full-screen form parts (the settings screen) ----

        // Nearly opaque plate for screens that cover the whole view.
        public static readonly Color PanelDeep = new Color(0.045f, 0.052f, 0.06f, 0.96f);
        // Faint row highlight / zebra band.
        public static readonly Color RowHover = new Color(0.50f, 0.58f, 0.64f, 0.14f);
        public static readonly Color RowBand = new Color(1f, 1f, 1f, 0.025f);

        // A flat tab: label over a transparent hit area and a thin accent underline shown while selected (SetTab).
        public static Button TabButton(RectTransform rect, string text, int size, UnityEngine.Events.UnityAction onClick, out Text label, out Image underline)
        {
            // White plate tinted by the button's colour block (normal = invisible), so hover still shows.
            var image = Box(rect, Color.white, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            // Not fully transparent: a mesh culled for zero alpha might stop taking the pointer.
            colors.normalColor = new Color(1f, 1f, 1f, 0.02f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.06f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.1f);
            colors.selectedColor = colors.normalColor;
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null) button.onClick.AddListener(onClick);
            label = Label(Fill("Label", rect, 4f), text, size, TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            underline = Box(Rect("Underline", rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 3f)), Accent);
            var line = (RectTransform)underline.transform;
            line.anchorMin = new Vector2(0.12f, 0f);
            line.anchorMax = new Vector2(0.88f, 0f);
            line.sizeDelta = new Vector2(0f, 3f);
            underline.enabled = false;
            return button;
        }

        public static void SetTab(Text label, Image underline, bool selected)
        {
            if (label != null) label.color = selected ? Text : TextDim;
            if (underline != null) underline.enabled = selected;
        }

        // A small steel caption with a hairline running to the right edge of `rect`.
        public static Text SectionHeader(RectTransform rect, string text)
        {
            var label = Label(Fill("Caption", rect), text, 17, Steel, TextAnchor.MiddleLeft, FontStyle.Bold);
            var line = Rect("Line", rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 1f));
            line.anchorMin = new Vector2(0f, 0.5f);
            line.anchorMax = new Vector2(1f, 0.5f);
            line.offsetMin = new Vector2(label.preferredWidth + 14f, -0.5f);
            line.offsetMax = new Vector2(0f, 0.5f);
            Box(line, SteelDim);
            return label;
        }

        // Wrapping body text, top-left aligned inside `rect`.
        public static Text Paragraph(RectTransform rect, string text, int size, Color color)
        {
            var label = Label(rect, text, size, color, TextAnchor.UpperLeft);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.lineSpacing = 1.15f;
            return label;
        }
    }
}
