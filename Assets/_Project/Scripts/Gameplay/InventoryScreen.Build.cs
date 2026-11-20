using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Construction of the inventory screen (once). Reference size 1920x1080; the panel is 1840x1000 and its columns are,
    // left to right: ground list, carried list, character with the worn pieces, weapon cards.
    public partial class InventoryScreen
    {
        const int SortingOrder = 100;
        const float PanelWidth = 1840f, PanelHeight = 1000f;
        const float ColumnTop = 64f, ColumnHeight = 900f, Gap = 16f;
        const float GroundWidth = 330f, BagWidth = 380f, CentreWidth = 520f, WeaponWidth = 562f;
        const float Pad = 14f, TitleHeight = 56f;
        const float GroundRowHeight = 56f, GroundRowGap = 5f;
        const float BagListTop = TitleHeight + 8f;
        const float BagRowHeight = 40f, BagRowGap = 3f; // fifteen rows (boost items, flash, molotov: ADR-116), then loose attachments
        const float BagOpticHeight = 42f, BagOpticGap = 4f, BagOpticLabel = 26f; // ADR-100: 16 ids = 6 columns x 3 rows
        const int BagOpticColumns = 6;
        const float SlotSize = 96f;
        const float WeaponCardHeight = 200f, WeaponCardGap = 8f;
        const float AttachChip = 46f, AttachGap = 8f;
        const float SmallCardWidth = 262f, SmallCardHeight = 224f;
        const float PipX = 14f, PipY = -166f, PipSize = 46f, PipGap = 6f;
        const float TooltipWidth = 340f, TooltipHeader = 84f;
        const float TooltipBarRow = 24f, TooltipBarLabel = 62f, TooltipBarValue = 76f; // ADR-493: a gun's stat bars

        static readonly Color ColumnPlate = new Color(0.045f, 0.052f, 0.06f, 0.93f);
        static readonly Color CharacterPlate = new Color(0.04f, 0.047f, 0.055f, 0.97f);
        static readonly Color Backdrop = new Color(0.01f, 0.012f, 0.015f, 0.74f);
        static readonly Color Durability = new Color(0.32f, 0.72f, 0.58f, 1f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 TopRight = new Vector2(1f, 1f);
        static readonly Vector2 MidLeft = new Vector2(0f, 0.5f);
        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopCentre = new Vector2(0.5f, 1f);

        void Build()
        {
            _canvas = UiKit.Canvas("InventoryCanvas", SortingOrder, true);
            _canvas.transform.SetParent(transform, false);
            var root = _canvas.transform;
            _backdropImage = UiKit.Box(UiKit.Fill("Backdrop", root), Backdrop, true);

            _panel = UiKit.Rect("InventoryPanel", root, Centre, Centre, Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            _panelGroup = _panel.gameObject.AddComponent<CanvasGroup>();
            var header = UiKit.Rect("Header", _panel, TopCentre, TopCentre, Vector2.zero, new Vector2(PanelWidth, 56f));
            UiKit.Label(UiKit.Fill("Hint", header), "끌어서 이동  ·  우클릭 장착·분리·사용  ·  더블클릭 사용  ·  Tab / Esc 닫기", 18, UiKit.TextDim, TextAnchor.MiddleRight);
            // #110: progress of a timed heal, centred in the header.
            var heal = UiKit.Rect("Healing", header, Centre, Centre, new Vector2(-60f, -2f), new Vector2(300f, 50f));
            _healLabel = UiKit.Label(UiKit.Rect("Label", heal, TopLeft, TopLeft, Vector2.zero, new Vector2(300f, 30f)), "", 19, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            var healTrack = UiKit.Rect("Track", heal, TopLeft, TopLeft, new Vector2(0f, -36f), new Vector2(300f, 8f));
            UiKit.Box(healTrack, UiKit.PanelLight);
            _healFill = UiKit.Fill("Fill", healTrack);
            _healFill.anchorMax = new Vector2(0f, 1f);
            UiKit.Box(_healFill, UiKit.Accent);
            _healBox = heal.gameObject;
            _healBox.SetActive(false);

            float x = 0f;
            var ground = Column("Ground", "주변", x, GroundWidth); x += GroundWidth + Gap;
            var pack = Column("Pack", "인벤토리", x, BagWidth); x += BagWidth + Gap;
            var centre = Column("Character", "", x, CentreWidth); x += CentreWidth + Gap;
            var weapons = Column("Weapons", "", x, WeaponWidth);

            BuildVicinity(ground, GroundWidth);
            BuildBackpack(pack, BagWidth);
            BuildCharacter(centre, CentreWidth);
            BuildWeapons(weapons, WeaponWidth);
            AddDropZone(ground, InventoryDropKind.Ground, 0);
            AddDropZone(pack, InventoryDropKind.Bag, 0);
            BuildDragGhost(root);
            BuildTooltip(root);
            SetSort(false);
        }

        RectTransform Column(string name, string title, float x, float width)
        {
            var column = UiKit.Rect(name, _panel, TopLeft, TopLeft, new Vector2(x, -ColumnTop), new Vector2(width, ColumnHeight));
            UiKit.Box(column, name == "Character" ? CharacterPlate : ColumnPlate, true);
            UiKit.Frame(column, 1f, new Color(UiKit.SteelDim.r, UiKit.SteelDim.g, UiKit.SteelDim.b, 0.55f));
            if (title.Length > 0)
            {
                UiKit.Label(UiKit.Rect("Title", column, TopLeft, TopLeft, new Vector2(Pad, -10f), new Vector2(width - 2f * Pad, 34f)), title, 26, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                var rule = UiKit.Rect("TitleRule", column, TopLeft, TopLeft, new Vector2(Pad, -TitleHeight + 4f), new Vector2(width - 2f * Pad, 1f));
                UiKit.Box(rule, UiKit.SteelDim);
            }
            return column;
        }

        static RectTransform Row(string name, Transform column, float width, float top, float height) =>
            UiKit.Rect(name, column, TopLeft, TopLeft, new Vector2(Pad, -top), new Vector2(width - 2f * Pad, height));

        static RectTransform InRow(string name, Transform row, float x, float width, float height) =>
            UiKit.Rect(name, row, MidLeft, MidLeft, new Vector2(x, 0f), new Vector2(width, height));

        static string PackName(int row)
        {
            if (row < InventoryBackend.AmmoTypeCount) return InventoryBackend.AmmoName(row) + " 탄약";
            switch (row)
            {
                case InventoryBackend.MedkitRow: return "구급상자";
                case InventoryBackend.BandageRow: return HealRules.BandageName;
                case InventoryBackend.FirstAidRow: return HealRules.FirstAidName;
                case InventoryBackend.SmokeRow: return ThrowableRules.SmokeName;
                case InventoryBackend.JerrycanRow: return FuelRules.JerrycanName;
                case InventoryBackend.DrinkRow: return HealRules.DrinkName;
                case InventoryBackend.PillRow: return HealRules.PillName;
                case InventoryBackend.SyringeRow: return HealRules.SyringeName;
                case InventoryBackend.FlashRow: return ThrowableRules.FlashName;
                case InventoryBackend.MolotovRow: return ThrowableRules.MolotovName;
                default: return ThrowableRules.GrenadeName;
            }
        }

        static string PackKey(int row)
        {
            if (row < InventoryBackend.AmmoTypeCount) return InventoryBackend.AmmoKey(row);
            switch (row)
            {
                case InventoryBackend.MedkitRow: return "medkit";
                case InventoryBackend.BandageRow: return "bandage";
                case InventoryBackend.FirstAidRow: return "firstaid";
                case InventoryBackend.SmokeRow: return "smoke";
                case InventoryBackend.JerrycanRow: return "jerrycan";
                case InventoryBackend.DrinkRow: return "drink";
                case InventoryBackend.PillRow: return "pill";
                case InventoryBackend.SyringeRow: return "syringe";
                case InventoryBackend.FlashRow: return "flash";
                case InventoryBackend.MolotovRow: return "molotov";
                default: return "grenade";
            }
        }

        void BuildVicinity(RectTransform column, float width)
        {
            float content = width - 2f * Pad;
            for (int i = 0; i < MaxVicinityRows; i++)
            {
                var row = Row("Loot" + i, column, width, TitleHeight + 8f + i * (GroundRowHeight + GroundRowGap), GroundRowHeight);
                UiKit.Box(row, UiKit.PanelLight, true);
                _vicinityIcons[i] = InventoryItemIcon.Build(InRow("Icon", row, 6f, 44f, 44f));
                _vicinityNames[i] = UiKit.Label(InRow("Name", row, 60f, content - 150f, GroundRowHeight), "", 20, UiKit.Text);
                _vicinityAmounts[i] = UiKit.Label(InRow("Amount", row, content - 86f, 78f, GroundRowHeight), "", 18, UiKit.TextDim, TextAnchor.MiddleRight);
                _vicinityDrags[i] = row.gameObject.AddComponent<InventoryDragItem>();
                _vicinityRows[i] = row.gameObject;
                row.gameObject.SetActive(false);
            }
            var empty = Row("Empty", column, width, TitleHeight + 8f, 64f);
            UiKit.Label(empty, "주변에 아이템 없음", 19, UiKit.TextDim, TextAnchor.MiddleCenter);
            _vicinityEmpty = empty.gameObject;
        }

        void BuildBackpack(RectTransform column, float width)
        {
            float content = width - 2f * Pad;
            // Sort of the carried list: by kind or by what was picked up last.
            var tabs = UiKit.Rect("Sort", column, TopRight, TopRight, new Vector2(-Pad, -14f), new Vector2(176f, 30f));
            UiKit.TabButton(UiKit.Rect("ByKind", tabs, TopLeft, TopLeft, Vector2.zero, new Vector2(86f, 30f)), "종류 순", 18, () => SetSort(false), out _sortKindLabel, out _sortKindLine);
            UiKit.TabButton(UiKit.Rect("ByRecent", tabs, TopLeft, TopLeft, new Vector2(90f, 0f), new Vector2(86f, 30f)), "최근 순", 18, () => SetSort(true), out _sortRecentLabel, out _sortRecentLine);

            for (int i = 0; i < InventoryBackend.BackpackRows; i++)
            {
                var row = Row("Bag" + i, column, width, BagListTop + i * (BagRowHeight + BagRowGap), BagRowHeight);
                UiKit.Box(row, UiKit.PanelLight, true);
                string name = PackName(i);
                _packIcons[i] = InventoryItemIcon.Build(InRow("Icon", row, 6f, BagRowHeight - 6f, BagRowHeight - 6f));
                _packIcons[i].Set(PackKey(i), name);
                _packNames[i] = UiKit.Label(InRow("Name", row, 52f, content - 130f, BagRowHeight), name, 19, UiKit.Text);
                _packCounts[i] = UiKit.Label(InRow("Count", row, content - 76f, 66f, BagRowHeight), "x0", 20, UiKit.Text, TextAnchor.MiddleRight, FontStyle.Bold);
                _packDrags[i] = row.gameObject.AddComponent<InventoryDragItem>();
                _packRows[i] = row.gameObject;
            }
            var emptyBag = Row("Empty", column, width, BagListTop, 56f);
            UiKit.Label(emptyBag, "아직 주운 것이 없습니다", 19, UiKit.TextDim, TextAnchor.MiddleCenter);
            _bagEmpty = emptyBag.gameObject;
            // Loose optics and attachments (ADR-082, ADR-100): a grid of cells, one per id held; drag one onto a gun or
            // right click to fit it.
            _bagOpticTitle = UiKit.Label(Row("OpticsTitle", column, width, BagListTop, BagOpticLabel), "조준경·부착물", 17, UiKit.Steel, TextAnchor.MiddleLeft, FontStyle.Bold);
            float cellWidth = (content - (BagOpticColumns - 1) * BagOpticGap) / BagOpticColumns;
            for (int i = 0; i < OpticBag.MaxId; i++)
            {
                float cx = Pad + (i % BagOpticColumns) * (cellWidth + BagOpticGap);
                var cellRect = UiKit.Rect("Optic" + i, column, TopLeft, TopLeft, new Vector2(cx, -BagListTop), new Vector2(cellWidth, BagOpticHeight));
                UiKit.Box(cellRect, UiKit.PanelLight, true);
                _bagOpticIcons[i] = InventoryItemIcon.Build(UiKit.Fill("Icon", cellRect, 3f));
                _bagOpticCounts[i] = UiKit.Label(UiKit.Rect("Count", cellRect, TopLeft, TopLeft, new Vector2(cellWidth - 32f, -BagOpticHeight + 20f), new Vector2(30f, 20f)), "x1", 15, UiKit.Text, TextAnchor.LowerRight, FontStyle.Bold);
                _bagOpticDrags[i] = cellRect.gameObject.AddComponent<InventoryDragItem>();
                _bagOpticCells[i] = cellRect.gameObject;
                cellRect.gameObject.SetActive(false);
            }
        }

        // Centre column: the worn pieces on the left, the live character, the bag's load under it.
        void BuildCharacter(RectTransform column, float width)
        {
            string[] labels = { "헬멧", "조끼", "가방" };
            for (int slot = 0; slot < GearSlots; slot++)
            {
                float y = 24f + slot * 160f;
                var square = UiKit.Rect(slot == 0 ? "Helmet" : slot == 1 ? "Vest" : "Backpack", column, TopLeft, TopLeft, new Vector2(Pad, -y), new Vector2(SlotSize, SlotSize));
                UiKit.Box(square, UiKit.PanelLight, true);
                UiKit.Frame(square, 1f, UiKit.SteelDim);
                _gearIcons[slot] = InventoryItemIcon.Build(UiKit.Fill("Icon", square, 6f));
                // The number in the corner: durability left (tier for the backpack).
                var badge = UiKit.Rect("Badge", square, TopRight, TopRight, new Vector2(-2f, -2f), new Vector2(38f, 22f));
                UiKit.Box(badge, new Color(0.02f, 0.025f, 0.03f, 0.92f));
                _gearBadge[slot] = UiKit.Label(UiKit.Fill("Text", badge), "", 16, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                _gearBadgeBox[slot] = badge.gameObject;
                var track = UiKit.Rect("Durability", column, TopLeft, TopLeft, new Vector2(Pad, -(y + SlotSize + 5f)), new Vector2(SlotSize, 6f));
                UiKit.Box(track, UiKit.PanelLight);
                _gearFill[slot] = UiKit.Fill("Fill", track);
                UiKit.Box(_gearFill[slot], Durability);
                _gearNames[slot] = UiKit.Label(UiKit.Rect("Name", column, TopLeft, TopLeft, new Vector2(Pad, -(y + SlotSize + 12f)), new Vector2(SlotSize + 30f, 22f)), labels[slot], 17, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                _gearDetails[slot] = UiKit.Label(UiKit.Rect("Detail", column, TopLeft, TopLeft, new Vector2(Pad, -(y + SlotSize + 32f)), new Vector2(SlotSize + 30f, 20f)), "", 16, UiKit.TextDim);
                _gearDrags[slot] = square.gameObject.AddComponent<InventoryDragItem>();
                AddDropZone(square, InventoryDropKind.Gear, slot);
            }

            // The character: a copy of the player's model drawn into a texture. Drag to turn it.
            var view = UiKit.Rect("CharacterView", column, TopLeft, TopLeft, new Vector2(Pad + SlotSize + 18f, -22f), new Vector2(width - SlotSize - 3f * Pad - 4f, 790f));
            var raw = view.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = true;
            raw.color = Color.white;
            _characterImage = raw;

            // The bag's load as text and a bar ("가방 용량 23/60"), at the foot of the column.
            var carry = Row("Carry", column, width, 836f, 54f);
            _carryLabel = UiKit.Label(UiKit.Rect("Label", carry, TopLeft, TopLeft, Vector2.zero, new Vector2(width - 2f * Pad, 28f)), "가방 용량 —", 19, UiKit.Text);
            var carryTrack = UiKit.Rect("Track", carry, TopLeft, TopLeft, new Vector2(0f, -34f), new Vector2(width - 2f * Pad, 10f));
            UiKit.Box(carryTrack, UiKit.PanelLight);
            _carryFill = UiKit.Fill("Fill", carryTrack);
            _carryFill.anchorMax = new Vector2(0f, 1f);
            _carryFillImage = UiKit.Box(_carryFill, Durability);
        }

        RawImage _characterImage;

        // ADR-493: five rows (label, bar, number) between the header and the body, shown for guns only.
        void BuildTooltipBars()
        {
            float width = TooltipWidth - 28f;
            _tooltipBars = UiKit.Rect("Bars", _tooltipRect, TopLeft, TopLeft, new Vector2(14f, -(TooltipHeader + 2f)), new Vector2(width, TooltipBarRow * TooltipBarCount));
            for (int i = 0; i < TooltipBarCount; i++)
            {
                var row = UiKit.Rect("Bar" + i, _tooltipBars, TopLeft, TopLeft, new Vector2(0f, -i * TooltipBarRow), new Vector2(width, TooltipBarRow));
                _tooltipBarLabels[i] = UiKit.Label(UiKit.Rect("Label", row, TopLeft, TopLeft, Vector2.zero, new Vector2(TooltipBarLabel, TooltipBarRow)), WeaponStats.BarLabels[i], 16, UiKit.TextDim);
                var track = UiKit.Rect("Track", row, MidLeft, MidLeft, new Vector2(TooltipBarLabel, 0f), new Vector2(width - TooltipBarLabel - TooltipBarValue - 8f, 8f));
                UiKit.Box(track, UiKit.PanelLight);
                _tooltipBarFills[i] = UiKit.Fill("Fill", track);
                UiKit.Box(_tooltipBarFills[i], UiKit.Accent);
                _tooltipBarValues[i] = UiKit.Label(UiKit.Rect("Value", row, TopRight, TopRight, Vector2.zero, new Vector2(TooltipBarValue, TooltipBarRow)), "", 16, UiKit.Text, TextAnchor.MiddleRight);
            }
            _tooltipBars.gameObject.SetActive(false);
        }

        // The texture is made with the preview; hooked up once it exists.
        void BindPreview()
        {
            if (_preview == null || _characterImage == null) return;
            _characterImage.texture = _preview.Texture;
            var drag = _characterImage.gameObject.AddComponent<InventoryPreviewDrag>();
            drag.Preview = _preview;
        }

        void BuildWeapons(RectTransform column, float width)
        {
            float cardWidth = width - 2f * Pad;
            for (int slot = 0; slot < 3; slot++)
            {
                var card = Row("Slot" + (slot + 1), column, width, 12f + slot * (WeaponCardHeight + WeaponCardGap), WeaponCardHeight);
                UiKit.Box(card, UiKit.PanelLight, true);
                var active = UiKit.Rect("Active", card, TopLeft, TopLeft, Vector2.zero, new Vector2(5f, WeaponCardHeight));
                UiKit.Box(active, UiKit.Accent);
                _weaponActive[slot] = active.gameObject;
                NumberBox(card, slot + 1);
                _weaponNames[slot] = UiKit.Label(UiKit.Rect("Name", card, TopLeft, TopLeft, new Vector2(56f, -8f), new Vector2(300f, 34f)), "비어 있음", 26, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                _weaponLabel[slot] = HiddenText(card, "SlotLabel");
                _weaponIcons[slot] = InventoryItemIcon.Build(UiKit.Rect("Icon", card, TopLeft, TopLeft, new Vector2(14f, -46f), new Vector2(226f, 96f)));
                // Right block: calibre, rounds in the magazine (big) and in reserve, fire mode.
                _weaponBadge[slot] = UiKit.Label(UiKit.Rect("Calibre", card, TopRight, TopRight, new Vector2(-14f, -12f), new Vector2(250f, 24f)), "구경 —", 18, UiKit.TextDim, TextAnchor.MiddleRight);
                _weaponAmmoBig[slot] = UiKit.Label(UiKit.Rect("Mag", card, TopRight, TopRight, new Vector2(-84f, -50f), new Vector2(90f, 46f)), "", 38, UiKit.Text, TextAnchor.MiddleRight, FontStyle.Bold);
                _weaponAmmoSmall[slot] = UiKit.Label(UiKit.Rect("Reserve", card, TopRight, TopRight, new Vector2(-14f, -54f), new Vector2(66f, 62f)), "", 18, UiKit.TextDim, TextAnchor.UpperLeft);
                // Shown on an empty card.
                _weaponHint[slot] = UiKit.Label(UiKit.Rect("Hint", card, TopLeft, TopLeft, new Vector2(14f, -80f), new Vector2(cardWidth - 28f, 40f)), "", 20, UiKit.TextDim, TextAnchor.MiddleCenter);
                // The text the tests read ("30 / 90발"); not shown, the big numbers carry it.
                _weaponAmmo[slot] = HiddenText(card, "AmmoText");
                // Attachment slots: sights, muzzle, grip, magazine, stock.
                for (int i = 0; i < AttachPlaces; i++)
                {
                    var place = PlaceOrder[i];
                    int ci = slot * AttachPlaces + (int)place;
                    var chip = UiKit.Rect("Attach" + (int)place, card, TopLeft, TopLeft, new Vector2(14f + i * (AttachChip + AttachGap), -(WeaponCardHeight - AttachChip - 6f)), new Vector2(AttachChip, AttachChip));
                    UiKit.Box(chip, UiKit.Panel, true);
                    UiKit.Frame(chip, 1f, UiKit.SteelDim);
                    _slotIcon[ci] = InventoryItemIcon.Build(UiKit.Fill("Icon", chip, 3f));
                    _slotDrag[ci] = chip.gameObject.AddComponent<InventoryDragItem>();
                    _slotRoot[ci] = chip.gameObject;
                    AddDropZone(chip, InventoryDropKind.Attach, Attachments.GearIndex(slot, place), true);
                }
                _weaponDrags[slot] = card.gameObject.AddComponent<InventoryDragItem>();
                AddDropZone(card, InventoryDropKind.Weapon, slot);
            }

            // Melee (4) and throwable (5) side by side under the guns.
            float smallTop = 12f + 3f * (WeaponCardHeight + WeaponCardGap) + 6f;
            BuildSmallCard(column, InventoryBackend.MeleeSlot, 4, "근접", 0f, smallTop, SmallCardWidth, false);
            BuildSmallCard(column, InventoryBackend.ThrowableSlot, 5, "투척", cardWidth - SmallCardWidth, smallTop, SmallCardWidth, true);
        }

        // A label that only holds a string the tests read.
        static Text HiddenText(RectTransform parent, string name)
        {
            var text = UiKit.Label(UiKit.Rect(name, parent, TopLeft, TopLeft, Vector2.zero, new Vector2(1f, 1f)), "", 12, Color.clear);
            text.enabled = false;
            return text;
        }

        void NumberBox(RectTransform card, int number)
        {
            var box = UiKit.Rect("Number", card, TopLeft, TopLeft, new Vector2(16f, -11f), new Vector2(32f, 32f));
            UiKit.Frame(box, 2f, UiKit.Steel);
            UiKit.Label(UiKit.Fill("Digit", box), number.ToString(), 21, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        void BuildSmallCard(RectTransform column, int slot, int number, string label, float x, float top, float width, bool throwable)
        {
            var card = UiKit.Rect(throwable ? "Slot5" : "Slot4", column, TopLeft, TopLeft, new Vector2(Pad + x, -top), new Vector2(width, SmallCardHeight));
            UiKit.Box(card, UiKit.PanelLight, true);
            var active = UiKit.Rect("Active", card, TopLeft, TopLeft, Vector2.zero, new Vector2(5f, SmallCardHeight));
            UiKit.Box(active, UiKit.Accent);
            _weaponActive[slot] = active.gameObject;
            NumberBox(card, number);
            _weaponLabel[slot] = UiKit.Label(UiKit.Rect("SlotLabel", card, TopLeft, TopLeft, new Vector2(58f, -14f), new Vector2(120f, 28f)), label, 20, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _weaponIcons[slot] = InventoryItemIcon.Build(UiKit.Rect("Icon", card, TopLeft, TopLeft, new Vector2((width - 128f) * 0.5f, -52f), new Vector2(128f, 76f)));
            _weaponNames[slot] = UiKit.Label(UiKit.Rect("Name", card, TopLeft, TopLeft, new Vector2(14f, -132f), new Vector2(width - 28f, 28f)), "비어 있음", 22, UiKit.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            _weaponBadge[slot] = UiKit.Label(UiKit.Rect("Calibre", card, TopRight, TopRight, new Vector2(-14f, -14f), new Vector2(120f, 24f)), "", 17, UiKit.TextDim, TextAnchor.MiddleRight);
            if (throwable)
            {
                _weaponAmmo[slot] = HiddenText(card, "AmmoText");
                _weaponAmmoBig[slot] = UiKit.Label(UiKit.Rect("Count", card, TopRight, TopRight, new Vector2(-16f, -42f), new Vector2(90f, 40f)), "", 30, UiKit.Text, TextAnchor.MiddleRight, FontStyle.Bold);
                _weaponAmmoSmall[slot] = UiKit.Label(UiKit.Rect("Spent", card, TopRight, TopRight, new Vector2(-16f, -82f), new Vector2(90f, 22f)), "", 16, UiKit.Danger, TextAnchor.MiddleRight);
                // A chip for each kind carried; the selected one has a frame. Click selects, double click takes it out.
                for (int i = 0; i < 4; i++)
                {
                    var chip = UiKit.Rect("Pip" + i, card, TopLeft, TopLeft, new Vector2(PipX, PipY), new Vector2(PipSize, PipSize));
                    UiKit.Box(chip, UiKit.Panel, true);
                    var select = UiKit.Fill("Selected", chip);
                    UiKit.Box(select, new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.22f));
                    UiKit.Frame(select, 2f, UiKit.Accent);
                    _pipSelect[i] = select.gameObject;
                    _pipIcon[i] = InventoryItemIcon.Build(UiKit.Fill("Icon", chip, 3f));
                    _pipCount[i] = UiKit.Label(UiKit.Rect("Count", chip, TopLeft, TopLeft, new Vector2(PipSize - 36f, -PipSize + 18f), new Vector2(34f, 18f)), "x1", 14, UiKit.Text, TextAnchor.LowerRight, FontStyle.Bold);
                    _pipDrag[i] = chip.gameObject.AddComponent<InventoryDragItem>();
                    _pipRoot[i] = chip.gameObject;
                    chip.gameObject.SetActive(false);
                }
                _weaponDrags[slot] = card.gameObject.AddComponent<InventoryDragItem>();
                AddDropZone(card, InventoryDropKind.Throwable, 0);
            }
            else
            {
                _weaponAmmo[slot] = UiKit.Label(UiKit.Rect("Detail", card, TopLeft, TopLeft, new Vector2(14f, -164f), new Vector2(width - 28f, 24f)), "", 18, UiKit.TextDim, TextAnchor.MiddleCenter);
                _weaponDrags[slot] = card.gameObject.AddComponent<InventoryDragItem>();
                AddDropZone(card, InventoryDropKind.Weapon, slot);
            }
        }

        void AddDropZone(RectTransform target, InventoryDropKind kind, int index, bool slotLook = false)
        {
            var overlayRect = UiKit.Fill("DropState", target, 0f);
            var overlay = UiKit.Box(overlayRect, Color.clear);
            overlay.enabled = false;
            var outline = UiKit.Frame(overlayRect, slotLook ? 2f : 2f, UiKit.Accent);
            foreach (var line in outline) line.enabled = false;
            var zone = target.gameObject.AddComponent<InventoryDropZone>();
            zone.Configure(this, kind, index, overlay, outline, slotLook);
            _dropZones.Add(zone);
        }

        void BuildDragGhost(Transform root)
        {
            _dragGhostRect = UiKit.Rect("DragIcon", root, Centre, Centre, Vector2.zero, new Vector2(76f, 76f));
            var group = _dragGhostRect.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            group.alpha = 0.95f;
            // A soft dark plate under the picture so it reads over any list.
            var shadow = UiKit.Rect("Shadow", _dragGhostRect, Centre, Centre, new Vector2(4f, -5f), new Vector2(84f, 84f));
            UiKit.Box(shadow, new Color(0f, 0f, 0f, 0.45f));
            var plate = UiKit.Rect("Plate", _dragGhostRect, Centre, Centre, Vector2.zero, new Vector2(84f, 84f));
            UiKit.Box(plate, new Color(0.05f, 0.06f, 0.07f, 0.92f));
            UiKit.Frame(plate, 2f, UiKit.Accent);
            var icon = UiKit.Rect("Icon", _dragGhostRect, Centre, Centre, Vector2.zero, new Vector2(72f, 72f));
            _dragGhostIcon = InventoryItemIcon.Build(icon);
            _dragGhost = _dragGhostRect.gameObject;
            _dragGhost.SetActive(false);
        }

        void BuildTooltip(Transform root)
        {
            _tooltipRect = UiKit.Rect("Tooltip", root, Centre, TopLeft, Vector2.zero, new Vector2(TooltipWidth, 120f));
            UiKit.Box(_tooltipRect, new Color(0.035f, 0.042f, 0.05f, 0.98f));
            UiKit.Frame(_tooltipRect, 1f, UiKit.Steel);
            UiKit.Box(UiKit.Rect("Accent", _tooltipRect, TopLeft, TopLeft, Vector2.zero, new Vector2(TooltipWidth, 3f)), UiKit.Accent);
            _tooltipGroup = _tooltipRect.gameObject.AddComponent<CanvasGroup>();
            _tooltipGroup.blocksRaycasts = false;
            _tooltipGroup.interactable = false;
            _tooltipIcon = InventoryItemIcon.Build(UiKit.Rect("Icon", _tooltipRect, TopLeft, TopLeft, new Vector2(14f, -16f), new Vector2(56f, 56f)));
            _tooltipTitle = UiKit.Label(UiKit.Rect("Title", _tooltipRect, TopLeft, TopLeft, new Vector2(82f, -14f), new Vector2(TooltipWidth - 96f, 30f)), "", 23, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            _tooltipType = UiKit.Label(UiKit.Rect("Type", _tooltipRect, TopLeft, TopLeft, new Vector2(82f, -46f), new Vector2(TooltipWidth - 96f, 26f)), "", 18, UiKit.Accent, TextAnchor.MiddleLeft);
            _tooltipRule = UiKit.Rect("Rule", _tooltipRect, TopLeft, TopLeft, new Vector2(14f, -(TooltipHeader - 6f)), new Vector2(TooltipWidth - 28f, 1f));
            UiKit.Box(_tooltipRule, UiKit.SteelDim);
            var body = UiKit.Rect("Body", _tooltipRect, TopLeft, TopLeft, new Vector2(14f, -(TooltipHeader + 4f)), new Vector2(TooltipWidth - 28f, 60f));
            _tooltipBody = UiKit.Paragraph(body, "", 18, new Color(0.82f, 0.85f, 0.88f, 1f));
            _tooltipBody.verticalOverflow = VerticalWrapMode.Overflow;
            BuildTooltipBars();
            _tooltip = _tooltipRect.gameObject;
            _tooltip.SetActive(false);
        }
    }
}
