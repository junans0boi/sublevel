using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The worn helmet and vest on the character (#179): the ground models of the worn tiers (LootMeshes, tier shows in
    // the colour and the plate bars), hung on the head and the upper-body bone so they follow the animation. Nothing
    // for a piece that is not worn (a broken helmet disappears). Added by CombatantView; follows CombatantGear on every
    // side. The own eye view hides them with the body (PlayerController).
    public class ArmorVisual : MonoBehaviour
    {
        // Character-frame placement, as the prefab stands (metres): helmet base above the head bone, vest panels on
        // the chest and the back.
        static readonly Vector3 HelmetBaseAboveHead = new Vector3(0f, 0.11f, 0.03f);
        static readonly Vector3 VestCentre = new Vector3(0f, 1.22f, 0f);
        // The ground models are bigger than the body: the helmet is taller (it has to cover the model's own one),
        // the vest is narrower and shorter. Scales are along the mesh's own axes.
        static readonly Vector3 HelmetScale = new Vector3(1.6f, 1.6f, 1.6f);
        static readonly Vector3 VestScale = new Vector3(1.15f, 1.15f, 1.15f);

        sealed class Part
        {
            public Transform Mount;
            public MeshFilter Filter;
            public Renderer Renderer;
            public SkinnedMeshRenderer Skin; // a wrapped piece (GearFit): skinned to the torso bones
            public GearBody Body;
            public int Shown = -1;
        }

        CombatantGear _gear;
        Part _helmet, _vestFront; // the vest is one wrap-around model now
        readonly Color[] _colors = new Color[3];

        public int ShownHelmetTier => _helmet == null || _helmet.Shown < 0 ? 0 : _helmet.Shown;
        public int ShownVestTier => _vestFront == null || _vestFront.Shown < 0 ? 0 : _vestFront.Shown;
        public bool HelmetVisible => _helmet != null && _helmet.Renderer.enabled;
        public bool VestVisible => _vestFront != null && _vestFront.Renderer.enabled;

        // Every renderer of the worn pieces (the eye view switches them with the body).
        public void SetVisible(bool visible)
        {
            _hidden = !visible;
            Apply(_helmet);
            Apply(_vestFront);
        }

        bool _hidden;

        void Awake()
        {
            _gear = GetComponent<CombatantGear>();
            if (_gear == null) { enabled = false; return; }
            Init();
        }

        void Init()
        {
            if (_helmet != null) return;
            if (WornGearMeshes.TryFit(transform, out var fit))
            {
                // Roster character: mounts on the Humanoid head and chest bones, sized by head height and shoulder width.
                _helmet = NewPart("Helmet", fit.Head, fit.HelmetBase, transform.rotation, Vector3.one * fit.HeadScale);
                // The vest wrapped onto this body and skinned to its torso bones when the body was measured (ADR-520).
                var body = GearBody.Load(fit.Def.Id);
                // isHuman: without a Humanoid avatar (a worktree lacks the gitignored roster FBX) GetBoneTransform throws; keep the rigid vest.
                _vestFront = body != null && fit.Def.Animator != null && fit.Def.Animator.isHuman && WornGearMeshes.Baked(LootKind.Vest, 1) != null
                    ? new Part { Skin = GearFit.AddRenderer(fit.Def.Animator, "Vest"), Body = body }
                    : NewPart("Vest", fit.Torso, fit.VestCentre, transform.rotation, Vector3.one * fit.TorsoScale);
                if (_vestFront.Skin != null) { _vestFront.Renderer = _vestFront.Skin; _vestFront.Mount = _vestFront.Skin.transform; }
                return;
            }
            var head = BackpackVisual.Humanoid(transform, HumanBodyBones.Head) ?? BackpackVisual.Deep(transform, "Head");
            var torso = BackpackVisual.FindTorso(transform);
            _helmet = NewPart("Helmet", head != null ? head : torso != null ? torso : transform,
                head != null ? head.position + transform.TransformVector(HelmetBaseAboveHead) : transform.TransformPoint(new Vector3(0f, 1.62f, 0f)),
                transform.rotation, HelmetScale);
            var bone = torso != null ? torso : transform;
            _vestFront = NewPart("Vest", bone, transform.TransformPoint(VestCentre), transform.rotation, VestScale);
        }

        Part NewPart(string name, Transform bone, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var part = new Part { Mount = new GameObject(name).transform };
            part.Mount.SetParent(bone, false);
            part.Mount.SetPositionAndRotation(position, rotation);
            part.Mount.localScale = scale / Mathf.Max(1e-4f, bone.lossyScale.y);
            part.Mount.gameObject.layer = bone.gameObject.layer;
            part.Filter = part.Mount.gameObject.AddComponent<MeshFilter>();
            part.Renderer = part.Mount.gameObject.AddComponent<MeshRenderer>();
            part.Renderer.meshLodSelectionBias = WornGearMeshes.LodBias;
            part.Renderer.enabled = false;
            return part;
        }

        void LateUpdate()
        {
            Show(LootKind.Helmet, _helmet, null, _gear.HelmetTier.Value);
            Show(LootKind.Vest, _vestFront, null, _gear.VestTier.Value);
        }

        // Lets a capture show tiers in edit mode, where Awake does not run.
        public void ShowTiers(int helmet, int vest)
        {
            Init();
            Show(LootKind.Helmet, _helmet, null, helmet);
            Show(LootKind.Vest, _vestFront, null, vest);
        }

        void Show(LootKind kind, Part a, Part b, int tier)
        {
            Init();
            if (tier == a.Shown) return;
            a.Shown = tier;
            if (b != null) b.Shown = tier;
            Fill(kind, a, tier);
            if (b != null) Fill(kind, b, tier);
        }

        void Fill(LootKind kind, Part part, int tier)
        {
            if (part.Skin != null) part.Skin.sharedMesh = tier > 0 ? GearFit.For(part.Body, kind, tier) : null;
            else part.Filter.sharedMesh = tier > 0 ? WornGearMeshes.Model(kind, tier) : null;
            if (tier > 0) part.Renderer.sharedMaterials = WornGearMeshes.Materials(kind, tier);
            Apply(part);
        }

        void Apply(Part part)
        {
            if (part != null) part.Renderer.enabled = !_hidden && part.Shown > 0 && (part.Skin != null ? part.Skin.sharedMesh : part.Filter.sharedMesh) != null;
        }
    }
}
