using System;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Drives a combatant's animator (ADR-045).
    // Base layer: legs and whole-body states — a 2D blend over the local velocity, hit reaction, death.
    // Upper layer: arms and torso while armed — weapon lowered when just moving, raised when aiming or firing.
    // Empty hands leave the upper layer off, so the base clips show their plain arm swing.
    // Also reports the moment a foot touches down (for footsteps) and tilts the torso when leaning.
    [RequireComponent(typeof(Combatant), typeof(CombatantMotion))]
    public class CombatantAnimation : MonoBehaviour
    {
        public const string Locomotion = "Locomotion", Hit = "Hit", Death = "Death";
        public const string Lowered = "Lowered", Aim = "Aim", Shoot = "Shoot";
        public const string MoveX = "MoveX", MoveZ = "MoveZ";
        public const string Pace = "Pace"; // playback speed of the locomotion blend only

        const int BaseLayer = 0, UpperLayer = 1;
        const float Fade = 0.12f;
        const float BlendDamp = 0.08f;
        const float HitHold = 0.35f;
        const float ShootHold = 0.22f;
        const float AimAfterShot = 0.9f;       // the weapon stays up this long after the last shot
        const float UpperWeightPerSecond = 7f;
        const float MaxPace = 2.5f;
        const float StepMinSpeed = 0.6f;

        [SerializeField] Animator _animator;
        [SerializeField] Transform _spine;
        [SerializeField] Transform _footLeft;
        [SerializeField] Transform _footRight;
        // Ground speed the clips were authored for (m/s), measured when the character was built.
        [SerializeField] float _forwardClipSpeed = 5f;
        [SerializeField] float _backClipSpeed = 4f;
        [SerializeField] float _sideClipSpeed = 4f;
        // Foot height above the character's origin: planted below the first, in the air above the second.
        [SerializeField] float _footPlantHeight = 0.12f;
        [SerializeField] float _footLiftHeight = 0.2f;

        Combatant _combatant;
        CombatantMotion _motion;
        FootContact _leftContact, _rightContact;
        int _moveX, _moveZ, _pace;
        string _base = Locomotion;
        string _upper;
        float _baseHoldUntil, _upperHoldUntil, _aimPoseUntil;
        float _upperWeight;
        float _lastHp;
        int _lastShotFrame = -1;

        // Raised on the frame a foot lands while the combatant is walking or running.
        public event Action FootDown;

        public string BaseState => _base;
        // Null while the upper layer is off (empty hands or dead).
        public string UpperState => _upperWeight > 0.5f ? _upper : null;
        public Vector2 MoveBlend => _animator != null ? new Vector2(_animator.GetFloat(_moveX), _animator.GetFloat(_moveZ)) : Vector2.zero;

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _motion = GetComponent<CombatantMotion>();
            _combatant.ShotFired += OnShot;
            _leftContact = new FootContact(_footPlantHeight, _footLiftHeight);
            _rightContact = new FootContact(_footPlantHeight, _footLiftHeight);
            _moveX = Animator.StringToHash(MoveX);
            _moveZ = Animator.StringToHash(MoveZ);
            _pace = Animator.StringToHash(Pace);
        }

        void OnDestroy() => _combatant.ShotFired -= OnShot;

        void OnShot(Vector3 origin, Vector3 end)
        {
            // A shotgun reports every pellet; one shot is one animation.
            if (_animator == null || _combatant.ActiveWeaponId == 0 || Time.frameCount == _lastShotFrame) return;
            _lastShotFrame = Time.frameCount;
            _animator.CrossFadeInFixedTime(Shoot, 0.04f, UpperLayer, 0f);
            _upper = Shoot;
            _upperHoldUntil = Time.time + ShootHold;
            _aimPoseUntil = Time.time + AimAfterShot;
        }

        void Update()
        {
            if (_animator == null) return;
            bool dead = _combatant.Dead.Value;
            float hp = _combatant.Hp.Value;
            bool hit = hp < _lastHp && !dead;
            _lastHp = hp;

            var v = _motion.LocalVelocity;
            _animator.SetFloat(_moveX, dead ? 0f : v.x, BlendDamp, Time.deltaTime);
            _animator.SetFloat(_moveZ, dead ? 0f : v.z, BlendDamp, Time.deltaTime);

            // Only the legs speed up to keep pace with the ground; weapon handling keeps its own timing.
            _animator.SetFloat(_pace, dead ? 1f : LocomotionPace(v));
            if (dead) SetBase(Death);
            else
            {
                if (hit)
                {
                    _animator.CrossFadeInFixedTime(Hit, 0.05f, BaseLayer, 0f);
                    _base = Hit;
                    _baseHoldUntil = Time.time + HitHold;
                }
                else if (Time.time >= _baseHoldUntil) SetBase(Locomotion);
            }

            bool armed = !dead && _combatant.ActiveWeaponId != 0;
            // The flinch is a whole-body clip: let it through the weapon pose while it plays.
            bool showUpper = armed && _base != Hit;
            _upperWeight = Mathf.MoveTowards(_upperWeight, showUpper ? 1f : 0f, UpperWeightPerSecond * Time.deltaTime);
            _animator.SetLayerWeight(UpperLayer, _upperWeight);
            if (armed && Time.time >= _upperHoldUntil)
                SetUpper(_combatant.AimMode != AimMode.Hip || Time.time < _aimPoseUntil ? Aim : Lowered);
        }

        // The blend tree covers speeds up to each clip's own; beyond that the clips play faster so the feet keep up.
        float LocomotionPace(Vector3 localVelocity)
        {
            float speed = new Vector2(localVelocity.x, localVelocity.z).magnitude;
            if (speed < 0.1f) return 1f;
            float along = Mathf.Abs(localVelocity.z) / speed, across = Mathf.Abs(localVelocity.x) / speed;
            float clip = along * (localVelocity.z >= 0f ? _forwardClipSpeed : _backClipSpeed) + across * _sideClipSpeed;
            return Mathf.Clamp(speed / Mathf.Max(0.1f, clip / (along + across)), 1f, MaxPace);
        }

        void SetBase(string state)
        {
            if (_base == state) return;
            _animator.CrossFadeInFixedTime(state, Fade, BaseLayer);
            _base = state;
        }

        void SetUpper(string state)
        {
            if (_upper == state) return;
            _animator.CrossFadeInFixedTime(state, Fade, UpperLayer);
            _upper = state;
        }

        // After the animator has posed the skeleton: read the feet, then lean the torso on top of the pose.
        void LateUpdate()
        {
            if (_animator == null) return;
            if (_footLeft != null && _footRight != null)
            {
                bool left = _leftContact.Update(transform.InverseTransformPoint(_footLeft.position).y);
                bool right = _rightContact.Update(transform.InverseTransformPoint(_footRight.position).y);
                var v = _motion.LocalVelocity;
                bool walking = !_combatant.Dead.Value && !_motion.Airborne && new Vector2(v.x, v.z).sqrMagnitude > StepMinSpeed * StepMinSpeed;
                if ((left || right) && walking) FootDown?.Invoke();
            }

            // Only on frames the animator has just re-posed the skeleton; otherwise the tilt would pile up.
            float lean = _combatant.LeanAmount;
            if (_spine != null && Mathf.Abs(lean) > 1e-3f && Time.deltaTime > 0f && _animator.isActiveAndEnabled)
                // Leaning right tips the torso clockwise seen from behind: a negative turn about the forward axis.
                _spine.rotation = Quaternion.AngleAxis(-lean * GameData.Rules.Lean.BodyRollDeg, transform.forward) * _spine.rotation;
        }
    }
}
