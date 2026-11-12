using System;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Drives a combatant's animator (ADR-045).
    // Base layer: legs and whole-body states — idle/walk/run/back-pedal by speed, jump poses, hit reaction, death.
    // There are no sidestep clips: the whole model turns toward the way it moves and the torso turns back to the
    // aim, so the legs run where the body goes while the weapon stays on target (ADR-049).
    // Upper layer: arms and torso while armed — weapon lowered when just moving, raised when aiming or firing.
    // Empty hands leave the upper layer off, so the base clips show their plain arm swing.
    // Also reports the moment a foot touches down (for footsteps) and tilts the torso when leaning.
    [RequireComponent(typeof(Combatant), typeof(CombatantMotion))]
    public class CombatantAnimation : MonoBehaviour
    {
        public const string Locomotion = "Locomotion", Air = "Air", Hit = "Hit", Death = "Death";
        public const string Lowered = "Lowered", Aim = "Aim", Shoot = "Shoot";
        public const string Move = "Move"; // speed along the legs' heading, negative when back-pedalling
        public const string Pace = "Pace"; // playback speed of the locomotion blend only
        public const string AirPhase = "AirPhase"; // which moment of the run cycle the air state holds (0..1)

        const int BaseLayer = 0, UpperLayer = 1;
        const float Fade = 0.12f;
        const float BlendDamp = 0.08f;
        const float HitHold = 0.35f;
        const float ShootHold = 0.22f;
        const float AimAfterShot = 0.9f;       // the weapon stays up this long after the last shot
        const float UpperWeightPerSecond = 7f;
        const float StepMinSpeed = 0.6f;
        const float AirPhasePerSecond = 1.5f;  // how fast the jump pose moves between rise, fall and landing
        const float StepPauseAfterLanding = 0.15f;

        [SerializeField] Animator _animator;
        [SerializeField] Transform _spine;
        [SerializeField] Transform _hips;
        [SerializeField] Transform _footLeft;
        [SerializeField] Transform _footRight;
        // Ground speed the clips were authored for (m/s), measured when the character was built.
        [SerializeField] float _forwardClipSpeed = 5f;
        [SerializeField] float _backClipSpeed = 4f;
        // Foot height above the character's origin: planted below the first, in the air above the second.
        [SerializeField] float _footPlantHeight = 0.12f;
        [SerializeField] float _footLiftHeight = 0.2f;

        Combatant _combatant;
        CombatantMotion _motion;
        FootContact _leftContact, _rightContact;
        readonly LegHeading _legs = new LegHeading();
        Quaternion _modelRest;
        Quaternion _hipsRest; // hips relative to the model in the pose the prefab was saved in (weapon aimed forward)
        int _move, _pace, _airPhaseId;
        float _airPhase;
        bool _inAir;
        float _landUntil = -1f; // touched down: the landing pose is held until this time
        string _base = Locomotion;
        string _upper;
        float _baseHoldUntil, _upperHoldUntil, _aimPoseUntil;
        float _upperWeight;
        float _lastHp;
        int _lastShotFrame = -1;

        // Raised on the frame a foot lands while the combatant is walking or running.
        public event Action FootDown;
        // Raised when the combatant comes down from a jump or a drop.
        public event Action Landed;

        public string BaseState => _base;
        // Null while the upper layer is off (empty hands or dead).
        public string UpperState => _upperWeight > 0.5f ? _upper : null;
        // Speed fed to the legs (negative = back-pedalling) and how far they are turned from the facing, right positive.
        public float MoveDrive => _animator != null ? _animator.GetFloat(_move) : 0f;
        public float LegTurnDeg => _legs.TurnDeg;

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _motion = GetComponent<CombatantMotion>();
            _combatant.ShotFired += OnShot;
            _leftContact = new FootContact(_footPlantHeight, _footLiftHeight);
            _rightContact = new FootContact(_footPlantHeight, _footLiftHeight);
            _move = Animator.StringToHash(Move);
            if (_animator != null)
            {
                _modelRest = _animator.transform.localRotation;
                if (_hips != null) _hipsRest = Quaternion.Inverse(_animator.transform.rotation) * _hips.rotation;
            }
            _pace = Animator.StringToHash(Pace);
            _airPhaseId = Animator.StringToHash(AirPhase);
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

            var v = dead ? Vector3.zero : _motion.LocalVelocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            _legs.Tick(v.x, v.z, Time.deltaTime, GameData.Rules.Gait);
            _animator.transform.localRotation = Quaternion.AngleAxis(_legs.TurnDeg, Vector3.up) * _modelRest;
            _animator.SetFloat(_move, _legs.Backward ? -speed : speed, BlendDamp, Time.deltaTime);

            // Only the legs speed up to keep pace with the ground; weapon handling keeps its own timing.
            _animator.SetFloat(_pace, Game.Core.Locomotion.LegPace(speed, _legs.Backward ? _backClipSpeed : _forwardClipSpeed, GameData.Rules.Gait));
            bool showAir = !dead && TrackAir();
            if (dead) SetBase(Death);
            else
            {
                if (hit)
                {
                    _animator.CrossFadeInFixedTime(Hit, 0.05f, BaseLayer, 0f);
                    _base = Hit;
                    _baseHoldUntil = Time.time + HitHold;
                }
                else if (Time.time >= _baseHoldUntil) SetBase(showAir ? Air : Locomotion);
            }
            _animator.SetFloat(_airPhaseId, _airPhase);

            bool armed = !dead && _combatant.ActiveWeaponId != 0;
            // The flinch is a whole-body clip: let it through the weapon pose while it plays.
            bool showUpper = armed && _base != Hit;
            _upperWeight = Mathf.MoveTowards(_upperWeight, showUpper ? 1f : 0f, UpperWeightPerSecond * Time.deltaTime);
            _animator.SetLayerWeight(UpperLayer, _upperWeight);
            if (armed && Time.time >= _upperHoldUntil)
                SetUpper(_combatant.AimMode != AimMode.Hip || Time.time < _aimPoseUntil ? Aim : Lowered);
        }

        // Follows take-off and touch-down, picks the jump pose, and says whether the base layer should show it: all
        // the time off the ground, and for a moment after touching down (the landing pose). Runs every frame, so a
        // landing is noticed even while another whole-body state (a flinch) is playing.
        bool TrackAir()
        {
            var jump = GameData.Rules.Jump;
            if (_motion.Airborne)
            {
                // Taking off: start on the rise pose instead of sweeping to it from wherever the last jump ended.
                if (!_inAir) _airPhase = jump.RisePhase;
                _inAir = true;
                _landUntil = -1f;
                _airPhase = Mathf.MoveTowards(_airPhase, Game.Core.Locomotion.AirPhase(_motion.VerticalSpeed, jump), AirPhasePerSecond * Time.deltaTime);
                return true;
            }
            if (_inAir)
            {
                _inAir = false;
                _landUntil = Time.time + jump.LandSeconds;
                Landed?.Invoke();
            }
            if (Time.time >= _landUntil) return false;
            _airPhase = Mathf.MoveTowards(_airPhase, jump.LandPhase, AirPhasePerSecond * Time.deltaTime);
            return true;
        }

        // How far the playing clip has turned the hips from their rest facing, in degrees about the vertical.
        float HipsYaw()
        {
            if (_hips == null) return 0f;
            var fromRest = _hips.rotation * Quaternion.Inverse(_animator.transform.rotation * _hipsRest);
            var facing = Vector3.ProjectOnPlane(fromRest * transform.forward, transform.up);
            return facing.sqrMagnitude < 1e-6f ? 0f : Vector3.SignedAngle(transform.forward, facing, transform.up);
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
                // The landing has its own sound; a foot settling out of the jump pose is not another step.
                bool walking = !_combatant.Dead.Value && _base == Locomotion && Time.time > _landUntil + StepPauseAfterLanding
                    && new Vector2(v.x, v.z).sqrMagnitude > StepMinSpeed * StepMinSpeed;
                if ((left || right) && walking) FootDown?.Invoke();
            }

            // Only on frames the animator has just re-posed the skeleton; otherwise the turns would pile up.
            if (_spine == null || Time.deltaTime <= 0f || !_animator.isActiveAndEnabled) return;
            // The model is turned with the legs; bring the torso (and with it head, arms and weapon) back to the facing.
            // The weapon poses were made for hips that face forward, but a running clip swings and turns the hips;
            // while a weapon pose is on, take that out too or the weapon points off to the side when moving.
            // (Not for the dead: the death clip turns the hips on purpose.)
            float turnBack = -_legs.TurnDeg - (_combatant.Dead.Value ? 0f : HipsYaw() * _upperWeight);
            if (Mathf.Abs(turnBack) > 1e-3f)
                _spine.rotation = Quaternion.AngleAxis(turnBack, transform.up) * _spine.rotation;
            float lean = _combatant.LeanAmount;
            if (Mathf.Abs(lean) > 1e-3f)
                // Leaning right tips the torso clockwise seen from behind: a negative turn about the forward axis.
                _spine.rotation = Quaternion.AngleAxis(-lean * GameData.Rules.Lean.BodyRollDeg, transform.forward) * _spine.rotation;
        }
    }
}
