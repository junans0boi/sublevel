using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Serialization;

namespace Game.Gameplay
{
    // Shows the optic fitted to the weapon in hand, on every side: the code-built optic model (OpticMeshes, ADR-060
    // (6)(8)) as a child of the weapon model, so it travels with it in the hand and in front of the camera alike.
    // Rebuilt whenever the weapon in hand or its optic changes.
    // Looking down a 1x sight from the eye (sights aim), the weapon in front of the camera is nudged so the sight
    // window sits on the middle of the screen, a hand's width from the eye, with the receiver showing below it.
    // The builder wires the same model and fit arrays as WeaponVisual plus three materials (dark metal, lens glass,
    // see-through window). Without the metal material nothing is drawn (logged once).
    // Runs after WeaponVisual, which places the weapon at the eye in its LateUpdate.
    [DefaultExecutionOrder(110)]
    [RequireComponent(typeof(Combatant))]
    public class OpticVisual : MonoBehaviour
    {
        [SerializeField] GameObject[] _modelsByWeaponId = new GameObject[0];
        [SerializeField] WeaponVisual.Fit[] _fitByWeaponId = new WeaponVisual.Fit[0];
        [FormerlySerializedAs("_material")]
        [SerializeField] Material _metal;
        [SerializeField] Material _glass;
        [SerializeField] Material _window;

        // How far back from the middle of the weapon the optic sits, as a share of the weapon's length.
        const float ScopeRearShare = 0.08f;
        const float SightRearShare = 0.20f;
        // The optic's feet sink this far into the top of the weapon.
        const float Sink = 0.004f;
        // Where the sight window goes in front of the eye while looking down a 1x sight (metres).
        const float ReflexEyeDistance = 0.16f;
        const float ProjectorEyeDistance = 0.18f;
        const float EyeBlendPerSecond = 12f;

        Combatant _combatant;
        CombatantOptics _optics;
        WeaponVisual _weaponVisual;
        GameObject _anchor;
        Transform _windowPoint;
        Renderer _renderer;
        Material[] _materials;
        OpticDef _def;
        int _builtWeapon = -1, _builtOptic = -1;
        float _eyeBlend;
        bool _warned, _forcedOff;

        // The optic parts currently on the weapon in hand (null when none are shown).
        public GameObject Parts => _anchor;
        public int ShownOptic => _anchor != null ? _builtOptic : 0;
        // Middle of the sight window (1x sights) or of the eye lens (scopes); null when no optic is shown.
        public Transform Window => _anchor != null ? _windowPoint : null;

        // [metal, glass, window] for other code-built optic views (the scope eyepiece); null entries when unwired.
        public Material[] Materials => _materials ??= new[] { _metal, _glass != null ? _glass : _metal, _window != null ? _window : _glass != null ? _glass : _metal };

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _optics = GetComponent<CombatantOptics>();
            _weaponVisual = GetComponent<WeaponVisual>();
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
            _windowPoint = null;
            _renderer = null;
            _def = null;
            _forcedOff = false;
            if (weapon <= 0 || optic == 0) return;
            Build(weapon, OpticRules.Current.Find(optic));
        }

        void Build(int weapon, OpticDef def)
        {
            if (def == null || weapon >= _modelsByWeaponId.Length || weapon >= _fitByWeaponId.Length || _modelsByWeaponId[weapon] == null) return;
            if (_metal == null)
            {
                if (!_warned) Debug.LogWarning("OpticVisual has no material: optics are not drawn on " + name);
                _warned = true;
                return;
            }
            var model = _modelsByWeaponId[weapon].transform;
            var fit = _fitByWeaponId[weapon];
            // The anchor sits at the top of the weapon, behind its middle, in the pose the weapon was fitted in
            // (muzzle along +z, up is +y), measured in metres whatever the model's own scale is.
            var ls = model.lossyScale;
            float scale = Mathf.Max(1e-4f, (ls.x + ls.y + ls.z) / 3f);
            var inverse = Quaternion.Inverse(fit.Rotation);
            float rear = def.Kind == OpticKind.Scope ? ScopeRearShare : SightRearShare;
            var offset = fit.CenterOffset + new Vector3(0f, fit.Size.y * 0.5f - Sink, -fit.Size.z * rear);
            _anchor = new GameObject("Optic" + def.Id);
            var t = _anchor.transform;
            t.SetParent(model, false);
            t.localRotation = inverse;
            t.localPosition = inverse * offset / scale;
            t.localScale = Vector3.one / scale;
            _anchor.layer = model.gameObject.layer;

            _anchor.AddComponent<MeshFilter>().sharedMesh = OpticMeshes.Model(def);
            _renderer = _anchor.AddComponent<MeshRenderer>();
            _renderer.sharedMaterials = Materials;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var window = new GameObject("Window").transform;
            window.SetParent(t, false);
            window.localPosition = OpticMeshes.WindowCentre(def);
            _windowPoint = window;
            _def = def;
        }

        // After WeaponVisual has put the weapon in front of the camera for this frame.
        void LateUpdate()
        {
            bool atEye = _anchor != null && _weaponVisual != null && _weaponVisual.AtEye;
            bool sight = atEye && _def != null && _def.Kind != OpticKind.Scope && IsLocalSights();
            _eyeBlend = Mathf.MoveTowards(_eyeBlend, sight ? 1f : 0f, EyeBlendPerSecond * Time.deltaTime);

            // Looking through a magnified scope the eyepiece view replaces the optic in front of the camera.
            bool off = atEye && ScopeView.HidesWeapon;
            if (_renderer != null && off != _forcedOff)
            {
                _forcedOff = off;
                _renderer.forceRenderingOff = off;
            }

            if (!atEye || _eyeBlend <= 0f || _windowPoint == null || _builtWeapon <= 0 || _builtWeapon >= _modelsByWeaponId.Length) return;
            var cam = Camera.main;
            if (cam == null) return;
            // Move the whole weapon so the window comes to the middle of the view at a fixed distance from the eye.
            var camT = cam.transform;
            var local = camT.InverseTransformPoint(_windowPoint.position);
            float distance = _def.Kind == OpticKind.Dot ? ReflexEyeDistance : ProjectorEyeDistance;
            var shift = new Vector3(local.x, local.y, local.z - distance) * _eyeBlend;
            _modelsByWeaponId[_builtWeapon].transform.position -= camT.rotation * shift;
        }

        bool IsLocalSights()
        {
            var pc = PlayerController.Local;
            return pc != null && pc.gameObject == gameObject && pc.Aim == AimMode.Sights;
        }
    }
}
