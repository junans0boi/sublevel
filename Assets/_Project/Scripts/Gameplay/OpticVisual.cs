using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Shows the optic fitted to the weapon in hand, on every side: a few boxes and cylinders built in code (no
    // model asset yet, ADR-058) as children of the weapon model, so they travel with it, in the hand and in front
    // of the camera alike. Rebuilt whenever the weapon in hand or its optic changes.
    // The builder wires the same model and fit arrays as WeaponVisual, plus one plain lit material.
    // Without a material nothing is drawn (logged once).
    [RequireComponent(typeof(Combatant))]
    public class OpticVisual : MonoBehaviour
    {
        [SerializeField] GameObject[] _modelsByWeaponId = new GameObject[0];
        [SerializeField] WeaponVisual.Fit[] _fitByWeaponId = new WeaponVisual.Fit[0];
        [SerializeField] Material _material;

        // How far back from the middle of the weapon the optic sits, as a share of the weapon's length.
        const float RearShare = 0.08f;
        // The optic's feet sink this far into the top of the weapon.
        const float Sink = 0.004f;

        static readonly Color Body = new Color(0.09f, 0.09f, 0.10f);
        static readonly Color DotWindow = new Color(0.55f, 0.12f, 0.09f);
        static readonly Color HoloWindow = new Color(0.18f, 0.32f, 0.42f);
        static readonly Color ScopeLens = new Color(0.20f, 0.36f, 0.56f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static MaterialPropertyBlock _block;

        Combatant _combatant;
        CombatantOptics _optics;
        GameObject _anchor;
        int _builtWeapon = -1, _builtOptic = -1;
        bool _warned;

        // The optic parts currently on the weapon in hand (null when none are shown).
        public GameObject Parts => _anchor;
        public int ShownOptic => _anchor != null ? _builtOptic : 0;

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _optics = GetComponent<CombatantOptics>();
        }

        void Update()
        {
            int weapon = _combatant.ActiveWeaponId;
            int optic = _optics != null ? _optics.ActiveOptic : 0;
            if (weapon == _builtWeapon && optic == _builtOptic) return;
            _builtWeapon = weapon;
            _builtOptic = optic;
            if (_anchor != null) Destroy(_anchor);
            _anchor = null;
            if (weapon <= 0 || optic == 0) return;
            Build(weapon, OpticRules.Current.Find(optic));
        }

        void Build(int weapon, OpticDef def)
        {
            if (def == null || weapon >= _modelsByWeaponId.Length || weapon >= _fitByWeaponId.Length || _modelsByWeaponId[weapon] == null) return;
            if (_material == null)
            {
                if (!_warned) Debug.LogWarning("OpticVisual has no material: optics are not drawn on " + name);
                _warned = true;
                return;
            }
            var model = _modelsByWeaponId[weapon].transform;
            var fit = _fitByWeaponId[weapon];
            // The anchor sits at the top of the weapon, a little behind its middle, in the pose the weapon was fitted
            // in (muzzle along +z, up is +y), measured in metres whatever the model's own scale is.
            var ls = model.lossyScale;
            float scale = Mathf.Max(1e-4f, (ls.x + ls.y + ls.z) / 3f);
            var inverse = Quaternion.Inverse(fit.Rotation);
            var offset = fit.CenterOffset + new Vector3(0f, fit.Size.y * 0.5f, -fit.Size.z * RearShare);
            _anchor = new GameObject("Optic" + def.Id);
            var t = _anchor.transform;
            t.SetParent(model, false);
            t.localRotation = inverse;
            t.localPosition = inverse * offset / scale;
            t.localScale = Vector3.one / scale;

            switch (def.Kind)
            {
                case OpticKind.Dot: BuildSight(t, 0.034f, 0.046f, 0.050f, DotWindow); break;
                case OpticKind.Holo: BuildSight(t, 0.040f, 0.050f, 0.072f, HoloWindow); break;
                default: BuildScope(t, def.Zoom); break;
            }
        }

        // An open hoop: a base on the rail, two thin side posts and a top bar, with a tinted rim on the front edge.
        // Nothing fills the window, so looking down the sights the world shows through it and the screen reticle
        // (ScopeView) sits inside the frame; a solid box with glass covered the aim point (wave-4 review).
        void BuildSight(Transform parent, float width, float height, float length, Color window)
        {
            const float wall = 0.0035f;
            float baseTop = -Sink + 0.010f;
            Part(parent, PrimitiveType.Cube, new Vector3(0f, -Sink + 0.005f, 0f), Vector3.zero, new Vector3(width * 0.8f, 0.010f, length), Body);
            float postX = (width - wall) * 0.5f;
            float midY = baseTop + height * 0.5f;
            for (int side = -1; side <= 1; side += 2)
                Part(parent, PrimitiveType.Cube, new Vector3(side * postX, midY, 0f), Vector3.zero, new Vector3(wall, height, length * 0.8f), Body);
            Part(parent, PrimitiveType.Cube, new Vector3(0f, baseTop + height - wall * 0.5f, 0f), Vector3.zero, new Vector3(width, wall, length * 0.8f), Body);
            // The tinted rim: a thin strip inside the front of the top bar, the only coloured part.
            Part(parent, PrimitiveType.Cube, new Vector3(0f, baseTop + height - wall * 1.2f, length * 0.4f), Vector3.zero, new Vector3(width - wall * 2f, wall * 0.6f, 0.003f), window);
        }

        // A tube on two mounts with a bell at each end and a lens at the front; longer and wider the higher the zoom.
        void BuildScope(Transform parent, float zoom)
        {
            float radius = 0.014f + 0.0012f * zoom;
            float length = Mathf.Min(0.30f, 0.09f + 0.018f * zoom);
            float centreY = -Sink + 0.014f + radius;
            var lie = new Vector3(90f, 0f, 0f); // a cylinder stands along y; this lays it along the muzzle
            // Unity's cylinder is 2 long and 1 across: scale y is half the length, x and z the diameter.
            Part(parent, PrimitiveType.Cylinder, new Vector3(0f, centreY, 0f), lie, new Vector3(radius * 2f, length * 0.5f, radius * 2f), Body);
            Part(parent, PrimitiveType.Cylinder, new Vector3(0f, centreY, length * 0.5f + 0.012f), lie, new Vector3(radius * 2.7f, 0.012f, radius * 2.7f), Body);
            Part(parent, PrimitiveType.Cylinder, new Vector3(0f, centreY, -length * 0.5f - 0.010f), lie, new Vector3(radius * 2.4f, 0.010f, radius * 2.4f), Body);
            Part(parent, PrimitiveType.Cylinder, new Vector3(0f, centreY, length * 0.5f + 0.0245f), lie, new Vector3(radius * 2.4f, 0.0008f, radius * 2.4f), ScopeLens);
            for (int side = -1; side <= 1; side += 2)
                Part(parent, PrimitiveType.Cube, new Vector3(0f, -Sink + 0.007f, side * length * 0.28f), Vector3.zero, new Vector3(0.016f, 0.016f, 0.014f), Body);
        }

        void Part(Transform parent, PrimitiveType type, Vector3 position, Vector3 euler, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = type.ToString();
            // Shots and pickups must not meet these.
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            t.localEulerAngles = euler;
            t.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _block ??= new MaterialPropertyBlock();
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            r.SetPropertyBlock(_block);
        }
    }
}
