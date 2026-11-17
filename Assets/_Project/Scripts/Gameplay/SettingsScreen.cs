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
        const float MarginX = 48f, MarginTopMenu = 112f, MarginTopOverlay = 48f, MarginBottom = 48f; // 8 px grid; the lobby's top bar sits above the panel
        const float Pad = 32f;
        const float HeaderHeight = 80f;
        const float TabWidth = 168f, TabGap = 8f, TabHeight = 56f;
        const float PageTabWidth = 112f;
        const float BodyTop = HeaderHeight + TabHeight + 16f;
        const float FooterHeight = 96f;
        const float ButtonWidth = 200f, ButtonHeight = 56f;
        const float DescriptionWidth = 460f;
        const float Gap = 24f;
        const float RowHeight = 56f, RowGap = 4f, SectionHeight = 48f;
        const float ControlWidth = 460f;
        const float ArrowWidth = 48f;
        const float ValueBoxWidth = 88f;
        const float ScrollbarWidth = 6f;
        const int PanelRadius = 16, InnerRadius = 8;
        const int MaxPips = 6;
        const int PageCount = 6;
        const int SwatchCells = 9;

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
        readonly Button[] _tabs = new Button[5];
        readonly Button[] _pageTabs = new Button[2];
        readonly Image[] _swatchCells = new Image[SwatchCells];
        GameObject _pageBar, _swatch;
        Text _keysNote;
        ScrollRect _scroll;
        Text _descTitle, _descBody, _dirtyLabel;
        Button _applyButton;
        UnityAction _onClose;

        // Builds the screen under `parent` (hidden pieces included); `onClose` runs after 뒤로 가기.
        public RectTransform Build(Transform parent, string name, UnityAction onClose, System.Action<MockScreen.Item> onBar = null)
        {
            _onClose = onClose;
            // The shared top bar of the adopted mockup (floor tabs, ping, icons) over the dimmed 3D lobby; the panel sits under it.
            RectTransform wrapper;
            bool overScene = onBar == null; // the Esc menu: a busy match behind, so the panel is nearly opaque
            if (onBar != null)
            {
                var bar = MockScreen.Build(parent, "set_gfx", onBar, it => it.r[1] >= 100f);
                wrapper = bar.Root;
                wrapper.name = name + "Wrapper";
            }
            else wrapper = UiKit.Fill(name + "Wrapper", parent);
            Root = UiKit.Fill(name, wrapper);
            Root.offsetMin = new Vector2(MarginX, MarginBottom);
            Root.offsetMax = new Vector2(-MarginX, -(overScene ? MarginTopOverlay : MarginTopMenu));
            UiPaint.Box(Root, overScene ? UiPaint.PnSolid : UiPaint.Pn, PanelRadius, UiPaint.Ln).raycastTarget = true;

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
            return wrapper;
        }

        // ---- key rebinding (#140) ----

        // The key row waiting for a key (null = none). True for the frame the wait ended too, so the key that ended it
        // (Esc, or the new key itself) does nothing else in the game.
        public static SettingsRow Listening { get; private set; }
        static int _listenEndedFrame = -1;
        static int _listenStartedFrame = -1;
        public static bool KeyCaptureActive => Listening != null || _listenEndedFrame == Time.frameCount;
        // The warning after the last change when the key is shared with another action; empty otherwise.
        public string ConflictWarning { get; private set; } = "";

        // Press on a key row: the next key (or mouse button) becomes that action's key.
        public void BeginRebind(SettingsRow row)
        {
            if (row == null || row.Binding == null) return;
            Listening = row;
            _listenStartedFrame = Time.frameCount;
            row.Value.text = "키를 누르세요...";
            row.Value.color = UiPaint.Ac;
        }

        public void CancelRebind()
        {
            if (Listening == null) return;
            _listenEndedFrame = Time.frameCount;
            Listening = null;
            Sync();
        }

        // Puts the row's action on `code` (saved at once); warns when another action is on it too.
        public void Rebind(SettingsRow row, int code)
        {
            var action = row.Binding.Action;
            var other = KeyBinds.Set(action, code);
            ConflictWarning = other.HasValue ? "경고: " + KeyBinds.Name(code) + " 키는 '" + LabelOf(other.Value) + "'에도 쓰이고 있습니다." : "";
            _listenEndedFrame = Time.frameCount;
            Listening = null;
            Sync();
        }

        static string LabelOf(BindAction action)
        {
            foreach (var k in SettingsCatalog.KeyBindings) if (k.Action == action) return k.Label;
            return action.ToString();
        }

        internal void PollRebind(SettingsRow row)
        {
            if (Listening != row || Time.frameCount <= _listenStartedFrame) return;
            if (!KeyBinds.TryCapture(out int code)) return;
            if (code == (int)UnityEngine.InputSystem.Key.Escape) CancelRebind();
            else Rebind(row, code);
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
            CancelRebind();
            GameSettings.Revert();
            Sync();
        }

        // 기본: the current tab's defaults, previewed until 적용.
        public void ResetCurrentTab()
        {
            if (CurrentTab == SettingsTab.Keys) { KeyBinds.ResetAll(); ConflictWarning = ""; }
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
            row.Background.color = UiPaint.Hover;
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
            if (_keysNote != null)
            {
                _keysNote.text = ConflictWarning.Length > 0 ? ConflictWarning : SettingsCatalog.KeysNote;
                _keysNote.color = ConflictWarning.Length > 0 ? UiPaint.Wr : UiPaint.T2;
            }
            bool dirty = GameSettings.HasUnappliedChanges;
            _dirtyLabel.enabled = dirty;
            SetPrimary(_applyButton, dirty);
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
            CancelRebind();
            _scroll.StopMovement();
            _scroll.content = _contents[page];
            _contents[page].anchoredPosition = Vector2.zero;
            for (int i = 0; i < _tabs.Length; i++) UiPaint.SetSeg(_tabs[i], i == (int)CurrentTab);
            _pageBar.SetActive(CurrentTab == SettingsTab.Graphics);
            for (int i = 0; i < _pageTabs.Length; i++) UiPaint.SetSeg(_pageTabs[i], i == (int)CurrentPage);
            Sync();
            var rows = _rows[page];
            Focus(rows.Count > 0 ? rows[0] : null);
        }

        static int PageIndex(SettingsTab tab, SettingsPage page) =>
            tab == SettingsTab.Graphics ? (page == SettingsPage.Advanced ? 1 : 0) : (int)tab + 1;

        void SyncRow(SettingsRow row)
        {
            var def = row.Def;
            if (def == null)
            {
                if (row.Binding != null && Listening != row)
                {
                    row.Value.text = row.Binding.Keys;
                    row.Value.color = KeyBinds.ConflictOf(row.Binding.Action).HasValue ? UiPaint.Wr : UiPaint.Tx;
                }
                return;
            }
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
                    SetToggle(row.Off, row.Def.Options[0], !on);
                    SetToggle(row.On, row.Def.Options[1], on);
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
                        for (int i = 0; i < row.Pips.Length; i++) row.Pips[i].color = i == index ? UiPaint.Ac : UiPaint.Ln;
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

        // 적용: the one filled accent button while there is something to apply, a quiet outlined one otherwise (as the lobby's
        // status button); the label stays readable either way.
        static void SetPrimary(Button button, bool active)
        {
            if (button == null) return;
            button.targetGraphic.color = active ? UiPaint.Ac : UiPaint.Sf;
            var ring = button.transform.Find("Ring");
            if (ring != null) ring.GetComponent<Image>().color = active ? UiPaint.Ac : UiPaint.Ln;
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = active ? UiPaint.Bg : UiPaint.T2;
        }

        // 켜짐/꺼짐 segments: chosen = accent wash + bar + ✓ (never colour alone).
        static void SetToggle(Button button, string text, bool on)
        {
            UiPaint.SetSeg(button, on);
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.text = (on ? "✓ " : "") + text;
        }

        // An outlined button of the lobby (+ 초대 / icon buttons): panel fill, 1 px border, bold label.
        static Button Quiet(Transform parent, string name, float x, float y, float w, float h, string text, int size, int radius, UnityAction onClick)
        {
            var b = UiPaint.Btn(parent, name, x, y, w, h, text, size, radius, UiPaint.Sf, UiPaint.Ln, UiPaint.Tx, onClick);
            return b;
        }

        void BuildHeader()
        {
            UiKit.Label(TopLeft("Title", Root, Pad, 16f, 600f, 48f), "환경 설정", 32, UiPaint.Tx, TextAnchor.MiddleLeft, FontStyle.Bold);
            var hint = UiKit.Rect("Hint", Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Pad, -16f), new Vector2(700f, 48f));
            UiKit.Label(hint, "바꾼 내용은 바로 미리 보이고, 적용을 눌러야 저장됩니다", 18, UiPaint.T2, TextAnchor.MiddleRight);
        }

        // Tabs are the lobby's segmented control; 기본 / 고급 is a smaller pill of two segments at the right end of the same row.
        void BuildTabs()
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                var tab = (SettingsTab)i;
                var rect = TopLeft("Tab " + SettingsCatalog.TabLabels[i], Root, Pad + i * (TabWidth + TabGap), HeaderHeight, TabWidth, TabHeight);
                _tabs[i] = UiPaint.Seg(Root, rect.name, 0f, 0f, TabWidth, TabHeight, SettingsCatalog.TabLabels[i], 20, 14, () => SelectTab(tab));
                var seg = (RectTransform)_tabs[i].transform;
                seg.anchorMin = seg.anchorMax = new Vector2(0f, 1f);
                seg.anchoredPosition = new Vector2(Pad + i * (TabWidth + TabGap), -HeaderHeight);
                Object.Destroy(rect.gameObject);
            }

            var bar = UiKit.Rect("Pages", Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Pad, -HeaderHeight), new Vector2(PageTabWidth * 2f + 4f, TabHeight));
            _pageBar = bar.gameObject;
            UiPaint.Box(bar, Color.clear, (int)(TabHeight * 0.5f), UiPaint.Ln);
            for (int i = 0; i < _pageTabs.Length; i++)
            {
                var page = (SettingsPage)i;
                _pageTabs[i] = UiPaint.Seg(bar, "Page " + SettingsCatalog.PageLabels[i], 2f + i * PageTabWidth, 2f, PageTabWidth, TabHeight - 4f,
                    SettingsCatalog.PageLabels[i], 18, (int)((TabHeight - 4f) * 0.5f), () => SelectPage(page));
            }
        }

        void BuildList()
        {
            var area = UiKit.Fill("List", Root);
            area.offsetMin = new Vector2(Pad, FooterHeight);
            area.offsetMax = new Vector2(-(Pad + DescriptionWidth + Gap), -BodyTop);
            UiPaint.Box(area, UiPaint.Well, 12, UiPaint.Ln).raycastTarget = true; // also catches the wheel between rows

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
            bar.offsetMin = new Vector2(-4f - ScrollbarWidth, 10f);
            bar.offsetMax = new Vector2(-4f, -10f);
            UiPaint.Box(bar, UiPaint.Ln, 3).raycastTarget = true;
            var handle = UiKit.Fill("Handle", UiKit.Fill("Sliding Area", bar));
            var handleImage = UiPaint.Box(handle, UiPaint.T2, 3);
            handleImage.raycastTarget = true;
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
            panel.offsetMin = new Vector2(-(Pad + DescriptionWidth), FooterHeight);
            panel.offsetMax = new Vector2(-Pad, -BodyTop);
            UiPaint.Box(panel, UiPaint.Pn, PanelRadius, UiPaint.Ln);

            float inner = DescriptionWidth - 56f;
            UiKit.Label(TopLeft("Caption", panel, 28f, 24f, inner, 24f), "설명", 16, UiPaint.T2, TextAnchor.MiddleLeft, FontStyle.Bold);
            _descTitle = UiKit.Label(TopLeft("Title", panel, 28f, 52f, inner, 40f), "", 28, UiPaint.Tx, TextAnchor.MiddleLeft, FontStyle.Bold);
            _descBody = UiKit.Paragraph(TopLeft("Body", panel, 28f, 108f, inner, 232f), "", 20, UiPaint.Tx);

            var swatch = TopLeft("Swatch", panel, 28f, 352f, inner, 72f);
            _swatch = swatch.gameObject;
            float cell = inner / SwatchCells;
            for (int i = 0; i < SwatchCells; i++)
                _swatchCells[i] = UiKit.Box(TopLeft("Cell" + i, swatch, i * cell, 0f, cell - 2f, 40f), Color.grey);
            UiKit.Label(TopLeft("SwatchCaption", swatch, 0f, 48f, inner, 24f), "견본", 16, UiPaint.T2, TextAnchor.MiddleLeft, FontStyle.Bold);
            _swatch.SetActive(false);
        }

        void BuildFooter()
        {
            float y = 24f;
            int radius = (int)(ButtonHeight * 0.5f);
            Quiet(Root, "기본", 0f, 0f, ButtonWidth, ButtonHeight, "기본", 22, radius, ResetCurrentTab);
            Quiet(Root, "뒤로 가기", 0f, 0f, ButtonWidth, ButtonHeight, "뒤로 가기", 22, radius, Back);
            _applyButton = Quiet(Root, "적용", 0f, 0f, ButtonWidth, ButtonHeight, "적용", 22, radius, Apply);
            Place(Root.Find("기본"), new Vector2(0f, 0f), Pad, y);
            Place(Root.Find("뒤로 가기"), new Vector2(1f, 0f), -Pad - ButtonWidth, y);
            Place(Root.Find("적용"), new Vector2(1f, 0f), -Pad - ButtonWidth * 2f - 16f, y);
            var dirty = UiKit.Rect("Unapplied", Root, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-Pad - ButtonWidth * 2f - 40f, y), new Vector2(520f, ButtonHeight));
            _dirtyLabel = UiKit.Label(dirty, "적용하지 않은 변경 사항이 있습니다", 18, UiPaint.Ac, TextAnchor.MiddleRight);
            _dirtyLabel.enabled = false;
            SetPrimary(_applyButton, false);
        }

        // Bottom-anchored placement of a button made by UiPaint.Btn (which hangs from the top-left).
        static void Place(Transform t, Vector2 anchor, float x, float y)
        {
            var r = (RectTransform)t;
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = new Vector2(anchor.x, 0f);
            r.anchoredPosition = new Vector2(anchor.x == 0f ? x : x + ButtonWidth, y);
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
                _keysNote = UiKit.Label(note, SettingsCatalog.KeysNote, 18, UiPaint.T2);
                y += 48f;
                var keys = SettingsCatalog.KeyBindings;
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i].Section != section) { section = keys[i].Section; y = Section(content, section, y); }
                    var row = NewRow(content, keys[i].Label, rows.Count, ref y);
                    row.Binding = keys[i];
                    var rowRef = row;
                    var key = Quiet(row.transform, "Key", 0f, 0f, 260f, RowHeight - 16f, keys[i].Keys, 20, InnerRadius, () => BeginRebind(rowRef));
                    var kr = (RectTransform)key.transform;
                    kr.anchorMin = kr.anchorMax = new Vector2(1f, 0.5f);
                    kr.pivot = new Vector2(1f, 0.5f);
                    kr.anchoredPosition = new Vector2(-14f, 0f);
                    row.KeyButton = key;
                    row.Value = key.GetComponentInChildren<Text>();
                    row.Value.fontStyle = FontStyle.Normal;
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

        // A small caption with a hairline running to the right edge.
        static float Section(RectTransform content, string text, float y)
        {
            var rect = Band("Section " + text, content, y + 12f, SectionHeight - 12f, 22f);
            var label = UiKit.Label(UiKit.Fill("Caption", rect), text, 18, UiPaint.T2, TextAnchor.MiddleLeft, FontStyle.Bold);
            var line = UiKit.Rect("Line", rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 1f));
            line.anchorMin = new Vector2(0f, 0.5f);
            line.anchorMax = new Vector2(1f, 0.5f);
            line.offsetMin = new Vector2(label.preferredWidth + 16f, -0.5f);
            line.offsetMax = new Vector2(0f, 0.5f);
            UiKit.Box(line, UiPaint.Ln);
            return y + SectionHeight;
        }

        SettingsRow NewRow(RectTransform content, string label, int index, ref float y)
        {
            var rect = Band(label, content, y, RowHeight, 6f);
            y += RowHeight + RowGap;
            var row = rect.gameObject.AddComponent<SettingsRow>();
            row.Owner = this;
            // Never fully transparent so the hover always registers.
            row.BaseColor = UiPaint.Off;
            row.Background = UiPaint.Box(rect, row.BaseColor, InnerRadius);
            row.Background.raycastTarget = true;
            var marker = UiKit.Rect("Marker", rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(3f, RowHeight - 24f));
            row.Marker = UiPaint.Box(marker, UiPaint.Ac, 1);
            row.Marker.enabled = false;
            var text = UiKit.Fill("Label", rect);
            text.offsetMin = new Vector2(22f, 0f);
            text.offsetMax = new Vector2(-(ControlWidth + 28f), 0f);
            UiKit.Label(text, label, 20, UiPaint.Tx);
            return row;
        }

        // The control area on the right of a row.
        static RectTransform Control(Transform row) =>
            UiKit.Rect("Control", row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(ControlWidth, RowHeight - 12f));

        // A read-out well: the lobby's map card (page background, 1 px border).
        static void ValueWell(RectTransform box) => UiPaint.Box(box, UiPaint.Bg, InnerRadius, UiPaint.Ln);

        void BuildChoice(SettingsRow row)
        {
            var id = row.Def.Id;
            var area = Control(row.transform);
            float h = RowHeight - 16f;
            row.Previous = Quiet(area, "Previous", 0f, 0f, ArrowWidth, h, "◀", 16, InnerRadius, () => Step(id, -1));
            row.Next = Quiet(area, "Next", 0f, 0f, ArrowWidth, h, "▶", 16, InnerRadius, () => Step(id, 1));
            AnchorMid(row.Previous.transform, 0f);
            AnchorMid(row.Next.transform, 1f);
            var box = UiKit.Rect("Value", area, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ControlWidth - (ArrowWidth + 8f) * 2f, h));
            ValueWell(box);
            row.Value = UiKit.Label(UiKit.Fill("Text", box), "", 20, UiPaint.Tx, TextAnchor.MiddleCenter);
            int count = row.Def.OptionCount;
            if (count > 1 && count <= MaxPips)
            {
                // Position pips just under the value box, outside its frame: which of the few options this is, at a
                // glance. Inside the box they read as underscores round the text ("_보통_").
                const float pipWidth = 14f, pipGap = 4f;
                float total = count * pipWidth + (count - 1) * pipGap;
                row.Pips = new Image[count];
                for (int i = 0; i < count; i++)
                {
                    var pip = UiKit.Rect("Pip" + i, box, new Vector2(0.5f, 0f), new Vector2(0f, 1f),
                        new Vector2(-total * 0.5f + i * (pipWidth + pipGap), -3f), new Vector2(pipWidth, 2f));
                    row.Pips[i] = UiKit.Box(pip, UiPaint.Ln);
                }
            }
        }

        // Pins a UiPaint.Btn (top-left anchored) to the left (0) or right (1) middle of its parent.
        static void AnchorMid(Transform t, float side)
        {
            var r = (RectTransform)t;
            r.anchorMin = r.anchorMax = new Vector2(side, 0.5f);
            r.pivot = new Vector2(side, 0.5f);
            r.anchoredPosition = Vector2.zero;
        }

        void BuildSlider(SettingsRow row)
        {
            var def = row.Def;
            var id = def.Id;
            var area = Control(row.transform);
            var rect = UiKit.Rect("Slider", area, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                new Vector2(ControlWidth - ValueBoxWidth - 20f, 24f));
            row.Slider = UiKit.BuildSlider(rect, def.Min, def.Max, GameSettings.Get(id));
            StyleSlider(row.Slider);
            row.Slider.wholeNumbers = def.Step >= 1f;
            row.Slider.onValueChanged.AddListener(v => OnSliderChanged(id, v));
            row.SliderTexts = SettingsCatalog.SliderTexts(def);
            var box = UiKit.Rect("Value", area, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(ValueBoxWidth, RowHeight - 16f));
            ValueWell(box);
            row.Value = UiKit.Label(UiKit.Fill("Text", box, 8f), "", 20, UiPaint.Tx, TextAnchor.MiddleRight);
        }

        // The kit slider with the lobby's shapes and colours: a rounded hairline track, an accent fill, a light round handle.
        static void StyleSlider(Slider slider)
        {
            foreach (var image in slider.GetComponentsInChildren<Image>(true))
            {
                switch (image.gameObject.name)
                {
                    case "Track": image.sprite = UiPaint.Round(3); image.type = Image.Type.Sliced; image.color = UiPaint.Ln; break;
                    case "Fill": image.sprite = UiPaint.Round(3); image.type = Image.Type.Sliced; image.color = UiPaint.Ac; break;
                    case "Handle": image.sprite = UiPaint.Round(6); image.type = Image.Type.Sliced; image.color = UiPaint.Tx; break;
                }
            }
        }

        // 꺼짐 / 켜짐: two segments in one outlined pill.
        void BuildToggle(SettingsRow row)
        {
            var id = row.Def.Id;
            var area = Control(row.transform);
            float h = RowHeight - 16f, w = (ControlWidth - 8f) * 0.5f;
            UiPaint.Box(area, Color.clear, (int)(h * 0.5f), UiPaint.Ln);
            row.Off = UiPaint.Seg(area, "Off", 0f, 0f, w, h - 4f, row.Def.Options[0], 18, (int)((h - 4f) * 0.5f), () => { GameSettings.Preview(id, 0f); Sync(); });
            row.On = UiPaint.Seg(area, "On", 0f, 0f, w, h - 4f, row.Def.Options[1], 18, (int)((h - 4f) * 0.5f), () => { GameSettings.Preview(id, 1f); Sync(); });
            var off = (RectTransform)row.Off.transform;
            off.anchorMin = off.anchorMax = new Vector2(0f, 0.5f); off.pivot = new Vector2(0f, 0.5f); off.anchoredPosition = new Vector2(2f, 0f);
            var on = (RectTransform)row.On.transform;
            on.anchorMin = on.anchorMax = new Vector2(1f, 0.5f); on.pivot = new Vector2(1f, 0.5f); on.anchoredPosition = new Vector2(-2f, 0f);
        }
    }
}
