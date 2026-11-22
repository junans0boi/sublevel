using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The Tab inventory (#179): the ground list and the carried list on the left, the live character with the worn
    // pieces in the middle, the weapon cards with their attachment slots on the right, melee and throwable slots below.
    // Presentation reads replicated state; every mutation goes through InventoryBackend (server rules unchanged).
    // Construction is in InventoryScreen.Build.cs.
    public partial class InventoryScreen : MonoBehaviour
    {
        public const int MaxVicinityRows = 12;
        public const int AttachPlaces = 5;

        public static InventoryScreen Instance { get; private set; }

        public bool IsOpen => _canvas != null && _canvas.gameObject.activeSelf;
        public int VicinityCount => _vicinity.Count;
        public bool Dragging => _dragSource.IsValid;
        public InventoryDragSource ActiveDragSource => _dragSource;
        public InventoryPreview Preview => _preview;
        public bool SortByRecent => _sortRecent;
        public Canvas ScreenCanvas => _canvas;

        Canvas _canvas;
        RectTransform _panel;
        CanvasGroup _panelGroup;
        Image _backdropImage;
        InventoryPreview _preview;
        readonly List<LootItem> _vicinity = new List<LootItem>(MaxVicinityRows);
        readonly List<InventoryDropZone> _dropZones = new List<InventoryDropZone>(24);
        int _hash;
        bool _dirty = true;
        float _openT = 1f;
        bool _sortRecent;
        readonly int[] _recentStamp = new int[InventoryBackend.BackpackRows];
        readonly int[] _lastCount = new int[InventoryBackend.BackpackRows];
        int _stampSeq;
        readonly List<int> _order = new List<int>(InventoryBackend.BackpackRows);

        readonly GameObject[] _vicinityRows = new GameObject[MaxVicinityRows];
        readonly Text[] _vicinityNames = new Text[MaxVicinityRows];
        readonly Text[] _vicinityAmounts = new Text[MaxVicinityRows];
        readonly InventoryItemIcon[] _vicinityIcons = new InventoryItemIcon[MaxVicinityRows];
        readonly InventoryDragItem[] _vicinityDrags = new InventoryDragItem[MaxVicinityRows];
        GameObject _vicinityEmpty;

        readonly Text[] _packNames = new Text[InventoryBackend.BackpackRows];
        readonly Text[] _packCounts = new Text[InventoryBackend.BackpackRows];
        readonly GameObject[] _packRows = new GameObject[InventoryBackend.BackpackRows];
        readonly InventoryItemIcon[] _packIcons = new InventoryItemIcon[InventoryBackend.BackpackRows];
        readonly InventoryDragItem[] _packDrags = new InventoryDragItem[InventoryBackend.BackpackRows];
        readonly GameObject[] _bagOpticCells = new GameObject[OpticBag.MaxId];
        readonly InventoryItemIcon[] _bagOpticIcons = new InventoryItemIcon[OpticBag.MaxId];
        readonly Text[] _bagOpticCounts = new Text[OpticBag.MaxId];
        readonly InventoryDragItem[] _bagOpticDrags = new InventoryDragItem[OpticBag.MaxId];
        Text _bagOpticTitle;
        GameObject _bagEmpty; // ADR-137: shown while nothing is carried
        Text _sortKindLabel, _sortRecentLabel;
        Image _sortKindLine, _sortRecentLine;
        GameObject _healBox;
        Text _healLabel;
        RectTransform _healFill;
        Text _carryLabel;
        RectTransform _carryFill;
        Image _carryFillImage;

        // Worn pieces: helmet, vest, backpack.
        const int GearSlots = 3;
        const int BackpackGear = 2;
        readonly Text[] _gearNames = new Text[GearSlots];
        readonly Text[] _gearDetails = new Text[GearSlots];
        readonly Text[] _gearBadge = new Text[GearSlots];
        readonly GameObject[] _gearBadgeBox = new GameObject[GearSlots];
        readonly RectTransform[] _gearFill = new RectTransform[GearSlots];
        readonly InventoryItemIcon[] _gearIcons = new InventoryItemIcon[GearSlots];
        readonly InventoryDragItem[] _gearDrags = new InventoryDragItem[GearSlots];

        readonly GameObject[] _weaponActive = new GameObject[Loadout.SlotCount];
        readonly Text[] _weaponNames = new Text[Loadout.SlotCount];
        readonly Text[] _weaponAmmo = new Text[Loadout.SlotCount];      // "30 / 90발  ·  연사", read by tests
        readonly Text[] _weaponAmmoBig = new Text[Loadout.SlotCount];
        readonly Text[] _weaponAmmoSmall = new Text[Loadout.SlotCount];
        readonly Text[] _weaponBadge = new Text[Loadout.SlotCount];
        readonly Text[] _weaponLabel = new Text[Loadout.SlotCount];
        readonly Text[] _weaponHint = new Text[3];
        readonly InventoryItemIcon[] _weaponIcons = new InventoryItemIcon[Loadout.SlotCount];
        readonly InventoryDragItem[] _weaponDrags = new InventoryDragItem[Loadout.SlotCount];
        // Attachment slots of the three gun cards: [weaponSlot * AttachPlaces + (int)AttachSlot].
        readonly GameObject[] _slotRoot = new GameObject[3 * AttachPlaces];
        readonly InventoryItemIcon[] _slotIcon = new InventoryItemIcon[3 * AttachPlaces];
        readonly InventoryDragItem[] _slotDrag = new InventoryDragItem[3 * AttachPlaces];
        // Throwable card: one chip per kind carried.
        readonly GameObject[] _pipRoot = new GameObject[4];
        readonly InventoryItemIcon[] _pipIcon = new InventoryItemIcon[4];
        readonly Text[] _pipCount = new Text[4];
        readonly GameObject[] _pipSelect = new GameObject[4];
        readonly InventoryDragItem[] _pipDrag = new InventoryDragItem[4];

        InventoryDragSource _dragSource;
        GameObject _dragGhost;
        RectTransform _dragGhostRect;
        InventoryItemIcon _dragGhostIcon;
        float _ghostPop;
        Vector2? _fakePointer; // tests and captures place the pointer themselves
        public void SetPointerForTests(Vector2? screenPosition) => _fakePointer = screenPosition;
        bool _dropHandled;
        bool _suppressQuickDropUntilRightRelease;

        GameObject _tooltip;
        RectTransform _tooltipRect;
        CanvasGroup _tooltipGroup;
        InventoryItemIcon _tooltipIcon;
        Text _tooltipTitle, _tooltipType, _tooltipBody;
        RectTransform _tooltipRule;
        const int TooltipBarCount = 5;
        RectTransform _tooltipBars;
        readonly Text[] _tooltipBarLabels = new Text[TooltipBarCount], _tooltipBarValues = new Text[TooltipBarCount];
        readonly RectTransform[] _tooltipBarFills = new RectTransform[TooltipBarCount];
        // ADR-493: the bars' numbers as "피해 40 | 사거리 120m | ...", for the tests; empty when no bars show.
        public string TooltipBars { get; private set; } = "";
        bool _tooltipWant;
        float _tooltipAlpha;

        public string TooltipTitle => _tooltipTitle != null ? _tooltipTitle.text : "";
        public string TooltipBody => _tooltipBody != null ? _tooltipType.text + "\n" + _tooltipBody.text : "";
        public bool TooltipVisible => _tooltip != null && _tooltip.activeSelf && _tooltipWant;

        void Awake()
        {
            Instance = this;
            Build();
            _preview = InventoryPreview.Create(transform);
            BindPreview();
            _canvas.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            UiFocus.Changed += OnFocusChanged;
            OnFocusChanged();
        }

        void OnDisable()
        {
            UiFocus.Changed -= OnFocusChanged;
            CancelDrag();
            if (_preview != null) _preview.Hide();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnFocusChanged()
        {
            if (_canvas == null) return;
            bool open = UiFocus.Current == UiScreen.Inventory;
            if (_canvas.gameObject.activeSelf != open) _canvas.gameObject.SetActive(open);
            if (!open)
            {
                CancelDrag();
                HideTooltipCore(true);
                _vicinity.Clear();
                if (_preview != null) _preview.Hide();
                return;
            }
            _dirty = true;
            _openT = 0f;
            ApplyOpen();
            var me = Combatant.LocalPlayer;
            if (me != null)
            {
                Refresh(me);
                if (_preview != null) _preview.Show(me);
            }
        }

        void Update()
        {
            if (!IsOpen) return;
            AnimateOpen();
            FollowPointer();
            AnimateTooltip();
            if (_suppressQuickDropUntilRightRelease && Mouse.current != null && !Mouse.current.rightButton.isPressed &&
                !Mouse.current.rightButton.wasReleasedThisFrame)
                _suppressQuickDropUntilRightRelease = false;
            if (Dragging && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                _suppressQuickDropUntilRightRelease = true;
                CancelDrag();
                return;
            }
            var me = Combatant.LocalPlayer;
            if (me == null || me.Dead.Value)
            {
                UiFocus.Close();
                return;
            }
            Refresh(me);
            ShowHealing();
            if (_preview != null) _preview.Refresh(me, InventoryPreview.KeyOf(me));
        }

        // A short fade and slide when the screen opens.
        void AnimateOpen()
        {
            if (_openT >= 1f) return;
            _openT = Mathf.Min(1f, _openT + Time.unscaledDeltaTime / 0.16f);
            ApplyOpen();
        }

        void ApplyOpen()
        {
            float e = 1f - (1f - _openT) * (1f - _openT) * (1f - _openT);
            _panelGroup.alpha = e;
            _panel.anchoredPosition = new Vector2(0f, (1f - e) * -26f);
            _backdropImage.color = new Color(Backdrop.r, Backdrop.g, Backdrop.b, Backdrop.a * e);
        }

        // The drag picture and the tooltip follow the pointer every frame, not only on the frames events arrive.
        Camera UiCamera => _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

        // For tests and captures: applies the placed pointer (drag picture, tooltip) now.
        public void RefreshPointerNow() => FollowPointer();

        void FollowPointer()
        {
            if (_fakePointer == null && Mouse.current == null) return;
            var position = _fakePointer ?? Mouse.current.position.ReadValue();
            if (Dragging)
            {
                MoveDrag(position, UiCamera);
                _ghostPop = Mathf.MoveTowards(_ghostPop, 1f, Time.unscaledDeltaTime * 10f);
                _dragGhostRect.localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(_ghostPop * Mathf.PI * 0.5f));
            }
            else if (_tooltipWant) PlaceTooltip(position, UiCamera);
        }

        // #110: the timed use (started by a right click here or by H) shows its progress in the header, since the
        // HUD's bar is under this screen.
        void ShowHealing()
        {
            var heal = CombatantHealing.Local;
            bool on = heal != null && heal.Using;
            if (_healBox.activeSelf != on) _healBox.SetActive(on);
            if (!on) return;
            _healLabel.text = heal.ItemName + " 사용 중  " + heal.RemainingSeconds.ToString("0.0") + "초";
            _healFill.anchorMax = new Vector2(Mathf.Clamp01(heal.Progress01), 1f);
        }

        void Refresh(Combatant me)
        {
            CollectVicinity(me.transform.position);
            int hash = unchecked(Hash(me) * 31 + ExtraHash(me));
            if (!_dirty && hash == _hash) return;
            _dirty = false;
            _hash = hash;
            ShowVicinity();
            ShowBackpack(me);
            ShowGear(me);
            ShowWeapons(me);
            ShowThrowables(me);
        }

        // Public so a capture can show the state it just set up without waiting a frame.
        public void RefreshNow()
        {
            var me = Combatant.LocalPlayer;
            if (me == null || !IsOpen) return;
            _dirty = true;
            Refresh(me);
        }

        void CollectVicinity(Vector3 position)
        {
            _vicinity.Clear();
            float rangeSqr = Combatant.PickupRange * Combatant.PickupRange;
            foreach (var item in LootItem.All)
            {
                if (item == null || (item.transform.position - position).sqrMagnitude >= rangeSqr) continue;
                var me = Combatant.LocalPlayer;
                if (me != null && !PickupSight.Reachable(position, me.EyePosition, item.transform, me.transform, 0f)) continue; // ADR-276: not through walls or from another storey
                _vicinity.Add(item);
                if (_vicinity.Count == MaxVicinityRows) break;
            }
        }

        int Hash(Combatant combatant)
        {
            unchecked
            {
                int hash = combatant.ActiveSlot.Value;
                for (int i = 0; i < Loadout.SlotCount; i++)
                {
                    hash = hash * 31 + InventoryBackend.WeaponId(combatant, i);
                    hash = hash * 31 + InventoryBackend.WeaponMag(combatant, i);
                    for (int s = 0; s < Attachments.Slots; s++) hash = hash * 31 + InventoryBackend.OpticOf(combatant, Attachments.GearIndex(i, (AttachSlot)s));
                    hash = hash * 31 + InventoryBackend.FireMode(combatant, i).Length;
                }
                hash = hash * 31 + (combatant.Holstered.Value ? 1 : 0);
                for (int i = 0; i < InventoryBackend.AmmoTypeCount; i++) hash = hash * 31 + InventoryBackend.Reserve(combatant, i);
                hash = hash * 31 + combatant.Medkits.Value;
                hash = hash * 31 + InventoryBackend.Grenades(combatant);
                for (int i = 0; i < 2; i++)
                {
                    var gear = InventoryBackend.Gear(combatant, i);
                    hash = hash * 31 + (gear.Has ? 1 : 0);
                    hash = hash * 31 + gear.Tier;
                    hash = hash * 31 + gear.Durability;
                    hash = hash * 31 + gear.MaxDurability;
                }
                hash = hash * 31 + InventoryBackend.BackpackTier(combatant);
                hash = hash * 31 + (int)InventoryBackend.SelectedThrowable(combatant);
                hash = hash * 31 + (_sortRecent ? 1 : 0);
                hash = hash * 31 + _vicinity.Count;
                for (int i = 0; i < _vicinity.Count; i++)
                {
                    var item = _vicinity[i];
                    hash = hash * 31 + item.ObjectId;
                    hash = hash * 31 + item.Kind.Value;
                    hash = hash * 31 + item.WeaponId.Value;
                    hash = hash * 31 + item.Ammo.Value;
                    hash = hash * 31 + item.Amount.Value;
                    hash = hash * 31 + item.Optic.Value;
                }
                return hash;
            }
        }

        // What the rows added after the first inventory show (bandages, kits, smokes, keys, bag load).
        static int ExtraHash(Combatant me)
        {
            unchecked
            {
                int hash = InventoryBackend.Bandages(me);
                hash = hash * 31 + InventoryBackend.FirstAids(me);
                hash = hash * 31 + InventoryBackend.Smokes(me);
                hash = hash * 31 + InventoryBackend.Jerrycans(me);
                hash = hash * 31 + InventoryBackend.BoostCount(me, HealKind.Drink);
                hash = hash * 31 + InventoryBackend.BoostCount(me, HealKind.Pill);
                hash = hash * 31 + InventoryBackend.BoostCount(me, HealKind.Syringe);
                hash = hash * 31 + InventoryBackend.Flashes(me);
                hash = hash * 31 + InventoryBackend.Molotovs(me);
                hash = hash * 31 + InventoryBackend.Keys(me);
                hash = hash * 31 + InventoryBackend.CarryHash(me);
                hash = hash * 31 + InventoryBackend.BagOptics(me).GetHashCode();
                return hash;
            }
        }

        void ShowVicinity()
        {
            for (int i = 0; i < MaxVicinityRows; i++)
            {
                bool used = i < _vicinity.Count;
                _vicinityRows[i].SetActive(used);
                if (!used) continue;
                var item = _vicinity[i];
                string name = InventoryBackend.LootName(item);
                _vicinityNames[i].text = name;
                _vicinityAmounts[i].text = InventoryBackend.LootAmount(item);
                _vicinityIcons[i].Set(InventoryBackend.LootIconKey(item), name);
                _vicinityDrags[i].Configure(this, new InventoryDragSource(InventorySourceKind.Vicinity, item, i));
            }
            _vicinityEmpty.SetActive(_vicinity.Count == 0);
        }

        // How many the bag holds of a row.
        static int PackCount(Combatant me, int row)
        {
            if (row < InventoryBackend.AmmoTypeCount) return InventoryBackend.Reserve(me, row);
            switch (row)
            {
                case InventoryBackend.MedkitRow: return me.Medkits.Value;
                case InventoryBackend.GrenadeRow: return InventoryBackend.Grenades(me);
                case InventoryBackend.BandageRow: return InventoryBackend.Bandages(me);
                case InventoryBackend.FirstAidRow: return InventoryBackend.FirstAids(me);
                case InventoryBackend.SmokeRow: return InventoryBackend.Smokes(me);
                case InventoryBackend.JerrycanRow: return InventoryBackend.Jerrycans(me);
                case InventoryBackend.DrinkRow: return InventoryBackend.BoostCount(me, HealKind.Drink);
                case InventoryBackend.PillRow: return InventoryBackend.BoostCount(me, HealKind.Pill);
                case InventoryBackend.SyringeRow: return InventoryBackend.BoostCount(me, HealKind.Syringe);
                case InventoryBackend.FlashRow: return InventoryBackend.Flashes(me);
                default: return InventoryBackend.MolotovRow == row ? InventoryBackend.Molotovs(me) : 0;
            }
        }

        InventoryDragSource PackSource(int row)
        {
            if (row < InventoryBackend.AmmoTypeCount) return AmmoSource(row);
            switch (row)
            {
                case InventoryBackend.MedkitRow: return MedkitSource();
                case InventoryBackend.GrenadeRow: return GrenadeSource();
                case InventoryBackend.BandageRow: return BandageSource();
                case InventoryBackend.FirstAidRow: return FirstAidSource();
                case InventoryBackend.SmokeRow: return SmokeSource();
                case InventoryBackend.JerrycanRow: return JerrycanSource();
                case InventoryBackend.DrinkRow: return ExtraSource(InventorySourceKind.Drink);
                case InventoryBackend.PillRow: return ExtraSource(InventorySourceKind.Pill);
                case InventoryBackend.SyringeRow: return ExtraSource(InventorySourceKind.Syringe);
                case InventoryBackend.FlashRow: return ExtraSource(InventorySourceKind.Flash);
                default: return ExtraSource(InventorySourceKind.Molotov);
            }
        }

        void ShowBackpack(Combatant me)
        {
            // ADR-137: a row exists only while its count is above zero. Rows are sorted by kind (row order) or by when
            // the count last grew (the newest on top).
            _order.Clear();
            for (int row = 0; row < InventoryBackend.BackpackRows; row++)
            {
                int count = PackCount(me, row);
                if (count > _lastCount[row]) _recentStamp[row] = ++_stampSeq;
                _lastCount[row] = count;
                _packRows[row].SetActive(count > 0);
                _packCounts[row].text = "x" + count;
                _packDrags[row].Configure(this, PackSource(row));
                if (count > 0) _order.Add(row);
            }
            if (_sortRecent)
                _order.Sort((a, b) => _recentStamp[a] != _recentStamp[b] ? _recentStamp[b].CompareTo(_recentStamp[a]) : a.CompareTo(b));

            // Loose optics and attachments (ADR-082, ADR-100): one cell per id held, packed in id order.
            int cell = 0;
            for (int id = 1; id <= OpticBag.MaxId; id++)
            {
                int count = InventoryBackend.BagOpticCount(me, id);
                if (count <= 0) continue;
                string name = InventoryBackend.OpticName(id);
                _bagOpticIcons[cell].Set(ItemIcons.OpticKey(id), name);
                _bagOpticCounts[cell].text = count > 1 ? "x" + count : "";
                _bagOpticDrags[cell].Configure(this, BagOpticSource(id));
                _bagOpticCells[cell].SetActive(true);
                cell++;
            }
            for (int i = cell; i < OpticBag.MaxId; i++) _bagOpticCells[i].SetActive(false);
            _bagOpticTitle.gameObject.SetActive(cell > 0);

            // Compact layout (ADR-137): held rows stack from the top, then the attachments grid.
            float top = BagListTop;
            foreach (int row in _order)
            {
                ((RectTransform)_packRows[row].transform).anchoredPosition = new Vector2(Pad, -top);
                top += BagRowHeight + BagRowGap;
            }
            if (cell > 0)
            {
                top += 6f;
                _bagOpticTitle.rectTransform.anchoredPosition = new Vector2(Pad, -top);
                float gridTop = top + BagOpticLabel;
                for (int i = 0; i < cell; i++)
                {
                    var rect = (RectTransform)_bagOpticCells[i].transform;
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -(gridTop + (i / BagOpticColumns) * (BagOpticHeight + BagOpticGap)));
                }
            }
            _bagEmpty.SetActive(_order.Count == 0 && cell == 0);

            // The bag's load against its size; red when over (the server then refuses what does not fit).
            string carry = InventoryBackend.CarryLabel(me);
            float fraction = InventoryBackend.CarryFraction(me);
            _carryLabel.text = carry.Length > 0 ? carry : "가방 용량 —";
            _carryFill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            _carryFillImage.color = fraction > 0.999f ? UiKit.Danger : fraction > 0.85f ? UiKit.Accent : Durability;
        }

        void ShowGear(Combatant me)
        {
            for (int slot = 0; slot < GearSlots; slot++)
            {
                if (slot == BackpackGear)
                {
                    int tier = InventoryBackend.BackpackTier(me);
                    _gearNames[slot].text = tier > 0 ? LootNames.Of(LootKind.Backpack, 0, AmmoType.Pistol9, tier) : "가방";
                    _gearNames[slot].color = tier > 0 ? UiKit.Text : UiKit.TextDim;
                    _gearDetails[slot].text = tier > 0 ? "용량 " + InventoryBackend.CarryCapacity(me).ToString("0") : "비어 있음";
                    _gearFill[slot].anchorMax = new Vector2(tier > 0 ? tier / 3f : 0f, 1f);
                    _gearIcons[slot].Set(ItemIcons.BackpackKey(tier), "가방");
                    _gearBadgeBox[slot].SetActive(tier > 0);
                    _gearBadge[slot].text = tier > 0 ? "T" + tier : "";
                    _gearDrags[slot].Configure(this, new InventoryDragSource(InventorySourceKind.Backpack, null, slot));
                    continue;
                }
                var gear = InventoryBackend.Gear(me, slot);
                string baseName = slot == InventoryBackend.HelmetSlot ? "헬멧" : "조끼";
                _gearNames[slot].text = gear.Has ? baseName + " " + gear.Tier + "단계" : baseName;
                _gearNames[slot].color = gear.Has ? UiKit.Text : UiKit.TextDim;
                _gearDetails[slot].text = gear.Has ? "내구도  " + gear.Durability + " / " + gear.MaxDurability : "비어 있음";
                _gearFill[slot].anchorMax = new Vector2(Mathf.Clamp01(gear.Fraction), 1f);
                _gearIcons[slot].Set(gear.Has ? (slot == 0 ? "helmet_" : "vest_") + gear.Tier : "", baseName);
                _gearBadgeBox[slot].SetActive(gear.Has);
                _gearBadge[slot].text = gear.Has ? gear.Durability.ToString() : "";
                var kind = slot == 0 ? InventorySourceKind.Helmet : InventorySourceKind.Vest;
                _gearDrags[slot].Configure(this, new InventoryDragSource(kind, null, slot));
            }
        }

        static readonly AttachSlot[] PlaceOrder = { AttachSlot.Optic, AttachSlot.Muzzle, AttachSlot.Grip, AttachSlot.Mag, AttachSlot.Stock };

        void ShowWeapons(Combatant me)
        {
            for (int slot = 0; slot < Loadout.SlotCount; slot++)
            {
                if (slot == InventoryBackend.ThrowableSlot) continue; // ShowThrowables
                int id = InventoryBackend.WeaponId(me, slot);
                int mag = InventoryBackend.WeaponMag(me, slot);
                bool active = me.ActiveSlot.Value == slot && !me.Holstered.Value;
                var weapon = GameData.Weapons.Find(id);
                _weaponActive[slot].SetActive(active);
                if (slot < 3)
                {
                    ShowAttachSlots(me, slot, weapon);
                    _weaponHint[slot].text = weapon == null ? (slot == InventoryBackend.SidearmSlot ? "권총을 이곳으로 끌어 놓으세요" : "총기를 이곳으로 끌어 놓으세요") : "";
                }
                if (weapon == null)
                {
                    _weaponNames[slot].text = "비어 있음";
                    _weaponNames[slot].color = UiKit.TextDim;
                    _weaponAmmo[slot].text = slot == InventoryBackend.MeleeSlot ? "근접 무기를 이곳에 놓으세요" : "총기를 이곳에 놓으세요";
                    _weaponBadge[slot].text = slot == InventoryBackend.MeleeSlot ? "" : InventoryBackend.SlotLabel(slot);
                    _weaponIcons[slot].Set("", "총");
                    if (_weaponAmmoBig[slot] != null) { _weaponAmmoBig[slot].text = ""; _weaponAmmoSmall[slot].text = ""; }
                }
                else if (weapon.IsMelee)
                {
                    // ADR-073: no rounds or calibre.
                    _weaponNames[slot].text = weapon.Name;
                    _weaponNames[slot].color = active ? UiKit.Accent : UiKit.Text;
                    _weaponAmmo[slot].text = "피해 " + weapon.Damage.ToString("0") + "  ·  사거리 " + weapon.Range.ToString("0.0") + "m";
                    _weaponBadge[slot].text = "";
                    _weaponIcons[slot].Set(InventoryBackend.WeaponKey(id), weapon.Name);
                }
                else
                {
                    int ammoIndex = InventoryBackend.AmmoIndex(weapon);
                    string mode = InventoryBackend.FireMode(me, slot);
                    int reserve = InventoryBackend.Reserve(me, ammoIndex);
                    _weaponNames[slot].text = weapon.Name;
                    _weaponNames[slot].color = active ? UiKit.Accent : UiKit.Text;
                    _weaponAmmo[slot].text = mag + " / " + reserve + "발" + (mode.Length > 0 ? "  ·  " + mode : "");
                    _weaponBadge[slot].text = InventoryBackend.SlotLabel(slot) + "  ·  " + InventoryBackend.AmmoName(ammoIndex);
                    _weaponIcons[slot].Set(InventoryBackend.WeaponKey(id), weapon.Name);
                    if (_weaponAmmoBig[slot] != null)
                    {
                        _weaponAmmoBig[slot].text = mag.ToString();
                        _weaponAmmoSmall[slot].text = "/ " + reserve + (mode.Length > 0 ? "\n" + mode : "");
                    }
                }
                if (weapon == null || weapon.IsMelee) { if (_weaponAmmoBig[slot] != null) { _weaponAmmoBig[slot].text = ""; _weaponAmmoSmall[slot].text = ""; } }
                _weaponDrags[slot].Configure(this, WeaponSource(slot));
            }
        }

        void ShowAttachSlots(Combatant me, int slot, WeaponDef weapon)
        {
            bool gun = weapon != null && !weapon.IsMelee;
            for (int p = 0; p < AttachPlaces; p++)
            {
                int ci = slot * AttachPlaces + p;
                var place = (AttachSlot)p;
                bool supported = gun && InventoryBackend.WeaponTakesPlace(weapon.Id, place);
                _slotRoot[ci].SetActive(supported);
                if (!supported) continue;
                int gearIndex = Attachments.GearIndex(slot, place);
                int fitted = InventoryBackend.OpticOf(me, gearIndex);
                if (fitted != 0)
                {
                    _slotIcon[ci].Set(ItemIcons.OpticKey(fitted), InventoryBackend.OpticName(fitted));
                    _slotDrag[ci].Configure(this, new InventoryDragSource(InventorySourceKind.Optic, null, gearIndex));
                }
                else
                {
                    _slotIcon[ci].SetGhost(InventoryBackend.PlaceGhostKey(place));
                    _slotDrag[ci].Configure(this, new InventoryDragSource(InventorySourceKind.EmptyAttach, null, gearIndex));
                }
            }
        }

        // Slot 5: the selected kind with its count, and a chip for each kind carried (click one to select it).
        void ShowThrowables(Combatant me)
        {
            const int slot = InventoryBackend.ThrowableSlot;
            var selected = InventoryBackend.SelectedThrowable(me);
            int total = 0;
            for (var k = ThrowableKind.Frag; k <= ThrowableKind.Molotov; k++) total += InventoryBackend.ThrowableCount(me, k);
            bool active = me.ActiveSlot.Value == slot && !me.Holstered.Value;
            _weaponActive[slot].SetActive(active);
            int count = InventoryBackend.ThrowableCount(me, selected);
            bool has = total > 0;
            _weaponNames[slot].text = has ? Throwables.NameOf(selected) : "비어 있음";
            _weaponNames[slot].color = !has ? UiKit.TextDim : active ? UiKit.Accent : UiKit.Text;
            _weaponAmmo[slot].text = has ? "x" + count : "투척물 슬롯";
            _weaponBadge[slot].text = "";
            _weaponIcons[slot].Set(has ? InventoryBackend.ThrowIconKey(selected) : "", Throwables.NameOf(selected));
            _weaponAmmoBig[slot].text = has ? "x" + count : "";
            _weaponAmmoSmall[slot].text = has && count == 0 ? "없음" : "";
            _weaponDrags[slot].Configure(this, new InventoryDragSource(InventorySourceKind.Weapon, null, slot));
            for (int i = 0; i < 4; i++)
            {
                var kind = (ThrowableKind)i;
                int n = InventoryBackend.ThrowableCount(me, kind);
                _pipRoot[i].SetActive(n > 0);
                if (n <= 0) continue;
                _pipIcon[i].Set(InventoryBackend.ThrowIconKey(kind), Throwables.NameOf(kind));
                _pipCount[i].text = "x" + n;
                _pipSelect[i].SetActive(kind == selected);
                _pipDrag[i].Configure(this, new InventoryDragSource(InventoryBackend.SourceKindOf(kind), null, 0));
            }
            // The chips pack to the left.
            int shown = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!_pipRoot[i].activeSelf) continue;
                ((RectTransform)_pipRoot[i].transform).anchoredPosition = new Vector2(PipX + shown * (PipSize + PipGap), PipY);
                shown++;
            }
        }

        // ---- Interaction paths (EventSystem handlers and PlayMode tests both call these) ----

        public InventoryDragSource VicinitySource(int index) => index >= 0 && index < _vicinity.Count
            ? new InventoryDragSource(InventorySourceKind.Vicinity, _vicinity[index], index) : default;
        public InventoryDragSource AmmoSource(int ammoIndex) => ammoIndex >= 0 && ammoIndex < InventoryBackend.AmmoTypeCount
            ? new InventoryDragSource(InventorySourceKind.Ammo, null, ammoIndex) : default;
        public InventoryDragSource MedkitSource() => new InventoryDragSource(InventorySourceKind.Medkit, null, 0);
        public InventoryDragSource GrenadeSource() => new InventoryDragSource(InventorySourceKind.Grenade, null, 0);
        public InventoryDragSource BandageSource() => new InventoryDragSource(InventorySourceKind.Bandage, null, 0);
        public InventoryDragSource FirstAidSource() => new InventoryDragSource(InventorySourceKind.FirstAid, null, 0);
        public InventoryDragSource SmokeSource() => new InventoryDragSource(InventorySourceKind.Smoke, null, 0);
        public InventoryDragSource ExtraSource(InventorySourceKind kind) => new InventoryDragSource(kind, null, 0);
        public InventoryDragSource JerrycanSource() => new InventoryDragSource(InventorySourceKind.Jerrycan, null, 0);
        public InventoryDragSource BagOpticSource(int opticId) => opticId >= 1 && opticId <= OpticBag.MaxId
            ? new InventoryDragSource(InventorySourceKind.BagOptic, null, opticId) : default;
        public InventoryDragSource WeaponSource(int slot) => slot >= 0 && slot < Loadout.SlotCount
            ? new InventoryDragSource(InventorySourceKind.Weapon, null, slot) : default;
        // A fitted attachment of a weapon card (gear index = Attachments.GearIndex(slot, place)).
        public InventoryDragSource AttachedSource(int weaponSlot, AttachSlot place) =>
            new InventoryDragSource(InventorySourceKind.Optic, null, Attachments.GearIndex(weaponSlot, place));
        public InventoryDragSource GearSource(int slot) => slot == InventoryBackend.HelmetSlot
            ? new InventoryDragSource(InventorySourceKind.Helmet, null, slot)
            : slot == InventoryBackend.VestSlot ? new InventoryDragSource(InventorySourceKind.Vest, null, slot) : default;

        public bool DropOnBag(InventoryDragSource source)
        {
            if (!CanDropOnBag(source)) return false;
            if (source.Kind == InventorySourceKind.Optic) InventoryBackend.DetachOpticToBag(Combatant.LocalPlayer, source.Index);
            else InventoryBackend.Pickup(Combatant.LocalPlayer, source.Loot);
            _dirty = true;
            return true;
        }

        public bool DropOnGround(InventoryDragSource source)
        {
            if (!CanDropOnGround(source)) return false;
            var me = Combatant.LocalPlayer;
            if (DropExtra(source, me))
            {
                _dirty = true;
                return true;
            }
            switch (source.Kind)
            {
                case InventorySourceKind.Ammo: InventoryBackend.DropAmmo(me, source.Index); break;
                case InventorySourceKind.Medkit: InventoryBackend.DropMedkit(me); break;
                case InventorySourceKind.Grenade: InventoryBackend.DropGrenade(me); break;
                case InventorySourceKind.Optic: InventoryBackend.DropOptic(me, source.Index); break;
                case InventorySourceKind.BagOptic: InventoryBackend.DropBagOptic(me, source.Index); break;
                case InventorySourceKind.Weapon: InventoryBackend.DropWeapon(me, source.Index); break;
                case InventorySourceKind.Helmet: InventoryBackend.DropArmor(me, InventoryBackend.HelmetSlot); break;
                case InventorySourceKind.Vest: InventoryBackend.DropArmor(me, InventoryBackend.VestSlot); break;
                default: return false;
            }
            _dirty = true;
            return true;
        }

        // Bandages, kits and smokes go one at a time.
        static bool DropExtra(InventoryDragSource source, Combatant me)
        {
            switch (source.Kind)
            {
                case InventorySourceKind.Bandage: InventoryBackend.DropHealing(me, HealKind.Bandage); return true;
                case InventorySourceKind.FirstAid: InventoryBackend.DropHealing(me, HealKind.FirstAid); return true;
                case InventorySourceKind.Smoke: InventoryBackend.DropSmoke(me); return true;
                case InventorySourceKind.Jerrycan: InventoryBackend.DropJerrycan(me); return true;
                case InventorySourceKind.Drink:
                case InventorySourceKind.Pill:
                case InventorySourceKind.Syringe: InventoryBackend.DropHealing(me, InventoryBackend.BoostKindOf(source.Kind)); return true;
                case InventorySourceKind.Flash: InventoryBackend.DropFlash(me); return true;
                case InventorySourceKind.Molotov: InventoryBackend.DropMolotov(me); return true;
                default: return false;
            }
        }

        // How many the bag holds of a row added after the first inventory; false for every other source.
        static bool TryExtraCount(InventoryDragSource source, Combatant me, out int count)
        {
            switch (source.Kind)
            {
                case InventorySourceKind.Bandage: count = InventoryBackend.Bandages(me); return true;
                case InventorySourceKind.FirstAid: count = InventoryBackend.FirstAids(me); return true;
                case InventorySourceKind.Smoke: count = InventoryBackend.Smokes(me); return true;
                case InventorySourceKind.Jerrycan: count = InventoryBackend.Jerrycans(me); return true;
                case InventorySourceKind.Drink:
                case InventorySourceKind.Pill:
                case InventorySourceKind.Syringe: count = InventoryBackend.BoostCount(me, InventoryBackend.BoostKindOf(source.Kind)); return true;
                case InventorySourceKind.Flash: count = InventoryBackend.Flashes(me); return true;
                case InventorySourceKind.Molotov: count = InventoryBackend.Molotovs(me); return true;
                default: count = 0; return false;
            }
        }

        public bool DropOutside(InventoryDragSource source) => DropOnGround(source);

        public bool DropOnWeaponSlot(InventoryDragSource source, int slot)
        {
            if (!CanDropOnWeaponSlot(source, slot)) return false;
            var me = Combatant.LocalPlayer;
            if (source.Kind == InventorySourceKind.Vicinity && InventoryBackend.IsOptic(source.Loot)) InventoryBackend.AttachOpticFromGround(me, source.Loot, slot);
            else if (source.Kind == InventorySourceKind.BagOptic) InventoryBackend.AttachOpticFromBag(me, source.Index, slot);
            else if (source.Kind == InventorySourceKind.Vicinity) InventoryBackend.PickupToSlot(me, source.Loot, slot);
            else if (source.Kind == InventorySourceKind.Weapon) InventoryBackend.SwapWeaponSlots(me, source.Index, slot);
            else return false;
            _dirty = true;
            return true;
        }

        // #179: onto one attachment slot of a weapon card (gear index); it must be that place on that gun.
        public bool DropOnAttachSlot(InventoryDragSource source, int gearIndex)
        {
            if (!CanDropOnAttachSlot(source, gearIndex)) return false;
            var me = Combatant.LocalPlayer;
            int slot = Attachments.WeaponSlotOf(gearIndex);
            if (source.Kind == InventorySourceKind.BagOptic) InventoryBackend.AttachOpticFromBag(me, source.Index, slot);
            else InventoryBackend.AttachOpticFromGround(me, source.Loot, slot);
            _dirty = true;
            return true;
        }

        // #179: onto the throwable slot: the kind becomes what slot 5 throws.
        public bool DropOnThrowable(InventoryDragSource source)
        {
            if (!CanDropOnThrowable(source)) return false;
            InventoryBackend.SetThrowable(Combatant.LocalPlayer, InventoryBackend.ThrowKindOf(source.Kind).Value, false);
            _dirty = true;
            return true;
        }

        public bool DropOnGear(InventoryDragSource source, int slot)
        {
            if (!CanDropOnGear(source, slot)) return false;
            InventoryBackend.EquipArmor(Combatant.LocalPlayer, source.Loot, slot);
            _dirty = true;
            return true;
        }

        public bool BeginDrag(InventoryDragSource source)
        {
            if (!CanDrag(source)) return false;
            CancelDrag();
            _dragSource = source;
            _dropHandled = false;
            _ghostPop = 0f;
            SourcePresentation(source, out string key, out string name);
            _dragGhostIcon.Set(key, name);
            _dragGhost.SetActive(true);
            _dragGhost.transform.SetAsLastSibling();
            SetDropHighlights(true);
            return true;
        }

        public void MoveDrag(Vector2 screenPosition, Camera eventCamera)
        {
            if (!Dragging) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_canvas.transform, screenPosition, eventCamera, out var local);
            _dragGhostRect.anchoredPosition = local + new Vector2(22f, -22f); // the slot under the pointer stays visible
        }

        public void EndDrag(Vector2 screenPosition, Camera eventCamera)
        {
            if (!Dragging) return;
            if (!_dropHandled && !RectTransformUtility.RectangleContainsScreenPoint(_panel, screenPosition, eventCamera)) DropOutside(_dragSource);
            CancelDrag();
        }

        public void CancelDrag()
        {
            _dragSource = default;
            _dropHandled = false;
            if (_dragGhost != null) _dragGhost.SetActive(false);
            SetDropHighlights(false);
        }

        public void Activate(InventoryDragSource source, int clickCount) =>
            Activate(source, clickCount, Keyboard.current != null && Keyboard.current.ctrlKey.isPressed);

        public void Activate(InventoryDragSource source, int clickCount, bool ctrl)
        {
            var me = Combatant.LocalPlayer;
            if (me == null) return;
            // ADR-144: Ctrl + click splits half an ammo stack onto the ground.
            if (clickCount == 1 && source.Kind == InventorySourceKind.Ammo && ctrl)
            {
                InventoryBackend.DropAmmoHalf(me, source.Index);
                _dirty = true;
                return;
            }
            // ADR-144: a double click fits a loose attachment to the best gun (like the right click).
            if (clickCount >= 2 && (source.Kind == InventorySourceKind.BagOptic || (source.Kind == InventorySourceKind.Vicinity && InventoryBackend.IsOptic(source.Loot))))
            {
                QuickDrop(source);
                return;
            }
            var throwKind = InventoryBackend.ThrowKindOf(source.Kind);
            if (throwKind != null)
            {
                // #179: a click sets the kind slot 5 throws; a double click also takes it out.
                InventoryBackend.SetThrowable(me, throwKind.Value, clickCount >= 2);
                _dirty = true;
                return;
            }
            if (source.Kind == InventorySourceKind.Vicinity && clickCount >= 2) InventoryBackend.Pickup(me, source.Loot);
            else if (clickCount >= 2 && InventoryBackend.RightClickHeal(source.Kind, out var heal)) InventoryBackend.UseHealing(me, heal);
            else if (source.Kind == InventorySourceKind.Jerrycan && clickCount >= 2) InventoryBackend.UseJerrycan(me);
            else if (source.Kind == InventorySourceKind.Weapon && source.Index == InventoryBackend.ThrowableSlot) InventoryBackend.SelectSlot(me, source.Index);
            else if (source.Kind == InventorySourceKind.Weapon && InventoryBackend.WeaponId(me, source.Index) != 0)
                InventoryBackend.SelectSlot(me, source.Index);
        }

        // Right click. Attachments: a loose one is fitted to the best gun, a fitted one goes to the bag; throwables are
        // set as the throwable slot's kind; ground items are picked up; the rest drops (healing items are used).
        public void QuickDrop(InventoryDragSource source)
        {
            if (_suppressQuickDropUntilRightRelease)
            {
                if (Mouse.current == null || !Mouse.current.rightButton.isPressed) _suppressQuickDropUntilRightRelease = false;
                return;
            }
            if (Dragging) { CancelDrag(); return; }
            var me = Combatant.LocalPlayer;
            if (me == null) return;
            var throwKind = InventoryBackend.ThrowKindOf(source.Kind);
            if (throwKind != null) { InventoryBackend.SetThrowable(me, throwKind.Value, false); _dirty = true; return; }
            switch (source.Kind)
            {
                case InventorySourceKind.Vicinity:
                    if (InventoryBackend.IsOptic(source.Loot))
                    {
                        int gun = InventoryBackend.AutoAttachSlot(me, source.Loot.Optic.Value);
                        if (gun >= 0) { InventoryBackend.AttachOpticFromGround(me, source.Loot, gun); _dirty = true; return; }
                    }
                    InventoryBackend.Pickup(me, source.Loot);
                    _dirty = true;
                    return;
                case InventorySourceKind.BagOptic:
                    AutoEquipFromBag(source.Index);
                    return;
                case InventorySourceKind.Optic:
                    if (CanDrag(source)) { InventoryBackend.DetachOpticToBag(me, source.Index); _dirty = true; }
                    return;
                case InventorySourceKind.EmptyAttach:
                case InventorySourceKind.Backpack:
                    return;
            }
            if (InventoryBackend.RightClickHeal(source.Kind, out var heal)) InventoryBackend.UseHealing(me, heal); // the server refuses when none is left
            else DropOnGround(source);
        }

        // Fits a loose attachment of the bag to the best gun: the one in hand first. False when no gun takes it.
        public bool AutoEquipFromBag(int opticId)
        {
            var me = Combatant.LocalPlayer;
            if (me == null || InventoryBackend.BagOpticCount(me, opticId) <= 0) return false;
            int slot = InventoryBackend.AutoAttachSlot(me, opticId);
            if (slot < 0) return false;
            InventoryBackend.AttachOpticFromBag(me, opticId, slot);
            _dirty = true;
            return true;
        }

        public void CancelDragFromPointer()
        {
            _suppressQuickDropUntilRightRelease = true;
            CancelDrag();
        }

        // Sort toggle of the carried list: by kind or by when it was picked up.
        public void SetSort(bool recent)
        {
            _sortRecent = recent;
            _dirty = true;
            UiKit.SetTab(_sortKindLabel, _sortKindLine, !recent);
            UiKit.SetTab(_sortRecentLabel, _sortRecentLine, recent);
        }

        internal bool DropOn(InventoryDropKind kind, int index, InventoryDragSource source)
        {
            switch (kind)
            {
                case InventoryDropKind.Bag: return DropOnBag(source);
                case InventoryDropKind.Ground: return DropOnGround(source);
                case InventoryDropKind.Weapon: return DropOnWeaponSlot(source, index);
                case InventoryDropKind.Gear: return DropOnGear(source, index);
                case InventoryDropKind.Throwable: return DropOnThrowable(source);
                case InventoryDropKind.Attach: return DropOnAttachSlot(source, index);
                default: return false;
            }
        }

        internal void MarkDropHandled() => _dropHandled = true;

        bool CanDrag(InventoryDragSource source)
        {
            var me = Combatant.LocalPlayer;
            if (me == null || !source.IsValid) return false;
            if (TryExtraCount(source, me, out int extra)) return extra > 0;
            switch (source.Kind)
            {
                case InventorySourceKind.Vicinity: return source.Loot != null && source.Loot.IsSpawned;
                case InventorySourceKind.Ammo: return InventoryBackend.Reserve(me, source.Index) > 0;
                case InventorySourceKind.Medkit: return me.Medkits.Value > 0;
                case InventorySourceKind.Grenade: return InventoryBackend.Grenades(me) > 0;
                case InventorySourceKind.Weapon: return source.Index != InventoryBackend.ThrowableSlot && InventoryBackend.WeaponId(me, source.Index) != 0;
                case InventorySourceKind.Optic: return InventoryBackend.OpticOf(me, source.Index) != 0;
                case InventorySourceKind.BagOptic: return InventoryBackend.BagOpticCount(me, source.Index) > 0;
                case InventorySourceKind.Helmet: return InventoryBackend.Gear(me, InventoryBackend.HelmetSlot).Has;
                case InventorySourceKind.Vest: return InventoryBackend.Gear(me, InventoryBackend.VestSlot).Has;
                default: return false;
            }
        }

        bool CanDropOnBag(InventoryDragSource source) =>
            source.Kind == InventorySourceKind.Vicinity ? source.Loot != null && source.Loot.IsSpawned
            : source.Kind == InventorySourceKind.Optic && CanDrag(source); // ADR-082: off the weapon into the bag

        bool CanDropOnGround(InventoryDragSource source)
        {
            if (source.Kind == InventorySourceKind.Vicinity) return false;
            return CanDrag(source);
        }

        bool CanDropOnWeaponSlot(InventoryDragSource source, int slot)
        {
            if (slot < 0 || slot >= Loadout.SlotCount) return false;
            var me = Combatant.LocalPlayer;
            if (me == null) return false;
            if (source.Kind == InventorySourceKind.Weapon)
            {
                if (source.Index == slot || !CanDrag(source)) return false;
                // Both weapons must fit the other's slot (a pistol only goes to the sidearm slot).
                int mine = InventoryBackend.WeaponId(me, source.Index), theirs = InventoryBackend.WeaponId(me, slot);
                return InventoryBackend.SlotAccepts(slot, mine) && (theirs == 0 || InventoryBackend.SlotAccepts(source.Index, theirs));
            }
            if (source.Kind == InventorySourceKind.BagOptic) return CanDrag(source) && InventoryBackend.CanAttachOptic(me, slot, source.Index);
            if (source.Kind != InventorySourceKind.Vicinity || source.Loot == null || !source.Loot.IsSpawned) return false;
            var kind = (LootKind)source.Loot.Kind.Value;
            if (kind == LootKind.Weapon) return InventoryBackend.SlotAccepts(slot, source.Loot.WeaponId.Value);
            if (kind == LootKind.Optic) return InventoryBackend.CanAttachOptic(me, slot, source.Loot.Optic.Value);
            return false;
        }

        // A loose attachment (bag or ground) on one slot of a gun card: right gun and right place.
        public bool CanDropOnAttachSlot(InventoryDragSource source, int gearIndex)
        {
            var me = Combatant.LocalPlayer;
            if (me == null) return false;
            int id;
            if (source.Kind == InventorySourceKind.BagOptic) { if (!CanDrag(source)) return false; id = source.Index; }
            else if (source.Kind == InventorySourceKind.Vicinity && InventoryBackend.IsOptic(source.Loot) && source.Loot.IsSpawned) id = source.Loot.Optic.Value;
            else return false;
            return InventoryBackend.CanFitPlace(me, Attachments.WeaponSlotOf(gearIndex), Attachments.SlotOfIndex(gearIndex), id);
        }

        bool CanDropOnThrowable(InventoryDragSource source)
        {
            var kind = InventoryBackend.ThrowKindOf(source.Kind);
            return kind != null && InventoryBackend.ThrowableCount(Combatant.LocalPlayer, kind.Value) > 0;
        }

        bool CanDropOnGear(InventoryDragSource source, int slot)
        {
            if (slot == BackpackGear)
                return source.Kind == InventorySourceKind.Vicinity && source.Loot != null && source.Loot.IsSpawned &&
                       InventoryBackend.CanEquipBackpack(Combatant.LocalPlayer, source.Loot);
            return slot >= 0 && slot < 2 && source.Kind == InventorySourceKind.Vicinity && source.Loot != null && source.Loot.IsSpawned &&
                   InventoryBackend.CanEquipArmor(Combatant.LocalPlayer, source.Loot, slot);
        }

        bool CanDropOn(InventoryDropKind kind, int index, InventoryDragSource source)
        {
            switch (kind)
            {
                case InventoryDropKind.Bag: return CanDropOnBag(source);
                case InventoryDropKind.Ground: return CanDropOnGround(source);
                case InventoryDropKind.Weapon: return CanDropOnWeaponSlot(source, index);
                case InventoryDropKind.Gear: return CanDropOnGear(source, index);
                case InventoryDropKind.Throwable: return CanDropOnThrowable(source);
                case InventoryDropKind.Attach: return CanDropOnAttachSlot(source, index);
                default: return false;
            }
        }

        static bool IsAttachmentSource(InventoryDragSource source) =>
            source.Kind == InventorySourceKind.BagOptic || source.Kind == InventorySourceKind.Optic ||
            (source.Kind == InventorySourceKind.Vicinity && InventoryBackend.IsOptic(source.Loot));

        void SetDropHighlights(bool active)
        {
            bool attachment = active && IsAttachmentSource(_dragSource);
            for (int i = 0; i < _dropZones.Count; i++)
            {
                var zone = _dropZones[i];
                if (zone.Kind == InventoryDropKind.Attach)
                {
                    // Only an attachment in hand lights the slots: green where it fits, a red pulse where it does not.
                    zone.SetDragState(attachment, attachment && CanDropOn(zone.Kind, zone.Index, _dragSource));
                    continue;
                }
                zone.SetDragState(active, active && CanDropOn(zone.Kind, zone.Index, _dragSource));
            }
        }

        // Test and capture reads of what the drop zones show during a drag.
        public bool AttachSlotShowsInvalid(int weaponSlot, AttachSlot place)
        {
            foreach (var zone in _dropZones)
                if (zone.Kind == InventoryDropKind.Attach && zone.Index == Attachments.GearIndex(weaponSlot, place)) return zone.ShowsInvalid;
            return false;
        }

        public bool AttachSlotShowsValid(int weaponSlot, AttachSlot place)
        {
            foreach (var zone in _dropZones)
                if (zone.Kind == InventoryDropKind.Attach && zone.Index == Attachments.GearIndex(weaponSlot, place)) return zone.ShowsValid;
            return false;
        }

        public bool AttachSlotVisible(int weaponSlot, AttachSlot place) => _slotRoot[weaponSlot * AttachPlaces + (int)place].activeSelf;
        public string AttachSlotIconKey(int weaponSlot, AttachSlot place) => _slotIcon[weaponSlot * AttachPlaces + (int)place].Key;
        public bool AttachSlotIsGhost(int weaponSlot, AttachSlot place) => _slotIcon[weaponSlot * AttachPlaces + (int)place].IsGhost;
        public InventoryDragSource AttachSlotSource(int weaponSlot, AttachSlot place) => _slotDrag[weaponSlot * AttachPlaces + (int)place] != null
            ? SourceOfSlot(weaponSlot, place) : default;

        InventoryDragSource SourceOfSlot(int weaponSlot, AttachSlot place)
        {
            var me = Combatant.LocalPlayer;
            int gearIndex = Attachments.GearIndex(weaponSlot, place);
            return InventoryBackend.OpticOf(me, gearIndex) != 0
                ? new InventoryDragSource(InventorySourceKind.Optic, null, gearIndex)
                : new InventoryDragSource(InventorySourceKind.EmptyAttach, null, gearIndex);
        }

        void SourcePresentation(InventoryDragSource source, out string key, out string name)
        {
            var me = Combatant.LocalPlayer;
            key = "";
            name = "아이템";
            switch (source.Kind)
            {
                case InventorySourceKind.Vicinity:
                    key = InventoryBackend.LootIconKey(source.Loot);
                    name = InventoryBackend.LootName(source.Loot);
                    break;
                case InventorySourceKind.Ammo:
                    key = InventoryBackend.AmmoKey(source.Index);
                    name = InventoryBackend.AmmoName(source.Index);
                    break;
                case InventorySourceKind.Optic:
                    int opticId = InventoryBackend.OpticOf(me, source.Index);
                    key = ItemIcons.OpticKey(opticId);
                    name = InventoryBackend.OpticName(opticId);
                    break;
                case InventorySourceKind.BagOptic:
                    key = ItemIcons.OpticKey(source.Index);
                    name = InventoryBackend.OpticName(source.Index);
                    break;
                case InventorySourceKind.EmptyAttach:
                    var place = Attachments.SlotOfIndex(source.Index);
                    key = InventoryBackend.PlaceGhostKey(place);
                    name = Attachments.PlaceName(place);
                    break;
                case InventorySourceKind.Backpack:
                    int bag = InventoryBackend.BackpackTier(me);
                    key = ItemIcons.BackpackKey(bag);
                    name = "가방";
                    break;
                case InventorySourceKind.Medkit: key = "medkit"; name = "구급상자"; break;
                case InventorySourceKind.Grenade: key = "grenade"; name = ThrowableRules.GrenadeName; break;
                case InventorySourceKind.Weapon:
                    if (source.Index == InventoryBackend.ThrowableSlot)
                    {
                        var kind = InventoryBackend.SelectedThrowable(me);
                        key = InventoryBackend.ThrowIconKey(kind);
                        name = Throwables.NameOf(kind);
                        break;
                    }
                    int id = InventoryBackend.WeaponId(me, source.Index);
                    var weapon = GameData.Weapons.Find(id);
                    key = InventoryBackend.WeaponKey(id);
                    name = weapon != null ? weapon.Name : "총";
                    break;
                case InventorySourceKind.Helmet:
                    var helmet = InventoryBackend.Gear(me, InventoryBackend.HelmetSlot);
                    key = helmet.Has ? "helmet_" + helmet.Tier : "";
                    name = "헬멧";
                    break;
                case InventorySourceKind.Vest:
                    var vest = InventoryBackend.Gear(me, InventoryBackend.VestSlot);
                    key = vest.Has ? "vest_" + vest.Tier : "";
                    name = "조끼";
                    break;
                case InventorySourceKind.Bandage: key = "bandage"; name = HealRules.BandageName; break;
                case InventorySourceKind.FirstAid: key = "firstaid"; name = HealRules.FirstAidName; break;
                case InventorySourceKind.Smoke: key = "smoke"; name = ThrowableRules.SmokeName; break;
                case InventorySourceKind.Jerrycan: key = "jerrycan"; name = FuelRules.JerrycanName; break;
                case InventorySourceKind.Drink: key = "drink"; name = HealRules.DrinkName; break;
                case InventorySourceKind.Pill: key = "pill"; name = HealRules.PillName; break;
                case InventorySourceKind.Syringe: key = "syringe"; name = HealRules.SyringeName; break;
                case InventorySourceKind.Flash: key = "flash"; name = ThrowableRules.FlashName; break;
                case InventorySourceKind.Molotov: key = "molotov"; name = ThrowableRules.MolotovName; break;
            }
        }

        // ---- Tooltip: name, type line, stat lines, with the item's picture ----

        public void ShowTooltip(InventoryDragSource source, Vector2 screenPosition, Camera eventCamera)
        {
            string description = source.Kind == InventorySourceKind.Weapon && source.Index == InventoryBackend.ThrowableSlot
                ? DescribeThrowableSlot(Combatant.LocalPlayer)
                : InventoryBackend.Describe(source, Combatant.LocalPlayer);
            if (description.Length == 0) return;
            SourcePresentation(source, out string key, out string name);
            var lines = description.Split('\n');
            var parts = new List<string>();
            for (int i = 1; i < lines.Length; i++) parts.AddRange(lines[i].Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries));
            _tooltipTitle.text = lines[0];
            _tooltipType.text = parts.Count > 0 ? parts[0] : "";
            _tooltipBody.text = parts.Count > 1 ? string.Join("\n", parts.GetRange(1, parts.Count - 1)) : "";
            if (source.Kind == InventorySourceKind.EmptyAttach) _tooltipIcon.SetGhost(key); else _tooltipIcon.Set(key, name);
            float barsHeight = ShowTooltipBars(InventoryBackend.GunOf(source, Combatant.LocalPlayer, out int packed), packed);
            bool hasBody = _tooltipBody.text.Length > 0;
            float bodyHeight = hasBody ? _tooltipBody.preferredHeight : 0f;
            _tooltipRule.gameObject.SetActive(hasBody || barsHeight > 0f);
            var bodyRect = _tooltipBody.rectTransform;
            bodyRect.sizeDelta = new Vector2(bodyRect.sizeDelta.x, bodyHeight + 2f);
            bodyRect.anchoredPosition = new Vector2(bodyRect.anchoredPosition.x, -(TooltipHeader + 4f + barsHeight));
            _tooltipRect.sizeDelta = new Vector2(TooltipWidth, TooltipHeader + barsHeight + (hasBody ? bodyHeight + 22f : 6f));
            _tooltipWant = true;
            _tooltip.SetActive(true);
            _tooltip.transform.SetAsLastSibling();
            PlaceTooltip(screenPosition, eventCamera);
        }

        // Fills the bars for a gun (null hides them); returns the height they take.
        float ShowTooltipBars(WeaponDef gun, int packed)
        {
            _tooltipBars.gameObject.SetActive(gun != null);
            if (gun == null) { TooltipBars = ""; return 0f; }
            var bars = WeaponStats.Bars(gun, Attachments.ModsOf(packed, OpticRules.Current));
            var seen = new List<string>();
            for (int i = 0; i < TooltipBarCount; i++)
            {
                _tooltipBarLabels[i].text = bars[i].Label;
                _tooltipBarValues[i].text = bars[i].Value;
                _tooltipBarFills[i].anchorMax = new Vector2(Mathf.Max(0.02f, bars[i].Fill), 1f);
                seen.Add(bars[i].Label + " " + bars[i].Value);
            }
            TooltipBars = string.Join(" | ", seen);
            return TooltipBarRow * TooltipBarCount + 10f;
        }

        string DescribeThrowableSlot(Combatant me)
        {
            var kind = InventoryBackend.SelectedThrowable(me);
            int total = 0;
            for (var k = ThrowableKind.Frag; k <= ThrowableKind.Molotov; k++) total += InventoryBackend.ThrowableCount(me, k);
            if (total == 0) return "투척 칸\n비어 있음 · 투척물을 끌어 놓거나 우클릭";
            return Throwables.NameOf(kind) + "\n투척 칸 · 보유 " + InventoryBackend.ThrowableCount(me, kind) + "개 · 클릭 또는 5: 다음 종류";
        }

        void PlaceTooltip(Vector2 screenPosition, Camera eventCamera)
        {
            var canvasRect = (RectTransform)_canvas.transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, eventCamera, out var local);
            local += new Vector2(22f, -22f);
            var size = _tooltipRect.rect.size;
            local.x = Mathf.Min(local.x, canvasRect.rect.xMax - size.x - 8f);
            if (local.x < canvasRect.rect.xMin + 8f) local.x = canvasRect.rect.xMin + 8f;
            // Flip above the pointer when it would run off the bottom.
            if (local.y - size.y < canvasRect.rect.yMin + 8f) local.y = local.y + size.y + 44f;
            local.y = Mathf.Min(local.y, canvasRect.rect.yMax - 8f);
            _tooltipRect.anchoredPosition = local;
        }

        public void HideTooltip() => HideTooltip(false);
        public void HideTooltip(bool now) => HideTooltipCore(now);

        void HideTooltipCore(bool now)
        {
            _tooltipWant = false;
            if (now && _tooltip != null)
            {
                _tooltipAlpha = 0f;
                _tooltipGroup.alpha = 0f;
                _tooltip.SetActive(false);
            }
        }

        void AnimateTooltip()
        {
            if (_tooltip == null || !_tooltip.activeSelf) return;
            _tooltipAlpha = Mathf.MoveTowards(_tooltipAlpha, _tooltipWant ? 1f : 0f, Time.unscaledDeltaTime * (_tooltipWant ? 9f : 14f));
            _tooltipGroup.alpha = _tooltipAlpha;
            if (!_tooltipWant && _tooltipAlpha <= 0f) _tooltip.SetActive(false);
        }

        // ---- Legacy public click hooks kept for existing tests and callers ----

        public void ClickVicinity(int index) => InventoryBackend.Pickup(Combatant.LocalPlayer, VicinityItem(index));
        public void ClickEquip(int slot) => InventoryBackend.SelectSlot(Combatant.LocalPlayer, slot);
        public void ClickDropWeapon(int slot) => InventoryBackend.DropWeapon(Combatant.LocalPlayer, slot);
        public void ClickDropAmmo(int ammoType) => InventoryBackend.DropAmmo(Combatant.LocalPlayer, ammoType);
        public void ClickUseMedkit() => InventoryBackend.UseMedkit(Combatant.LocalPlayer);
        public void ClickDropMedkit() => InventoryBackend.DropMedkit(Combatant.LocalPlayer);

        // ---- Read-only hooks ----

        public LootItem VicinityItem(int index) => index >= 0 && index < _vicinity.Count ? _vicinity[index] : null;
        public string VicinityText(int index) => _vicinityNames[index].text + " " + _vicinityAmounts[index].text;
        public string BackpackText(int row) => _packNames[row].text + " " + _packCounts[row].text;
        public bool PackRowVisible(int row) => _packRows[row].activeSelf;
        public float PackRowTop(int row) => -((RectTransform)_packRows[row].transform).anchoredPosition.y;
        public string CarryText => _carryLabel != null ? _carryLabel.text : "";
        public string GearCardText(int slot) => _gearNames[slot].text + "\n" + _gearDetails[slot].text;
        public string WeaponCardText(int slot) => _weaponNames[slot].text + "\n" + _weaponAmmo[slot].text + " " + _weaponBadge[slot].text;
        public bool HealBarVisible => _healBox != null && _healBox.activeSelf;
        public bool ThrowChipVisible(ThrowableKind kind) => _pipRoot[(int)kind].activeSelf;
        public bool ThrowChipSelected(ThrowableKind kind) => _pipRoot[(int)kind].activeSelf && _pipSelect[(int)kind].activeSelf;
        public InventoryDragSource ThrowChipSource(ThrowableKind kind) => new InventoryDragSource(InventoryBackend.SourceKindOf(kind), null, 0);
        public InventoryDragSource ThrowSlotSource() => new InventoryDragSource(InventorySourceKind.Weapon, null, InventoryBackend.ThrowableSlot);
        public bool DropOnThrowableSlot(InventoryDragSource source) => DropOnThrowable(source);
    }
}
