using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The five-cell weapon strip (ADR-057/058): (1)(2) primary, (3) sidearm, (4) melee (always empty for now),
    // (5) throwable with its count. The cell in hand is outlined in the accent colour. Cells are rewritten on change only.
    public sealed class WeaponStrip
    {
        public const int Cells = HudBackend.SlotCount;
        public const float CellWidth = 76f, CellHeight = 72f, Gap = 6f;
        public const float Width = Cells * CellWidth + (Cells - 1) * Gap;

        static readonly string[] Keys = { "1", "2", "3", "4", "5" };

        public RectTransform Root { get; private set; }

        readonly Image[][] _frame = new Image[Cells][];
        readonly Image[] _bar = new Image[Cells];
        readonly Text[] _key = new Text[Cells];
        readonly Text[] _count = new Text[Cells];
        readonly Text[] _optic = new Text[Cells];
        readonly Text[] _empty = new Text[Cells];
        readonly GameObject[] _iconRoot = new GameObject[Cells];
        readonly InventoryItemIcon[] _icon = new InventoryItemIcon[Cells];
        readonly int[] _shownWeapon = new int[Cells];
        readonly string[] _shownOptic = new string[Cells];
        int _shownActive = int.MinValue, _shownGrenades = int.MinValue;

        // ---- Read-only state for tests ----

        public int ActiveShown => _shownActive;                                  // the highlighted cell, -1 for none
        public int WeaponShown(int cell) => _shownWeapon[cell];                  // weapon id drawn in the cell, 0 = empty
        public string IconKeyShown(int cell) => _icon[cell].Key;
        public int GrenadesShown => _shownGrenades;
        public string OpticShown(int cell) => _shownOptic[cell] ?? "";
        public bool IsHighlighted(int cell) => _bar[cell] != null && _bar[cell].enabled;

        public static WeaponStrip Build(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position)
        {
            var s = new WeaponStrip();
            var root = UiKit.Rect("WeaponStrip", parent, anchor, pivot, position, new Vector2(Width, CellHeight));
            s.Root = root;
            var topLeft = new Vector2(0f, 1f);
            var topRight = new Vector2(1f, 1f);
            var bottomLeft = new Vector2(0f, 0f);
            var centre = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < Cells; i++)
            {
                var cell = UiKit.Rect("Cell" + (i + 1), root, topLeft, topLeft, new Vector2(i * (CellWidth + Gap), 0f), new Vector2(CellWidth, CellHeight));
                UiKit.Box(cell, UiKit.Panel);
                s._frame[i] = UiKit.Frame(cell, 1f, UiKit.SteelDim);

                var iconRect = UiKit.Rect("Icon", cell, centre, centre, new Vector2(0f, 2f), new Vector2(CellWidth - 10f, CellHeight - 22f));
                s._icon[i] = InventoryItemIcon.Build(iconRect);
                s._iconRoot[i] = iconRect.gameObject;
                s._iconRoot[i].SetActive(false);
                s._empty[i] = UiKit.Label(UiKit.Rect("Empty", cell, centre, centre, new Vector2(0f, 2f), new Vector2(CellWidth, 30f)), "—", 22, UiKit.SteelDim, TextAnchor.MiddleCenter);

                s._key[i] = UiKit.Label(UiKit.Rect("Key", cell, topLeft, topLeft, new Vector2(5f, -1f), new Vector2(24f, 22f)), Keys[i], 18, UiKit.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                s._optic[i] = UiKit.Label(UiKit.Rect("Optic", cell, topRight, topRight, new Vector2(-4f, -1f), new Vector2(46f, 22f)), "", 18, UiKit.Text, TextAnchor.MiddleRight);
                s._count[i] = UiKit.Label(UiKit.Rect("Count", cell, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-5f, 5f), new Vector2(40f, 22f)), "", 20, UiKit.Text,
                    TextAnchor.MiddleRight, FontStyle.Bold);
                s._bar[i] = UiKit.Box(UiKit.Rect("Active", cell, bottomLeft, bottomLeft, Vector2.zero, new Vector2(CellWidth, 3f)), UiKit.Accent);
                s._bar[i].enabled = false;
                s._shownWeapon[i] = int.MinValue;
            }
            return s;
        }

        public void Tick(Combatant me)
        {
            for (int i = 0; i < HudBackend.Throwable; i++)
            {
                if (i == HudBackend.Melee) { ShowEmpty(i); continue; }
                ShowWeapon(i, HudBackend.WeaponInSlot(me, i), me);
            }
            ShowGrenades(HudBackend.Grenades(me));

            int active = HudBackend.ActiveSlot(me);
            // Highlight only what is really in hand: an empty slot is no selection.
            if (active < 0 || active >= HudBackend.Throwable || HudBackend.WeaponInSlot(me, active) == 0) active = -1;
            if (active != _shownActive)
            {
                _shownActive = active;
                for (int i = 0; i < Cells; i++)
                {
                    bool on = i == active;
                    _bar[i].enabled = on;
                    _key[i].color = on ? UiKit.Accent : UiKit.TextDim;
                    var line = on ? UiKit.Accent : UiKit.SteelDim;
                    for (int l = 0; l < 4; l++) _frame[i][l].color = line;
                }
            }
        }

        void ShowEmpty(int cell)
        {
            if (_shownWeapon[cell] == 0) return;
            _shownWeapon[cell] = 0;
            _iconRoot[cell].SetActive(false);
            _empty[cell].enabled = true;
        }

        void ShowWeapon(int cell, int weaponId, Combatant me)
        {
            string optic = weaponId != 0 ? HudBackend.OpticOf(me, cell) : "";
            if (optic != _shownOptic[cell])
            {
                _shownOptic[cell] = optic;
                _optic[cell].text = optic;
            }
            if (_shownWeapon[cell] == weaponId) return;
            _shownWeapon[cell] = weaponId;
            if (weaponId == 0)
            {
                _iconRoot[cell].SetActive(false);
                _empty[cell].enabled = true;
                return;
            }
            var weapon = GameData.Weapons.Find(weaponId);
            _icon[cell].Set(InventoryBackend.WeaponKey(weaponId), weapon != null ? weapon.Name : "");
            _iconRoot[cell].SetActive(true);
            _empty[cell].enabled = false;
        }

        void ShowGrenades(int grenades)
        {
            if (grenades == _shownGrenades) return;
            _shownGrenades = grenades;
            const int cell = HudBackend.Throwable;
            bool has = grenades > 0;
            _shownWeapon[cell] = has ? 1 : 0;
            _iconRoot[cell].SetActive(has);
            _empty[cell].enabled = !has;
            _count[cell].text = has ? "x" + HudDigits.IntText(grenades) : "";
            if (has) _icon[cell].Set("grenade", "수류탄");
        }
    }
}
