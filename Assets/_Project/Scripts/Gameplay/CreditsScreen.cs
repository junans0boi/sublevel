using Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The main menu's 크레딧 panel (#89): the studio, then every asset that asks for credit (Credits.Entries) with its
    // title, author, licence and source link, and a 뒤로 가기 button.
    public class CreditsScreen
    {
        const float Width = 1180f;
        const float RowHeight = 54f;

        public RectTransform Build(Transform parent, UnityEngine.Events.UnityAction back)
        {
            var panel = UiKit.CenteredPanel("Credits", parent, Width);
            float y = UiKit.MenuPadding;
            var title = UiKit.HangRow("Title", panel, Width - UiKit.MenuPadding * 2f, 48f, y);
            UiKit.Label(title, "크레딧", 34, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 56f;
            var studio = UiKit.HangRow("Studio", panel, Width - UiKit.MenuPadding * 2f, 30f, y);
            UiKit.Label(studio, "SUBLEVEL — STEADY TO VIVID STUDIO", 20, UiKit.Accent);
            y += 40f;
            var note = UiKit.HangRow("Note", panel, Width - UiKit.MenuPadding * 2f, 28f, y);
            UiKit.Label(note, "아래 에셋은 크리에이티브 커먼즈 저작자표시 4.0(CC BY 4.0, https://creativecommons.org/licenses/by/4.0/)으로 사용합니다.", 17, UiKit.TextDim);
            y += 40f;
            foreach (var e in Credits.Entries)
            {
                var row = UiKit.HangRow("Entry", panel, Width - UiKit.MenuPadding * 2f, RowHeight, y);
                var line = UiKit.Rect("Line", row, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(Width - UiKit.MenuPadding * 2f, 28f));
                UiKit.Label(line, Credits.Line(e), 20, UiKit.Text);
                var link = UiKit.Rect("Link", row, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -27f), new Vector2(Width - UiKit.MenuPadding * 2f, 24f));
                UiKit.Label(link, e.Url, 15, UiKit.TextDim);
                y += RowHeight + 4f;
            }
            y += 12f;
            UiKit.Button(UiKit.HangRow("Back", panel, 260f, UiKit.MenuButtonHeight, y), "뒤로 가기", 24, back);
            y += UiKit.MenuButtonHeight;
            panel.sizeDelta = new Vector2(Width, y + UiKit.MenuPadding);
            return panel;
        }
    }
}
