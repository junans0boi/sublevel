using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Tab inventory. Created for the local player by PlayerController; shows itself while UiFocus says so.
    // Three columns: loot on the ground nearby, what the backpack holds, the two weapon slots. It only shows the
    // server's replicated state and sends requests; the server decides what actually changes hands.
    public class InventoryScreen : MonoBehaviour
    {
        public const int MaxVicinityRows = 12;

        const int AmmoTypeCount = 3;
        const int MedkitRow = AmmoTypeCount;
        const int BackpackRows = AmmoTypeCount + 1;
        const int SortingOrder = 100;

        const float ColumnWidth = 520f, ColumnHeight = 860f, ColumnSpacing = 550f, ColumnY = -40f;
        const float Pad = 16f, HeaderHeight = 60f;
        const float RowWidth = ColumnWidth - 2f * Pad, RowHeight = 56f, RowGap = 6f;
        const float CardHeight = 190f, CardGap = 12f;
        const float ButtonWidth = 90f, ButtonHeight = 40f;

        static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.55f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 MidLeft = new Vector2(0f, 0.5f);
        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopCentre = new Vector2(0.5f, 1f);

        public static InventoryScreen Instance { get; private set; }

        public bool IsOpen => _canvas != null && _canvas.gameObject.activeSelf;
        // Loot rows currently listed (at most MaxVicinityRows). Zero while closed.
        public int VicinityCount => _vicinity.Count;

        Canvas _canvas;
        readonly List<LootItem> _vicinity = new List<LootItem>(MaxVicinityRows);
        int _hash;
        bool _dirty = true;

        readonly GameObject[] _vicinityRow = new GameObject[MaxVicinityRows];
        readonly Text[] _vicinityName = new Text[MaxVicinityRows];
        readonly Text[] _vicinityAmount = new Text[MaxVicinityRows];
        GameObject _vicinityEmpty;

        readonly Text[] _packName = new Text[BackpackRows];
        readonly Text[] _packCount = new Text[BackpackRows];
        readonly GameObject[] _packDrop = new GameObject[BackpackRows];
        GameObject _medkitUse;

        readonly GameObject[] _weaponActive = new GameObject[Loadout.SlotCount];
        readonly Text[] _weaponName = new Text[Loadout.SlotCount];
        readonly Text[] _weaponAmmo = new Text[Loadout.SlotCount];
        readonly GameObject[] _weaponEquip = new GameObject[Loadout.SlotCount];
        readonly GameObject[] _weaponDrop = new GameObject[Loadout.SlotCount];

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

        void OnDisable() => UiFocus.Changed -= OnFocusChanged;

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
            var me = Combatant.LocalPlayer;
            // The dead carry nothing to manage, and the death prompt must not be covered.
            if (me == null || me.Dead.Value)
            {
                UiFocus.Close();
                return;
            }
            Refresh(me);
        }

        // Rows are rewritten only when what they show has changed.
        void Refresh(Combatant me)
        {
            CollectVicinity(me.transform.position);
            int hash = Hash(me);
            if (!_dirty && hash == _hash) return;
            _dirty = false;
            _hash = hash;
            ShowVicinity();
            ShowBackpack(me);
            ShowWeapons(me);
        }

        // Same reach as the [F] prompt and the server's pickup check.
        void CollectVicinity(Vector3 position)
        {
            _vicinity.Clear();
            float rangeSqr = Combatant.PickupRange * Combatant.PickupRange;
            foreach (var item in LootItem.All)
            {
                if (item == null) continue;
                if ((item.transform.position - position).sqrMagnitude >= rangeSqr) continue;
                _vicinity.Add(item);
                if (_vicinity.Count == MaxVicinityRows) break;
            }
        }

        int Hash(Combatant c)
        {
            unchecked
            {
                int h = c.ActiveSlot.Value;
                h = h * 31 + c.Slot0.Value; h = h * 31 + c.Slot1.Value;
                h = h * 31 + c.Mag0.Value; h = h * 31 + c.Mag1.Value;
                h = h * 31 + c.ReserveLight.Value; h = h * 31 + c.ReserveMedium.Value; h = h * 31 + c.ReserveShell.Value;
                h = h * 31 + c.Medkits.Value;
                h = h * 31 + _vicinity.Count;
                for (int i = 0; i < _vicinity.Count; i++)
                {
                    var item = _vicinity[i];
                    h = h * 31 + item.ObjectId;
                    h = h * 31 + item.Kind.Value; h = h * 31 + item.WeaponId.Value;
                    h = h * 31 + item.Ammo.Value; h = h * 31 + item.Amount.Value;
                }
                return h;
            }
        }

        void ShowVicinity()
        {
            for (int i = 0; i < MaxVicinityRows; i++)
            {
                bool used = i < _vicinity.Count;
                _vicinityRow[i].SetActive(used);
                if (!used) continue;
                var item = _vicinity[i];
                _vicinityName[i].text = LootName(item);
                _vicinityAmount[i].text = LootAmount(item);
            }
            _vicinityEmpty.SetActive(_vicinity.Count == 0);
        }

        void ShowBackpack(Combatant me)
        {
            for (int i = 0; i < AmmoTypeCount; i++) ShowPackRow(i, me.ReserveFor((AmmoType)i));
            ShowPackRow(MedkitRow, me.Medkits.Value);
            _medkitUse.SetActive(me.Medkits.Value > 0);
        }

        // Things the player has none of stay listed but dimmed, with nothing to press.
        void ShowPackRow(int row, int count)
        {
            bool any = count > 0;
            _packCount[row].text = count.ToString();
            _packName[row].color = any ? UiKit.Text : UiKit.TextDim;
            _packCount[row].color = any ? UiKit.Text : UiKit.TextDim;
            _packDrop[row].SetActive(any);
        }

        void ShowWeapons(Combatant me)
        {
            for (int slot = 0; slot < Loadout.SlotCount; slot++)
            {
                int id = slot == 0 ? me.Slot0.Value : me.Slot1.Value;
                int mag = slot == 0 ? me.Mag0.Value : me.Mag1.Value;
                bool active = me.ActiveSlot.Value == slot;
                WeaponDef weapon = GameData.Weapons.Find(id);
                _weaponActive[slot].SetActive(active);
                if (weapon == null)
                {
                    _weaponName[slot].text = "비어 있음";
                    _weaponName[slot].color = UiKit.TextDim;
                    _weaponAmmo[slot].text = "";
                    _weaponEquip[slot].SetActive(false);
                    _weaponDrop[slot].SetActive(false);
                    continue;
                }
                _weaponName[slot].text = weapon.Name;
                _weaponName[slot].color = active ? UiKit.Accent : UiKit.Text;
                _weaponAmmo[slot].text = mag + " / " + me.ReserveFor(weapon.Ammo) + "   " + AmmoName(weapon.Ammo);
                _weaponEquip[slot].SetActive(!active);
                _weaponDrop[slot].SetActive(true);
            }
        }

        static string LootName(LootItem item)
        {
            switch ((LootKind)item.Kind.Value)
            {
                case LootKind.Weapon:
                    WeaponDef weapon = GameData.Weapons.Find(item.WeaponId.Value);
                    return weapon != null ? weapon.Name : "무기";
                case LootKind.Ammo: return AmmoName((AmmoType)item.Ammo.Value);
                default: return "구급상자";
            }
        }

        static string LootAmount(LootItem item) =>
            (LootKind)item.Kind.Value == LootKind.Weapon ? "탄창 " + item.Amount.Value + "발" : "x" + item.Amount.Value;

        static string AmmoName(AmmoType type) =>
            type == AmmoType.Light ? "소형 탄약" : type == AmmoType.Medium ? "중형 탄약" : "산탄";

        // ---- What the buttons do (also called by tests) ----

        public void ClickVicinity(int index)
        {
            var me = Combatant.LocalPlayer;
            if (me == null || index < 0 || index >= _vicinity.Count) return;
            var item = _vicinity[index];
            if (item == null || !item.IsSpawned) return;
            me.CmdPickup(item.NetworkObject);
        }

        public void ClickEquip(int slot)
        {
            var me = Combatant.LocalPlayer;
            if (me != null) me.CmdSelectSlot(slot);
        }

        public void ClickDropWeapon(int slot)
        {
            var me = Combatant.LocalPlayer;
            if (me != null) me.CmdDropWeapon(slot);
        }

        public void ClickDropAmmo(int ammoType)
        {
            var me = Combatant.LocalPlayer;
            if (me != null) me.CmdDropAmmo(ammoType);
        }

        public void ClickUseMedkit()
        {
            var me = Combatant.LocalPlayer;
            if (me != null) me.CmdUseMedkit();
        }

        public void ClickDropMedkit()
        {
            var me = Combatant.LocalPlayer;
            if (me != null) me.CmdDropMedkit();
        }

        // ---- Hooks for tests ----

        public LootItem VicinityItem(int index) => index >= 0 && index < _vicinity.Count ? _vicinity[index] : null;
        public string VicinityText(int index) => _vicinityName[index].text + " " + _vicinityAmount[index].text;
        public string BackpackText(int row) => _packName[row].text + " " + _packCount[row].text;
        // Name line and ammo line of a weapon card.
        public string WeaponCardText(int slot) => _weaponName[slot].text + "\n" + _weaponAmmo[slot].text;

        // ---- Construction (once) ----

        void Build()
        {
            _canvas = UiKit.Canvas("InventoryCanvas", SortingOrder, true);
            _canvas.transform.SetParent(transform, false);
            var root = _canvas.transform;
            // Swallows clicks so nothing behind the screen reacts.
            UiKit.Box(UiKit.Fill("Backdrop", root), Backdrop, true);

            // Laid out around the centre so the columns stay on screen on 16:10 displays too.
            float totalWidth = ColumnSpacing * 2f + ColumnWidth;
            var header = UiKit.Rect("Header", root, TopCentre, TopCentre, new Vector2(0f, -36f), new Vector2(totalWidth, 50f));
            UiKit.Label(UiKit.Fill("Title", header), "인벤토리", 36, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Label(UiKit.Fill("Hint", header), "Tab / Esc — 닫기", 20, UiKit.TextDim, TextAnchor.MiddleRight);

            BuildVicinity(Column(root, "주변", -ColumnSpacing));
            BuildBackpack(Column(root, "가방", 0f));
            BuildWeapons(Column(root, "무기", ColumnSpacing));
        }

        static RectTransform Column(Transform parent, string title, float x)
        {
            var column = UiKit.Rect(title, parent, Centre, Centre, new Vector2(x, ColumnY), new Vector2(ColumnWidth, ColumnHeight));
            UiKit.Box(column, UiKit.Panel);
            var titleRect = UiKit.Rect("Title", column, TopLeft, TopLeft, new Vector2(Pad, -12f), new Vector2(RowWidth, 36f));
            UiKit.Label(titleRect, title, 22, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            return column;
        }

        static RectTransform Row(string name, Transform column, float top, float height) =>
            UiKit.Rect(name, column, TopLeft, TopLeft, new Vector2(Pad, -top), new Vector2(RowWidth, height));

        // A child of a row: `x` from the row's left edge, vertically centred.
        static RectTransform InRow(string name, Transform row, float x, float width, float height) =>
            UiKit.Rect(name, row, MidLeft, MidLeft, new Vector2(x, 0f), new Vector2(width, height));

        void BuildVicinity(RectTransform column)
        {
            for (int i = 0; i < MaxVicinityRows; i++)
            {
                int index = i;
                var row = Row("Loot" + i, column, HeaderHeight + i * (RowHeight + RowGap), RowHeight);
                // The whole row is the button: click to pick up.
                var button = UiKit.Button(row, "", 22, () => ClickVicinity(index));
                _vicinityName[i] = button.GetComponentInChildren<Text>(true);
                _vicinityName[i].alignment = TextAnchor.MiddleLeft;
                _vicinityAmount[i] = UiKit.Label(UiKit.Fill("Amount", row, 14f), "", 20, UiKit.TextDim, TextAnchor.MiddleRight);
                _vicinityRow[i] = row.gameObject;
                row.gameObject.SetActive(false);
            }
            var empty = Row("Empty", column, HeaderHeight, RowHeight);
            UiKit.Label(empty, "주변에 아이템 없음", 20, UiKit.TextDim);
            _vicinityEmpty = empty.gameObject;
        }

        void BuildBackpack(RectTransform column)
        {
            float dropX = RowWidth - 8f - ButtonWidth;
            float useX = dropX - 8f - ButtonWidth;
            for (int i = 0; i < BackpackRows; i++)
            {
                int index = i;
                bool medkit = i == MedkitRow;
                var row = Row(medkit ? "Medkits" : "Ammo" + i, column, HeaderHeight + i * (RowHeight + RowGap), RowHeight);
                UiKit.Box(row, UiKit.Panel);
                string label = medkit ? "구급상자" : AmmoName((AmmoType)i);
                _packName[i] = UiKit.Label(InRow("Name", row, 14f, 170f, RowHeight), label, 22, UiKit.TextDim);
                _packCount[i] = UiKit.Label(InRow("Count", row, 190f, 80f, RowHeight), "0", 22, UiKit.TextDim, TextAnchor.MiddleRight);
                var drop = InRow("Drop", row, dropX, ButtonWidth, ButtonHeight);
                if (medkit) UiKit.Button(drop, "버리기", 18, ClickDropMedkit);
                else UiKit.Button(drop, "버리기", 18, () => ClickDropAmmo(index));
                _packDrop[i] = drop.gameObject;
                drop.gameObject.SetActive(false);
                if (!medkit) continue;
                var use = InRow("Use", row, useX, ButtonWidth, ButtonHeight);
                UiKit.Button(use, "사용", 18, ClickUseMedkit);
                _medkitUse = use.gameObject;
                use.gameObject.SetActive(false);
            }
            // Places kept for what the spec lists but the game does not have yet.
            float top = HeaderHeight + BackpackRows * (RowHeight + RowGap) + 18f;
            UiKit.Label(Row("Throwables", column, top, 30f), "투척 무기  —", 18, UiKit.TextDim);
            UiKit.Label(Row("Keys", column, top + 34f, 30f), "열쇠 / 칩  —", 18, UiKit.TextDim);
        }

        void BuildWeapons(RectTransform column)
        {
            for (int i = 0; i < Loadout.SlotCount; i++)
            {
                int slot = i;
                var card = Row("Slot" + (i + 1), column, HeaderHeight + i * (CardHeight + CardGap), CardHeight);
                UiKit.Box(card, UiKit.Panel);
                // Accent bar down the left edge of the weapon in hand.
                var bar = UiKit.Rect("Active", card, TopLeft, TopLeft, Vector2.zero, new Vector2(6f, CardHeight));
                UiKit.Box(bar, UiKit.Accent);
                _weaponActive[i] = bar.gameObject;
                bar.gameObject.SetActive(false);

                UiKit.Label(UiKit.Rect("Slot", card, TopLeft, TopLeft, new Vector2(22f, -12f), new Vector2(200f, 22f)), "슬롯 " + (i + 1), 16, UiKit.TextDim);
                _weaponName[i] = UiKit.Label(UiKit.Rect("Name", card, TopLeft, TopLeft, new Vector2(22f, -40f), new Vector2(440f, 40f)), "비어 있음", 30, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                _weaponAmmo[i] = UiKit.Label(UiKit.Rect("Ammo", card, TopLeft, TopLeft, new Vector2(22f, -86f), new Vector2(440f, 28f)), "", 20, UiKit.TextDim);

                var equip = UiKit.Rect("Equip", card, TopLeft, TopLeft, new Vector2(22f, -134f), new Vector2(130f, ButtonHeight));
                UiKit.Button(equip, "장착", 18, () => ClickEquip(slot));
                _weaponEquip[i] = equip.gameObject;
                equip.gameObject.SetActive(false);
                var drop = UiKit.Rect("Drop", card, TopLeft, TopLeft, new Vector2(162f, -134f), new Vector2(ButtonWidth, ButtonHeight));
                UiKit.Button(drop, "버리기", 18, () => ClickDropWeapon(slot));
                _weaponDrop[i] = drop.gameObject;
                drop.gameObject.SetActive(false);
            }
            float top = HeaderHeight + Loadout.SlotCount * (CardHeight + CardGap) + 18f;
            UiKit.Label(Row("Helmet", column, top, 30f), "헬멧  —", 18, UiKit.TextDim);
            UiKit.Label(Row("Vest", column, top + 34f, 30f), "조끼  —", 18, UiKit.TextDim);
        }
    }
}
