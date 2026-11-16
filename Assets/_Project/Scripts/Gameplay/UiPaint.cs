using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Drawing helpers for the adopted lobby (docs/research/ui_mockups): reference-stage placement in the mockup's own
    // pixel coordinates, rounded 9-slice sprites, gradients and the PNGs exported from the mockup (Resources/Ui).
    public static class UiPaint
    {
        public static readonly Color Tx = new Color32(230, 236, 242, 255);
        public static readonly Color T2 = new Color32(152, 165, 179, 255);
        public static readonly Color Ac = new Color32(106, 169, 221, 255);
        public static readonly Color AcS = new Color32(106, 169, 221, 61);   // rgba(106,169,221,.24)
        public static readonly Color Ok = new Color32(108, 207, 156, 255);
        public static readonly Color Wr = new Color32(240, 138, 118, 255);
        public static readonly Color Bg = new Color32(6, 9, 13, 255);
        public static readonly Color Sf = new Color32(13, 18, 24, 255);
        public static readonly Color Ln = new Color(1f, 1f, 1f, 0.2f);
        public static readonly Color Pn = new Color32(10, 14, 20, 184);      // rgba(10,14,20,.72)

        // A rect placed by top-left corner and size in stage pixels.
        public static RectTransform At(Transform parent, string name, float x, float y, float w, float h)
        {
            var r = UiKit.Rect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h));
            return r;
        }

        static readonly Dictionary<long, Sprite> Cache = new Dictionary<long, Sprite>();

        // Rounded rectangle, 9-sliced; ring > 0 makes only the outline of that thickness. Corner radius in stage pixels.
        public static Sprite Round(int radius, int ring = 0, bool dashed = false)
        {
            radius = Mathf.Max(1, radius);
            long key = radius * 1000L + ring * 10L + (dashed ? 1 : 0);
            if (Cache.TryGetValue(key, out var cached)) return cached;
            int size = radius * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - c) - (c - radius), qy = Mathf.Abs(y + 0.5f - c) - (c - radius);
                    float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float a = Mathf.Clamp01(0.5f - d);
                    if (ring > 0) a *= Mathf.Clamp01(d + ring + 0.5f);
                    if (dashed && (x + y) % 14 >= 8) a = 0f;
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1)); // 100 = the canvas reference PPU, so slice borders keep their pixel size
            Cache[key] = s;
            return s;
        }

        // The platform tile of the mockup: top edge 140 wide, bottom edge 172 wide, 56 high (points -70,40 70,40 86,96 -86,96).
        public static Sprite Trapezoid(bool outline, bool dashed = false)
        {
            long key = -1000 - (outline ? 10 : 0) - (dashed ? 1 : 0);
            if (Cache.TryGetValue(key, out var c)) return c;
            const int W = 172, H = 56;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    bool In(float inset)
                    {
                        float t = y / (float)(H - 1), half = 70f + 16f * t - inset * 1.1f;
                        return y >= inset && y <= H - 1 - inset && Mathf.Abs(x + 0.5f - W * 0.5f) <= half;
                    }
                    bool on = outline ? In(0f) && !In(3f) : In(0f);
                    if (on && dashed && (x + y) % 14 >= 8) on = false;
                    px[y * W + x] = new Color(1f, 1f, 1f, on ? 1f : 0f);
                }
            tex.SetPixels(px);
            tex.Apply();
            var s = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = s;
            return s;
        }

        public static Image Shape(RectTransform rect, Sprite sprite, Color color)
        {
            var img = rect.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // A rounded box (fill) with an optional outline drawn as a child ring.
        public static Image Box(RectTransform rect, Color fill, int radius, Color? ring = null, int ringWidth = 1, bool dashed = false)
        {
            var img = rect.gameObject.AddComponent<Image>();
            img.sprite = Round(radius);
            img.type = Image.Type.Sliced;
            img.color = fill;
            img.raycastTarget = false;
            if (ring.HasValue)
            {
                var r = UiKit.Fill("Ring", rect);
                var ri = r.gameObject.AddComponent<Image>();
                ri.sprite = Round(radius, ringWidth, dashed);
                ri.type = Image.Type.Sliced;
                ri.color = ring.Value;
                ri.raycastTarget = false;
            }
            return img;
        }

        public static Image Circle(Transform parent, string name, float cx, float cy, float d, Color color)
        {
            var r = UiKit.Rect(name, parent, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(cx, -cy), new Vector2(d, d));
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = Round((int)Mathf.Max(8, 32), 0);
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // Vertical gradient from `top` to `bottom` (alpha included).
        public static Image GradientOpaque(RectTransform rect, Color top, Color bottom)
        {
            var img = rect.gameObject.AddComponent<Image>();
            img.sprite = UiKit.White;
            img.raycastTarget = false;
            var g = rect.gameObject.AddComponent<VertexGradient>();
            g.Top = top; g.Bottom = bottom;
            return img;
        }

        // A texture from Resources/Ui as an Image (stage pixels).
        public static Image Pic(Transform parent, string name, string resource, float x, float y, float w, float h)
        {
            var r = At(parent, name, x, y, w, h);
            var img = r.gameObject.AddComponent<Image>();
            var tex = Resources.Load<Texture2D>("Ui/" + resource);
            if (tex != null)
            {
                tex.filterMode = FilterMode.Bilinear;
                img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 1f);
            }
            else img.color = new Color(1f, 0f, 1f, 0.3f);
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string name, float x, float y, float w, float h, string text, int size, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var t = UiKit.Label(At(parent, name, x, y, w, h), text, size, color, anchor, style);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        // A clickable rounded button: fill + ring + centred label. Returns the button; label via GetComponentInChildren<Text>.
        public static Button Btn(Transform parent, string name, float x, float y, float w, float h, string text, int size, int radius,
            Color fill, Color ring, Color textColor, UnityEngine.Events.UnityAction onClick, FontStyle style = FontStyle.Bold, int ringWidth = 1)
        {
            var r = At(parent, name, x, y, w, h);
            var img = Box(r, fill, radius, ring, ringWidth);
            img.raycastTarget = true;
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.fadeDuration = 0.05f;
            b.colors = colors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null) b.onClick.AddListener(onClick);
            UiKit.Label(UiKit.Fill("Label", r), text, size, textColor, TextAnchor.MiddleCenter, style);
            return b;
        }
    }

    // Colours the top and bottom of an Image (opaque gradient).
    [AddComponentMenu("")]
    public class VertexGradient : BaseMeshEffect
    {
        public Color Top = Color.white, Bottom = Color.black;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var v = new UIVertex();
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++) { vh.PopulateUIVertex(ref v, i); min = Mathf.Min(min, v.position.y); max = Mathf.Max(max, v.position.y); }
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float t = max > min ? (v.position.y - min) / (max - min) : 0f;
                v.color = Color.Lerp(Bottom, Top, t);
                vh.SetUIVertex(v, i);
            }
        }
    }
}
