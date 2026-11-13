using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Colours a pickup by what it holds; weapon pickups show the weapon's model instead of the box.
    // Armour has no model yet: a box tinted by piece and tier (helmets blue, vests orange, darker = higher tier),
    // small and cubic for a helmet, tall and flat for a vest.
    [RequireComponent(typeof(LootItem))]
    public class LootView : MonoBehaviour
    {
        [SerializeField] Renderer _renderer;
        [SerializeField] Material[] _materialsByKind = new Material[0];
        [SerializeField] GameObject[] _weaponModelsById = new GameObject[0];

        static readonly Color[] HelmetColors = { new Color(0.55f, 0.75f, 0.95f), new Color(0.2f, 0.5f, 0.95f), new Color(0.1f, 0.2f, 0.6f) };
        static readonly Color[] VestColors = { new Color(0.95f, 0.7f, 0.5f), new Color(0.9f, 0.4f, 0.15f), new Color(0.55f, 0.15f, 0.05f) };
        static readonly Vector3 HelmetShape = new Vector3(0.8f, 0.8f, 0.8f);
        static readonly Vector3 VestShape = new Vector3(1f, 1.3f, 0.55f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        LootItem _item;
        MaterialPropertyBlock _tint;
        Vector3 _boxScale = Vector3.one;
        int _shownKind = -1;
        int _shownWeapon = -1;
        int _shownTier = -1;

        // The box colour of an armour pickup. Tiers outside the table get the nearest one.
        public static Color ArmorColor(LootKind kind, int tier)
        {
            var colors = kind == LootKind.Helmet ? HelmetColors : VestColors;
            return colors[Mathf.Clamp(tier - 1, 0, colors.Length - 1)];
        }

        // The Korean name of what lies here (shared with the HUD prompt and the inventory rows).
        public string Label => LootNames.Label(_item);

        void Awake()
        {
            _item = GetComponent<LootItem>();
            if (_renderer != null) _boxScale = _renderer.transform.localScale;
        }

        void Update()
        {
            int kind = _item.Kind.Value;
            int weapon = kind == (int)LootKind.Weapon ? _item.WeaponId.Value : 0;
            int tier = _item.Tier.Value;
            if (kind == _shownKind && weapon == _shownWeapon && tier == _shownTier) return;
            bool armor = Armor.IsArmor((LootKind)kind);
            // Kinds this prefab has no material for keep whatever was shown, except armour, which is tinted.
            if (_renderer == null || kind < 0 || (!armor && kind >= _materialsByKind.Length)) return;
            _shownKind = kind;
            _shownWeapon = weapon;
            _shownTier = tier;

            if (kind < _materialsByKind.Length) _renderer.sharedMaterial = _materialsByKind[kind];
            else if (_materialsByKind.Length > 0) _renderer.sharedMaterial = _materialsByKind[0];
            if (armor)
            {
                _tint ??= new MaterialPropertyBlock();
                var color = ArmorColor((LootKind)kind, tier);
                _tint.SetColor(BaseColorId, color);
                _tint.SetColor(ColorId, color);
                _renderer.SetPropertyBlock(_tint);
                _renderer.transform.localScale = Vector3.Scale(_boxScale, kind == (int)LootKind.Helmet ? HelmetShape : VestShape);
            }
            else
            {
                _renderer.SetPropertyBlock(null);
                _renderer.transform.localScale = _boxScale;
            }

            bool hasModel = weapon > 0 && weapon < _weaponModelsById.Length && _weaponModelsById[weapon] != null;
            _renderer.enabled = !hasModel;
            for (int i = 0; i < _weaponModelsById.Length; i++)
                if (_weaponModelsById[i] != null) _weaponModelsById[i].SetActive(hasModel && i == weapon);
        }
    }
}
