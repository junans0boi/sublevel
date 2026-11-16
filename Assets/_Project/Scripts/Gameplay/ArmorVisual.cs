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
        static readonly Vector3 HelmetBaseAboveHead = new Vector3(0f, 0.07f, 0.012f);
        static readonly Vector3 VestFront = new Vector3(0f, 1.22f, 0.12f);
        static readonly Vector3 VestBack = new Vector3(0f, 1.22f, -0.12f);
        // The ground models are bigger than the body: the helmet is taller (it has to cover the model's own one),
        // the vest is narrower and shorter. Scales are along the mesh's own axes.
        static readonly Vector3 HelmetScale = new Vector3(1f, 1.15f, 1f);
        static readonly Vector3 VestScale = new Vector3(0.8f, 1f, 0.65f);

        sealed class Part
        {
            public Transform Mount;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public int Shown = -1;
        }

        CombatantGear _gear;
        Part _helmet, _vestFront, _vestBack;
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
            Apply(_vestBack);
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
            var head = BackpackVisual.Humanoid(transform, HumanBodyBones.Head) ?? BackpackVisual.Deep(transform, "Head");
            var torso = BackpackVisual.FindTorso(transform);
            _helmet = NewPart("Helmet", head != null ? head : torso != null ? torso : transform,
                head != null ? head.position + transform.TransformVector(HelmetBaseAboveHead) : transform.TransformPoint(new Vector3(0f, 1.62f, 0f)),
                transform.rotation, HelmetScale);
            var bone = torso != null ? torso : transform;
            // The flat ground model stands up: its top face (+y) turns to the front, the shoulder straps (+z) go up.
            var front = transform.rotation * Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(-90f, 0f, 0f);
            var back = transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
            _vestFront = NewPart("VestFront", bone, transform.TransformPoint(VestFront), front, VestScale);
            _vestBack = NewPart("VestBack", bone, transform.TransformPoint(VestBack), back, VestScale);
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
            part.Renderer.enabled = false;
            return part;
        }

        void LateUpdate()
        {
            Show(LootKind.Helmet, _helmet, null, _gear.HelmetTier.Value);
            Show(LootKind.Vest, _vestFront, _vestBack, _gear.VestTier.Value);
        }

        // Lets a capture show tiers in edit mode, where Awake does not run.
        public void ShowTiers(int helmet, int vest)
        {
            Init();
            Show(LootKind.Helmet, _helmet, null, helmet);
            Show(LootKind.Vest, _vestFront, _vestBack, vest);
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
            part.Filter.sharedMesh = tier > 0 ? LootMeshes.Model(kind, default, tier) : null;
            if (tier > 0)
            {
                LootMeshes.Colors(kind, default, tier, _colors);
                part.Renderer.sharedMaterials = new[] { BackpackVisual.Tint(_colors[0]), BackpackVisual.Tint(_colors[1]), BackpackVisual.Tint(_colors[2]) };
            }
            Apply(part);
        }

        void Apply(Part part)
        {
            if (part != null) part.Renderer.enabled = !_hidden && part.Shown > 0 && part.Filter.sharedMesh != null;
        }
    }
}
