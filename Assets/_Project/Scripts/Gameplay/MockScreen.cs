using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Draws a screen of the adopted mockup from its measured elements (tools/mock_export.js -> Resources/Ui/Screens/<name>.json):
    // boxes (fill, outline, radius, gradient, bottom bar), texts (size, weight, colour, measured box) and rasterised
    // images, all in the mockup's 1920x1080 stage pixels. Elements the mockup marks clickable become buttons that report
    // their action string; screens swap the parts that must show live data through `skip`.
    public class MockScreen
    {
        [Serializable]
        public class Item
        {
            public string k, t, src, id, act, go, al;
            public float[] r;
            public int[] bg, gt, gb, bc, barC, col;
            public float bw, fs;
            public int rad, barH, fw, lines;
            public bool btn, dis, wrap, mono;
        }

        [Serializable] class Doc { public string name; public Item[] items; }

        public RectTransform Root { get; private set; }
        public RectTransform Stage { get; private set; }
        public readonly List<Item> Items = new List<Item>();
        public readonly Dictionary<Item, RectTransform> Rects = new Dictionary<Item, RectTransform>();
        public readonly Dictionary<string, Button> ById = new Dictionary<string, Button>();

        // `skip` drops items that the caller draws itself (live data). `onAct` gets the clickable item.
        public static MockScreen Build(Transform parent, string name, Action<Item> onAct, Func<Item, bool> skip = null, bool scrimOnly = false, float dim = 0.84f)
        {
            var asset = Resources.Load<TextAsset>("Ui/Screens/" + name);
            if (asset == null) { Debug.LogError("[MockScreen] missing Ui/Screens/" + name); return null; }
            var doc = JsonUtility.FromJson<Doc>(asset.text);
            var s = new MockScreen();
            s.Root = UiKit.Fill("Mock_" + name, parent);
            // The mockup's backdrop art is replaced by the real 3D lobby behind; non-lobby screens dim it as the mockup does (.84).
            if (!scrimOnly && dim > 0f) UiKit.Box(s.Root, new Color(UiPaint.Bg.r, UiPaint.Bg.g, UiPaint.Bg.b, dim), true);
            s.Stage = UiKit.Rect("Stage", s.Root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(LobbyPanel.StageW, LobbyPanel.StageH));
            s.Root.gameObject.AddComponent<StageFit>().Stage = s.Stage;
            foreach (var item in doc.items)
            {
                if (skip != null && skip(item)) continue;
                if (item.k == "img" && item.r[2] >= 1900f && item.r[3] >= 1000f) continue; // backdrop art and scene drawing: the 3D lobby is behind
                // The mockup blurs what is behind a modal card (backdrop-filter); without blur the card is drawn near-opaque.
                if (scrimOnly && item.k == "box" && item.bg != null && item.bg.Length == 4 && item.bg[3] < 255 && item.r[2] >= 800f && item.r[2] < 1900f) item.bg[3] = 255;
                s.Add(item, onAct);
            }
            return s;
        }

        public RectTransform Add(Item it, Action<Item> onAct, RectTransform parent = null)
        {
            Items.Add(it);
            var r = UiPaint.At(parent != null ? parent : Stage, it.k + Items.Count, it.r[0], it.r[1], it.r[2], it.r[3]);
            Rects[it] = r;
            switch (it.k)
            {
                case "box": DrawBox(it, r, onAct); break;
                case "text": DrawText(it, r); break;
                case "img": DrawImg(it, r); break;
            }
            return r;
        }

        static Color C(int[] c) => c == null || c.Length < 4 ? Color.clear : new Color32((byte)c[0], (byte)c[1], (byte)c[2], (byte)c[3]);

        static void DrawBox(Item it, RectTransform r, Action<Item> onAct)
        {
            float rad = Mathf.Min(it.rad, Mathf.Min(it.r[2], it.r[3]) * 0.5f);
            Image img;
            if (it.gt != null && it.gt.Length == 4 && it.gb != null && it.gb.Length == 4)
            {
                img = UiPaint.GradientOpaque(r, C(it.gt), C(it.gb));
            }
            else
            {
                img = UiPaint.Box(r, C(it.bg), Mathf.Max(1, Mathf.RoundToInt(rad)), it.bw > 0 && it.bc != null ? C(it.bc) : (Color?)null, Mathf.Max(1, Mathf.RoundToInt(it.bw)));
            }
            // Glass panel: a dark gradient (lighter at the top) and a 1 px highlight along the top edge instead of blur.
            if (it.bg != null && it.bg.Length == 4 && it.bg[3] > 0 && it.bg[3] < 255 && it.r[2] >= 200f && it.r[3] >= 60f && rad >= 12f)
            {
                var g = r.gameObject.AddComponent<VertexGradient>();
                var bgc = C(it.bg);
                g.Top = new Color(Mathf.Min(1f, bgc.r * 1.5f + 0.02f), Mathf.Min(1f, bgc.g * 1.5f + 0.02f), Mathf.Min(1f, bgc.b * 1.5f + 0.03f), Mathf.Min(1f, bgc.a + 0.06f));
                g.Bottom = bgc;
                var hl = UiPaint.At(r, "Highlight", rad, 1f, it.r[2] - rad * 2f, 1f);
                UiKit.Box(hl, new Color(1f, 1f, 1f, 0.09f));
            }
            if (it.barH > 0 && it.barC != null)
            {
                var bar = UiPaint.At(r, "Bar", 0, it.r[3] - it.barH, it.r[2], it.barH);
                UiPaint.Box(bar, C(it.barC), 1);
            }
            if (it.btn && onAct != null)
            {
                img.raycastTarget = true;
                var b = r.gameObject.AddComponent<Button>();
                b.targetGraphic = img;
                var colors = b.colors;
                colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
                colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.7f);
                colors.fadeDuration = 0.15f;
                b.colors = colors;
                b.navigation = new Navigation { mode = Navigation.Mode.None };
                b.interactable = !it.dis;
                var captured = it;
                b.onClick.AddListener(() => onAct(captured));
            }
        }

        static void DrawText(Item it, RectTransform r)
        {
            bool bold = it.fw >= 600;
            var anchor = it.wrap ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            if (!it.wrap && it.al == "center") anchor = TextAnchor.MiddleCenter;
            if (!it.wrap && it.al == "right") anchor = TextAnchor.MiddleRight;
            // A little slack so a wider glyph set (Noto vs the mockup's system font) does not clip or re-wrap too early.
            if (!it.wrap) { r.sizeDelta = new Vector2(it.r[2] + 12f, it.r[3]); if (it.al == "center") r.anchoredPosition -= new Vector2(6f, 0f); else if (it.al == "right") r.anchoredPosition -= new Vector2(12f, 0f); }
            else r.sizeDelta = new Vector2(it.r[2] * 1.06f, it.r[3]);
            string text = it.t == "[게임 이름]" ? "SUBLEVEL" : it.t.StartsWith("v0.1.0-dev") ? "v" + Application.version : it.t; // the mockup's placeholder name
            var t = UiKit.Label(r, text, Mathf.RoundToInt(it.fs), C(it.col), anchor, bold ? FontStyle.Bold : FontStyle.Normal);
            t.horizontalOverflow = it.wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }

        static void DrawImg(Item it, RectTransform r)
        {
            var img = r.gameObject.AddComponent<Image>();
            string res = "Ui/Screens/" + System.IO.Path.GetFileNameWithoutExtension(it.src);
            var tex = Resources.Load<Texture2D>(res);
            if (tex == null) { img.color = new Color(1f, 0f, 1f, 0.3f); return; }
            tex.filterMode = FilterMode.Bilinear;
            img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            img.raycastTarget = false;
        }
    }
}
