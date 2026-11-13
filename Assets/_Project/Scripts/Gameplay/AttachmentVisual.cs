using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Shows the muzzle device, grip, stock and magazine fitted to the weapon in hand (#154, ADR-120): the code-built
    // attachment models of LootMeshes as children of the weapon model, so they travel with it in the hand and in front
    // of the camera alike (same anchoring as OpticVisual). Rebuilt when the weapon in hand or its fit changes.
    // Placement is by proportions of the weapon's measured box (WeaponVisual.Fit): muzzle at the barrel's end, grip under
    // the handguard, magazine down from the receiver, stock at the rear. Added by CombatantView like BackpackVisual.
    [DefaultExecutionOrder(115)]
    [RequireComponent(typeof(Combatant))]
    public class AttachmentVisual : MonoBehaviour
    {
        static readonly Color[] Colors = new Color[3];
        static readonly Dictionary<(int, int), Material> Tinted = new Dictionary<(int, int), Material>();
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");

        Combatant _combatant;
        CombatantOptics _optics;
        WeaponVisual _weapon;
        OpticVisual _opticVisual;
        readonly GameObject[] _parts = new GameObject[Attachments.Slots];
        int _builtWeapon = -1, _builtFit = -1;

        // The model of what is fitted in one place on the weapon in hand, or null.
        public GameObject Part(AttachSlot slot) => _parts[(int)slot];

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _optics = GetComponent<CombatantOptics>();
            _weapon = GetComponent<WeaponVisual>();
            _opticVisual = GetComponent<OpticVisual>();
        }

        void Update()
        {
            int weapon = _combatant.ActiveWeaponId;
            int slot = _combatant.ActiveSlot.Value;
            int fit = 0;
            if (_optics != null)
                for (int s = 1; s < Attachments.Slots; s++) fit = Attachments.With(fit, (AttachSlot)s, _optics.AttachmentInSlot(slot, (AttachSlot)s));
            if (weapon == _builtWeapon && fit == _builtFit) return;
            _builtWeapon = weapon;
            _builtFit = fit;
            Clear();
            var metal = _opticVisual != null ? _opticVisual.Materials[0] : null;
            var model = _weapon != null ? _weapon.ModelFor(weapon) : null;
            if (weapon <= 0 || fit == 0 || metal == null || model == null || !_weapon.TryFit(weapon, out var shape)) return;
            Build(model.transform, shape, fit, metal, _parts, weapon);
        }

        // The stock is not drawn in the eye view: at the hip it fills a corner of the screen and in the sights it sits
        // in front of the lens (the gun is drawn right at the camera there).
        void LateUpdate()
        {
            var stock = _parts[(int)AttachSlot.Stock];
            if (stock == null || _weapon == null) return;
            var r = stock.GetComponent<MeshRenderer>();
            if (r != null) r.forceRenderingOff = _weapon.AtEye;
        }

        void Clear()
        {
            for (int i = 0; i < _parts.Length; i++)
            {
                if (_parts[i] != null) Destroy(_parts[i]);
                _parts[i] = null;
            }
        }

        // Builds the parts of `packed` (slots 1..4) under `model`; into[slot] gets each one's object.
        public static void Build(Transform model, WeaponVisual.Fit fit, int packed, Material metal, GameObject[] into, int weaponId)
        {
            var ls = model.lossyScale;
            float scale = Mathf.Max(1e-4f, (ls.x + ls.y + ls.z) / 3f);
            var inverse = Quaternion.Inverse(fit.Rotation);
            for (int s = 1; s < Attachments.Slots; s++)
            {
                int id = Attachments.Get(packed, (AttachSlot)s);
                var mesh = id != 0 ? LootMeshes.Attachment(id) : null;
                if (mesh == null) continue;
                Pose((AttachSlot)s, weaponId, fit, out var position, out var rotation);
                var go = new GameObject("Attachment" + id);
                var t = go.transform;
                t.SetParent(model, false);
                t.localRotation = inverse * rotation;
                t.localPosition = inverse * position / scale;
                t.localScale = Vector3.one / scale;
                go.layer = model.gameObject.layer;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                LootMeshes.AttachmentColors(Colors);
                r.sharedMaterials = new[] { Tint(metal, Colors[0]), Tint(metal, Colors[1]), Tint(metal, Colors[2]) };
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                into[s] = go;
            }
        }

        // Where each place sits on a weapon, as shares of its measured box from the middle (z along the barrel, y up):
        // the barrel's height at the muzzle, the underside of the handguard for a grip, the feed lips of the magazine well,
        // and the height of the stock. Found by looking at the captures (AttachmentOnGunCaptureTests).
        public struct Mount
        {
            public float MuzzleY, GripZ, GripY, MagZ, MagY, StockY;
        }

        static readonly Mount Default = new Mount { MuzzleY = 0.18f, GripZ = 0.31f, GripY = -0.04f, MagZ = 0.15f, MagY = -0.03f, StockY = 0.10f };

        public static Mount MountOf(int weaponId)
        {
            switch (weaponId)
            {
                case 1: return new Mount { MuzzleY = 0.25f, MagZ = -0.2f, MagY = -0.15f }; // pistol: the magazine is in the grip, at the back
                case 5: return new Mount { MuzzleY = 0.18f, GripZ = 0.31f, GripY = -0.04f, MagZ = 0.02f, MagY = 0.02f, StockY = 0.10f }; // sniper rifle
                default: return Default;
            }
        }

        // Where an attachment's model origin goes and how it turns, in the weapon's fitted pose (muzzle along +z, up
        // +y, units metres, measured from the middle of the weapon's box). The ground models lie on their side, so
        // each one is turned upright here and shifted so its axis lands on the weapon's.
        public static void Pose(AttachSlot slot, int weaponId, WeaponVisual.Fit fit, out Vector3 position, out Quaternion rotation)
        {
            var c = fit.CenterOffset;
            var size = fit.Size;
            var m = MountOf(weaponId);
            switch (slot)
            {
                case AttachSlot.Muzzle:
                    // mesh x (the barrel axis) -> +z; the collar goes over the end of the barrel
                    rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                    position = c + new Vector3(0f, size.y * m.MuzzleY - 0.035f, size.z * 0.5f + 0.05f);
                    break;
                case AttachSlot.Grip:
                    // the post (mesh z) points down, the clamp (mesh x) lies along the rail
                    rotation = Quaternion.LookRotation(Vector3.down, Vector3.left);
                    position = c + new Vector3(0f, size.y * m.GripY, size.z * m.GripZ) + rotation * new Vector3(0f, -0.038f, 0f);
                    break;
                case AttachSlot.Stock:
                    rotation = Quaternion.identity;
                    position = c + new Vector3(0f, size.y * m.StockY - 0.035f, -size.z * 0.5f + 0.13f);
                    break;
                default: // Mag: stands in the magazine well, feed lips up
                    rotation = Quaternion.LookRotation(Vector3.right, Vector3.forward);
                    position = c + new Vector3(0f, size.y * m.MagY - 0.1f, size.z * m.MagZ - 0.03f);
                    break;
            }
        }

        static Material Tint(Material material, Color color)
        {
            var key = (material.GetInstanceID(), ((Color32)color).GetHashCode());
            if (Tinted.TryGetValue(key, out var tinted) && tinted != null) return tinted;
            tinted = new Material(material) { name = material.name + "_att" };
            if (tinted.HasProperty(BaseColorId)) tinted.SetColor(BaseColorId, color);
            if (tinted.HasProperty(ColorId)) tinted.SetColor(ColorId, color);
            Tinted[key] = tinted;
            return tinted;
        }
    }
}
