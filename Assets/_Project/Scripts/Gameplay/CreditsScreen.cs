using System;
using Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // 크레딧 (#89), drawn from the adopted mockup's credits screen (Resources/Ui/Screens/credits.json): the top bar, the
    // studio and licence cards and the table header come from the mockup; the table rows are Credits.Entries in the
    // mockup's row geometry (title + link, author, use, licence badge), scrollable with the mouse wheel.
    public class CreditsScreen
    {
        const float TableX = 620f, TableY = 290f, TableW = 1236f, TableH = 700f, RowPitch = 72.8f;

        public MockScreen Screen { get; private set; }
        public int RowCount { get; private set; }

        public RectTransform Build(Transform parent, Action<MockScreen.Item> onAct)
        {
            Screen = MockScreen.Build(parent, "credits", onAct,
                it => it.r[1] >= TableY && it.r[0] >= TableX);                // rows are drawn from Credits.Entries below
            var asset = Resources.Load<TextAsset>("Ui/Screens/credits");
            var doc = JsonUtility.FromJson<Doc>(asset.text);
            var template = new System.Collections.Generic.List<MockScreen.Item>();
            foreach (var it in doc.items) if (it.r[1] >= TableY && it.r[0] >= TableX) template.Add(it);
            // The first six items are one row: title, link, author, use, badge box, badge text.
            float y0 = template[0].r[1];
            var view = UiPaint.At(Screen.Stage, "Rows", TableX, TableY, TableW, TableH);
            view.gameObject.AddComponent<RectMask2D>();
            var content = UiKit.Rect("Content", view, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(TableW, Credits.Entries.Length * RowPitch + 40f));
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            UiKit.Box(view, Color.clear, true); // catches the wheel over empty space too
            for (int i = 0; i < Credits.Entries.Length; i++)
            {
                var e = Credits.Entries[i];
                float dy = i * RowPitch;
                for (int k = 0; k < 6; k++)
                {
                    var t = template[k];
                    var it = new MockScreen.Item { k = t.k, r = new[] { t.r[0] - TableX, t.r[1] - TableY + dy - (y0 - TableY - 12f), t.r[2], t.r[3] }, bg = t.bg, bw = t.bw, bc = t.bc, rad = t.rad, fs = t.fs, fw = t.fw, col = t.col, al = t.al, wrap = false, t = t.t };
                    switch (k)
                    {
                        case 0: it.t = e.Title; break;
                        case 1: it.t = e.Url.Replace("https://", ""); break;
                        case 2: it.t = e.Author; break;
                        case 3: it.t = e.Use; break;
                        case 5: it.t = e.Licence; break;
                    }
                    var rect = Screen.Add(it, null, content);
                    if (k == 1) { var tx = rect.GetComponent<Text>(); tx.resizeTextForBestFit = true; tx.resizeTextMinSize = 10; tx.resizeTextMaxSize = 15; rect.sizeDelta = new Vector2(587f, it.r[3]); } // long links shrink to their column, as the mockup clips them
                    if (k == 5) { float w = rect.GetComponent<Text>().preferredWidth + 22f; _lastBadgeW = w; }
                    if (k == 4) _badge = rect;
                    if (k == 5 && _badge != null) _badge.sizeDelta = new Vector2(_lastBadgeW, _badge.sizeDelta.y);
                }
            }
            RowCount = Credits.Entries.Length;
            return Screen.Root;
        }

        RectTransform _badge;
        float _lastBadgeW;

        [Serializable] class Doc { public MockScreen.Item[] items; }
    }
}
