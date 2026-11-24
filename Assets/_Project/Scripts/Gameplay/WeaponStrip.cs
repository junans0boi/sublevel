using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The weapon stack (HUD v2, ADR-057/058): one row per filled slot, left of the minimap, rows hugging the bottom.
    // (1)(2) primary, (3) sidearm, (4) melee, (5) throwable with its count. A row reads "name ammo · icon · key"; the
    // row in hand sits on a panel with the key in the accent colour, the others are dimmed. Hidden while driving.
    // Rows are rewritten on change only.
    public sealed class WeaponStrip
    {
        public const int Cells = HudBackend.SlotCount;
        public const float Width = 220f, CellHeight = 38f, Gap = 5f;
        public const float Height = Cells * CellHeight + (Cells - 1) * Gap;
        const float IconWidth = 84f, IconHeight = 30f, KeyWidth = 14f, Pad = 8f;
        const float DimAlpha = 0.5f;

        static readonly string[] Keys = { "1", "2", "3", "4", "5" };

        public RectTransform Root { get; private set; }

        readonly RectTransform[] _row = new RectTransform[Cells];
        readonly CanvasGroup[] _group = new CanvasGroup[Cells];
        readonly Image[] _panel = new Image[Cells];
        readonly Text[] _key = new Text[Cells];
        readonly Text[] _label = new Text[Cells];
        readonly Image[] _icon = new Image[Cells];
        readonly string[] _iconKey = new string[Cells];
        static readonly Color SilhouetteTint = new Color(0.84f, 0.87f, 0.89f, 1f); // #d6dde4, the mock's weapon shape
        static readonly Dictionary<string, Sprite> Silhouettes = new Dictionary<string, Sprite>();
        readonly int[] _shownWeapon = new int[Cells];
        readonly int[] _shownMag = new int[Cells];
        readonly string[] _shownOptic = new string[Cells];
        int _shownActive = int.MinValue, _shownGrenades = int.MinValue, _shownThrowKey = int.MinValue, _shownLayout = -1;
        string _shownThrowName = "";

        // ---- Read-only state for tests ----

        public int ActiveShown => _shownActive;                                  // the highlighted row, -1 for none
        public int WeaponShown(int cell) => _shownWeapon[cell];                  // weapon id drawn in the row, 0 = empty
        public string IconKeyShown(int cell) => _iconKey[cell] ?? "";
        public string RowText(int cell) => _row[cell].gameObject.activeSelf ? _label[cell].text : "";
        public bool RowShown(int cell) => _row[cell].gameObject.activeSelf;
        public bool Shown => Root.gameObject.activeSelf;                         // off while driving
        public int GrenadesShown => _shownGrenades;                              // count of the selected throwable
        public string ThrowableNameShown => _shownThrowName;                     // "파편 수류탄" / "연막탄", "" when empty
        public string OpticShown(int cell) => _shownOptic[cell] ?? "";
        public bool IsHighlighted(int cell) => _panel[cell] != null && _panel[cell].enabled;

        public static WeaponStrip Build(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position)
        {
            var s = new WeaponStrip();
            var root = UiKit.Rect("WeaponStrip", parent, anchor, pivot, position, new Vector2(Width, Height));
            s.Root = root;
            var bottomRight = new Vector2(1f, 0f);
            var midRight = new Vector2(1f, 0.5f);
            for (int i = 0; i < Cells; i++)
            {
                var row = UiKit.Rect("Row" + (i + 1), root, bottomRight, bottomRight, Vector2.zero, new Vector2(Width, CellHeight));
                s._row[i] = row;
                s._group[i] = row.gameObject.AddComponent<CanvasGroup>();
                s._group[i].blocksRaycasts = false;
                s._panel[i] = UiKit.Box(row, UiKit.Panel);
                s._panel[i].enabled = false;
                s._key[i] = UiKit.Label(UiKit.Rect("Key", row, midRight, midRight, new Vector2(-Pad, 0f), new Vector2(KeyWidth, CellHeight)), Keys[i], 18,
                    UiKit.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
                var iconRect = UiKit.Rect("Icon", row, midRight, midRight, new Vector2(-Pad - KeyWidth - Pad, 0f), new Vector2(IconWidth, IconHeight));
                s._icon[i] = iconRect.gameObject.AddComponent<Image>();
                s._icon[i].preserveAspect = true;
                s._icon[i].raycastTarget = false;
                s._icon[i].color = SilhouetteTint;
                s._icon[i].enabled = false;
                float labelWidth = Width - 2f * Pad - KeyWidth - Pad - IconWidth - Pad;
                s._label[i] = UiKit.Label(UiKit.Rect("Label", row, midRight, midRight, new Vector2(-Pad - KeyWidth - Pad - IconWidth - Pad, 0f),
                    new Vector2(labelWidth, CellHeight)), "", 16, UiKit.Text, TextAnchor.MiddleRight);
                s._label[i].horizontalOverflow = HorizontalWrapMode.Overflow; // a long name runs left rather than wrapping
                s._shownWeapon[i] = int.MinValue;
                s._shownMag[i] = int.MinValue;
                row.gameObject.SetActive(false);
            }
            return s;
        }

        public void Tick(Combatant me)
        {
            // The driver's hands are on the wheel: the cluster takes this corner's job (seat 0 = number 1).
            bool driving = HudBackend.VehicleSeatNumber(me) == 1;
            if (Root.gameObject.activeSelf == driving) Root.gameObject.SetActive(!driving);
            if (driving) return;

            // The melee row shows its weapon like the others (ADR-073).
            for (int i = 0; i < HudBackend.Throwable; i++)
                ShowWeapon(i, HudBackend.WeaponInSlot(me, i), me);
            ShowGrenades(HudBackend.Grenades(me));
            Layout();

            int active = HudBackend.ActiveSlot(me);
            // Highlight only what is really in hand: an empty slot is no selection.
            // The throwable row lights when it is in hand and something is carried (ADR-144).
            bool throwOut = active == HudBackend.Throwable && ThrowableUser.Local != null && ThrowableUser.Local.TotalCount > 0;
            if (!throwOut && (active < 0 || active >= HudBackend.Throwable || HudBackend.WeaponInSlot(me, active) == 0)) active = -1;
            if (active != _shownActive)
            {
                _shownActive = active;
                for (int i = 0; i < Cells; i++)
                {
                    bool on = i == active;
                    _panel[i].enabled = on;
                    _group[i].alpha = on ? 1f : DimAlpha;
                    _key[i].color = on ? UiKit.Accent : UiKit.TextDim;
                    _label[i].color = on ? UiKit.Text : UiKit.TextDim;
                }
            }
        }

        // Filled rows stack from the bottom up in slot order (slot 1 on top), like the mock.
        void Layout()
        {
            int mask = 0;
            for (int i = 0; i < Cells; i++) if (_shownWeapon[i] != 0) mask |= 1 << i;
            if (mask == _shownLayout) return;
            _shownLayout = mask;
            float y = 0f;
            for (int i = Cells - 1; i >= 0; i--)
            {
                bool on = (mask & (1 << i)) != 0;
                if (_row[i].gameObject.activeSelf != on) _row[i].gameObject.SetActive(on);
                if (!on) continue;
                _row[i].anchoredPosition = new Vector2(0f, y);
                y += CellHeight + Gap;
            }
        }

        void ShowWeapon(int cell, int weaponId, Combatant me)
        {
            _shownOptic[cell] = weaponId != 0 ? HudBackend.OpticOf(me, cell) : "";
            int mag = weaponId != 0 ? HudBackend.MagInSlot(me, cell) : 0;
            if (_shownWeapon[cell] == weaponId && _shownMag[cell] == mag) return;
            bool changed = _shownWeapon[cell] != weaponId;
            _shownWeapon[cell] = weaponId;
            _shownMag[cell] = mag;
            if (weaponId == 0) return;
            var weapon = GameData.Weapons.Find(weaponId);
            string name = weapon != null ? weapon.Name : "";
            if (changed) SetIcon(cell, InventoryBackend.WeaponKey(weaponId));
            // Melee has no magazine: the name alone.
            _label[cell].text = weapon != null && weapon.IsMelee ? name : name + "  " + HudDigits.IntText(mag);
        }

        // The throwable row shows the selected kind (T switches) and how many of it are left. With none of the
        // selected kind but some of the other, the selected one shows with x0; with none at all the row is gone.
        void ShowGrenades(int grenades)
        {
            var user = ThrowableUser.Local;
            var kind = user != null ? user.SelectedKind : ThrowableKind.Frag;
            int count = user != null ? user.SelectedCount : grenades;
            int total = user != null ? user.TotalCount : grenades;
            bool has = total > 0;
            // The pin is out: the fuse left shows instead of the count (tenths of a second).
            bool cooking = user != null && user.Cooking.Value;
            int cookTenths = cooking ? Mathf.CeilToInt(user.CookRemaining * 10f) : 0;
            int key = has ? (count * 2 + (int)kind) * 1000 + (cooking ? 1 + cookTenths : 0) : -1;
            if (key == _shownThrowKey) return;
            _shownThrowKey = key;
            _shownGrenades = has ? count : 0;
            const int cell = HudBackend.Throwable;
            _shownWeapon[cell] = has ? 1 : 0;
            _shownThrowName = has ? Throwables.NameOf(kind) : "";
            if (!has) return;
            SetIcon(cell, kind == ThrowableKind.Smoke ? "smoke" : kind == ThrowableKind.Flash ? "flash" : kind == ThrowableKind.Molotov ? "molotov" : "grenade");
            _label[cell].text = _shownThrowName + "  " + (cooking ? (cookTenths / 10f).ToString("0.0") + "초" : "x" + HudDigits.IntText(count));
        }
    
        void SetIcon(int cell, string key)
        {
            if (_iconKey[cell] == key) return;
            _iconKey[cell] = key;
            var sprite = Silhouette(key);
            _icon[cell].sprite = sprite;
            _icon[cell].enabled = sprite != null;
        }

        // The item render as a flat light shape (alpha kept, colour white; tinted by the Image), made once per key. The
        // renders are dark metal that vanishes on the HUD's dark panels; the mock draws plain silhouettes.
        // Blit + ReadPixels so the source texture need not be readable.
        static Sprite Silhouette(string key)
        {
            if (Silhouettes.TryGetValue(key, out var made)) return made;
            var src = ItemIcons.Get(key);
            if (src == null) { Silhouettes[key] = null; return null; }
            var r = src.textureRect;
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            var rt = RenderTexture.GetTemporary(src.texture.width, src.texture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src.texture, rt);
            var before = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            tex.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, px[i].a);
            tex.SetPixels32(px);
            tex.Apply();
            made = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            made.hideFlags = HideFlags.HideAndDontSave;
            Silhouettes[key] = made;
            return made;
        }
    }
}
