using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The worn backpack on the character's back (ADR-091): the ground model (LootMeshes) of the worn tier, hung on the
    // upper-body bone so it follows the torso's lean and stance. Nothing when no backpack is worn. Added by
    // CombatantView; follows CombatantCarry.BackpackTier on every side.
    public class BackpackVisual : MonoBehaviour
    {
        static readonly string[] TorsoNames = { "Torso", "Chest", "Abdomen" }; // as CharacterKit.UpperBodyRoot
        // Where the bag's bottom centre sits, in the character's frame as the prefab stands: behind the back,
        // below the shoulders. Worn a little smaller than the (enlarged) ground model.
        static readonly Vector3 BottomInCharacter = new Vector3(0f, 0.98f, -0.115f);
        const float WornScale = 1.1f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Dictionary<Color32, Material> Tinted = new Dictionary<Color32, Material>();
        static Shader _shader;

        CombatantCarry _carry;
        Transform _mount;
        MeshFilter _filter;
        Renderer _renderer;
        // Fitted onto the measured body (ADR-520): the back panel on the back, the shoulder straps over the shoulders. Held
        // rigidly by the upper-chest bone, not skinned: the pack is one stiff block and CPU skinning (gpuSkinning off)
        // of fifty packs cost more than it showed.
        GearBody _body;
        readonly Color[] _colors = new Color[3];
        int _shown = -1;

        public int ShownTier => _shown < 0 ? 0 : _shown;
        public Renderer Renderer => _renderer;

        void Awake()
        {
            _carry = GetComponent<CombatantCarry>();
            if (_carry == null) { enabled = false; return; }
            Init();
        }

        // Lazy so a capture can show a tier in edit mode, where Awake does not run.
        void Init()
        {
            if (_mount != null) return;
            // Taken now, while the character still stands as the prefab was saved: the mount keeps that relation to
            // the bone however the animation turns it later.
            Transform bone; Vector3 at; float scale;
            bool roster = WornGearMeshes.TryFit(transform, out var fit);
            if (roster && (_body = GearBody.Load(fit.Def.Id)) != null && WornGearMeshes.Baked(LootKind.Backpack, 1) != null)
            {
                bone = fit.Torso; at = bone.position; scale = 1f; // the fitted mesh is in the body frame: origin on this bone
            }
            else if (roster) { _body = null; bone = fit.Torso; at = fit.PackBottom; scale = fit.TorsoScale; }
            else { bone = FindTorso(transform) ?? transform; at = transform.TransformPoint(BottomInCharacter); scale = WornScale; }
            _mount = new GameObject("Backpack").transform;
            _mount.SetParent(bone, false);
            _mount.position = at;
            _mount.rotation = transform.rotation; // the model hangs toward -z, its back panel on the body
            _mount.localScale = Vector3.one * (scale / Mathf.Max(1e-4f, bone.lossyScale.y));
            _mount.gameObject.layer = bone.gameObject.layer;
            _filter = _mount.gameObject.AddComponent<MeshFilter>();
            _renderer = _mount.gameObject.AddComponent<MeshRenderer>();
            _renderer.meshLodSelectionBias = WornGearMeshes.LodBias;
            _renderer.enabled = false;
        }

        void LateUpdate() => Show(_carry.BackpackTier.Value);

        public void Show(int tier)
        {
            Init();
            if (tier == _shown) return;
            _shown = tier;
            if (tier <= 0) { _renderer.enabled = false; return; }
            var mesh = _body != null ? GearFit.For(_body, LootKind.Backpack, tier) : WornGearMeshes.Model(LootKind.Backpack, tier);
            _filter.sharedMesh = mesh;
            _renderer.sharedMaterials = WornGearMeshes.Materials(LootKind.Backpack, tier);
            _renderer.enabled = mesh != null;
        }

        // A bone of the Humanoid rig under `root` (ADR-230); null for a rig without an avatar.
        internal static Transform Humanoid(Transform root, HumanBodyBones bone)
        {
            var animator = root.GetComponentInChildren<Animator>();
            return animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;
        }

        internal static Transform FindTorso(Transform root)
        {
            var chest = Humanoid(root, HumanBodyBones.Chest);
            if (chest != null) return chest;
            foreach (var name in TorsoNames)
            {
                var found = Deep(root, name);
                if (found != null) return found;
            }
            return null;
        }

        internal static Transform Deep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = Deep(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        internal static Material Tint(Color color)
        {
            if (Tinted.TryGetValue(color, out var m) && m != null) return m;
            if (_shader == null) _shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(_shader) { name = "Backpack" };
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, color);
            Tinted[color] = m;
            return m;
        }
    }
}
