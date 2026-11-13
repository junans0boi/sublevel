using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Four-zone Tab inventory. Presentation reads replicated state; every mutation goes through InventoryBackend.
    public class InventoryScreen : MonoBehaviour
    {
        public const int MaxVicinityRows = 12;

        const int SortingOrder = 100;
        const float PanelWidth = 1680f, PanelHeight = 900f;
        const float ColumnHeight = 810f, ColumnTop = 70f, Gap = 20f;
        const float GroundWidth = 340f, BagWidth = 360f, GearWidth = 360f, WeaponWidth = 560f;
        const float Pad = 14f, HeaderHeight = 54f;
        const float GroundRowHeight = 54f, GroundRowGap = 6f;
        const float BagRowHeight = 52f, BagRowGap = 4f; // ten rows with the jerrycan (ADR-075), then loose optics (ADR-082)
        const float BagOpticHeight = 40f, BagOpticGap = 3f, BagOpticLabel = 22f; // ADR-100: 16 ids = 6 columns x 3 rows
        const int BagOpticColumns = 6, BagOpticRows = 3;
        const float AttachChip = 46f, AttachGap = 4f;
        const float GearCardHeight = 220f, WeaponCardHeight = 142f, WeaponCardGap = 8f;

        static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.62f);
        static readonly Color Durability = new Color(0.32f, 0.72f, 0.58f, 1f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 MidLeft = new Vector2(0f, 0.5f);
        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopCentre = new Vector2(0.5f, 1f);

        public static InventoryScreen Instance { get; private set; }

        public bool IsOpen => _canvas != null && _canvas.gameObject.activeSelf;
        public int VicinityCount => _vicinity.Count;
        public bool Dragging => _dragSource.IsValid;
        public InventoryDragSource ActiveDragSource => _dragSource;

        Canvas _canvas;
        RectTransform _panel;
        readonly List<LootItem> _vicinity = new List<LootItem>(MaxVicinityRows);
        readonly List<InventoryDropZone> _dropZones = new List<InventoryDropZone>(6);
        int _hash;
        bool _dirty = true;

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
        GameObject _healBox;
        Text _healLabel;
        RectTransform _healFill;
        Text _carryLabel;
        RectTransform _carryFill;
        Image _carryFillImage;

        readonly Text[] _gearNames = new Text[2];
        readonly Text[] _gearDetails = new Text[2];
        readonly RectTransform[] _gearFill = new RectTransform[2];
        readonly InventoryItemIcon[] _gearIcons = new InventoryItemIcon[2];
        readonly InventoryDragItem[] _gearDrags = new InventoryDragItem[2];

        readonly GameObject[] _weaponActive = new GameObject[Loadout.SlotCount];
        readonly Text[] _weaponNames = new Text[Loadout.SlotCount];
        readonly Text[] _weaponAmmo = new Text[Loadout.SlotCount];
        readonly Text[] _weaponBadge = new Text[Loadout.SlotCount];
        readonly InventoryItemIcon[] _weaponIcons = new InventoryItemIcon[Loadout.SlotCount];
        readonly InventoryDragItem[] _weaponDrags = new InventoryDragItem[Loadout.SlotCount];
        readonly GameObject[] _opticChip = new GameObject[Loadout.SlotCount];
        readonly Text[] _opticName = new Text[Loadout.SlotCount];
        readonly InventoryItemIcon[] _opticIcon = new InventoryItemIcon[Loadout.SlotCount];
        readonly InventoryDragItem[] _opticDrag = new InventoryDragItem[Loadout.SlotCount];
        // ADR-100: muzzle, grip, stock and magazine chips of each weapon card, [slot * 4 + (AttachSlot - 1)].
        readonly GameObject[] _attachChip = new GameObject[Loadout.SlotCount * 4];
        readonly InventoryItemIcon[] _attachIcon = new InventoryItemIcon[Loadout.SlotCount * 4];
        readonly InventoryDragItem[] _attachDrag = new InventoryDragItem[Loadout.SlotCount * 4];

        InventoryDragSource _dragSource;
        GameObject _dragGhost;
        RectTransform _dragGhostRect;
        InventoryItemIcon _dragGhostIcon;
        bool _dropHandled;
        bool _suppressQuickDropUntilRightRelease;

        GameObject _tooltip;
        RectTransform _tooltipRect;
        Text _tooltipText;

        void Awake()
        {
            Instance = this;
            Build();
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
                HideTooltip();
                _vicinity.Clear();
                return;
            }
            _dirty = true;
            var me = Combatant.LocalPlayer;
            if (me != null) Refresh(me);
        }

        void Update()
        {
            if (!IsOpen) return;
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
        }

        void CollectVicinity(Vector3 position)
        {
            _vicinity.Clear();
            float rangeSqr = Combatant.PickupRange * Combatant.PickupRange;
            foreach (var item in LootItem.All)
            {
                if (item == null || (item.transform.position - position).sqrMagnitude >= rangeSqr) continue;
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
                    hash = hash * 31 + InventoryBackend.OpticOf(combatant, i);
                    for (int s = 1; s < Attachments.Slots; s++) hash = hash * 31 + InventoryBackend.OpticOf(combatant, Attachments.GearIndex(i, (AttachSlot)s));
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

        void ShowBackpack(Combatant me)
        {
            for (int i = 0; i < InventoryBackend.AmmoTypeCount; i++)
            {
                int count = InventoryBackend.Reserve(me, i);
                ShowPackRow(i, count);
                _packDrags[i].Configure(this, AmmoSource(i));
            }
            ShowPackRow(InventoryBackend.MedkitRow, me.Medkits.Value);
            _packDrags[InventoryBackend.MedkitRow].Configure(this, MedkitSource());
            int grenades = InventoryBackend.Grenades(me);
            ShowPackRow(InventoryBackend.GrenadeRow, grenades);
            _packDrags[InventoryBackend.GrenadeRow].Configure(this, GrenadeSource());
            ShowPackRow(InventoryBackend.BandageRow, InventoryBackend.Bandages(me));
            _packDrags[InventoryBackend.BandageRow].Configure(this, BandageSource());
            ShowPackRow(InventoryBackend.FirstAidRow, InventoryBackend.FirstAids(me));
            _packDrags[InventoryBackend.FirstAidRow].Configure(this, FirstAidSource());
            ShowPackRow(InventoryBackend.SmokeRow, InventoryBackend.Smokes(me));
            _packDrags[InventoryBackend.SmokeRow].Configure(this, SmokeSource());
            ShowPackRow(InventoryBackend.JerrycanRow, InventoryBackend.Jerrycans(me));
            _packDrags[InventoryBackend.JerrycanRow].Configure(this, JerrycanSource());

            // Loose optics (ADR-082): one cell per optic id held, packed in id order.
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
            _bagOpticTitle.text = cell == 0 ? "조준경·부착물  ·  없음" : "조준경·부착물";

            // The bag's load against its size; red when over (the server then refuses what does not fit).
            string carry = InventoryBackend.CarryLabel(me);
            float fraction = InventoryBackend.CarryFraction(me);
            _carryLabel.text = carry.Length > 0 ? carry : "가방 용량 —";
            _carryFill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            _carryFillImage.color = fraction > 0.999f ? UiKit.Danger : fraction > 0.85f ? UiKit.Accent : Durability;
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
                hash = hash * 31 + InventoryBackend.Keys(me);
                hash = hash * 31 + InventoryBackend.CarryHash(me);
                hash = hash * 31 + InventoryBackend.BagOptics(me).GetHashCode();
                return hash;
            }
        }

        void ShowPackRow(int row, int count)
        {
            bool any = count > 0;
            _packCounts[row].text = "x" + count;
            _packNames[row].color = any ? UiKit.Text : UiKit.TextDim;
            _packCounts[row].color = any ? UiKit.Text : UiKit.TextDim;
            _packRows[row].GetComponent<Image>().color = any ? UiKit.PanelLight : new Color(UiKit.PanelLight.r, UiKit.PanelLight.g, UiKit.PanelLight.b, 0.45f);
        }

        void ShowGear(Combatant me)
        {
            for (int slot = 0; slot < 2; slot++)
            {
                var gear = InventoryBackend.Gear(me, slot);
                string baseName = slot == InventoryBackend.HelmetSlot ? "헬멧" : "조끼";
                _gearNames[slot].text = gear.Has ? baseName + " " + gear.Tier + "단계" : baseName + "  ·  비어 있음";
                _gearNames[slot].color = gear.Has ? UiKit.Text : UiKit.TextDim;
                _gearDetails[slot].text = gear.Has ? "내구도  " + gear.Durability + " / " + gear.MaxDurability : "장구류를 이곳에 놓으세요";
                _gearFill[slot].anchorMax = new Vector2(Mathf.Clamp01(gear.Fraction), 1f);
                _gearIcons[slot].Set(gear.Has ? (slot == 0 ? "helmet_" : "vest_") + gear.Tier : "", baseName);
                var kind = slot == 0 ? InventorySourceKind.Helmet : InventorySourceKind.Vest;
                _gearDrags[slot].Configure(this, new InventoryDragSource(kind, null, slot));
            }
        }

        void ShowWeapons(Combatant me)
        {
            for (int slot = 0; slot < Loadout.SlotCount; slot++)
            {
                int id = InventoryBackend.WeaponId(me, slot);
                int mag = InventoryBackend.WeaponMag(me, slot);
                bool active = me.ActiveSlot.Value == slot && !me.Holstered.Value;
                var weapon = GameData.Weapons.Find(id);
                _weaponActive[slot].SetActive(active);
                int opticId = weapon != null ? InventoryBackend.OpticOf(me, slot) : 0;
                _opticChip[slot].SetActive(opticId != 0);
                if (opticId != 0)
                {
                    string opticName = InventoryBackend.OpticName(opticId);
                    _opticName[slot].text = opticName;
                    _opticIcon[slot].Set(ItemIcons.OpticKey(opticId), opticName);
                    _opticDrag[slot].Configure(this, new InventoryDragSource(InventorySourceKind.Optic, null, slot));
                }
                for (int s = 1; s < Attachments.Slots; s++)
                {
                    int ci = slot * 4 + s - 1;
                    int fitted = weapon != null ? InventoryBackend.OpticOf(me, Attachments.GearIndex(slot, (AttachSlot)s)) : 0;
                    _attachChip[ci].SetActive(fitted != 0);
                    if (fitted == 0) continue;
                    _attachIcon[ci].Set(ItemIcons.OpticKey(fitted), InventoryBackend.OpticName(fitted));
                    _attachDrag[ci].Configure(this, new InventoryDragSource(InventorySourceKind.Optic, null, Attachments.GearIndex(slot, (AttachSlot)s)));
                }
                if (slot == InventoryBackend.ThrowableSlot)
                {
                    int grenades = InventoryBackend.Grenades(me);
                    _weaponNames[slot].text = grenades > 0 ? "수류탄" : "비어 있음";
                    _weaponNames[slot].color = grenades > 0 ? UiKit.Text : UiKit.TextDim;
                    _weaponAmmo[slot].text = grenades > 0 ? "x" + grenades : "투척물 슬롯";
                    _weaponBadge[slot].text = "투척";
                    _weaponIcons[slot].Set(grenades > 0 ? "grenade" : "", "수류탄");
                    _weaponDrags[slot].Configure(this, grenades > 0 ? GrenadeSource() : default);
                    continue;
                }
                if (weapon == null)
                {
                    _weaponNames[slot].text = "비어 있음";
                    _weaponNames[slot].color = UiKit.TextDim;
                    _weaponAmmo[slot].text = slot == InventoryBackend.MeleeSlot ? "근접 무기를 이곳에 놓으세요" : "총기를 이곳에 놓으세요";
                    _weaponBadge[slot].text = "구경 —";
                    _weaponIcons[slot].Set("", "총");
                }
                else if (weapon.IsMelee)
                {
                    // ADR-073: no rounds or calibre.
                    _weaponNames[slot].text = weapon.Name;
                    _weaponNames[slot].color = active ? UiKit.Accent : UiKit.Text;
                    _weaponAmmo[slot].text = "피해 " + weapon.Damage.ToString("0") + "  ·  사거리 " + weapon.Range.ToString("0.0") + "m";
                    _weaponBadge[slot].text = "근접";
                    _weaponIcons[slot].Set(InventoryBackend.WeaponKey(id), weapon.Name);
                }
                else
                {
                    int ammoIndex = InventoryBackend.AmmoIndex(weapon);
                    string mode = InventoryBackend.FireMode(me, slot);
                    _weaponNames[slot].text = weapon.Name;
                    _weaponNames[slot].color = active ? UiKit.Accent : UiKit.Text;
                    _weaponAmmo[slot].text = mag + " / " + InventoryBackend.Reserve(me, ammoIndex) + "발" + (mode.Length > 0 ? "  ·  " + mode : "");
                    _weaponBadge[slot].text = InventoryBackend.AmmoName(ammoIndex);
                    _weaponIcons[slot].Set(InventoryBackend.WeaponKey(id), weapon.Name);
                }
                _weaponDrags[slot].Configure(this, WeaponSource(slot));
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
        public InventoryDragSource JerrycanSource() => new InventoryDragSource(InventorySourceKind.Jerrycan, null, 0);
        public InventoryDragSource BagOpticSource(int opticId) => opticId >= 1 && opticId <= OpticBag.MaxId
            ? new InventoryDragSource(InventorySourceKind.BagOptic, null, opticId) : default;
        public InventoryDragSource WeaponSource(int slot) => slot >= 0 && slot < Loadout.SlotCount
            ? new InventoryDragSource(InventorySourceKind.Weapon, null, slot) : default;
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
            _dragGhostRect.anchoredPosition = local + new Vector2(18f, -18f);
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

        public void Activate(InventoryDragSource source, int clickCount)
        {
            var me = Combatant.LocalPlayer;
            if (me == null) return;
            if (source.Kind == InventorySourceKind.Vicinity && clickCount >= 2) InventoryBackend.Pickup(me, source.Loot);
            else if (source.Kind == InventorySourceKind.Medkit && clickCount >= 2) InventoryBackend.UseMedkit(me);
            else if (source.Kind == InventorySourceKind.Bandage && clickCount >= 2) InventoryBackend.UseHealing(me, HealKind.Bandage);
            else if (source.Kind == InventorySourceKind.FirstAid && clickCount >= 2) InventoryBackend.UseHealing(me, HealKind.FirstAid);
            else if (source.Kind == InventorySourceKind.Jerrycan && clickCount >= 2) InventoryBackend.UseJerrycan(me);
            else if (source.Kind == InventorySourceKind.Weapon && InventoryBackend.WeaponId(me, source.Index) != 0)
                InventoryBackend.SelectSlot(me, source.Index);
        }

        public void QuickDrop(InventoryDragSource source)
        {
            if (_suppressQuickDropUntilRightRelease)
            {
                if (Mouse.current == null || !Mouse.current.rightButton.isPressed) _suppressQuickDropUntilRightRelease = false;
                return;
            }
            if (Dragging) CancelDrag();
            else if (InventoryBackend.RightClickHeal(source.Kind, out var heal))
            {
                if (TryExtraCount(source, Combatant.LocalPlayer, out int held) && held > 0) InventoryBackend.UseHealing(Combatant.LocalPlayer, heal);
            }
            else DropOnGround(source);
        }

        public void CancelDragFromPointer()
        {
            _suppressQuickDropUntilRightRelease = true;
            CancelDrag();
        }

        internal bool DropOn(InventoryDropKind kind, int index, InventoryDragSource source)
        {
            switch (kind)
            {
                case InventoryDropKind.Bag: return DropOnBag(source);
                case InventoryDropKind.Ground: return DropOnGround(source);
                case InventoryDropKind.Weapon: return DropOnWeaponSlot(source, index);
                case InventoryDropKind.Gear: return DropOnGear(source, index);
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
                case InventorySourceKind.Weapon: return InventoryBackend.WeaponId(me, source.Index) != 0;
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

        bool CanDropOnGear(InventoryDragSource source, int slot)
        {
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
                default: return false;
            }
        }

        void SetDropHighlights(bool active)
        {
            for (int i = 0; i < _dropZones.Count; i++)
            {
                var zone = _dropZones[i];
                zone.SetDragState(active, active && CanDropOn(zone.Kind, zone.Index, _dragSource));
            }
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
                case InventorySourceKind.Medkit: key = "medkit"; name = "구급상자"; break;
                case InventorySourceKind.Grenade: key = "grenade"; name = "수류탄"; break;
                case InventorySourceKind.Weapon:
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
            }
        }

        public void ShowTooltip(InventoryDragSource source, Vector2 screenPosition, Camera eventCamera)
        {
            string description = InventoryBackend.Describe(source, Combatant.LocalPlayer);
            if (description.Length == 0) return;
            _tooltipText.text = description;
            var canvasRect = (RectTransform)_canvas.transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, eventCamera, out var local);
            local += new Vector2(18f, -18f);
            local.x = Mathf.Clamp(local.x, canvasRect.rect.xMin, canvasRect.rect.xMax - _tooltipRect.rect.width);
            local.y = Mathf.Clamp(local.y, canvasRect.rect.yMin + _tooltipRect.rect.height, canvasRect.rect.yMax);
            _tooltipRect.anchoredPosition = local;
            _tooltip.SetActive(true);
            _tooltip.transform.SetAsLastSibling();
        }

        public void HideTooltip()
        {
            if (_tooltip != null) _tooltip.SetActive(false);
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
        public string CarryText => _carryLabel != null ? _carryLabel.text : "";
        public string GearCardText(int slot) => _gearNames[slot].text + "\n" + _gearDetails[slot].text;
        public string WeaponCardText(int slot) => _weaponNames[slot].text + "\n" + _weaponAmmo[slot].text + " " + _weaponBadge[slot].text;

        // ---- Construction (once) ----

        void Build()
        {
            _canvas = UiKit.Canvas("InventoryCanvas", SortingOrder, true);
            _canvas.transform.SetParent(transform, false);
            var root = _canvas.transform;
            UiKit.Box(UiKit.Fill("Backdrop", root), Backdrop, true);

            _panel = UiKit.Rect("InventoryPanel", root, Centre, Centre, Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            var header = UiKit.Rect("Header", _panel, TopCentre, TopCentre, Vector2.zero, new Vector2(PanelWidth, 54f));
            UiKit.Label(UiKit.Fill("Title", header), "인벤토리", 34, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Label(UiKit.Fill("Hint", header), "끌어서 이동  ·  더블클릭 사용  ·  우클릭 버리기 (회복템은 사용)  ·  Tab / Esc 닫기", 18, UiKit.TextDim, TextAnchor.MiddleRight);
            // #110: progress of a timed heal, centred in the header.
            var heal = UiKit.Rect("Healing", header, Centre, Centre, new Vector2(-60f, 0f), new Vector2(300f, 50f));
            _healLabel = UiKit.Label(UiKit.Rect("Label", heal, TopLeft, TopLeft, Vector2.zero, new Vector2(300f, 30f)), "", 19, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            var healTrack = UiKit.Rect("Track", heal, TopLeft, TopLeft, new Vector2(0f, -36f), new Vector2(300f, 8f));
            UiKit.Box(healTrack, UiKit.PanelLight);
            _healFill = UiKit.Fill("Fill", healTrack);
            _healFill.anchorMax = new Vector2(0f, 1f);
            UiKit.Box(_healFill, UiKit.Accent);
            _healBox = heal.gameObject;
            _healBox.SetActive(false);

            float x = 0f;
            var ground = Column(_panel, "바닥", "주변 아이템", x, GroundWidth); x += GroundWidth + Gap;
            var bag = Column(_panel, "가방", "보유 아이템", x, BagWidth); x += BagWidth + Gap;
            var gear = Column(_panel, "장구류", "헬멧 · 조끼", x, GearWidth); x += GearWidth + Gap;
            var weapons = Column(_panel, "총기", "5칸 · 조준경", x, WeaponWidth);

            BuildVicinity(ground, GroundWidth);
            BuildBackpack(bag, BagWidth);
            BuildGear(gear, GearWidth);
            BuildWeapons(weapons, WeaponWidth);
            AddDropZone(ground, InventoryDropKind.Ground, 0);
            AddDropZone(bag, InventoryDropKind.Bag, 0);
            BuildDragGhost(root);
            BuildTooltip(root);
        }

        static RectTransform Column(Transform parent, string title, string subtitle, float x, float width)
        {
            var column = UiKit.Rect(title, parent, TopLeft, TopLeft, new Vector2(x, -ColumnTop), new Vector2(width, ColumnHeight));
            UiKit.Box(column, UiKit.Panel, true);
            UiKit.Label(UiKit.Rect("Title", column, TopLeft, TopLeft, new Vector2(Pad, -8f), new Vector2(width - 2f * Pad, 28f)), title, 23, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Label(UiKit.Rect("Subtitle", column, TopLeft, TopLeft, new Vector2(Pad, -34f), new Vector2(width - 2f * Pad, 20f)), subtitle, 18, UiKit.TextDim);
            return column;
        }

        static RectTransform Row(string name, Transform column, float width, float top, float height) =>
            UiKit.Rect(name, column, TopLeft, TopLeft, new Vector2(Pad, -top), new Vector2(width - 2f * Pad, height));

        static RectTransform InRow(string name, Transform row, float x, float width, float height) =>
            UiKit.Rect(name, row, MidLeft, MidLeft, new Vector2(x, 0f), new Vector2(width, height));

        void BuildVicinity(RectTransform column, float width)
        {
            float content = width - 2f * Pad;
            for (int i = 0; i < MaxVicinityRows; i++)
            {
                var row = Row("Loot" + i, column, width, HeaderHeight + i * (GroundRowHeight + GroundRowGap), GroundRowHeight);
                UiKit.Box(row, UiKit.PanelLight, true);
                _vicinityIcons[i] = InventoryItemIcon.Build(InRow("Icon", row, 6f, 42f, 42f));
                _vicinityNames[i] = UiKit.Label(InRow("Name", row, 56f, content - 142f, GroundRowHeight), "", 19, UiKit.Text);
                _vicinityAmounts[i] = UiKit.Label(InRow("Amount", row, content - 80f, 68f, GroundRowHeight), "", 18, UiKit.TextDim, TextAnchor.MiddleRight);
                _vicinityDrags[i] = row.gameObject.AddComponent<InventoryDragItem>();
                _vicinityRows[i] = row.gameObject;
                row.gameObject.SetActive(false);
            }
            var empty = Row("Empty", column, width, HeaderHeight, 64f);
            UiKit.Label(empty, "주변에 아이템 없음", 19, UiKit.TextDim, TextAnchor.MiddleCenter);
            _vicinityEmpty = empty.gameObject;
        }

        void BuildBackpack(RectTransform column, float width)
        {
            float content = width - 2f * Pad;
            for (int i = 0; i < InventoryBackend.BackpackRows; i++)
            {
                var row = Row("Bag" + i, column, width, HeaderHeight + i * (BagRowHeight + BagRowGap), BagRowHeight);
                UiKit.Box(row, UiKit.PanelLight, true);
                string name;
                string key;
                if (i < InventoryBackend.AmmoTypeCount) { name = InventoryBackend.AmmoName(i) + " 탄약"; key = InventoryBackend.AmmoKey(i); }
                else if (i == InventoryBackend.MedkitRow) { name = "구급상자"; key = "medkit"; }
                else if (i == InventoryBackend.BandageRow) { name = HealRules.BandageName; key = "bandage"; }
                else if (i == InventoryBackend.FirstAidRow) { name = HealRules.FirstAidName; key = "firstaid"; }
                else if (i == InventoryBackend.SmokeRow) { name = ThrowableRules.SmokeName; key = "smoke"; }
                else if (i == InventoryBackend.JerrycanRow) { name = FuelRules.JerrycanName; key = "jerrycan"; }
                else { name = "수류탄"; key = "grenade"; }
                _packIcons[i] = InventoryItemIcon.Build(InRow("Icon", row, 8f, 44f, 44f));
                _packIcons[i].Set(key, name);
                _packNames[i] = UiKit.Label(InRow("Name", row, 70f, content - 156f, BagRowHeight), name, 20, UiKit.TextDim);
                _packCounts[i] = UiKit.Label(InRow("Count", row, content - 74f, 62f, BagRowHeight), "x0", 22, UiKit.TextDim, TextAnchor.MiddleRight, FontStyle.Bold);
                _packDrags[i] = row.gameObject.AddComponent<InventoryDragItem>();
                _packRows[i] = row.gameObject;
            }
            // Loose optics (ADR-082): a grid of cells, one per optic id held; drag one onto a weapon card to fit it.
            float opticTop = HeaderHeight + InventoryBackend.BackpackRows * (BagRowHeight + BagRowGap);
            _bagOpticTitle = UiKit.Label(Row("OpticsTitle", column, width, opticTop, BagOpticLabel), "조준경·부착물  ·  없음", 17, UiKit.TextDim);
            float cellWidth = (content - (BagOpticColumns - 1) * BagOpticGap) / BagOpticColumns;
            for (int i = 0; i < OpticBag.MaxId; i++)
            {
                float cx = Pad + (i % BagOpticColumns) * (cellWidth + BagOpticGap);
                float cy = opticTop + BagOpticLabel + (i / BagOpticColumns) * (BagOpticHeight + BagOpticGap);
                var cellRect = UiKit.Rect("Optic" + i, column, TopLeft, TopLeft, new Vector2(cx, -cy), new Vector2(cellWidth, BagOpticHeight));
                UiKit.Box(cellRect, UiKit.PanelLight, true);
                _bagOpticIcons[i] = InventoryItemIcon.Build(InRow("Icon", cellRect, 2f, cellWidth - 4f, BagOpticHeight));
                _bagOpticCounts[i] = UiKit.Label(UiKit.Rect("Count", cellRect, TopLeft, TopLeft, new Vector2(cellWidth - 30f, -BagOpticHeight + 20f), new Vector2(28f, 20f)), "x1", 15, UiKit.Text, TextAnchor.LowerRight, FontStyle.Bold);
                _bagOpticDrags[i] = cellRect.gameObject.AddComponent<InventoryDragItem>();
                _bagOpticCells[i] = cellRect.gameObject;
                cellRect.gameObject.SetActive(false);
            }
            // Under them: the bag's load as text and a bar ("가방 용량 23/60").
            float carryTop = opticTop + BagOpticLabel + BagOpticRows * (BagOpticHeight + BagOpticGap) + 2f;
            var carry = Row("Carry", column, width, carryTop, 52f);
            _carryLabel = UiKit.Label(UiKit.Rect("Label", carry, TopLeft, TopLeft, Vector2.zero, new Vector2(content, 28f)), "가방 용량 —", 19, UiKit.Text);
            var track = UiKit.Rect("Track", carry, TopLeft, TopLeft, new Vector2(0f, -34f), new Vector2(content, 10f));
            UiKit.Box(track, UiKit.PanelLight);
            _carryFill = UiKit.Fill("Fill", track);
            _carryFill.anchorMax = new Vector2(0f, 1f);
            _carryFillImage = UiKit.Box(_carryFill, Durability);
        }

        void BuildGear(RectTransform column, float width)
        {
            for (int slot = 0; slot < 2; slot++)
            {
                var card = Row(slot == 0 ? "Helmet" : "Vest", column, width, HeaderHeight + slot * (GearCardHeight + 16f), GearCardHeight);
                UiKit.Box(card, UiKit.PanelLight, true);
                _gearIcons[slot] = InventoryItemIcon.Build(UiKit.Rect("Icon", card, TopLeft, TopLeft, new Vector2(16f, -18f), new Vector2(92f, 92f)));
                _gearNames[slot] = UiKit.Label(UiKit.Rect("Name", card, TopLeft, TopLeft, new Vector2(124f, -20f), new Vector2(width - 166f, 36f)), "", 23, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                _gearDetails[slot] = UiKit.Label(UiKit.Rect("Detail", card, TopLeft, TopLeft, new Vector2(124f, -62f), new Vector2(width - 166f, 48f)), "", 18, UiKit.TextDim);
                var track = UiKit.Rect("Durability", card, TopLeft, TopLeft, new Vector2(16f, -136f), new Vector2(width - 60f, 10f));
                UiKit.Box(track, UiKit.Panel);
                _gearFill[slot] = UiKit.Fill("Fill", track);
                UiKit.Box(_gearFill[slot], Durability);
                UiKit.Label(UiKit.Rect("DropHint", card, TopLeft, TopLeft, new Vector2(16f, -162f), new Vector2(width - 60f, 34f)), "바닥으로 끌어 버리기", 18, UiKit.TextDim);
                _gearDrags[slot] = card.gameObject.AddComponent<InventoryDragItem>();
                AddDropZone(card, InventoryDropKind.Gear, slot);
            }
        }

        void BuildWeapons(RectTransform column, float width)
        {
            for (int slot = 0; slot < Loadout.SlotCount; slot++)
            {
                var card = Row("Slot" + (slot + 1), column, width, HeaderHeight + slot * (WeaponCardHeight + WeaponCardGap), WeaponCardHeight);
                UiKit.Box(card, UiKit.PanelLight, true);
                var active = UiKit.Rect("Active", card, TopLeft, TopLeft, Vector2.zero, new Vector2(6f, WeaponCardHeight));
                UiKit.Box(active, UiKit.Accent);
                _weaponActive[slot] = active.gameObject;
                UiKit.Label(UiKit.Rect("SlotLabel", card, TopLeft, TopLeft, new Vector2(18f, -6f), new Vector2(124f, 22f)), (slot + 1) + "  " + InventoryBackend.SlotLabel(slot), 17, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                _weaponIcons[slot] = InventoryItemIcon.Build(UiKit.Rect("Icon", card, TopLeft, TopLeft, new Vector2(18f, -32f), new Vector2(124f, 88f)));
                _weaponNames[slot] = UiKit.Label(UiKit.Rect("Name", card, TopLeft, TopLeft, new Vector2(156f, -8f), new Vector2(width - 186f, 32f)), "비어 있음", 25, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                _weaponAmmo[slot] = UiKit.Label(UiKit.Rect("Ammo", card, TopLeft, TopLeft, new Vector2(156f, -42f), new Vector2(width - 186f, 28f)), "", 20, UiKit.TextDim);
                var badge = UiKit.Rect("CalibreBadge", card, TopLeft, TopLeft, new Vector2(156f, -78f), new Vector2(112f, 30f));
                UiKit.Box(badge, new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.18f));
                _weaponBadge[slot] = UiKit.Label(UiKit.Fill("Label", badge, 4f), "구경 —", 17, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                // The optic on this weapon: icon and name; drag it off to the ground to drop it.
                var chip = UiKit.Rect("Optic", card, TopLeft, TopLeft, new Vector2(278f, -76f), new Vector2(width - 2f * Pad - 278f - 6f, 46f));
                UiKit.Box(chip, UiKit.Panel, true);
                _opticIcon[slot] = InventoryItemIcon.Build(UiKit.Rect("Icon", chip, MidLeft, MidLeft, new Vector2(2f, 0f), new Vector2(70f, 44f)));
                _opticName[slot] = UiKit.Label(UiKit.Rect("Name", chip, MidLeft, MidLeft, new Vector2(76f, 0f), new Vector2(width - 2f * Pad - 278f - 6f - 84f, 46f)), "", 19, UiKit.Text, TextAnchor.MiddleLeft);
                _opticDrag[slot] = chip.gameObject.AddComponent<InventoryDragItem>();
                _opticChip[slot] = chip.gameObject;
                chip.gameObject.SetActive(false);
                // Muzzle, grip, stock and magazine: small icon chips in the card's top right corner (ADR-100).
                for (int s = 1; s < Attachments.Slots; s++)
                {
                    int ci = slot * 4 + s - 1;
                    var small = UiKit.Rect("Attach" + s, card, TopLeft, TopLeft, new Vector2(width - Pad - 12f - (Attachments.Slots - s) * (AttachChip + AttachGap) + AttachGap, -8f), new Vector2(AttachChip, AttachChip));
                    UiKit.Box(small, UiKit.Panel, true);
                    _attachIcon[ci] = InventoryItemIcon.Build(UiKit.Fill("Icon", small, 2f));
                    _attachDrag[ci] = small.gameObject.AddComponent<InventoryDragItem>();
                    _attachChip[ci] = small.gameObject;
                    small.gameObject.SetActive(false);
                }
                _weaponDrags[slot] = card.gameObject.AddComponent<InventoryDragItem>();
                AddDropZone(card, InventoryDropKind.Weapon, slot);
            }
        }

        void AddDropZone(RectTransform target, InventoryDropKind kind, int index)
        {
            var overlayRect = UiKit.Fill("DropState", target, 2f);
            var overlay = UiKit.Box(overlayRect, Color.clear);
            overlay.enabled = false;
            var zone = target.gameObject.AddComponent<InventoryDropZone>();
            zone.Configure(this, kind, index, overlay);
            _dropZones.Add(zone);
        }

        void BuildDragGhost(Transform root)
        {
            _dragGhostRect = UiKit.Rect("DragIcon", root, Centre, Centre, Vector2.zero, new Vector2(72f, 72f));
            var group = _dragGhostRect.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            group.alpha = 0.92f;
            _dragGhostIcon = InventoryItemIcon.Build(_dragGhostRect);
            _dragGhost = _dragGhostRect.gameObject;
            _dragGhost.SetActive(false);
        }

        void BuildTooltip(Transform root)
        {
            _tooltipRect = UiKit.Rect("Tooltip", root, Centre, TopLeft, Vector2.zero, new Vector2(260f, 92f));
            UiKit.Box(_tooltipRect, new Color(0.04f, 0.05f, 0.06f, 0.97f));
            _tooltipText = UiKit.Label(UiKit.Fill("Text", _tooltipRect, 12f), "", 18, UiKit.Text, TextAnchor.UpperLeft);
            _tooltip = _tooltipRect.gameObject;
            _tooltip.SetActive(false);
        }
    }
}
