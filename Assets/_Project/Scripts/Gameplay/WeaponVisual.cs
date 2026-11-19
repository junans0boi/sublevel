using System;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Shows the model of the weapon a combatant is holding. Models are indexed by weapon id (0 = none).
    // For the local player's eye view (first person, sights) the body is hidden, so the held weapon is drawn in
    // front of the camera instead of in the hand (ADR-048), with gloved forearms reaching to its grips (FirstPersonArms,
    // #216) and the view model's own motion on top (ViewmodelMotion, #217).
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
        [SerializeField] Vector3 _sightsAnchor = new Vector3(0f, -0.014f, 0.22f);
        [SerializeField] Vector3 _hipAnchor = new Vector3(0.13f, -0.13f, 0.28f);
        [SerializeField] float _anchorChangePerSecond = 12f;
        const float HipYawDeg = 3.5f, HandDrop = 0.022f, HandDropSights = 0.03f, PistolExtraDrop = 0.03f;

        Combatant _combatant;
        CombatantAnimation _anim;
        CombatantMotion _body;
        readonly ViewmodelMotion _motion = new ViewmodelMotion();
        FirstPersonArms _arms;
        Vector3[] _gripRest;        // per model: where the hand bone is, in the model's own axes (meters)
        Vector3 _lastEyeAngles;
        bool _hadEyeAngles;
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
        // The camera the weapon is drawn in front of this frame, or null while it is in the hand (MeleeVisual).
        public Transform EyeCamera => _atEye != 0 ? _eyeCamera : null;
        // The model shown for a weapon id, or null.
        public GameObject ModelFor(int weaponId) => weaponId > 0 && weaponId < _modelsByWeaponId.Length ? _modelsByWeaponId[weaponId] : null;
        // How the model of a weapon id was fitted when the prefab was built (AttachmentVisual places parts by it).
        public bool TryFit(int weaponId, out Fit fit)
        {
            bool ok = weaponId > 0 && weaponId < _fitByWeaponId.Length;
            fit = ok ? _fitByWeaponId[weaponId] : default;
            return ok;
        }
        // Where spent cases leave the weapon shown this frame (#199), at the eye or in the hand: the right side of the
        // receiver a little above and ahead of the grip (behind it on the bullpup), and the weapon's right, up and forward.
        public bool TryEjectionPort(out Vector3 port, out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            int id = _shown;
            port = right = up = forward = default;
            if (id <= 0 || id >= _fitByWeaponId.Length || id >= _modelsByWeaponId.Length || _modelsByWeaponId[id] == null
                || !_modelsByWeaponId[id].activeInHierarchy || _fitByWeaponId[id].Rotation == default) return false;
            var fit = _fitByWeaponId[id];
            var model = _modelsByWeaponId[id].transform;
            // The fitted frame: x right, y up, z along the muzzle.
            var basis = model.rotation * Quaternion.Inverse(fit.Rotation);
            right = basis * Vector3.right; up = basis * Vector3.up; forward = basis * Vector3.forward;
            var grip = model.position + model.rotation * _gripRest[id];
            var center = model.position + basis * fit.CenterOffset;
            bool pistol = fit.Size.z < 0.4f;
            float ahead = id == BullpupId ? -0.17f : pistol ? 0.02f : 0.07f;
            float above = pistol ? 0.045f : 0.06f;
            // Sideways from the middle of the model (the grip hangs under its centre line), out to its right face.
            float side = Vector3.Dot(center - grip, right) + fit.Size.x * 0.45f;
            port = grip + right * side + up * above + forward * ahead;
            return true;
        }
        const int BullpupId = 3; // the Warden: its action sits behind the grip

        // The magazine of a weapon model as a piece of its own (#200): OnGun is drawn on the gun (hidden while the
        // reload has it in the hand), Mesh/Materials draw the one in the hand, Rotation and Scale turn the weapon frame
        // into the mesh's, Center is the mesh's middle in its own space.
        public sealed class Magazine
        {
            public GameObject OnGun;
            public Mesh Mesh;
            public Material[] Materials;
            public Quaternion Rotation;
            public Vector3 Scale, Center;
        }

        Magazine[] _mags;
        bool[] _magTried;

        // The magazine of weapon `id`: the model's "Magazine" piece, split off its mesh when the prefab was built
        // (MagazineKit) or a block standing in for it; null when it has none (pistols, the break-action shotgun).
        public Magazine MagazineOf(int id)
        {
            if (id <= 0 || id >= _modelsByWeaponId.Length || _modelsByWeaponId[id] == null || id >= _fitByWeaponId.Length) return null;
            if (_mags == null) { _mags = new Magazine[_modelsByWeaponId.Length]; _magTried = new bool[_modelsByWeaponId.Length]; }
            if (_magTried[id]) return _mags[id];
            _magTried[id] = true;
            var model = _modelsByWeaponId[id].transform;
            var fit = _fitByWeaponId[id];
            if (fit.Rotation == default) return null;
            Transform piece = null;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == "Magazine") { piece = t; break; }
            if (piece == null || !piece.TryGetComponent(out MeshFilter filter) || !piece.TryGetComponent(out MeshRenderer renderer) || filter.sharedMesh == null) return null;
            var basis = model.rotation * Quaternion.Inverse(fit.Rotation);
            return _mags[id] = new Magazine { OnGun = piece.gameObject, Mesh = filter.sharedMesh, Materials = renderer.sharedMaterials,
                Rotation = Quaternion.Inverse(basis) * piece.rotation, Scale = piece.lossyScale, Center = filter.sharedMesh.bounds.center };
        }

        // True while the weapon at the eye (and the optic on it) is not drawn because a magnified scope fills the view.
        public bool HiddenForScope => _hiddenModel != 0;

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _anim = GetComponent<CombatantAnimation>();
            _body = GetComponent<CombatantMotion>();
            _handPosition = new Vector3[_modelsByWeaponId.Length];
            _handRotation = new Quaternion[_modelsByWeaponId.Length];
            _gripRest = new Vector3[_modelsByWeaponId.Length];
            for (int i = 0; i < _modelsByWeaponId.Length; i++)
            {
                if (_modelsByWeaponId[i] == null) continue;
                var parent = _modelsByWeaponId[i].transform.parent;
                if (parent != null) _gripRest[i] = Quaternion.Inverse(_modelsByWeaponId[i].transform.rotation) * (parent.position - _modelsByWeaponId[i].transform.position);
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

        void OnEnable() => _combatant.ShotFired += OnShot;
        void OnDisable() { _combatant.ShotFired -= OnShot; if (_arms != null) { _arms.Destroy(); _arms = null; } }
        void OnShot(Vector3 a, Vector3 b) => _motion.Kick();

        // The forearms, made on first use from the uniform and glove materials the body already carries.
        FirstPersonArms Arms()
        {
            if (_arms != null) return _arms;
            Material source = null;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.sharedMaterial != null) { source = r.sharedMaterial; break; }
            if (source == null) return null;
            Material Flat(Color c)
            {
                var m = new Material(source) { name = "FirstPersonArm", color = c };
                m.mainTexture = null;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); // tubes are seen from either side of their wall
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                return m;
            }
            _arms = new FirstPersonArms(transform, Flat(new Color(0.17f, 0.19f, 0.16f)), Flat(new Color(0.1f, 0.1f, 0.105f)));
            return _arms;
        }

        void Update()
        {
            int id = _anim != null ? _anim.HeldId : _combatant.ActiveWeaponId;
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
            if (wanted == 0) { PlaceItemArms(); return; }
            _propAtEye = false;

            _sightsBlend = Mathf.MoveTowards(_sightsBlend, _sights ? 1f : 0f, _anchorChangePerSecond * Time.deltaTime);
            var fit = _fitByWeaponId[wanted];
            var anchor = Vector3.Lerp(HipAnchorFor(fit), SightsAnchorFor(fit), _sightsBlend);
            // From the top rear of the weapon to its middle.
            var center = anchor + new Vector3(0f, -fit.Size.y * 0.5f, fit.Size.z * 0.5f);
            var model = _modelsByWeaponId[wanted].transform;
            // Reload and draw: the weapon dips and tips (same motion as third person), the magazine shows in hand (#200).
            var act = _anim != null ? _anim.ActionShown : default;
            // A magazine reload comes up toward the middle of the view and rolls over so the well and the hand at it are
            // in sight (#200); a draw still dips.
            float reload = _anim != null && _anim.ReloadProgress >= 0f ? Mathf.Clamp01(act.PitchDeg / 18f) : 0f;
            center += new Vector3(0f, -act.Down * 1.6f * (1f - reload), 0f) + new Vector3(-0.08f, 0.07f, -0.03f) * reload;
            var actTurn = Quaternion.Euler(act.PitchDeg * 0.7f - 22f * reload, -12f * reload, -act.RollDeg * 0.9f - 25f * reload);
            var origin = center - fit.CenterOffset; // the model's origin in camera space

            // The view model's own motion (#217), turning about the right hand.
            var m = _motion.Step(ViewInput());
            // At the hip the muzzle points a little in toward the middle of the view, so every class aims at about one spot.
            var rm = Quaternion.Euler(m.PitchDeg, m.YawDeg - HipYawDeg * (1f - _sightsBlend), m.RollDeg);
            var restTurn = actTurn * fit.Rotation;
            var pivot = origin + restTurn * _gripRest[wanted];
            origin = pivot + rm * (origin - pivot) + new Vector3(m.X, m.Y, m.Z);

            model.rotation = _eyeCamera.rotation * rm * restTurn;
            model.position = _eyeCamera.position + _eyeCamera.rotation * origin;
            PlaceArms(wanted, model, rm * actTurn, act);
        }

        // Per weapon class by length (#229): a pistol is held out at arm's length with its sights well ahead of the eye,
        // an SMG a little nearer, the long guns tucked in; the hip spots keep each class's muzzle pointing at about the same place.
        Vector3 SightsAnchorFor(Fit fit) => fit.Size.z < 0.4f ? new Vector3(0f, -0.012f, 0.42f) : fit.Size.z < 0.65f ? new Vector3(0f, -0.012f, 0.32f) : _sightsAnchor;
        Vector3 HipAnchorFor(Fit fit) => fit.Size.z < 0.4f ? new Vector3(0.12f, -0.125f, 0.36f) : fit.Size.z < 0.65f ? new Vector3(0.13f, -0.135f, 0.3f) : _hipAnchor;

        bool _propAtEye;

        // First person melee (MeleeVisual moves the model): the right hand closed on the grip where the third-person hand
        // holds it, the left arm kept low and out of view at rest; swinging (`swing` 0..1), it comes up and round with the
        // turning torso (`torso`, world rotation of the turned chest) so the body is seen to put itself into the blow (#219).
        public void PlaceMeleeArms(Transform model, int id, float swing = 0f, Quaternion? torso = null)
        {
            var arms = Arms();
            if (arms == null || _eyeCamera == null) return;
            var cam = _eyeCamera;
            arms.SetVisible(true);
            var grip = model.position + model.rotation * _gripRest[id];
            arms.Place(cam, 1, grip, cam.right * 0.6f + cam.up * 0.5f, 1f);
            var body = torso ?? cam.rotation;
            var leftHand = Vector3.Lerp(new Vector3(0.1f, -0.5f, 0.1f), new Vector3(0.16f, -0.3f, 0.3f), swing);
            arms.Place(cam, 0, FirstPersonArms.Shoulder(cam, 0) + body * leftHand, cam.up * 0.4f - cam.right * 0.5f, 0.4f + 0.5f * swing, false);
        }

        // Nothing aimed: the hands with a throwable, a bandage or a bottle, or reaching for loot (#220 #221 #212).
        void PlaceItemArms()
        {
            bool on = _eyeCamera != null && _anim != null && _anim.ItemActive && !_combatant.Dead.Value;
            var arms = on ? Arms() : null;
            if (arms == null)
            {
                if (_arms != null) _arms.SetVisible(false);
                if (_propAtEye && _anim != null) { var p = _anim.Props(); if (p != null) p.Hide(); _propAtEye = false; }
                // Only the eye view's own magazine: in third person CombatantAnimation shows it, after which this ran and hid it.
                if (_anim != null && _eyeCamera != null) _anim.ShowMagAt(false, Vector3.zero, Quaternion.identity);
                return;
            }
            var pose = _anim.ItemPose;
            float w = _anim.ItemWeight;
            var cam = _eyeCamera;
            var rest = new Vector3(0.15f, -0.45f, 0.05f);
            arms.SetVisible(true);
            // The body-frame spots are at the chest, below the lens: in view they ride higher and further out.
            var lift = new Vector3(0.02f, 0.14f, 0.34f);
            Vector3 Spot(int side, Vector3 off, float weight) =>
                FirstPersonArms.Shoulder(cam, side) + cam.rotation * (lift * weight * w) + cam.rotation * Vector3.Lerp(rest * (side == 1 ? 1f : -1f) + new Vector3(side == 1 ? 0f : 0.3f, 0f, 0f), off, weight * w);
            if (pose.RightW > 0f) arms.Place(cam, 1, Spot(1, new Vector3(pose.RX, pose.RY, pose.RZ), pose.RightW), cam.up * 0.6f + cam.right * 0.5f, pose.Curl);
            else arms.Place(cam, 1, Spot(1, Vector3.zero, 0f), cam.up * 0.6f + cam.right * 0.5f, 0.3f, false);
            if (pose.LeftW > 0f) arms.Place(cam, 0, Spot(0, new Vector3(pose.LX, pose.LY, pose.LZ), pose.LeftW), cam.up * 0.4f - cam.right * 0.5f, 0.6f);
            else arms.Place(cam, 0, Spot(0, Vector3.zero, 0f), cam.up * 0.4f - cam.right * 0.5f, 0.3f, false);
            var props = _anim.Props();
            if (props != null)
            {
                if (pose.Prop != HandProp.None && w > 0.3f) { props.Show(pose.Prop, arms.PalmPosition(1) + cam.up * 0.04f + cam.forward * 0.02f, Quaternion.LookRotation(cam.forward, Vector3.up)); _propAtEye = true; }
                else { props.Hide(); _propAtEye = false; }
            }
        }

        ViewmodelInput ViewInput()
        {
            var v = _body != null ? _body.LocalVelocity : Vector3.zero;
            float speed = new Vector2(v.x, v.z).magnitude;
            var loco = GameData.Rules.Locomotion;
            float sprint = loco.SprintSpeed > loco.WalkSpeed ? Mathf.InverseLerp(loco.WalkSpeed * 1.05f, loco.SprintSpeed * 0.9f, speed) : 0f;
            var angles = _eyeCamera.eulerAngles;
            float yawRate = 0f, pitchRate = 0f;
            if (_hadEyeAngles && Time.deltaTime > 0f)
            {
                yawRate = Mathf.DeltaAngle(_lastEyeAngles.y, angles.y) / Time.deltaTime;
                pitchRate = Mathf.DeltaAngle(_lastEyeAngles.x, angles.x) / Time.deltaTime;
            }
            _lastEyeAngles = angles; _hadEyeAngles = true;
            return new ViewmodelInput
            {
                Dt = Time.deltaTime, Speed = speed, RunSpeed = loco.WalkSpeed, Sprint01 = sprint, Aim01 = _sightsBlend,
                Airborne = _body != null && _body.Airborne, VerticalSpeed = _body != null ? _body.VerticalSpeed : 0f,
                LookYawRate = Mathf.Clamp(yawRate, -600f, 600f), LookPitchRate = Mathf.Clamp(pitchRate, -400f, 400f),
            };
        }

        // Both gloved hands on the weapon: the right where the third-person hand holds it, the left on the handguard
        // (or cupping the right on a pistol), or at the magazine well while reloading.
        void PlaceArms(int id, Transform model, Quaternion weaponTurn, ActionPose act)
        {
            float ahead = _anim != null ? _anim.SupportAheadOf(id) : 0f;
            var arms = ahead > 0f && !HiddenForScope ? Arms() : null;
            if (arms == null)
            {
                if (_arms != null) _arms.SetVisible(false);
                if (_anim != null) _anim.ShowMagAt(false, Vector3.zero, Quaternion.identity);
                return;
            }
            arms.SetVisible(true);
            var cam = _eyeCamera;
            var basis = cam.rotation * weaponTurn;
            var axis = basis * Vector3.forward; var up = basis * Vector3.up; var across = basis * Vector3.right;
            // The palm sits a little under the model's hand point so the glove does not stand beside the receiver in the sights.
            var right = model.position + model.rotation * _gripRest[id] - up * (HandDrop + HandDropSights * _sightsBlend + (_anim.IsPistolGrip(id) ? PistolExtraDrop * (1f + _sightsBlend) : 0f));
            var left = _anim.IsPistolGrip(id)
                ? right - across * CombatantAnimation.SupportPistolCupSide + axis * ahead - up * 0.01f
                : right + axis * ahead - up * CombatantAnimation.SupportPalmBelowBore;
            if (act.LeftBlend > 0.001f)
                left = Vector3.Lerp(left, right + axis * act.LeftA + up * act.LeftU + across * act.LeftC, act.LeftBlend);
            arms.Place(cam, 1, right, across * 0.75f + up * 0.65f, 0.85f);
            arms.Place(cam, 0, left, -up * 0.9f - across * 0.3f, act.LeftBlend > 0.001f ? 0.45f : 0.75f);
            _anim.ShowMagAt(act.MagInHand, left - up * 0.02f, basis);
        }
    }
}
