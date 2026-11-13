using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Colours a pickup by what it holds; weapon pickups show the weapon's model instead of the box.
    // Armour has no model yet: a box tinted by piece and tier (helmets blue, vests orange, darker = higher tier),
    // small and cubic for a helmet, tall and flat for a vest. An optic shows the same code-built model as on the
    // weapon (OpticMeshes), enlarged so it reads on the floor; without the optic materials it falls back to a tinted bar.
    [RequireComponent(typeof(LootItem))]
    public class LootView : MonoBehaviour
    {
        [SerializeField] Renderer _renderer;
        [SerializeField] Material[] _materialsByKind = new Material[0];
        [SerializeField] GameObject[] _weaponModelsById = new GameObject[0];
        // Dark metal, lens glass and see-through window for the optic model (the same ones as OpticVisual).
        [SerializeField] Material _opticMetal;
        [SerializeField] Material _opticGlass;
        [SerializeField] Material _opticWindow;

        static readonly Color[] HelmetColors = { new Color(0.55f, 0.75f, 0.95f), new Color(0.2f, 0.5f, 0.95f), new Color(0.1f, 0.2f, 0.6f) };
        static readonly Color[] VestColors = { new Color(0.95f, 0.7f, 0.5f), new Color(0.9f, 0.4f, 0.15f), new Color(0.55f, 0.15f, 0.05f) };
        static readonly Vector3 HelmetShape = new Vector3(0.8f, 0.8f, 0.8f);
        static readonly Vector3 VestShape = new Vector3(1f, 1.3f, 0.55f);
        static readonly Vector3 OpticShape = new Vector3(0.45f, 0.45f, 1.1f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        // The optic model's longest side on the floor (metres), within these enlargement limits.
        const float OpticLootSize = 0.34f, OpticMinScale = 1.4f, OpticMaxScale = 4.5f;

        LootItem _item;
        MaterialPropertyBlock _tint;
        Vector3 _boxScale = Vector3.one;
        int _shownKind = -1;
        int _shownWeapon = -1;
        int _shownTier = -1;
        int _shownOptic = -1;
        GameObject _opticModel;
        MeshFilter _opticMesh;

        // The optic model shown for this pickup (null when it holds no optic or the materials are missing).
        public GameObject OpticModel => _opticModel != null && _opticModel.activeSelf ? _opticModel : null;

        // The box colour of an armour pickup. Tiers outside the table get the nearest one.
        public static Color ArmorColor(LootKind kind, int tier)
        {
            var colors = kind == LootKind.Helmet ? HelmetColors : VestColors;
            return colors[Mathf.Clamp(tier - 1, 0, colors.Length - 1)];
        }

        // The box colour of an optic pickup: warm for the 1x sights, cooler with more zoom.
        public static Color OpticColor(int opticId)
        {
            var def = OpticRules.Current.Find(opticId);
            if (def == null) return new Color(0.5f, 0.5f, 0.5f);
            if (def.Kind == OpticKind.Dot) return new Color(0.85f, 0.3f, 0.22f);
            if (def.Kind == OpticKind.Holo) return new Color(0.9f, 0.65f, 0.25f);
            return Color.Lerp(new Color(0.55f, 0.75f, 0.85f), new Color(0.2f, 0.3f, 0.6f), Mathf.Clamp01((def.Zoom - 2f) / 6f));
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
            int optic = kind == (int)LootKind.Optic ? _item.Optic.Value : 0;
            if (kind == _shownKind && weapon == _shownWeapon && tier == _shownTier && optic == _shownOptic) return;
            bool armor = Armor.IsArmor((LootKind)kind);
            bool opticKind = kind == (int)LootKind.Optic;
            // Kinds this prefab has no material for keep whatever was shown, except armour and optics, which are tinted.
            if (_renderer == null || kind < 0 || (!armor && !opticKind && kind >= _materialsByKind.Length)) return;
            _shownKind = kind;
            _shownWeapon = weapon;
            _shownTier = tier;
            _shownOptic = optic;

            if (kind < _materialsByKind.Length) _renderer.sharedMaterial = _materialsByKind[kind];
            else if (_materialsByKind.Length > 0) _renderer.sharedMaterial = _materialsByKind[0];
            if (armor || opticKind)
            {
                _tint ??= new MaterialPropertyBlock();
                var color = armor ? ArmorColor((LootKind)kind, tier) : OpticColor(optic);
                _tint.SetColor(BaseColorId, color);
                _tint.SetColor(ColorId, color);
                _renderer.SetPropertyBlock(_tint);
                _renderer.transform.localScale = Vector3.Scale(_boxScale, opticKind ? OpticShape : kind == (int)LootKind.Helmet ? HelmetShape : VestShape);
            }
            else
            {
                _renderer.SetPropertyBlock(null);
                _renderer.transform.localScale = _boxScale;
            }

            bool hasModel = weapon > 0 && weapon < _weaponModelsById.Length && _weaponModelsById[weapon] != null;
            bool opticModel = opticKind && ShowOptic(OpticRules.Current.Find(optic));
            if (!opticKind && _opticModel != null) _opticModel.SetActive(false);
            _renderer.enabled = !hasModel && !opticModel;
            for (int i = 0; i < _weaponModelsById.Length; i++)
                if (_weaponModelsById[i] != null) _weaponModelsById[i].SetActive(hasModel && i == weapon);
        }

        // Puts the optic model in place of the box; false when it cannot be drawn.
        bool ShowOptic(OpticDef def)
        {
            if (def == null || _opticMetal == null)
            {
                if (_opticModel != null) _opticModel.SetActive(false);
                return false;
            }
            if (_opticModel == null)
            {
                _opticModel = new GameObject("OpticModel");
                _opticModel.layer = gameObject.layer;
                _opticModel.transform.SetParent(transform, false);
                _opticMesh = _opticModel.AddComponent<MeshFilter>();
                var r = _opticModel.AddComponent<MeshRenderer>();
                var glass = _opticGlass != null ? _opticGlass : _opticMetal;
                r.sharedMaterials = new[] { _opticMetal, glass, _opticWindow != null ? _opticWindow : glass };
            }
            var mesh = OpticMeshes.Model(def);
            _opticMesh.sharedMesh = mesh;
            var size = mesh.bounds.size;
            float scale = Mathf.Clamp(OpticLootSize / Mathf.Max(1e-4f, Mathf.Max(size.x, Mathf.Max(size.y, size.z))), OpticMinScale, OpticMaxScale);
            var t = _opticModel.transform;
            // Lying on its rail, turned a little so the side and the window both show; centred on the pickup point.
            t.localRotation = Quaternion.Euler(0f, 35f, 0f);
            t.localScale = Vector3.one * scale;
            t.localPosition = -(t.localRotation * mesh.bounds.center) * scale;
            _opticModel.SetActive(true);
            return true;
        }
    }
}
