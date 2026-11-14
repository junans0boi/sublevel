using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Sprite when the master registry has one; a stable colour tile and the first Hangul/Latin character otherwise.
    public sealed class InventoryItemIcon
    {
        readonly Image _background;
        readonly Image _sprite;
        readonly Text _fallback;
        static readonly Color WeaponPlate = new Color(0.62f, 0.67f, 0.71f, 0.92f);
        static readonly Color GhostTint = new Color(1f, 1f, 1f, 0.2f);
        string _key;
        string _name;
        bool _ghost;

        InventoryItemIcon(Image background, Image sprite, Text fallback)
        {
            _background = background;
            _sprite = sprite;
            _fallback = fallback;
        }

        public string Key => _key ?? "";
        public bool IsGhost => _ghost;

        // #179: an empty attachment slot shows the dim picture of what goes there (key "" = nothing).
        public void SetGhost(string key)
        {
            key = key ?? "";
            if (_ghost && _key == key) return;
            _ghost = true;
            _key = key;
            _name = "";
            Sprite sprite = ItemIcons.Get(key);
            _sprite.sprite = sprite;
            _sprite.enabled = sprite != null;
            _sprite.color = GhostTint;
            _background.color = UiKit.Panel;
            _fallback.enabled = false;
        }

        public static InventoryItemIcon Build(RectTransform rect)
        {
            var background = UiKit.Box(rect, UiKit.PanelLight);
            var spriteRect = UiKit.Fill("Sprite", rect, 5f);
            var sprite = spriteRect.gameObject.AddComponent<Image>();
            sprite.preserveAspect = true;
            sprite.raycastTarget = false;
            var fallback = UiKit.Label(UiKit.Fill("Fallback", rect, 2f), "?", 24, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            return new InventoryItemIcon(background, sprite, fallback);
        }

        public void Set(string key, string displayName)
        {
            key = key ?? "";
            displayName = displayName ?? "";
            if (_key == key && _name == displayName && !_ghost) return;
            _ghost = false;
            _sprite.color = Color.white;
            _key = key;
            _name = displayName;
            Sprite sprite = ItemIcons.Get(key);
            bool hasItem = key.Length > 0;
            _sprite.sprite = sprite;
            _sprite.enabled = sprite != null;
            // The weapon renders are dark metal: they need a light plate to read on the dark panels.
            if (sprite != null && key.StartsWith("weapon_")) _background.color = WeaponPlate;
            else _background.color = sprite != null || !hasItem ? UiKit.PanelLight : FallbackColor(key);
            _fallback.enabled = sprite == null && hasItem;
            _fallback.text = displayName.Length > 0 ? displayName[0].ToString() : "?";
        }

        static Color FallbackColor(string key)
        {
            if (key.StartsWith("weapon_")) return new Color(0.27f, 0.31f, 0.35f, 1f);
            if (key.StartsWith("ammo_")) return new Color(0.35f, 0.29f, 0.17f, 1f);
            if (key.StartsWith("helmet_")) return new Color(0.22f, 0.31f, 0.29f, 1f);
            if (key.StartsWith("vest_")) return new Color(0.24f, 0.27f, 0.33f, 1f);
            if (key == "medkit") return new Color(0.24f, 0.32f, 0.26f, 1f);
            if (key == "grenade") return new Color(0.24f, 0.31f, 0.20f, 1f);
            return new Color(0.28f, 0.28f, 0.30f, 1f);
        }
    }
}
