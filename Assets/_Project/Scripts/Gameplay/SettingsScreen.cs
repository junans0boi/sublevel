using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // 환경 설정 (ADR-059): top tabs 그래픽 / 오디오 / 컨트롤 / 키 설정 / 게임 플레이, 기본 / 고급 inside 그래픽, a scrolling list
    // of rows (label left, control right) under small section headers, a description panel on the right for the
    // hovered row, and 기본 / 적용 / 뒤로 가기 at the bottom. Every change previews at once (GameSettings.Preview); only
    // 적용 saves, 뒤로 가기 drops unapplied changes, 기본 resets the current tab. Shared by the main menu and the Esc menu.
    // Laid out against the 1920x1080 reference canvas, stretched to the screen with fixed margins, so it fits any
    // 16:9 size from 1280x720 to 2560x1440 (the list scrolls when it is longer than the panel).
    public class SettingsScreen
    {
        const float MarginX = 110f, MarginY = 56f;
        const float Pad = 36f;
        const float HeaderHeight = 78f;
        const float TabWidth = 172f, TabHeight = 50f;
        const float PageTop = HeaderHeight + TabHeight + 12f;
        const float PageTabWidth = 116f, PageTabHeight = 38f;
        const float BodyTop = PageTop + PageTabHeight + 14f;
        const float FooterHeight = 86f;
        const float ButtonWidth = 200f, ButtonHeight = 50f;
        const float DescriptionWidth = 460f;
        const float Gap = 24f;
        const float RowHeight = 54f, RowGap = 2f, SectionHeight = 46f;
        const float ControlWidth = 460f;
        const float ArrowWidth = 46f;
        const float ValueBoxWidth = 86f;
        const float ScrollbarWidth = 6f;
        const int MaxPips = 6;
        const int PageCount = 6;
        const int SwatchCells = 9;

        static readonly Color Well = new Color(0f, 0f, 0f, 0.22f);
        static readonly Color ValueBack = new Color(0f, 0f, 0f, 0.3f);
        static readonly Color DescriptionBack = new Color(0.075f, 0.085f, 0.095f, 0.92f);

        public RectTransform Root { get; private set; }
        public SettingsTab CurrentTab { get; private set; } = SettingsTab.Graphics;
        public SettingsPage CurrentPage { get; private set; } = SettingsPage.Basic;
        public SettingsRow FocusedRow { get; private set; }
        public string DescriptionTitle => _descTitle != null ? _descTitle.text : "";
        public string DescriptionBody => _descBody != null ? _descBody.text : "";
        public bool PreviewSwatchShown => _swatch != null && _swatch.activeSelf;
        public bool HasUnappliedChanges => GameSettings.HasUnappliedChanges;
        // Rows of the shown tab (and page), top to bottom.
        public IReadOnlyList<SettingsRow> VisibleRows => _rows[PageIndex(CurrentTab, CurrentPage)];

        readonly List<SettingsRow>[] _rows = new List<SettingsRow>[PageCount];
        readonly RectTransform[] _contents = new RectTransform[PageCount];
        readonly SettingsRow[] _byId = new SettingsRow[SettingsCatalog.Count];
        readonly Text[] _tabLabels = new Text[5];
        readonly Image[] _tabLines = new Image[5];
        readonly Text[] _pageLabels = new Text[2];
        readonly Image[] _pageLines = new Image[2];
        readonly Image[] _swatchCells = new Image[SwatchCells];
        GameObject _pageBar, _swatch;
        ScrollRect _scroll;
        Text _descTitle, _descBody, _dirtyLabel;
        Button _applyButton;
        UnityAction _onClose;

        // Builds the screen under `parent` (hidden pieces included); `onClose` runs after 뒤로 가기.
        public RectTransform Build(Transform parent, string name, UnityAction onClose)
        {
            _onClose = onClose;
            Root = UiKit.Fill(name, parent);
            Root.offsetMin = new Vector2(MarginX, MarginY);
            Root.offsetMax = new Vector2(-MarginX, -MarginY);
            UiKit.Box(Root, UiKit.PanelDeep, true);
            UiKit.Frame(Root, 1f, UiKit.SteelDim);
            UiKit.Brackets(Root, 22f, 2f, UiKit.Steel);

            BuildHeader();
            BuildTabs();
            BuildList();
            BuildDescription();
            BuildFooter();

            BuildPage(PageIndex(SettingsTab.Graphics, SettingsPage.Basic), SettingsTab.Graphics, SettingsPage.Basic);
            BuildPage(PageIndex(SettingsTab.Graphics, SettingsPage.Advanced), SettingsTab.Graphics, SettingsPage.Advanced);
            BuildPage(PageIndex(SettingsTab.Audio, SettingsPage.Basic), SettingsTab.Audio, SettingsPage.Basic);
            BuildPage(PageIndex(SettingsTab.Controls, SettingsPage.Basic), SettingsTab.Controls, SettingsPage.Basic);
            BuildPage(PageIndex(SettingsTab.Keys, SettingsPage.Basic), SettingsTab.Keys, SettingsPage.Basic);
            BuildPage(PageIndex(SettingsTab.Gameplay, SettingsPage.Basic), SettingsTab.Gameplay, SettingsPage.Basic);

            ShowPage();
            return Root;
        }

        // ---- actions ----

        // Called whenever the screen is shown: values may have changed elsewhere.
        public void Open()
        {
            ShowPage();
        }

        public void SelectTab(SettingsTab tab)
        {
            CurrentTab = tab;
            CurrentPage = SettingsPage.Basic;
            ShowPage();
        }

        // 기본 / 고급; only the graphics tab has pages.
        public void SelectPage(SettingsPage page)
        {
            if (CurrentTab != SettingsTab.Graphics) return;
            CurrentPage = page;
            ShowPage();
        }

        // 적용
        public void Apply()
        {
            GameSettings.Commit();
            Sync();
        }

        // 뒤로 가기: drops unapplied changes, then leaves.
        public void Back()
        {
            Cancel();
            _onClose?.Invoke();
        }

        // Drops unapplied changes without leaving (the menu was closed some other way).
        public void Cancel()
        {
            GameSettings.Revert();
            Sync();
        }

        // 기본: the current tab's defaults, previewed until 적용.
        public void ResetCurrentTab()
        {
            GameSettings.ResetTab(CurrentTab);
            Sync();
        }

        // The row of a setting (on whichever page it lives).
        public SettingsRow Row(SettingId id) => _byId[(int)id];

        // ◀ / ▶ of a choice row.
        public void Step(SettingId id, int direction)
        {
            GameSettings.Preview(id, GameSettings.GetIndex(id) + direction);
            Sync();
        }

        // Shows `row` in the description panel and marks it.
        public void Focus(SettingsRow row)
        {
            if (row == FocusedRow) return;
            if (FocusedRow != null)
            {
                FocusedRow.Background.color = FocusedRow.BaseColor;
                FocusedRow.Marker.enabled = false;
            }
            FocusedRow = row;
            if (row == null)
            {
                _descTitle.text = "";
                _descBody.text = "";
                _swatch.SetActive(false);
                return;
            }
            row.Background.color = UiKit.RowHover;
            row.Marker.enabled = true;
            _descTitle.text = row.Label;
            _descBody.text = row.Binding != null ? row.Description + "\n\n" + SettingsCatalog.KeysNote : row.Description;
            _swatch.SetActive(row.Def != null && row.Def.ShowsPreview);
            RefreshSwatch();
        }

        // Shows the current values on the visible page and the footer state.
        public void Sync()
        {
            var rows = VisibleRows;
            for (int i = 0; i < rows.Count; i++) SyncRow(rows[i]);
            bool dirty = GameSettings.HasUnappliedChanges;
            _dirtyLabel.enabled = dirty;
            UiKit.SetSelected(_applyButton, dirty);
            RefreshSwatch();
        }

        void OnSliderChanged(SettingId id, float value)
        {
            GameSettings.Preview(id, value);
            Sync();
        }

        void ShowPage()
        {
            int page = PageIndex(CurrentTab, CurrentPage);
            for (int i = 0; i < PageCount; i++)
                if (_contents[i] != null) _contents[i].gameObject.SetActive(i == page);
            _scroll.StopMovement();
            _scroll.content = _contents[page];
            _contents[page].anchoredPosition = Vector2.zero;
            for (int i = 0; i < _tabLabels.Length; i++) UiKit.SetTab(_tabLabels[i], _tabLines[i], i == (int)CurrentTab);
            _pageBar.SetActive(CurrentTab == SettingsTab.Graphics);
            for (int i = 0; i < _pageLabels.Length; i++) UiKit.SetTab(_pageLabels[i], _pageLines[i], i == (int)CurrentPage);
            Sync();
            var rows = _rows[page];
            Focus(rows.Count > 0 ? rows[0] : null);
        }

        static int PageIndex(SettingsTab tab, SettingsPage page) =>
            tab == SettingsTab.Graphics ? (page == SettingsPage.Advanced ? 1 : 0) : (int)tab + 1;

        void SyncRow(SettingsRow row)
        {
            var def = row.Def;
            if (def == null) return;
            switch (def.Kind)
            {
                case SettingKind.Slider:
                {
                    float value = GameSettings.Get(def.Id);
                    row.Slider.SetValueWithoutNotify(value);
                    row.Value.text = row.SliderTexts[Mathf.Clamp(SettingsCatalog.StepIndex(def, value), 0, row.SliderTexts.Length - 1)];
                    break;
                }
                case SettingKind.Toggle:
                {
                    bool on = GameSettings.GetToggle(def.Id);
                    UiKit.SetSelected(row.Off, !on);
                    UiKit.SetSelected(row.On, on);
                    break;
                }
                default:
                {
                    int count = def.OptionCount;
                    int index = Mathf.Clamp(GameSettings.GetIndex(def.Id), 0, Mathf.Max(0, count - 1));
                    row.Value.text = count > 0 ? def.Options[index] : "";
                    row.Previous.interactable = index > 0;
                    row.Next.interactable = index < count - 1;
                    if (row.Pips != null)
                        for (int i = 0; i < row.Pips.Length; i++) row.Pips[i].color = i == index ? UiKit.Accent : UiKit.SteelDim;
                    break;
                }
            }
        }

        // The brightness swatch: a grey ramp shown through the current exposure offset.
        void RefreshSwatch()
        {
            if (_swatch == null || !_swatch.activeSelf) return;
            float gain = Mathf.Pow(2f, SettingsCatalog.BrightnessToExposure(GameSettings.Get(SettingId.Brightness)));
            for (int i = 0; i < SwatchCells; i++)
            {
                float grey = Mathf.Clamp01((0.02f + 0.9f * i / (SwatchCells - 1f)) * gain);
                _swatchCells[i].color = new Color(grey, grey, grey, 1f);
            }
        }

        // ---- building ----

        // A rect hung from the top-left corner of `parent`.
        static RectTransform TopLeft(string name, Transform parent, float x, float y, float width, float height) =>
            UiKit.Rect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(width, height));

        // A full-width band hung `y` from the top of `parent`, inset `inset` on both sides.
        static RectTransform Band(string name, Transform parent, float y, float height, float inset = 0f)
        {
            var rect = UiKit.Rect(name, parent, new Vector2(0f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.sizeDelta = new Vector2(-inset * 2f, height);
            return rect;
        }

        void BuildHeader()
        {
            UiKit.Label(TopLeft("Title", Root, Pad, 18f, 600f, 48f), "환경 설정", 34, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            var hint = UiKit.Rect("Hint", Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Pad, -18f), new Vector2(700f, 48f));
            UiKit.Label(hint, "바꾼 내용은 바로 미리 보이고, 적용을 눌러야 저장됩니다", 17, UiKit.TextDim, TextAnchor.MiddleRight);
        }

        void BuildTabs()
        {
            for (int i = 0; i < _tabLabels.Length; i++)
            {
                var tab = (SettingsTab)i;
                UiKit.TabButton(TopLeft("Tab " + SettingsCatalog.TabLabels[i], Root, Pad + i * TabWidth, HeaderHeight, TabWidth, TabHeight),
                    SettingsCatalog.TabLabels[i], 22, () => SelectTab(tab), out _tabLabels[i], out _tabLines[i]);
            }
            var line = Band("TabLine", Root, HeaderHeight + TabHeight, 1f, Pad);
            UiKit.Box(line, UiKit.SteelDim);

            var bar = TopLeft("Pages", Root, Pad, PageTop, PageTabWidth * 2f, PageTabHeight);
            _pageBar = bar.gameObject;
            for (int i = 0; i < _pageLabels.Length; i++)
            {
                var page = (SettingsPage)i;
                var rect = TopLeft("Page " + SettingsCatalog.PageLabels[i], bar, i * PageTabWidth, 0f, PageTabWidth, PageTabHeight);
                UiKit.TabButton(rect, SettingsCatalog.PageLabels[i], 19, () => SelectPage(page), out _pageLabels[i], out _pageLines[i]);
            }
        }

        void BuildList()
        {
            var area = UiKit.Fill("List", Root);
            area.offsetMin = new Vector2(Pad, FooterHeight + 10f);
            area.offsetMax = new Vector2(-(Pad + DescriptionWidth + Gap), -BodyTop);
            UiKit.Box(area, Well, true); // also catches the wheel between rows
            UiKit.Frame(area, 1f, UiKit.SteelDim);

            var viewport = UiKit.Fill("Viewport", area, 1f);
            viewport.offsetMax = new Vector2(-(ScrollbarWidth + 8f), -1f);
            viewport.gameObject.AddComponent<RectMask2D>();

            _scroll = area.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.inertia = false;
            _scroll.scrollSensitivity = 40f;

            var bar = UiKit.Rect("Scrollbar", area, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(ScrollbarWidth, 0f));
            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.offsetMin = new Vector2(-4f - ScrollbarWidth, 6f);
            bar.offsetMax = new Vector2(-4f, -6f);
            UiKit.Box(bar, new Color(UiKit.SteelDim.r, UiKit.SteelDim.g, UiKit.SteelDim.b, 0.4f), true);
            var handle = UiKit.Fill("Handle", UiKit.Fill("Sliding Area", bar));
            var handleImage = UiKit.Box(handle, UiKit.Steel, true);
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            _scroll.verticalScrollbar = scrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            for (int i = 0; i < PageCount; i++)
            {
                var content = Band("Page" + i, viewport, 0f, 100f);
                content.gameObject.SetActive(false);
                _contents[i] = content;
                _rows[i] = new List<SettingsRow>(12);
            }
        }

        void BuildDescription()
        {
            var panel = UiKit.Rect("Description", Root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
            panel.anchorMin = new Vector2(1f, 0f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.offsetMin = new Vector2(-(Pad + DescriptionWidth), FooterHeight + 10f);
            panel.offsetMax = new Vector2(-Pad, -BodyTop);
            UiKit.Box(panel, DescriptionBack, true);
            UiKit.Frame(panel, 1f, UiKit.SteelDim);
            UiKit.Brackets(panel, 12f, 2f, UiKit.Steel);

            float inner = DescriptionWidth - 56f;
            UiKit.Label(TopLeft("Caption", panel, 28f, 22f, inner, 22f), "설명", 16, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _descTitle = UiKit.Label(TopLeft("Title", panel, 28f, 50f, inner, 36f), "", 26, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Box(TopLeft("Rule", panel, 28f, 94f, 44f, 2f), UiKit.Accent);
            _descBody = UiKit.Paragraph(TopLeft("Body", panel, 28f, 112f, inner, 230f), "", 19, UiKit.Text);

            var swatch = TopLeft("Swatch", panel, 28f, 352f, inner, 64f);
            _swatch = swatch.gameObject;
            float cell = inner / SwatchCells;
            for (int i = 0; i < SwatchCells; i++)
                _swatchCells[i] = UiKit.Box(TopLeft("Cell" + i, swatch, i * cell, 0f, cell - 2f, 40f), Color.grey);
            UiKit.Label(TopLeft("SwatchCaption", swatch, 0f, 44f, inner, 20f), "견본", 15, UiKit.TextDim);
            _swatch.SetActive(false);
        }

        void BuildFooter()
        {
            var line = UiKit.Rect("FooterLine", Root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.anchoredPosition = new Vector2(0f, FooterHeight - 4f);
            line.sizeDelta = new Vector2(-Pad * 2f, 1f);
            UiKit.Box(line, UiKit.SteelDim);

            float y = (FooterHeight - 4f - ButtonHeight) * 0.5f;
            UiKit.Button(UiKit.Rect("기본", Root, Vector2.zero, Vector2.zero, new Vector2(Pad, y), new Vector2(ButtonWidth, ButtonHeight)),
                "기본", 22, ResetCurrentTab);
            UiKit.Button(UiKit.Rect("뒤로 가기", Root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Pad, y), new Vector2(ButtonWidth, ButtonHeight)),
                "뒤로 가기", 22, Back);
            _applyButton = UiKit.Button(UiKit.Rect("적용", Root, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-Pad - ButtonWidth - 14f, y), new Vector2(ButtonWidth, ButtonHeight)), "적용", 22, Apply);
            var dirty = UiKit.Rect("Unapplied", Root, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-Pad - ButtonWidth * 2f - 40f, y), new Vector2(520f, ButtonHeight));
            _dirtyLabel = UiKit.Label(dirty, "적용하지 않은 변경 사항이 있습니다", 18, UiKit.Accent, TextAnchor.MiddleRight);
            _dirtyLabel.enabled = false;
        }

        void BuildPage(int page, SettingsTab tab, SettingsPage sub)
        {
            var content = _contents[page];
            var rows = _rows[page];
            float y = 8f;
            string section = null;
            if (tab == SettingsTab.Keys)
            {
                var note = Band("Note", content, y, 40f, 22f);
                UiKit.Label(note, SettingsCatalog.KeysNote, 18, UiKit.Accent);
                y += 44f;
                var keys = SettingsCatalog.KeyBindings;
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i].Section != section) { section = keys[i].Section; y = Section(content, section, y); }
                    var row = NewRow(content, keys[i].Label, rows.Count, ref y);
                    row.Binding = keys[i];
                    var box = ValueBox(row.transform, 260f);
                    row.Value = UiKit.Label(UiKit.Fill("Text", box), keys[i].Keys, 19, UiKit.Text, TextAnchor.MiddleCenter);
                    rows.Add(row);
                }
            }
            else
            {
                var all = SettingsCatalog.All;
                for (int i = 0; i < all.Length; i++)
                {
                    var def = all[i];
                    if (def.Tab != tab || def.Page != sub) continue;
                    if (def.Section != section) { section = def.Section; y = Section(content, section, y); }
                    var row = NewRow(content, def.Label, rows.Count, ref y);
                    row.Def = def;
                    if (def.Kind == SettingKind.Slider) BuildSlider(row);
                    else if (def.Kind == SettingKind.Toggle) BuildToggle(row);
                    else BuildChoice(row);
                    _byId[(int)def.Id] = row;
                    rows.Add(row);
                }
            }
            content.sizeDelta = new Vector2(0f, y + 8f);
        }

        static float Section(RectTransform content, string text, float y)
        {
            UiKit.SectionHeader(Band("Section " + text, content, y + 10f, SectionHeight - 10f, 22f), text);
            return y + SectionHeight;
        }

        SettingsRow NewRow(RectTransform content, string label, int index, ref float y)
        {
            var rect = Band(label, content, y, RowHeight, 6f);
            y += RowHeight + RowGap;
            var row = rect.gameObject.AddComponent<SettingsRow>();
            row.Owner = this;
            // Never fully transparent so the hover always registers.
            row.BaseColor = index % 2 == 0 ? UiKit.RowBand : new Color(1f, 1f, 1f, 0.004f);
            row.Background = UiKit.Box(rect, row.BaseColor, true);
            var marker = UiKit.Rect("Marker", rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(3f, RowHeight - 16f));
            row.Marker = UiKit.Box(marker, UiKit.Accent);
            row.Marker.enabled = false;
            var text = UiKit.Fill("Label", rect);
            text.offsetMin = new Vector2(22f, 0f);
            text.offsetMax = new Vector2(-(ControlWidth + 28f), 0f);
            UiKit.Label(text, label, 21, UiKit.Text);
            return row;
        }

        // The control area on the right of a row.
        static RectTransform Control(Transform row) =>
            UiKit.Rect("Control", row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(ControlWidth, RowHeight - 12f));

        // A dark framed box at the right end of a row (value read-outs).
        static RectTransform ValueBox(Transform row, float width)
        {
            var box = UiKit.Rect("Value", row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(width, RowHeight - 16f));
            UiKit.Box(box, ValueBack);
            UiKit.Frame(box, 1f, UiKit.SteelDim);
            return box;
        }

        void BuildChoice(SettingsRow row)
        {
            var id = row.Def.Id;
            var area = Control(row.transform);
            float h = RowHeight - 14f;
            row.Previous = UiKit.Button(UiKit.Rect("Previous", area, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(ArrowWidth, h)),
                "◀", 16, () => Step(id, -1));
            row.Next = UiKit.Button(UiKit.Rect("Next", area, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(ArrowWidth, h)),
                "▶", 16, () => Step(id, 1));
            var box = UiKit.Rect("Value", area, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ControlWidth - (ArrowWidth + 8f) * 2f, h));
            UiKit.Box(box, ValueBack);
            UiKit.Frame(box, 1f, UiKit.SteelDim);
            row.Value = UiKit.Label(UiKit.Fill("Text", box), "", 20, UiKit.Text, TextAnchor.MiddleCenter);
            int count = row.Def.OptionCount;
            if (count > 1 && count <= MaxPips)
            {
                // Position pips along the bottom edge: which of the few options this is, at a glance.
                const float pipWidth = 14f, pipGap = 4f;
                float total = count * pipWidth + (count - 1) * pipGap;
                row.Pips = new Image[count];
                // Lift the text clear of the pips (it touched them at 720p).
                var text = row.Value.rectTransform;
                text.offsetMin = new Vector2(text.offsetMin.x, 5f);
                for (int i = 0; i < count; i++)
                {
                    var pip = UiKit.Rect("Pip" + i, box, new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                        new Vector2(-total * 0.5f + i * (pipWidth + pipGap), 2f), new Vector2(pipWidth, 2f));
                    row.Pips[i] = UiKit.Box(pip, UiKit.SteelDim);
                }
            }
        }

        void BuildSlider(SettingsRow row)
        {
            var def = row.Def;
            var id = def.Id;
            var area = Control(row.transform);
            var rect = UiKit.Rect("Slider", area, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                new Vector2(ControlWidth - ValueBoxWidth - 18f, 22f));
            row.Slider = UiKit.BuildSlider(rect, def.Min, def.Max, GameSettings.Get(id));
            row.Slider.wholeNumbers = def.Step >= 1f;
            row.Slider.onValueChanged.AddListener(v => OnSliderChanged(id, v));
            row.SliderTexts = SettingsCatalog.SliderTexts(def);
            var box = UiKit.Rect("Value", area, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(ValueBoxWidth, RowHeight - 16f));
            UiKit.Box(box, ValueBack);
            UiKit.Frame(box, 1f, UiKit.SteelDim);
            row.Value = UiKit.Label(UiKit.Fill("Text", box, 6f), "", 20, UiKit.Text, TextAnchor.MiddleRight);
        }

        void BuildToggle(SettingsRow row)
        {
            var id = row.Def.Id;
            var area = Control(row.transform);
            float w = (ControlWidth - 8f) * 0.5f, h = RowHeight - 14f;
            row.Off = UiKit.Button(UiKit.Rect("Off", area, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(w, h)),
                row.Def.Options[0], 19, () => { GameSettings.Preview(id, 0f); Sync(); });
            row.On = UiKit.Button(UiKit.Rect("On", area, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(w, h)),
                row.Def.Options[1], 19, () => { GameSettings.Preview(id, 1f); Sync(); });
        }
    }
}
