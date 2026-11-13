using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Shows a pickup as the thing it holds, lying on the floor under the pickup point (#114): guns and melee weapons
    // as their models (guns turned on their side), an optic as its code-built model (OpticMeshes), everything else
    // as a shared code-built model (LootMeshes) on the box's renderer in shared tinted materials. The pickup collider
    // stays where it was; only the visuals move down onto whatever is below. A kind without a model keeps the box.
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
        static readonly Vector3 OpticShape = new Vector3(0.45f, 0.45f, 1.1f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        // One copy of a kind's material per colour, shared by every pickup (keeps them batchable).
        static readonly Dictionary<(int, Color32), Material> Tinted = new Dictionary<(int, Color32), Material>();
        static readonly RaycastHit[] Hits = new RaycastHit[16];
        // The optic model's longest side on the floor (metres), within these enlargement limits.
        const float OpticLootSize = 0.34f, OpticMinScale = 1.4f, OpticMaxScale = 4.5f;
        // How far above and below the pickup point the floor is looked for.
        const float FloorAbove = 0.3f, FloorBelow = 1.2f;

        LootItem _item;
        bool _ready;
        Transform _visual; // everything drawn, moved as one onto the floor
        MeshFilter _boxFilter;
        Mesh _boxMesh;
        Vector3 _boxScale = Vector3.one;
        readonly Color[] _colors = new Color[3];
        int _shownKind = -1;
        int _shownWeapon = -1;
        int _shownAmmo = -1;
        int _shownTier = -1;
        int _shownOptic = -1;
        GameObject _opticModel;
        MeshFilter _opticMesh;

        // The optic model shown for this pickup (null when it holds no optic or the materials are missing).
        public GameObject OpticModel => _opticModel != null && _opticModel.activeSelf ? _opticModel : null;

        // The code-built model shown on the box renderer (null while the box itself or another model is shown).
        public Mesh ShownMesh => _boxFilter != null && _boxFilter.sharedMesh != _boxMesh && !_renderer.forceRenderingOff ? _boxFilter.sharedMesh : null;

        // The armour colour by piece and tier (helmets blue, vests orange, darker = higher). Tiers outside the table get the nearest one.
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

        void Awake() => Init();

        // Lazy so the view also works where Awake has not run (edit-mode captures).
        void Init()
        {
            if (_ready) return;
            _ready = true;
            _item = GetComponent<LootItem>();
            _visual = new GameObject("Visual").transform;
            _visual.gameObject.layer = gameObject.layer;
            _visual.SetParent(transform, false);
            if (_renderer != null)
            {
                _boxScale = _renderer.transform.localScale;
                _boxFilter = _renderer.GetComponent<MeshFilter>();
                if (_boxFilter != null) _boxMesh = _boxFilter.sharedMesh;
                _renderer.transform.SetParent(_visual, false);
            }
            for (int i = 0; i < _weaponModelsById.Length; i++)
            {
                var model = _weaponModelsById[i];
                if (model == null) continue;
                model.transform.SetParent(_visual, false);
                // Guns were placed upright as held; on the floor they lie on their side (melee models already lie).
                if (!MeleeMeshes.Has(i)) model.transform.localRotation = Quaternion.Euler(0f, 0f, 90f) * model.transform.localRotation;
            }
        }

        void Update()
        {
            Show((LootKind)_item.Kind.Value, _item.WeaponId.Value, (AmmoType)_item.Ammo.Value, _item.Tier.Value, _item.Optic.Value);
        }

        // Shows what the pickup holds (Update follows the synced values; captures call it directly).
        public void Show(LootKind lootKind, int weapon, AmmoType ammo, int tier, int optic)
        {
            Init();
            int kind = (int)lootKind;
            bool armor = Armor.IsArmor(lootKind);
            bool opticKind = lootKind == LootKind.Optic;
            if (lootKind != LootKind.Weapon) weapon = 0;
            if (lootKind != LootKind.Ammo) ammo = 0;
            if (!armor && lootKind != LootKind.Backpack) tier = 0;
            if (!opticKind) optic = 0;
            if (kind == _shownKind && weapon == _shownWeapon && (int)ammo == _shownAmmo && tier == _shownTier && optic == _shownOptic) return;
            if (_renderer == null || kind < 0) return;
            _shownKind = kind;
            _shownWeapon = weapon;
            _shownAmmo = (int)ammo;
            _shownTier = tier;
            _shownOptic = optic;

            var material = kind < _materialsByKind.Length ? _materialsByKind[kind] : _materialsByKind.Length > 0 ? _materialsByKind[0] : _renderer.sharedMaterial;
            bool attachment = opticKind && Attachments.SlotOf(optic) is AttachSlot place && place != AttachSlot.Optic;
            var mesh = attachment ? LootMeshes.Attachment(optic) : LootMeshes.Model(lootKind, ammo, tier);
            var box = _renderer.transform;
            if (mesh != null && _boxFilter != null && material != null)
            {
                _boxFilter.sharedMesh = mesh;
                box.localScale = Vector3.one;
                if (attachment) LootMeshes.AttachmentColors(_colors); else LootMeshes.Colors(lootKind, ammo, tier, _colors);
                _renderer.sharedMaterials = new[] { Tint(material, _colors[0]), Tint(material, _colors[1]), Tint(material, _colors[2]) };
            }
            else
            {
                if (_boxFilter != null) _boxFilter.sharedMesh = _boxMesh;
                // Without the optic materials an optic is a bar tinted by its zoom.
                box.localScale = opticKind ? Vector3.Scale(_boxScale, OpticShape) : _boxScale;
                _renderer.sharedMaterial = opticKind && material != null ? Tint(material, OpticColor(optic)) : material;
            }

            bool hasModel = weapon > 0 && weapon < _weaponModelsById.Length && _weaponModelsById[weapon] != null;
            bool opticModel = opticKind && !attachment && ShowOptic(OpticRules.Current.Find(optic));
            if ((!opticKind || attachment) && _opticModel != null) _opticModel.SetActive(false);
            // forceRenderingOff, not enabled: FishNet switches the renderers' enabled flag itself on a host when the
            // object becomes visible to the local client, which brought the box back over a freshly spawned optic.
            _renderer.forceRenderingOff = hasModel || opticModel;
            for (int i = 0; i < _weaponModelsById.Length; i++)
                if (_weaponModelsById[i] != null) _weaponModelsById[i].SetActive(hasModel && i == weapon);
            PlaceOnFloor();
        }

        static Material Tint(Material material, Color color)
        {
            var key = (material.GetInstanceID(), (Color32)color);
            if (Tinted.TryGetValue(key, out var tinted) && tinted != null) return tinted;
            tinted = new Material(material) { name = material.name + "_" + ColorUtility.ToHtmlStringRGB(color) };
            if (tinted.HasProperty(BaseColorId)) tinted.SetColor(BaseColorId, color);
            if (tinted.HasProperty(ColorId)) tinted.SetColor(ColorId, color);
            Tinted[key] = tinted;
            return tinted;
        }

        // Centres what is drawn over the pickup point and sets it down on the first solid thing below (not a
        // combatant or a moving body). With nothing below in reach it stays centred on the point, as before.
        void PlaceOnFloor()
        {
            _visual.localPosition = Vector3.zero;
            bool any = false;
            var bounds = new Bounds();
            foreach (var r in _visual.GetComponentsInChildren<Renderer>())
            {
                if (r.forceRenderingOff) continue;
                // From the mesh itself: a renderer's bounds can lag a mesh swapped in the same frame.
                var filter = r.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    var local = filter.sharedMesh.bounds;
                    var m = r.transform.localToWorldMatrix;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = m.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                        if (!any) bounds = new Bounds(corner, Vector3.zero); else bounds.Encapsulate(corner);
                        any = true;
                    }
                }
                else
                {
                    if (!any) bounds = r.bounds; else bounds.Encapsulate(r.bounds);
                    any = true;
                }
            }
            if (!any) return;
            var pivot = transform.position;
            var shift = new Vector3(pivot.x - bounds.center.x, pivot.y - bounds.center.y, pivot.z - bounds.center.z);
            if (FindFloor(pivot, out float floor)) shift.y = floor - bounds.min.y + 0.002f;
            _visual.position += shift;
        }

        bool FindFloor(Vector3 pivot, out float floor)
        {
            floor = 0f;
            int n = Physics.RaycastNonAlloc(pivot + Vector3.up * FloorAbove, Vector3.down, Hits, FloorAbove + FloorBelow,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = Hits[i].collider;
                if (c.transform.IsChildOf(transform) || c.attachedRigidbody != null || c.GetComponentInParent<Combatant>() != null) continue;
                if (Hits[i].distance < best) { best = Hits[i].distance; floor = Hits[i].point.y; }
            }
            return best < float.MaxValue;
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
                _opticModel.transform.SetParent(_visual, false);
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
