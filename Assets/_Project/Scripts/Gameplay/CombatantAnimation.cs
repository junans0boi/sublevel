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
    public partial class CombatantAnimation : MonoBehaviour
    {
        public const string LocomotionAim = "LocomotionAim", MoveX = "MoveX", MoveZ = "MoveZ"; // 8-way trees on the local velocity (ADR-391)
        public const string Locomotion = "Locomotion", Air = "Air", Death = "Death", Prone = "Prone", Unarmed = "Unarmed", UnarmedCrouch = "UnarmedCrouch", SwimState = "Swim";
        public const string RifleCrouch = "RifleCrouch"; // armed crouch: the Mixamo kneel and crouch walks (ADR-536, #303)
        // Action layer (reload clips) and Additive layer (breathing, hit flinch), ADR-188.
        public const string ActionNone = "None", ReloadStand = "ReloadStand", ReloadCrouch = "ReloadCrouch", ReloadProne = "ReloadProne";
        public const string BreathState = "Breath", FlinchState = "Flinch";
        public const string ActionSpeed = "ActionSpeed"; // playback speed of the Action layer: the clip is stretched to the weapon's own reload time
        public const string Lowered = "Lowered", Aim = "Aim", Shoot = "Shoot";
        public const string PunchLeft = "PunchLeft", PunchRight = "PunchRight";
        public const string Move = "Move"; // speed along the legs' heading, negative when back-pedalling
        public const string Pace = "Pace"; // playback speed of the locomotion blend only
        public const string AirPhase = "AirPhase"; // which moment of its clip the air state holds (0..1)
        public const string JumpStart = "JumpStart", JumpLoop = "JumpLoop", JumpLand = "JumpLand"; // base layer jump clips (ADR-537)

        const int BaseLayer = 0, UpperLayer = 1, ActionLayer = 2, AdditiveLayer = 3;
        const float Fade = 0.12f;
        const float ProneFade = 0.35f;
        const float ClipCrouchPerSecond = 5f;
        const float ProneLookMaxDeg = 35f, ProneLookShare = 0.8f;
        const float ProneHeadAheadOfEyes = 0.15f; // lying, the head joint ends this far ahead of the capsule / eyes
        const float BlendDamp = 0.08f;
        const float FlinchShare = 0.4f; // of the hit clip: it carries on into a stagger past this, the flinch is only its first part
        const float ShootHold = 0.22f;
        const float AimAfterShot = 0.9f;       // the weapon stays up this long after the last shot
        const float UpperWeightPerSecond = 7f;
        const float PunchLeadSeconds = 0.02f, PunchFade = 0.02f; // the fist is out about 40 ms after the punch event
        // When the last punch's fist is furthest out (Time.time), for the timing tests.
        public float PunchImpactAt { get; private set; } = -1f;
        const float PunchHold = 0.5f;          // the upper layer shows the punch this long
        const float PunchWeightPerSecond = 50f; // a punch is quick: the layer comes up faster than for a weapon
        const float StepMinSpeed = 0.6f;
        public const float SwimClipSpeed = 2.5f; // ground speed (m/s) the UAL swim stroke was made for: the Swim tree blends idle -> stroke up to it
        const float AirPhasePerSecond = 1.5f;  // how fast the jump pose moves between rise, fall and landing
        const float StepPauseAfterLanding = 0.15f;
        const float TakeoffAttackShare = 0.3f; // of the take-off push spent getting into it
        const float UnarmedShoulderSwingCut = 0.6f; // share of the bare-hand clips' shoulder twist taken out
        const float LandArmShare = 0.5f;       // of the arm lift shown again while absorbing a landing
        const float FallbackLegLength = 0.9f;  // hip to ankle, when the leg bones are not wired
        // Bones turned in code after the animator has posed them, parents before children.
        const int Torso = 0, ThighLeft = 1, ThighRight = 2, ShinLeft = 3, ShinRight = 4, FootLeft = 5, FootRight = 6,
            ArmLeft = 7, ArmRight = 8, Pelvis = 9, TurnedBones = 10;

        [SerializeField] Animator _animator;
        [SerializeField] Transform _spine;
        [SerializeField] Transform _hips;
        [SerializeField] Transform _footLeft;
        [SerializeField] Transform _footRight;
        // Upper legs, lower legs, upper arms; left then right of each.
        [SerializeField] Transform[] _jumpBones;
        // Lying (ADR-231): how far ahead of the model's origin the head joint is in the prone clip, measured when the character was built.
        [SerializeField] float _proneHeadAhead = 0.8f;
        // Ground speed the clips were authored for (m/s), measured when the character was built. The sprint's spot (ADR-481) tops both
        // locomotion trees; the jog played backwards ends their back-pedal (ADR-390).
        [SerializeField] float _sprintClipSpeed = 6.5f;
        // Each 8-way tree's clip directions (deg, 0 = forward, right positive) and the ground speed of the fastest clip along each, measured
        // when the character was built (ADR-391).
        [SerializeField] float[] _dirsRifle, _topsRifle, _dirsAim, _topsAim, _dirsUnarmed, _topsUnarmed;
        float _snapTurn; // legs turned onto the nearest clip direction of the 8-way tree (deg, right positive)
        float _clipDir;  // that clip direction (deg): 0 or 180 = the UAL cycles, anything else a Mixamo strafe
        // The bare-handed walk and jog clips' ground speed (ADR-255), and the second in each punch clip when the fist is furthest out (the hit).
        [SerializeField] float _unarmedWalkSpeed = 1.2f, _unarmedRunSpeed = 3.5f, _unarmedCrouchSpeed = 1.2f;
        // The rifle crouch walks' ground speed ahead and back (ADR-536).
        [SerializeField] float _rifleCrouchSpeed = 1.2f, _rifleCrouchBackSpeed = 1.2f;
        [SerializeField] float _punchImpactLeft = 0.15f, _punchImpactRight = 0.15f;
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
        int _move, _pace, _airPhaseId, _moveX, _moveZ;
        float _airPhase;
        bool _inAir;
        JumpClip _jumpClip;          // which jump clip the logical Air state shows (ADR-537)
        bool _jumpClips;             // the controller has the jump clips; without them the jump is posed in code (ADR-048)
        RuntimeAnimatorController _jumpClipsOf;
        float _landUntil = -1f; // touched down: the landing pose is held until this time
        float _takeoffAt = float.NegativeInfinity; // left the ground going up
        float _landedAt = float.NegativeInfinity;
        float _push, _tuck, _dip; // 0..1: take-off push, knees tucked up, landing absorbed
        float _drop;              // how far the model is lowered right now (m)
        CombatantRagdoll _ragdoll; // missing on a prefab built before ADR-395: the death clip only
        CombatantStance _stance;  // missing on a prefab built before stances existed: never crouches
        float _legLength = FallbackLegLength;
        float _footRestHeight;    // the lower foot's height over the character's origin in the saved (standing) pose
        float _crouch;            // 0..1: how much of the crouch is shown
        float _clipCrouch;        // 0..1: how much the crouch trees play (UAL bare-handed ADR-255, Mixamo rifle ADR-536): their own cadence and leg turn
        float _prone;             // 0..1: how much of the lying pose is shown
        float _proneClip;         // the share of it that is the prone clip (all of it unless swimming)
        float _legTurnShown;      // the leg turn actually applied to the model (none while lying down)
        float _parkour;           // 0..1: how much of the vault/climb pose is shown (ADR-065 (4))
        float _seated;            // 0..1: how much of the seated pose is shown (in a car, ADR-065 (5))
        bool _driverPose;         // seated at the wheel: empty hands go to the steering wheel
        // A bone no playing clip keys keeps what was written to it last, so a turn added every frame would pile
        // up: each turned bone's own rotation is remembered and put back first if the animator has not re-posed it.
        readonly Transform[] _bones = new Transform[TurnedBones];
        readonly Quaternion[] _boneOwn = new Quaternion[TurnedBones];
        readonly Quaternion[] _boneLeftAt = new Quaternion[TurnedBones];
        readonly bool[] _boneTurned = new bool[TurnedBones];
        string _base = Locomotion;
        string _upper, _additive = BreathState;
        float _upperHoldUntil, _aimPoseUntil, _flinchUntil;
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
        // A long gun held in both hands (ADR-083) counts too: its pose is code, but it follows the same states.
        public string UpperState => _upperWeight > 0.5f || _rifle > 0.5f ? _upper : null;
        // The upper-layer state last started, whatever the layer's weight.
        public string UpperStateStarted => _upper;
        // Punches shown so far (both hands).
        public int PunchesShown { get; private set; }
        // Speed fed to the legs (negative = back-pedalling) and how far they are turned from the facing, right positive.
        public float MoveDrive => _animator != null ? _animator.GetFloat(_move) : 0f;
        public float LegTurnDeg => _legTurnShown;
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
        // Height of the lower ankle over the character's origin right now, and standing at rest (pose review).
        public float FootLow => _footLeft == null || _footRight == null ? 0f : Mathf.Min(transform.InverseTransformPoint(_footLeft.position).y, transform.InverseTransformPoint(_footRight.position).y);
        public float FootRest => _footRestHeight;

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _motion = GetComponent<CombatantMotion>();
            _combatant.ShotFired += OnShot;
            _combatant.Punched += OnPunch;
            _leftContact = new FootContact(_footPlantHeight, _footLiftHeight);
            _rightContact = new FootContact(_footPlantHeight, _footLiftHeight);
            _move = Animator.StringToHash(Move);
            _moveX = Animator.StringToHash(MoveX);
            _moveZ = Animator.StringToHash(MoveZ);
            if (_animator != null)
            {
                _modelRest = _animator.transform.localRotation;
                _modelRestPosition = _animator.transform.localPosition;
                if (_hips != null) _hipsRest = Quaternion.Inverse(_animator.transform.rotation) * _hips.rotation;
            }
            _pace = Animator.StringToHash(Pace);
            _airPhaseId = Animator.StringToHash(AirPhase);
            _bones[Torso] = _spine;
            _bones[Pelvis] = _hips;
            _bones[FootLeft] = _footLeft;
            _bones[FootRight] = _footRight;
            for (int i = 0; _jumpBones != null && i < _jumpBones.Length; i++)
            {
                // Feet sit between the shins and the arms in the turn order.
                int slot = i < 4 ? ThighLeft + i : i < 6 ? ArmLeft + i - 4 : -1;
                if (slot >= 0) _bones[slot] = _jumpBones[i];
            }
            _stance = GetComponent<CombatantStance>();
            _ragdoll = GetComponentInChildren<CombatantRagdoll>(true);
            FindHead();
            FindArms();
            if (_footLeft != null && _footRight != null)
                _footRestHeight = Mathf.Min(transform.InverseTransformPoint(_footLeft.position).y, transform.InverseTransformPoint(_footRight.position).y);
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
            // A melee weapon swings with bone turns, not the punch clips (CombatantAnimation.Melee.cs).
            if (punch.WeaponId != 0)
            {
                StartSwing(punch.WeaponId);
                return;
            }
            // Non-looping clips: restarting from the beginning lets the same hand punch twice in a row if it ever has to.
            var state = punch.Right ? PunchRight : PunchLeft;
            // The server judged this punch just now: start the clip so its fist is furthest out PunchLeadSeconds from here (the hit and the
            // fist agree within a frame or two), skipping only the wind-up before that.
            float impact = punch.Right ? _punchImpactRight : _punchImpactLeft;
            _animator.CrossFadeInFixedTime(state, PunchFade, UpperLayer, Mathf.Max(0f, impact - PunchLeadSeconds - PunchFade));
            PunchImpactAt = Time.time + Mathf.Min(impact, PunchLeadSeconds + PunchFade);
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
            KickRifle();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Stopwatch ticks every combatant spent in Update + LateUpdate since the last reset: the cost of the code half of the animation (PERF, ADR-233).
        public static long PerfTicks;
        void Update() { long t = System.Diagnostics.Stopwatch.GetTimestamp(); UpdateBody(); PerfTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t; }
        void LateUpdate() { long t = System.Diagnostics.Stopwatch.GetTimestamp(); LateUpdateBody(); PerfTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t; }
#else
        void Update() => UpdateBody();
        void LateUpdate() => LateUpdateBody();
#endif

        void UpdateBody()
        {
            if (_animator == null) return;
            // A ragdoll owns the bones (ADR-395): the death state stays the logical one, nothing is posed.
            if (_ragdoll != null && _ragdoll.Active) { _base = Death; return; }
            UpdateLod();
            bool dead = _combatant.Dead.Value;
            float hp = _combatant.Hp.Value;
            bool hit = hp < _lastHp && !dead;
            _lastHp = hp;
            TrackSwim(dead);
            TrackBungee(dead);
            TrackGait();

            // Seated, the body rides with the car: none of that motion is walking.
            float seatAmount = dead ? 0f : VehicleSeat.SeatedAmount(_combatant);
            _seated = seatAmount * seatAmount * (3f - 2f * seatAmount);
            _driverPose = VehicleSeat.InDriverSeat(_combatant);
            var v = dead || _seated > 0.001f ? Vector3.zero : _motion.LocalVelocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            _legs.Tick(v.x, v.z, Time.deltaTime, GameData.Rules.Gait);
            float proneAmount = dead || _stance == null ? 0f : _stance.ProneAmount;
            // Vaulting or climbing, replicated so everyone sees it: eased in and out.
            bool parkour = !dead && _stance != null && _stance.Parkour.Value != 0 && !OnLedge; // the ledge has its own pose (.Ledge.cs)
            _parkour = Mathf.MoveTowards(_parkour, parkour ? 1f : 0f, ParkourRules.Current.PosePerSecond * Time.deltaTime);
            _prone = proneAmount * proneAmount * (3f - 2f * proneAmount);
            // Lying, the body always faces where it looks (it crawls, it does not run sideways): no leg turn. The 8-way trees step sideways
            // and diagonally themselves (ADR-391): only the forward-only trees (bare-handed crouch, swimming) still turn the legs.
            // The 8-way trees: the legs turn the few degrees onto the nearest clip direction, so one direction's clips play at a time
            // (blending the UAL forward cycle with a Mixamo side cycle of another cadence crossed the feet, ~7 m/s slide).
            float oldTrees = Mathf.Max(_clipCrouch, _swimAmount);
            var dirs = _unarmedBody ? _dirsUnarmed : Aiming ? _dirsAim : _dirsRifle;
            var tops = _unarmedBody ? _topsUnarmed : Aiming ? _topsAim : _topsRifle;
            bool eightWay = dirs != null && dirs.Length > 0 && tops != null && tops.Length == dirs.Length;
            int nearest = eightWay ? NearestDirection(dirs, Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg) : -1;
            float snapTarget = eightWay && speed >= GameData.Rules.Gait.LegTurnMinSpeed ? Mathf.DeltaAngle(dirs[nearest], Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg) : 0f;
            _snapTurn = Mathf.MoveTowards(_snapTurn, snapTarget, GameData.Rules.Gait.LegTurnDegPerSecond * Time.deltaTime);
            _clipDir = nearest >= 0 && speed >= GameData.Rules.Gait.LegTurnMinSpeed ? dirs[nearest] : 0f;
            _legTurnShown = (1f - _prone) * Mathf.Lerp(_snapTurn, _legs.TurnDeg, oldTrees);
            var proneRules = StanceRules.Current;
            // Lying is the prone clip (ADR-231); swimming is the Swim tree (UAL, #236), already flat: the body is not pitched for either.
            _proneClip = _prone * (1f - _swimAmount);
            const float pitched = 0f;
            _animator.transform.localRotation = Quaternion.AngleAxis(proneRules.ProneModelPitchDeg * pitched, Vector3.right)
                * Quaternion.AngleAxis(_legTurnShown, Vector3.up) * _modelRest;
            // Every speed, slow walk included, is the blend at that speed: the walk, jog and sprint are one library's cycles and line up (ADR-390).
            _animator.SetFloat(_move, _legs.Backward ? -speed : speed, BlendDamp, Time.deltaTime);
            // The velocity as the turned legs see it.
            var legV = Quaternion.AngleAxis(-_snapTurn, Vector3.up) * new Vector3(v.x, 0f, v.z);
            _animator.SetFloat(_moveX, legV.x, BlendDamp, Time.deltaTime);
            _animator.SetFloat(_moveZ, legV.z, BlendDamp, Time.deltaTime);

            // Only the legs speed up to keep pace with the ground; weapon handling keeps its own timing.
            // Bare hands (or a melee weapon, posed in code over the base) play the bare-handed clips (ADR-255).
            // Seated, the arms are placed on the grips by code (PoseSeated) from the clip's forward hands; the bare-handed tree's swinging arms start too far away.
            _unarmedBody = (HeldId == 0 || HoldsMelee) && _seated < 0.5f;
            // Each tree's clips cover the ground of their spot (the jog and sprint are slowed to the game's run and sprint, ADR-481), so up
            // to the fastest spot the blend already matches the ground (pace 1); only beyond it do the legs speed up (ADR-390).
            float clipSpeed = Mathf.Clamp(speed, 0.01f, nearest >= 0 ? tops[nearest] : _legs.Backward ? _unarmedRunSpeed : _sprintClipSpeed);
            float pace = Game.Core.Locomotion.LegPace(speed, clipSpeed, GameData.Rules.Gait);
            // Crouched the body moves slower than the clip was made for: play the legs in proportion so the feet do not skate.
            if (_crouch > 0.01f) pace = Mathf.Lerp(pace, Stances.CrouchLegPace(speed, clipSpeed, StanceRules.Current.CrouchMinLegPace), _crouch);
            // The crouch clips have their own cadence: pace by their ground speed.
            float crouchClipSpeed = _unarmedBody ? _unarmedCrouchSpeed : _legs.Backward ? _rifleCrouchBackSpeed : _rifleCrouchSpeed;
            if (_clipCrouch > 0.01f) pace = Mathf.Lerp(pace, Game.Core.Locomotion.LegPace(speed, crouchClipSpeed, GameData.Rules.Gait), _clipCrouch);
            // Crawling is the walk clip at a slow pace; at the crawl's low speed the blend also shows only a part of the stride.
            if (_prone > 0.01f) pace = Mathf.Lerp(pace, Stances.CrouchLegPace(speed, clipSpeed, proneRules.ProneMinLegPace), _prone);
            _animator.SetFloat(_pace, pace);
            bool showAir = !dead && TrackAir() && _seated < 0.5f;
            if (_animator.layerCount > AdditiveLayer)
            {
                _animator.SetLayerWeight(AdditiveLayer, _lod >= 2 ? 0f : 1f);
                if (Time.time >= _flinchUntil) SetAdditive(BreathState);
            }
            if (dead) SetBase(Death);
            else
            {
                // The flinch is an additive layer on top of whatever plays, so a hit no longer replaces the pose.
                if (hit) { _animator.CrossFadeInFixedTime(FlinchState, 0.03f, AdditiveLayer, 0f); _additive = FlinchState; _flinchUntil = Time.time + FlinchShare * FlinchClipLength(); }
                SetBase(_swimAmount > 0.5f ? SwimState : _proneClip > 0.5f && !showAir ? Prone : showAir ? Air : Locomotion);
            }
            _animator.SetFloat(_airPhaseId, _airPhase);
            TrackJumpPose(dead);

            // A melee weapon keeps the upper layer off: its ready pose and swings are bone turns (ADR-073).
            bool armed = !dead && HeldId != 0 && !HoldsMelee && _swimAmount < 0.5f;
            bool punching = !dead && Time.time < _punchUntil;
            TrackItems(dead, hit);
            TrackRifle(armed, _combatant.AimMode != AimMode.Hip || Time.time < _aimPoseUntil, dead ? Vector3.zero : v);
            DriveAction(dead);
            // The flinch is a whole-body clip: let it through the weapon pose while it plays. A long gun in both
            // hands is posed in code over the base layer (ADR-083), so the clip pose stays off for it.
            bool showUpper = ((armed && !_rifleWanted) || punching) && _proneClip < 0.5f;
            float weightRate = punching ? PunchWeightPerSecond : UpperWeightPerSecond;
            _upperWeight = Mathf.MoveTowards(_upperWeight, showUpper ? 1f : 0f, weightRate * Time.deltaTime);
            _animator.SetLayerWeight(UpperLayer, _upperWeight);
            if (armed && Time.time >= _upperHoldUntil)
                // Crouched or lying, the weapon stays up: hanging down from hips this low it would go through the floor.
                // Seated too: lowered it would go through the car.
                SetUpper(_combatant.AimMode != AimMode.Hip || Time.time < _aimPoseUntil || _crouch > 0.5f || _prone > 0.5f || _seated > 0.5f ? Aim : Lowered);
            TurnOverForBungee();
        }

        // Follows take-off and touch-down, picks the jump pose, and says whether the base layer should show it: all
        // the time off the ground, and for a moment after touching down (the landing pose). Runs every frame, so a
        // landing is noticed even while another whole-body state (a flinch) is playing.
        bool TrackAir()
        {
            var jump = GameData.Rules.Jump;
            if (_animator.runtimeAnimatorController != _jumpClipsOf && _animator.isInitialized)
            {
                _jumpClipsOf = _animator.runtimeAnimatorController;
                _jumpClips = _animator.HasState(BaseLayer, Animator.StringToHash(JumpLoop));
            }
            bool show = TrackAirTimes(jump);
            _jumpClip = _jumpClips ? Game.Core.Locomotion.PickJumpClip(_inAir, show && !_inAir, _takeoffAt > float.NegativeInfinity, Time.time - _takeoffAt, OnLedge, jump) : JumpClip.Hold;
            return show;
        }

        bool TrackAirTimes(JumpRules jump)
        {
            // Floating is not falling: no jump pose over deep water.
            if (_swimAmount > 0.01f || _bungee > 0.01f) { _inAir = false; _landUntil = -1f; return false; }   // nor is hanging on the bungee cord
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
                // On a ledge the body is lifted, not jumping: hold the still pose (the clip's rise lifts the hips off the path).
                _airPhase = Mathf.MoveTowards(_airPhase, Game.Core.Locomotion.AirPhase(OnLedge ? 0f : _motion.VerticalSpeed, jump), AirPhasePerSecond * Time.deltaTime);
                return true;
            }
            if (_inAir)
            {
                _inAir = false;
                // Arriving on top from a pull-up is no landing: its knee-bend dip read as a snap to sitting (#233, ADR-542).
                bool fromLedge = LedgePose > 0.5f;
                _landUntil = fromLedge ? -1f : Time.time + (_jumpClips && !OnLedge ? jump.LandClipSeconds : jump.LandSeconds);
                _landedAt = fromLedge ? float.NegativeInfinity : Time.time;
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
            if (!dead && _inAir && !_jumpClips)
            {
                float sinceTakeoff = Time.time - _takeoffAt;
                if (sinceTakeoff < jump.TakeoffSeconds)
                    push = Game.Core.Locomotion.Pulse(sinceTakeoff, jump.TakeoffSeconds * TakeoffAttackShare, jump.TakeoffSeconds);
                else tuck = Game.Core.Locomotion.JumpTuck(_motion.VerticalSpeed, jump);
            }
            _push = push;
            // A vault or climb tucks the knees like the top of a jump, on the ground too.
            tuck = Mathf.Max(tuck, _parkour * ParkourRules.Current.PoseTuck);
            tuck *= 1f - LedgePose; // hanging or pulling up the legs are posed in .Ledge.cs, not tucked like a jump
            _tuck = Mathf.MoveTowards(_tuck, tuck, jump.TuckPerSecond * Time.deltaTime);
            // The ground probe reports the landing a little above the floor, so the dip is deepest about as the feet arrive.
            _dip = dead || _jumpClips ? 0f : Game.Core.Locomotion.Pulse(Time.time - _landedAt, jump.LandAttackSeconds, jump.LandAbsorbSeconds);
            // The crouch follows the replicated stance, eased in and out; in the air the tuck takes over from it.
            float crouch = dead || _stance == null ? 0f : _stance.CrouchAmount;
            _crouch = crouch * crouch * (3f - 2f * crouch) * (1f - _tuck);
            // The crouch is clips for every body (ADR-536): they lower the hips themselves, so no code knee bend or model drop.
            var rules = StanceRules.Current;
            _clipCrouch = Mathf.MoveTowards(_clipCrouch, _crouch > 0.5f ? 1f : 0f, ClipCrouchPerSecond * Time.deltaTime);
            _drop = _dip * jump.LandDropMeters;
            // Lying: the spine line rests at body height over the ground and the model is moved back from the capsule,
            // so the head (which the pitch swung forward to the end of the body) ends up under the eyes.
            // Pitched (swim): the spine line lies at body height. Clip: the model's head is moved back to the eyes (the clip lies along +z from the hips).
            var lying = (Vector3.up * rules.ProneBodyHeight + Vector3.back * rules.ProneModelBackShift) * (_prone - _proneClip)
                + Vector3.back * (_proneHeadAhead - ProneHeadAheadOfEyes) * _proneClip;
            // Seated: the hips go down and back into the seat (the body's origin is already placed for the eyes).
            var seatRules = VehicleSeat.RulesOf(_combatant);
            var sitting = SeatedShift(Vector3.down * seatRules.SeatedHipDrop + Vector3.back * seatRules.SeatedHipBack) * _seated;
            _animator.transform.localPosition = _modelRestPosition + Vector3.down * (_drop + _plantDrop) + lying + sitting;
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

        // Level of detail by distance to the camera (ADR-189 (3), ADR-233): 0 within IkMeters = everything; 1 within AnimateMeters = no foot IK;
        // 2 beyond = no foot IK, no additive layer, no breathing, and the skeleton is only updated while the model is on screen (the footsteps
        // and the held weapon matter only where they can be heard: the footstep source reaches 25 m). A headless server is always level 2.
        public const float IkMeters = 20f, AnimateMeters = 40f;
        int _lod;
        float _lodNext;
        public int Lod => _lod;

        void UpdateLod()
        {
            if (Time.time < _lodNext) return;
            _lodNext = Time.time + 0.25f + (GetInstanceID() & 7) * 0.02f; // spread over frames
            float d;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) d = float.MaxValue;
            else
            {
                var cam = PlayerController.Local != null ? PlayerController.Local.Camera : Camera.main;
                d = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 0f;
            }
            _lod = d <= IkMeters ? 0 : d <= AnimateMeters ? 1 : 2;
            var mode = _lod >= 2 ? AnimatorCullingMode.CullUpdateTransforms : AnimatorCullingMode.AlwaysAnimate;
            if (_animator.cullingMode != mode) _animator.cullingMode = mode;
        }

        bool _unarmedBody;

        // `_base` is the logical state (Locomotion covers both trees); the animator plays Unarmed or Locomotion for it.
        void SetBase(string state)
        {
            bool tree = state == Locomotion;
            string wanted = state == Air ? AirShown() : !tree ? state : _crouch > 0.5f ? (_unarmedBody ? UnarmedCrouch : RifleCrouch) : _unarmedBody ? Unarmed : Aiming ? LocomotionAim : Locomotion;
            if (_base == state && _baseShown == wanted) return;
            // Down to and up from the ground is slower than a step.
            _animator.CrossFadeInFixedTime(wanted, state == Prone || _base == Prone ? ProneFade : Fade, BaseLayer);
            _base = state;
            _baseShown = wanted;
        }

        string _baseShown = Locomotion; // the animator state actually playing for _base

        string AirShown() => _jumpClip == JumpClip.Start ? JumpStart : _jumpClip == JumpClip.Loop ? JumpLoop : _jumpClip == JumpClip.Land ? JumpLand : Air;
        public string BaseShown => _baseShown;
        bool Aiming => _combatant.AimMode != AimMode.Hip;

        // Pure: index of the direction (deg, any range) nearest to `deg`, the short way round. Straight ahead and straight back (the UAL
        // cycles, which take a turned leg best) win anything within 10 deg of a tie.
        public static int NearestDirection(float[] dirs, float deg)
        {
            float Cost(int i) { float d = Mathf.Abs(Mathf.DeltaAngle(dirs[i], deg)); float a = Mathf.Abs(Mathf.DeltaAngle(dirs[i], 0f)); return a < 1f || a > 179f ? d : d + 10f; }
            int best = 0;
            for (int i = 1; i < dirs.Length; i++) if (Cost(i) < Cost(best)) best = i;
            return best;
        }
        float _flinchLength;
        float FlinchClipLength()
        {
            if (_flinchLength <= 0f)
                foreach (var c in _animator.runtimeAnimatorController.animationClips)
                    if (c.name == "U_Hit_Chest") _flinchLength = c.length;
            return _flinchLength > 0f ? _flinchLength : 0.5f;
        }

        void SetAdditive(string state)
        {
            if (_additive == state) return;
            _animator.CrossFadeInFixedTime(state, 0.15f, AdditiveLayer);
            _additive = state;
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
        void LateUpdateBody()
        {
            if (_animator == null || (_ragdoll != null && _ragdoll.Active)) return;
            if (_footLeft != null && _footRight != null)
            {
                // Heights as the clip has them: without the landing dip.
                bool left = _leftContact.Update(transform.InverseTransformPoint(_footLeft.position).y + _drop + _plantDrop);
                bool right = _rightContact.Update(transform.InverseTransformPoint(_footRight.position).y + _drop + _plantDrop);
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
            // (A long gun squares the shoulders itself, PoseRifle.)
            // Bare hands: the jog/walk clip yaws the pelvis (with a bias), which reads as running sideways: turn the whole body back from the hips.
            // Armed, the legs are the same UAL cycles past a standstill (ADR-390): square their pelvis too while moving (the planted foot slid
            // sideways ~1.5 m/s), but leave the rifle idle's own stance alone.
            bool pelvisFixed = _unarmedBody && _prone < 0.001f && !_combatant.Dead.Value;
            var lv = _motion.LocalVelocity;
            float lspeed = new Vector2(lv.x, lv.z).magnitude;
            float pelvisFix = pelvisFixed ? 1f : _prone < 0.001f && _seated < 0.5f && !_combatant.Dead.Value ? Mathf.Clamp01(lspeed / 1.5f) : 0f;
            // Only the forward/back (UAL) part: a sidestep clip turns its hips into the step on purpose (ADR-391).
            // Squaring a strafe clip's hips swung its planted feet round in arcs (the backward diagonal slid ~7 m/s).
            pelvisFix *= Mathf.Clamp01((Mathf.Abs(Mathf.Cos(_clipDir * Mathf.Deg2Rad)) - 0.95f) / 0.05f);
            float hipsYawNow = HipsYaw();
            if (pelvisFix > 0f) Turn(Pelvis, -hipsYawNow * pelvisFix, transform.up);
            // The clip's shoulders counter-twist the hips by far more than a person does: take most of that swing out (shoulder line measured on the posed arms).
            float turnBack = -_legTurnShown + (_combatant.Dead.Value ? 0f : -hipsYawNow * (1f - pelvisFix) * _upperWeight);
            // About the model's own up: the character's up while standing, along the ground while lying.
            Turn(Torso, turnBack, _prone > 0.001f ? _animator.transform.up : transform.up);
            // A sidestep or diagonal clip turns the chest with the step (~15 deg off the aim on the rifle diagonal): square the shoulders back
            // to the aim, all of it with a gun, as with bare hands below (ADR-391).
            float strafeShare = 1f - Mathf.Clamp01((Mathf.Abs(Mathf.Cos(_clipDir * Mathf.Deg2Rad)) - 0.95f) / 0.05f);
            if (!pelvisFixed && strafeShare > 0f && _prone < 0.001f && !_combatant.Dead.Value && _bones[ArmLeft] != null && _bones[ArmRight] != null)
            {
                var across = transform.InverseTransformDirection(_bones[ArmRight].position - _bones[ArmLeft].position);
                Turn(Torso, -strafeShare * Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg, transform.up);
            }
            if (pelvisFixed && Time.time >= _punchUntil && _bones[ArmLeft] != null && _bones[ArmRight] != null)
            {
                var across = transform.InverseTransformDirection(_bones[ArmRight].position - _bones[ArmLeft].position);
                Turn(Torso, -UnarmedShoulderSwingCut * Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg, transform.up);
            }
            // Leaning right tips the torso clockwise seen from behind: a negative turn about the forward axis.
            Turn(Torso, -_combatant.LeanAmount * GameData.Rules.Lean.BodyRollDeg * (1f - _prone), transform.forward);

            // The jump. A positive turn about the right axis tips the top of the body forward, swings a hanging
            // limb backward and points the toes down.
            var jump = GameData.Rules.Jump;
            var stanceRules = StanceRules.Current;
            var parkourRules = ParkourRules.Current;
            Turn(Torso, jump.TorsoPitchDeg * _tuck + jump.LandPitchDeg * _dip - jump.TakeoffArchDeg * _push
                + parkourRules.PoseTorsoPitchDeg * _parkour, transform.right);
            // Lying: the chest and head come back up (a negative turn about the right axis undoes the forward tip).
            Turn(Torso, -stanceRules.ProneHeadLiftDeg * (_prone - _proneClip), transform.right);
            // Lying with the clip: looking up or down tips the chest, and the weapon with it.
            Turn(Torso, -Mathf.Clamp(LookPitch(), -ProneLookMaxDeg, ProneLookMaxDeg) * ProneLookShare * _proneClip, transform.right);
            // The legs face where the model is turned, not where the combatant looks.
            var legRight = Quaternion.AngleAxis(_legTurnShown, transform.up) * transform.right;
            // Knees up in front, shins folded back under them. The shin turns relative to the thigh, which already
            // leans forward by t, so to point back by s from vertical it turns t + s (a landing uses s = t, i.e.
            // twice the thigh angle). The shin's net swing from vertical is then s, so the foot turns -s to be flat.
            float thigh = -(jump.TuckThighDeg * _tuck + jump.LandBendDeg * _dip);
            float shin = jump.TuckShinDeg * _tuck + 2f * jump.LandBendDeg * _dip;
            float foot = jump.TakeoffToeDeg * _push - jump.LandBendDeg * _dip;
            PoseLeg(0, ThighLeft, ShinLeft, FootLeft, thigh, shin, legRight);
            PoseLeg(1, ThighRight, ShinRight, FootRight, thigh, shin, legRight);
            Turn(FootLeft, foot, legRight);
            Turn(FootRight, foot, legRight);
            // Empty hands: arms out sideways (a weapon pose keeps both arms on the weapon). About the forward
            // axis a positive turn swings a hanging arm out to the right.
            float armLift = jump.ArmLiftDeg * Mathf.Max(_tuck, _dip * LandArmShare) * (1f - Mathf.Max(_upperWeight, _rifle));
            Turn(ArmLeft, -armLift, transform.forward);
            Turn(ArmRight, armLift, transform.forward);
            // Parkour: both arms reach forward and down onto the top (a negative turn swings a hanging arm forward).
            float reach = -parkourRules.PoseArmReachDeg * _parkour;
            Turn(ArmLeft, reach, transform.right);
            Turn(ArmRight, reach, transform.right);
            PoseGait();
            PoseRifle();
            PoseClipMagazine();
            PoseMelee();
            PoseItems();
            PoseLedge();
            PoseEmote();
            PoseSwim();
            PoseBungee();
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
            SitOnCushion(s);
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
            PoseVehicleHands(free, astride, s); // #89: hands onto the model's grips, feet along the machine
        }

        // Turns a thigh and shin and brings the foot along. In this skeleton the feet are not children of the
        // shins (they are separate control bones), so a foot left behind would stretch the leg down to the floor.
        void PoseLeg(int side, int thighIndex, int shinIndex, int footIndex, float thighDeg, float shinDeg, Vector3 axis)
        {
            Transform shin = _bones[shinIndex], foot = _bones[footIndex];
            // A Humanoid rig hangs the foot under the shin, so it follows by itself.
            if (shin == null || foot == null || shin.childCount == 0 || foot.IsChildOf(shin) || (Mathf.Abs(thighDeg) <= 1e-3f && Mathf.Abs(shinDeg) <= 1e-3f))
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
