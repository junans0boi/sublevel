using System;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Shows the model of the weapon a combatant is holding. Models are indexed by weapon id (0 = none).
    // For the local player's eye view (first person, sights) the body is hidden, so the held weapon is drawn in
    // front of the camera instead of in the hand (ADR-048). No arms: temporary until there is a first-person rig.
    // Runs after CombatantAnimation: that turns and leans the torso the hand hangs from, which would drag an
    // eye-placed weapon off its spot.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Combatant))]
    public class WeaponVisual : MonoBehaviour
    {
        // How a weapon model sits when the character faces +Z and the muzzle points forward, measured when the
        // prefab was built: the model's rotation, where the middle of the model is relative to its origin, and its size.
        [Serializable]
        public struct Fit
        {
            public Quaternion Rotation;
            public Vector3 CenterOffset;
            public Vector3 Size;
        }

        [SerializeField] GameObject[] _modelsByWeaponId = new GameObject[0];
        [SerializeField] Fit[] _fitByWeaponId = new Fit[0];
        // Where the top rear of the weapon goes, in camera space (x right, y up, z forward).
        [SerializeField] Vector3 _sightsAnchor = new Vector3(0f, -0.035f, 0.22f);
        [SerializeField] Vector3 _hipAnchor = new Vector3(0.13f, -0.13f, 0.28f);
        [SerializeField] float _anchorChangePerSecond = 12f;

        Combatant _combatant;
        Vector3[] _handPosition;
        Quaternion[] _handRotation;
        Transform _eyeCamera;
        bool _sights;
        float _sightsBlend;
        int _atEye; // id of the model currently moved to the camera, 0 = none
        int _shown = -1;
        int _hiddenModel; // id of the model whose renderers are switched off for a magnified scope, 0 = none
        readonly System.Collections.Generic.List<Renderer> _renderers = new System.Collections.Generic.List<Renderer>();

        public int ShownWeaponId => _shown;
        public bool AtEye => _atEye != 0;
        // True while the weapon at the eye (and the optic on it) is not drawn because a magnified scope fills the view.
        public bool HiddenForScope => _hiddenModel != 0;

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _handPosition = new Vector3[_modelsByWeaponId.Length];
            _handRotation = new Quaternion[_modelsByWeaponId.Length];
            for (int i = 0; i < _modelsByWeaponId.Length; i++)
            {
                if (_modelsByWeaponId[i] == null) continue;
                _handPosition[i] = _modelsByWeaponId[i].transform.localPosition;
                _handRotation[i] = _modelsByWeaponId[i].transform.localRotation;
            }
        }

        // The owner's view sits at the eyes: draw the weapon in front of `camera`. Call every frame it applies.
        public void ShowAtEye(Transform camera, bool sights)
        {
            // Coming from the hand, start at the stance's own spot instead of sliding in from the other one.
            if (_eyeCamera == null) _sightsBlend = sights ? 1f : 0f;
            _eyeCamera = camera;
            _sights = sights;
        }

        public void ShowInHand() => _eyeCamera = null;

        void Update()
        {
            int id = _combatant.ActiveWeaponId;
            if (id == _shown) return;
            _shown = id;
            for (int i = 0; i < _modelsByWeaponId.Length; i++)
                if (_modelsByWeaponId[i] != null) _modelsByWeaponId[i].SetActive(i == id);
        }

        void SetScopeHidden(int id)
        {
            if (id != _hiddenModel && _hiddenModel != 0 && _hiddenModel < _modelsByWeaponId.Length && _modelsByWeaponId[_hiddenModel] != null)
                ForceOff(_modelsByWeaponId[_hiddenModel], false);
            _hiddenModel = id;
            // Every frame while hidden: the optic parts are rebuilt when the optic changes.
            if (id != 0) ForceOff(_modelsByWeaponId[id], true);
        }

        void ForceOff(GameObject model, bool off)
        {
            model.GetComponentsInChildren(true, _renderers);
            foreach (var r in _renderers) r.forceRenderingOff = off;
        }

        // After the animator has moved the hand.
        void LateUpdate()
        {
            bool canShow = _shown > 0 && _shown < _fitByWeaponId.Length && _shown < _modelsByWeaponId.Length && _modelsByWeaponId[_shown] != null;
            int wanted = _eyeCamera != null && canShow ? _shown : 0;
            if (_atEye != 0 && _atEye != wanted)
            {
                var back = _modelsByWeaponId[_atEye].transform;
                back.localPosition = _handPosition[_atEye];
                back.localRotation = _handRotation[_atEye];
            }
            _atEye = wanted;
            // Looking through a magnified scope the lens fills the view: the weapon and the optic body in front of the
            // camera would only cover it, so they are not drawn (the scope overlay draws the tube's ring instead).
            SetScopeHidden(wanted != 0 && ScopeView.HidesWeapon ? wanted : 0);
            if (wanted == 0) return;

            _sightsBlend = Mathf.MoveTowards(_sightsBlend, _sights ? 1f : 0f, _anchorChangePerSecond * Time.deltaTime);
            var fit = _fitByWeaponId[wanted];
            var anchor = Vector3.Lerp(_hipAnchor, _sightsAnchor, _sightsBlend);
            // From the top rear of the weapon to its middle.
            var center = anchor + new Vector3(0f, -fit.Size.y * 0.5f, fit.Size.z * 0.5f);
            var model = _modelsByWeaponId[wanted].transform;
            model.rotation = _eyeCamera.rotation * fit.Rotation;
            model.position = _eyeCamera.position + _eyeCamera.rotation * (center - fit.CenterOffset);
        }
    }
}
