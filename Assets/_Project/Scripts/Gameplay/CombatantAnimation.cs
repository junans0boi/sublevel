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
    // Empty hands leave the upper layer off, so the base clips show their plain arm swing; a punch (ADR-059)
    // raises it for the moment the punch clip plays, also while moving.
    // Also reports the moment a foot touches down (for footsteps) and tilts the torso when leaning.
    // The jump has no clip: the air state holds a standing pose and the push, tuck and landing are bone turns
    // made here on top of it (ADR-048). Crouching has no clip either: the same bone turns bend the knees and the
    // model is lowered by what the bend takes off the legs, on top of whatever the base layer plays (ADR-053).
// Lying down (ADR-058) is made the same way: the whole model is pitched forward about the character's right axis
// with its feet as the pivot and moved back so the head sits under the eyes, the chest and head are turned back
// up, and the arms are turned back by the rest so a held weapon stays level and points where the player aims.
// Worked out with Unity's turn direction (a positive turn about the right axis tips the top of a body forward and
// down): the body's up (0,1,0) goes to (0,cos a,sin a) and its facing (0,0,1) to (0,-sin a,cos a), so at 85 degrees
// the body lies along +Z, belly down. The chest then turns -40 about the same axis: its facing is 45 degrees below
// level, which is the head lifted; the arms turn -45 more and are level again.
    [RequireComponent(typeof(Combatant), typeof(CombatantMotion))]
    public class CombatantAnimation : MonoBehaviour
    {
        public const string Locomotion = "Locomotion", Air = "Air", Hit = "Hit", Death = "Death";
        public const string Lowered = "Lowered", Aim = "Aim", Shoot = "Shoot";
        public const string PunchLeft = "PunchLeft", PunchRight = "PunchRight";
        public const string Move = "Move"; // speed along the legs' heading, negative when back-pedalling
        public const string Pace = "Pace"; // playback speed of the locomotion blend only
        public const string AirPhase = "AirPhase"; // which moment of its clip the air state holds (0..1)

        const int BaseLayer = 0, UpperLayer = 1;
        const float Fade = 0.12f;
        const float BlendDamp = 0.08f;
        const float HitHold = 0.35f;
        const float ShootHold = 0.22f;
        const float AimAfterShot = 0.9f;       // the weapon stays up this long after the last shot
        const float UpperWeightPerSecond = 7f;
        const float PunchHold = 0.5f;          // the upper layer shows the punch this long
        const float PunchWeightPerSecond = 20f; // a punch is quick: the layer comes up faster than for a weapon
        const float StepMinSpeed = 0.6f;
        const float AirPhasePerSecond = 1.5f;  // how fast the jump pose moves between rise, fall and landing
        const float StepPauseAfterLanding = 0.15f;
        const float TakeoffAttackShare = 0.3f; // of the take-off push spent getting into it
        const float LandArmShare = 0.5f;       // of the arm lift shown again while absorbing a landing
        const float FallbackLegLength = 0.9f;  // hip to ankle, when the leg bones are not wired
        // Bones turned in code after the animator has posed them, parents before children.
        const int Torso = 0, ThighLeft = 1, ThighRight = 2, ShinLeft = 3, ShinRight = 4, FootLeft = 5, FootRight = 6,
            ArmLeft = 7, ArmRight = 8, TurnedBones = 9;

        [SerializeField] Animator _animator;
        [SerializeField] Transform _spine;
        [SerializeField] Transform _hips;
        [SerializeField] Transform _footLeft;
        [SerializeField] Transform _footRight;
        // Upper legs, lower legs, upper arms; left then right of each.
        [SerializeField] Transform[] _jumpBones;
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
        Vector3 _modelRestPosition;
        Quaternion _hipsRest; // hips relative to the model in the pose the prefab was saved in (weapon aimed forward)
        readonly Vector3[] _footOwnPosition = new Vector3[2], _footLeftAtPosition = new Vector3[2];
        readonly bool[] _footMoved = new bool[2];
        int _move, _pace, _airPhaseId;
        float _airPhase;
        bool _inAir;
        float _landUntil = -1f; // touched down: the landing pose is held until this time
        float _takeoffAt = float.NegativeInfinity; // left the ground going up
        float _landedAt = float.NegativeInfinity;
        float _push, _tuck, _dip; // 0..1: take-off push, knees tucked up, landing absorbed
        float _drop;              // how far the model is lowered right now (m)
        CombatantStance _stance;  // missing on a prefab built before stances existed: never crouches
        float _legLength = FallbackLegLength;
        float _crouch;            // 0..1: how much of the crouch is shown
        float _crouchWalk;        // 0..1: how much the crouched body is walking (eased); a walk bends the knees less
        float _prone;             // 0..1: how much of the lying pose is shown
        float _legTurnShown;      // the leg turn actually applied to the model (none while lying down)
        float _parkour;           // 0..1: how much of the vault/climb pose is shown (ADR-065 (4))
        float _seated;            // 0..1: how much of the seated pose is shown (in a car, ADR-065 (5))
        bool _driverPose;         // seated at the wheel: empty hands go to the steering wheel
        const float CrouchWalkBlendPerSecond = 6f;
        // A bone no playing clip keys keeps what was written to it last, so a turn added every frame would pile
        // up: each turned bone's own rotation is remembered and put back first if the animator has not re-posed it.
        readonly Transform[] _bones = new Transform[TurnedBones];
        readonly Quaternion[] _boneOwn = new Quaternion[TurnedBones];
        readonly Quaternion[] _boneLeftAt = new Quaternion[TurnedBones];
        readonly bool[] _boneTurned = new bool[TurnedBones];
        string _base = Locomotion;
        string _upper;
        float _baseHoldUntil, _upperHoldUntil, _aimPoseUntil;
        float _punchUntil = -1f;
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
        // The upper-layer state last started, whatever the layer's weight.
        public string UpperStateStarted => _upper;
        // Punches shown so far (both hands).
        public int PunchesShown { get; private set; }
        // Speed fed to the legs (negative = back-pedalling) and how far they are turned from the facing, right positive.
        public float MoveDrive => _animator != null ? _animator.GetFloat(_move) : 0f;
        public float LegTurnDeg => _legs.TurnDeg;
        // How far the knees are tucked up in the air and how deep a landing is being absorbed, both 0..1.
        public float JumpTuck => _tuck;
        public float LandDip => _dip;
        // How much of the crouch pose is shown, 0..1, and how far the model is lowered for it and for landings (m).
        public float CrouchPose => _crouch;
        // How much of the lying pose is shown, 0..1.
        public float PronePose => _prone;
        // How much of the parkour pose (knees tucked, hands forward onto the top) is shown, 0..1.
        public float ParkourPose => _parkour;
        // How much of the seated pose is shown, 0..1. Follows the replicated seats (VehicleSeat), eased over the
        // 0.6 s it takes to get in or out, so it is the same on every side.
        public float SeatedPose => _seated;
        public float ModelDrop => _drop;

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _motion = GetComponent<CombatantMotion>();
            _combatant.ShotFired += OnShot;
            _combatant.Punched += OnPunch;
            _leftContact = new FootContact(_footPlantHeight, _footLiftHeight);
            _rightContact = new FootContact(_footPlantHeight, _footLiftHeight);
            _move = Animator.StringToHash(Move);
            if (_animator != null)
            {
                _modelRest = _animator.transform.localRotation;
                _modelRestPosition = _animator.transform.localPosition;
                if (_hips != null) _hipsRest = Quaternion.Inverse(_animator.transform.rotation) * _hips.rotation;
            }
            _pace = Animator.StringToHash(Pace);
            _airPhaseId = Animator.StringToHash(AirPhase);
            _bones[Torso] = _spine;
            _bones[FootLeft] = _footLeft;
            _bones[FootRight] = _footRight;
            for (int i = 0; _jumpBones != null && i < _jumpBones.Length; i++)
            {
                // Feet sit between the shins and the arms in the turn order.
                int slot = i < 4 ? ThighLeft + i : i < 6 ? ArmLeft + i - 4 : -1;
                if (slot >= 0) _bones[slot] = _jumpBones[i];
            }
            _stance = GetComponent<CombatantStance>();
            // Hip to ankle in the pose the prefab was saved in: what a knee bend shortens.
            Transform thigh = _bones[ThighLeft], shin = _bones[ShinLeft];
            if (thigh != null && shin != null && shin.childCount > 0)
            {
                float length = Vector3.Distance(thigh.position, shin.position) + Vector3.Distance(shin.position, shin.GetChild(0).position);
                if (length > 0.1f) _legLength = length;
            }
        }

        void OnDestroy()
        {
            _combatant.ShotFired -= OnShot;
            _combatant.Punched -= OnPunch;
        }

        void OnPunch(in Combatant.PunchResult punch)
        {
            if (_animator == null) return;
            // Non-looping clips: restarting from the beginning lets the same hand punch twice in a row if it ever has to.
            var state = punch.Right ? PunchRight : PunchLeft;
            _animator.CrossFadeInFixedTime(state, 0.05f, UpperLayer, 0f);
            _upper = state;
            _punchUntil = Time.time + PunchHold;
            _upperHoldUntil = _punchUntil;
            PunchesShown++;
        }

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

            // Seated, the body rides with the car: none of that motion is walking.
            float seatAmount = dead ? 0f : VehicleSeat.SeatedAmount(_combatant);
            _seated = seatAmount * seatAmount * (3f - 2f * seatAmount);
            _driverPose = VehicleSeat.InDriverSeat(_combatant);
            var v = dead || _seated > 0.001f ? Vector3.zero : _motion.LocalVelocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            _legs.Tick(v.x, v.z, Time.deltaTime, GameData.Rules.Gait);
            float proneAmount = dead || _stance == null ? 0f : _stance.ProneAmount;
            // Vaulting or climbing, replicated so everyone sees it: eased in and out.
            bool parkour = !dead && _stance != null && _stance.Parkour.Value != 0;
            _parkour = Mathf.MoveTowards(_parkour, parkour ? 1f : 0f, ParkourRules.Current.PosePerSecond * Time.deltaTime);
            _prone = proneAmount * proneAmount * (3f - 2f * proneAmount);
            // Lying, the body always faces where it looks (it crawls, it does not run sideways): no leg turn.
            _legTurnShown = _legs.TurnDeg * (1f - _prone);
            var proneRules = StanceRules.Current;
            _animator.transform.localRotation = Quaternion.AngleAxis(proneRules.ProneModelPitchDeg * _prone, Vector3.right)
                * Quaternion.AngleAxis(_legTurnShown, Vector3.up) * _modelRest;
            _animator.SetFloat(_move, _legs.Backward ? -speed : speed, BlendDamp, Time.deltaTime);

            // Only the legs speed up to keep pace with the ground; weapon handling keeps its own timing.
            float clipSpeed = _legs.Backward ? _backClipSpeed : _forwardClipSpeed;
            float pace = Game.Core.Locomotion.LegPace(speed, clipSpeed, GameData.Rules.Gait);
            // Crouched the body moves slower than the clip was made for: play the legs in proportion so the feet do not skate.
            if (_crouch > 0.01f) pace = Mathf.Lerp(pace, Stances.CrouchLegPace(speed, clipSpeed, StanceRules.Current.CrouchMinLegPace), _crouch);
            // Crawling is the walk clip at a slow pace; at the crawl's low speed the blend also shows only a part of the stride.
            if (_prone > 0.01f) pace = Mathf.Lerp(pace, Stances.CrouchLegPace(speed, clipSpeed, proneRules.ProneMinLegPace), _prone);
            _animator.SetFloat(_pace, pace);
            bool showAir = !dead && TrackAir() && _seated < 0.5f;
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
            TrackJumpPose(dead);

            bool armed = !dead && _combatant.ActiveWeaponId != 0;
            bool punching = !dead && Time.time < _punchUntil;
            // The flinch is a whole-body clip: let it through the weapon pose while it plays.
            bool showUpper = (armed || punching) && _base != Hit;
            float weightRate = punching ? PunchWeightPerSecond : UpperWeightPerSecond;
            _upperWeight = Mathf.MoveTowards(_upperWeight, showUpper ? 1f : 0f, weightRate * Time.deltaTime);
            _animator.SetLayerWeight(UpperLayer, _upperWeight);
            if (armed && Time.time >= _upperHoldUntil)
                // Crouched or lying, the weapon stays up: hanging down from hips this low it would go through the floor.
                // Seated too: lowered it would go through the car.
                SetUpper(_combatant.AimMode != AimMode.Hip || Time.time < _aimPoseUntil || _crouch > 0.5f || _prone > 0.5f || _seated > 0.5f ? Aim : Lowered);
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
                if (!_inAir)
                {
                    _airPhase = jump.RisePhase;
                    // Only a jump pushes off; walking off an edge does not.
                    _takeoffAt = _motion.VerticalSpeed > 0f ? Time.time : float.NegativeInfinity;
                }
                _inAir = true;
                _landUntil = -1f;
                _airPhase = Mathf.MoveTowards(_airPhase, Game.Core.Locomotion.AirPhase(_motion.VerticalSpeed, jump), AirPhasePerSecond * Time.deltaTime);
                return true;
            }
            if (_inAir)
            {
                _inAir = false;
                _landUntil = Time.time + jump.LandSeconds;
                _landedAt = Time.time;
                Landed?.Invoke();
            }
            if (Time.time >= _landUntil) return false;
            _airPhase = Mathf.MoveTowards(_airPhase, jump.LandPhase, AirPhasePerSecond * Time.deltaTime);
            return true;
        }

        // How much of each part of the jump to show this frame: the push right after take-off, then the tuck (most
        // at the top), then the dip that absorbs the landing. LateUpdate turns the bones; the dip also lowers the model.
        void TrackJumpPose(bool dead)
        {
            var jump = GameData.Rules.Jump;
            float push = 0f, tuck = 0f;
            if (!dead && _inAir)
            {
                float sinceTakeoff = Time.time - _takeoffAt;
                if (sinceTakeoff < jump.TakeoffSeconds)
                    push = Game.Core.Locomotion.Pulse(sinceTakeoff, jump.TakeoffSeconds * TakeoffAttackShare, jump.TakeoffSeconds);
                else tuck = Game.Core.Locomotion.JumpTuck(_motion.VerticalSpeed, jump);
            }
            _push = push;
            // A vault or climb tucks the knees like the top of a jump, on the ground too.
            tuck = Mathf.Max(tuck, _parkour * ParkourRules.Current.PoseTuck);
            _tuck = Mathf.MoveTowards(_tuck, tuck, jump.TuckPerSecond * Time.deltaTime);
            // The ground probe reports the landing a little above the floor, so the dip is deepest about as the feet arrive.
            _dip = dead ? 0f : Game.Core.Locomotion.Pulse(Time.time - _landedAt, jump.LandAttackSeconds, jump.LandAbsorbSeconds);
            // The crouch follows the replicated stance, eased in and out; in the air the tuck takes over from it.
            float crouch = dead || _stance == null ? 0f : _stance.CrouchAmount;
            _crouch = crouch * crouch * (3f - 2f * crouch) * (1f - _tuck);
            // Walking, the clip already swings the legs, so the crouch bends them less on top of it.
            var rules = StanceRules.Current;
            var v = dead ? Vector3.zero : _motion.LocalVelocity;
            float walk = rules.CrouchWalkFullSpeed <= 0f ? 0f : Mathf.Clamp01(new Vector2(v.x, v.z).magnitude / rules.CrouchWalkFullSpeed);
            _crouchWalk = Mathf.MoveTowards(_crouchWalk, walk, CrouchWalkBlendPerSecond * Time.deltaTime);
            float scale = _crouch * Stances.CrouchBendScale(_crouchWalk, rules.CrouchWalkBend);
            // Lowered by exactly what the bent knees take off the legs, so the feet stay on the ground.
            _drop = _dip * jump.LandDropMeters + Stances.CrouchHipDrop(_legLength, rules.CrouchThighDeg * scale, rules.CrouchShinDeg * scale);
            // Lying: the spine line rests at body height over the ground and the model is moved back from the capsule,
            // so the head (which the pitch swung forward to the end of the body) ends up under the eyes.
            var lying = (Vector3.up * rules.ProneBodyHeight + Vector3.back * rules.ProneModelBackShift) * _prone;
            // Seated: the hips go down and back into the seat (the body's origin is already placed for the eyes).
            var seatRules = VehicleSeat.RulesOf(_combatant);
            var sitting = (Vector3.down * seatRules.SeatedHipDrop + Vector3.back * seatRules.SeatedHipBack) * _seated;
            _animator.transform.localPosition = _modelRestPosition + Vector3.down * _drop + lying + sitting;
        }

        // How far the playing clip has turned the hips from their rest facing, in degrees about the vertical.
        float HipsYaw()
        {
            if (_hips == null) return 0f;
            var fromRest = _hips.rotation * Quaternion.Inverse(_animator.transform.rotation * _hipsRest);
            if (_prone > 0.001f)
            {
                // Lying, the model's up is along the ground: measure the twist in the model's own frame (about its up).
                var own = Quaternion.Inverse(_animator.transform.rotation) * fromRest * _animator.transform.rotation;
                var ownFacing = Vector3.ProjectOnPlane(own * Vector3.forward, Vector3.up);
                return ownFacing.sqrMagnitude < 1e-6f ? 0f : Vector3.SignedAngle(Vector3.forward, ownFacing, Vector3.up);
            }
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

        // Adds a turn about a world axis to a bone on top of its pose.
        void Turn(int index, float degrees, Vector3 axis)
        {
            var bone = _bones[index];
            if (bone == null || Mathf.Abs(degrees) <= 1e-3f) return;
            if (!_boneTurned[index])
            {
                _boneOwn[index] = bone.localRotation;
                _boneTurned[index] = true;
            }
            bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        // After the animator has posed the skeleton: read the feet, then turn the torso and pose the jump on top.
        void LateUpdate()
        {
            if (_animator == null) return;
            if (_footLeft != null && _footRight != null)
            {
                // Heights as the clip has them: without the landing dip.
                bool left = _leftContact.Update(transform.InverseTransformPoint(_footLeft.position).y + _drop);
                bool right = _rightContact.Update(transform.InverseTransformPoint(_footRight.position).y + _drop);
                var v = _motion.LocalVelocity;
                // The landing has its own sound; a foot settling out of the jump pose is not another step.
                bool walking = !_combatant.Dead.Value && _base == Locomotion && Time.time > _landUntil + StepPauseAfterLanding && _prone < 0.5f && _seated < 0.5f
                    && new Vector2(v.x, v.z).sqrMagnitude > StepMinSpeed * StepMinSpeed;
                if ((left || right) && walking) FootDown?.Invoke();
            }

            // Only on frames the animator poses the skeleton.
            if (_spine == null || Time.deltaTime <= 0f || !_animator.isActiveAndEnabled) return;
            for (int side = 0; side < 2; side++)
            {
                if (!_footMoved[side]) continue;
                _footMoved[side] = false;
                var footBone = _bones[side == 0 ? FootLeft : FootRight];
                // Same rule as for rotations below: only undo our own move if no clip has re-posed the foot.
                if (footBone != null && footBone.localPosition == _footLeftAtPosition[side]) footBone.localPosition = _footOwnPosition[side];
            }
            for (int i = 0; i < TurnedBones; i++)
            {
                if (!_boneTurned[i]) continue;
                _boneTurned[i] = false;
                // Still exactly as it was left: no clip re-posed this bone, so take last frame's turn out.
                if (_bones[i] != null && SameRotation(_bones[i].localRotation, _boneLeftAt[i])) _bones[i].localRotation = _boneOwn[i];
            }

            // The model is turned with the legs; bring the torso (and with it head, arms and weapon) back to the facing.
            // The weapon poses were made for hips that face forward, but a running clip swings and turns the hips;
            // while a weapon pose is on, take that out too or the weapon points off to the side when moving.
            // (Not for the dead: the death clip turns the hips on purpose.)
            float turnBack = -_legTurnShown - (_combatant.Dead.Value ? 0f : HipsYaw() * _upperWeight);
            // About the model's own up: the character's up while standing, along the ground while lying.
            Turn(Torso, turnBack, _prone > 0.001f ? _animator.transform.up : transform.up);
            // Leaning right tips the torso clockwise seen from behind: a negative turn about the forward axis.
            Turn(Torso, -_combatant.LeanAmount * GameData.Rules.Lean.BodyRollDeg * (1f - _prone), transform.forward);

            // The jump. A positive turn about the right axis tips the top of the body forward, swings a hanging
            // limb backward and points the toes down.
            var jump = GameData.Rules.Jump;
            var stanceRules = StanceRules.Current;
            // Thigh forward and shin back, each measured from vertical; the same scale as the model drop uses.
            float crouchScale = _crouch * Stances.CrouchBendScale(_crouchWalk, stanceRules.CrouchWalkBend);
            float crouchThigh = stanceRules.CrouchThighDeg * crouchScale;
            float crouchShin = stanceRules.CrouchShinDeg * crouchScale;
            // A weapon pose keeps the weapon level, so only empty hands hunch forward in a crouch.
            float crouchPitch = stanceRules.CrouchTorsoPitchDeg * _crouch * (1f - _upperWeight);
            var parkourRules = ParkourRules.Current;
            Turn(Torso, jump.TorsoPitchDeg * _tuck + jump.LandPitchDeg * _dip - jump.TakeoffArchDeg * _push + crouchPitch
                + parkourRules.PoseTorsoPitchDeg * _parkour, transform.right);
            // Lying: the chest and head come back up (a negative turn about the right axis undoes the forward tip).
            Turn(Torso, -stanceRules.ProneHeadLiftDeg * _prone, transform.right);
            // The legs face where the model is turned, not where the combatant looks.
            var legRight = Quaternion.AngleAxis(_legTurnShown, transform.up) * transform.right;
            // Knees up in front, shins folded back under them. The shin turns relative to the thigh, which already
            // leans forward by t, so to point back by s from vertical it turns t + s (a landing uses s = t, i.e.
            // twice the thigh angle). The shin's net swing from vertical is then s, so the foot turns -s to be flat.
            float thigh = -(jump.TuckThighDeg * _tuck + jump.LandBendDeg * _dip + crouchThigh);
            float shin = jump.TuckShinDeg * _tuck + 2f * jump.LandBendDeg * _dip + crouchThigh + crouchShin;
            float foot = jump.TakeoffToeDeg * _push - jump.LandBendDeg * _dip - crouchShin;
            PoseLeg(0, ThighLeft, ShinLeft, FootLeft, thigh, shin, legRight);
            PoseLeg(1, ThighRight, ShinRight, FootRight, thigh, shin, legRight);
            Turn(FootLeft, foot, legRight);
            Turn(FootRight, foot, legRight);
            // Empty hands: arms out sideways (a weapon pose keeps both arms on the weapon). About the forward
            // axis a positive turn swings a hanging arm out to the right.
            float armLift = jump.ArmLiftDeg * Mathf.Max(_tuck, _dip * LandArmShare) * (1f - _upperWeight);
            Turn(ArmLeft, -armLift, transform.forward);
            Turn(ArmRight, armLift, transform.forward);
            // Lying with a weapon: the arms (and the weapon in the hand) turn back up by what the pitch minus the head
            // lift left them tipped down, so the weapon is level again and points the way the player looks.
            float armUp = -Stances.ProneArmCounterDeg(stanceRules) * _prone * _upperWeight;
            Turn(ArmLeft, armUp, transform.right);
            Turn(ArmRight, armUp, transform.right);
            // Parkour: both arms reach forward and down onto the top (a negative turn swings a hanging arm forward).
            float reach = -parkourRules.PoseArmReachDeg * _parkour;
            Turn(ArmLeft, reach, transform.right);
            Turn(ArmRight, reach, transform.right);
            if (_seated > 0.001f) PoseSeated();

            for (int i = 0; i < TurnedBones; i++)
                if (_boneTurned[i]) _boneLeftAt[i] = _bones[i].localRotation;
        }

        // Sitting in a car (ADR-065 (5)), no clip: thighs forward to about level, shins down with the feet a little
        // forward, feet flat, the torso leaning back a little. Empty hands go to the steering wheel at the driver's
        // seat and rest on the lap elsewhere; a weapon keeps the upper layer's weapon pose.
        // Same turn directions as the jump: a negative turn about the right axis lifts a thigh forward; the shin turns
        // back by the thigh's angle plus its own; the foot turns back by the shin's.
        // Astride a motorcycle: thighs less far forward and apart (about the forward axis a positive turn swings a
        // hanging limb to the right), shins back to the pegs, the rider's hands forward on the bars.
        void PoseSeated()
        {
            var r = VehicleSeat.RulesOf(_combatant);
            float s = _seated;
            bool astride = VehicleSeat.Astride(_combatant);
            float thighDeg = astride ? r.AstrideThighDeg : r.SeatedThighDeg;
            float shinDeg = astride ? r.AstrideShinDeg : r.SeatedShinDeg;
            if (astride)
            {
                // Through PoseLeg so the feet (separate bones) follow the spread legs.
                PoseLeg(0, ThighLeft, ShinLeft, FootLeft, -r.AstrideApartDeg * s, 0f, transform.forward);
                PoseLeg(1, ThighRight, ShinRight, FootRight, r.AstrideApartDeg * s, 0f, transform.forward);
            }
            float thigh = -thighDeg * s;
            float shin = (thighDeg + shinDeg) * s;
            PoseLeg(0, ThighLeft, ShinLeft, FootLeft, thigh, shin, transform.right);
            PoseLeg(1, ThighRight, ShinRight, FootRight, thigh, shin, transform.right);
            Turn(FootLeft, -shinDeg * s, transform.right);
            Turn(FootRight, -shinDeg * s, transform.right);
            if (!astride) Turn(Torso, -r.SeatedTorsoBackDeg * s, transform.right);
            float free = s * (1f - _upperWeight);
            float forward = _driverPose ? (astride ? r.AstrideArmDeg : r.DriverArmDeg) : r.PassengerArmDeg;
            Turn(ArmLeft, -forward * free, transform.right);
            Turn(ArmRight, -forward * free, transform.right);
            if (_driverPose)
            {
                // About the forward axis a positive turn swings a hanging arm to the right: both hands in toward the wheel.
                Turn(ArmLeft, r.DriverArmInDeg * free, transform.forward);
                Turn(ArmRight, -r.DriverArmInDeg * free, transform.forward);
            }
        }

        // Turns a thigh and shin and brings the foot along. In this skeleton the feet are not children of the
        // shins (they are separate control bones), so a foot left behind would stretch the leg down to the floor.
        void PoseLeg(int side, int thighIndex, int shinIndex, int footIndex, float thighDeg, float shinDeg, Vector3 axis)
        {
            Transform shin = _bones[shinIndex], foot = _bones[footIndex];
            if (shin == null || foot == null || shin.childCount == 0 || (Mathf.Abs(thighDeg) <= 1e-3f && Mathf.Abs(shinDeg) <= 1e-3f))
            {
                Turn(thighIndex, thighDeg, axis);
                Turn(shinIndex, shinDeg, axis);
                return;
            }
            var ankle = shin.GetChild(0); // the shin's end marker
            var shinBefore = shin.rotation;
            var footFromAnkle = foot.position - ankle.position;
            Turn(thighIndex, thighDeg, axis);
            Turn(shinIndex, shinDeg, axis);
            var swing = shin.rotation * Quaternion.Inverse(shinBefore);
            if (!_boneTurned[footIndex])
            {
                _boneOwn[footIndex] = foot.localRotation;
                _boneTurned[footIndex] = true;
            }
            if (!_footMoved[side])
            {
                _footOwnPosition[side] = foot.localPosition;
                _footMoved[side] = true;
            }
            foot.rotation = swing * foot.rotation;
            foot.position = ankle.position + swing * footFromAnkle;
            _footLeftAtPosition[side] = foot.localPosition;
        }

        // Exact, unlike ==, which also accepts rotations that are merely close.
        static bool SameRotation(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
    }
}
