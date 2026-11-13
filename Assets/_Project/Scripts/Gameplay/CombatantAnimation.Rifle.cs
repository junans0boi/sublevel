using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // A long gun in both hands (ADR-083, #111). The character file has only one-handed pistol clips (the upper layer's
    // Idle_Gun / Idle_Gun_Pointing), which showed a rifle hanging from one hand over a frozen torso. With a long gun
    // the upper layer stays off, so the torso moves with the base layer's walk and run, and the weapon is placed in
    // code relative to the right shoulder (the torso turned right a little, as a shooter stands bladed): low ready (muzzle down, turned in across the body) unless aiming or firing,
    // shouldered and level when aiming. The right hand is put on the grip (the weapon model hangs from it) and the
    // left on the handguard with two-bone IK. The torso pitches with speed, rolls into a sidestep and leans into a
    // start; the weapon bobs with the steps. Numbers: rules.json "RifleHold" (RifleHoldRules).
    public partial class CombatantAnimation
    {
        const float RifleWeightPerSecond = 7f;
        const float MinLongGunMeters = 0.4f;
        const int MaxWeaponId = 32;
        const float LowReadySquare = 0.7f;     // share of the shoulders' turn away from the hold taken out at low ready   // shorter (the pistol) keeps the one-handed clip pose
        const float AccelSmoothPerSecond = 8f;
        const float PalmBelowBore = 0.03f;     // the left palm holds the handguard this far under the bore line

        Transform _upperArmR, _wristR;
        float _shoulderRestYaw;      // shoulder line's turn from the facing in the saved pose (degrees, right positive)
        Quaternion _wristRBind;      // right wrist relative to the character in the saved pose, weapon pointing forward
        WeaponVisual _weapons;
        float[] _supportAhead;       // per weapon id: the left hand this far along the weapon from the grip; 0 = not a long gun
        bool _rifleWanted;           // a long gun in hand and nothing else posing the arms
        float _rifle;                // 0..1: how much of the two-handed hold is shown
        float _ready;                // 0 low ready .. 1 shouldered
        float _sprintShown;          // 0..1, eased
        float _kick;                 // 0..1 after a shot
        Vector3 _smoothVelocity, _smoothAccel;

        // How much of the two-handed long gun hold is shown (0..1) and how far it is raised (0 low ready, 1 shouldered).
        public float RifleHoldShown => _rifle;
        public float RifleReady => _ready;
        // The hold pose shown this frame (angles and stock offset, RifleHoldRules).
        public RifleHoldPose RiflePose { get; private set; }

        void FindArms()
        {
            // The arm and hand bones the seated grips use (CombatantAnimation.Vehicle.cs): Reach and Grip work on them.
            if (!_gripBonesLooked) FindGripBones();
            _upperArmR = _bones[ArmRight];
            _wristR = _wrist[1];
            _weapons = GetComponent<WeaponVisual>();
            if (_wristR == null || _upperArmR == null || _wrist[0] == null || _weapons == null) return;
            // Saved pose = the aiming reference pose: the weapons point along the character's forward from the right hand.
            _wristRBind = Quaternion.Inverse(transform.rotation) * _wristR.rotation;
            _shoulderRestYaw = ShoulderYaw();
            _supportAhead = new float[MaxWeaponId + 1];
            for (int id = 1; id <= MaxWeaponId; id++)
            {
                var model = _weapons.ModelFor(id);
                if (model == null || !Span(model, out float back, out float front) || front - back < MinLongGunMeters) continue;
                // Close in front of the grip: the short arms reach no further (the rest of the barrel sticks out past it).
                _supportAhead[id] = Mathf.Clamp(front * 0.3f, 0.1f, 0.16f);
            }
        }

        // How far the weapon model reaches behind (negative) and in front of the right hand, along the character's forward.
        bool Span(GameObject model, out float back, out float front)
        {
            back = float.MaxValue; front = float.MinValue;
            float hand = transform.InverseTransformPoint(_wristR.position).z;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float z = transform.InverseTransformPoint(filter.transform.TransformPoint(corner)).z - hand;
                    back = Mathf.Min(back, z);
                    front = Mathf.Max(front, z);
                }
            }
            return front > back;
        }

        // Which way the shoulder line faces, degrees from the character's facing, right positive.
        float ShoulderYaw()
        {
            var across = transform.InverseTransformDirection(_upperArmR.position - _bones[ArmLeft].position);
            return Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg;
        }

        bool LongGunInHand(int id) => _supportAhead != null && id > 0 && id < _supportAhead.Length && _supportAhead[id] > 0f;

        // Update: how much of the hold to show and how far raised. `armed` = a gun (not melee) in hand, alive.
        void TrackRifle(bool armed, bool aiming, Vector3 localVelocity)
        {
            var r = GameData.Rules.RifleHold;
            float dt = Time.deltaTime;
            _rifleWanted = armed && LongGunInHand(_combatant.ActiveWeaponId) && _prone < 0.5f && _seated < 0.5f && _parkour < 0.5f;
            _rifle = Mathf.MoveTowards(_rifle, _rifleWanted ? 1f : 0f, RifleWeightPerSecond * dt);
            // The local player's aim key raises it on the press, before the tap/hold decision makes it an aim (#121).
            var local = PlayerController.Local;
            aiming |= _combatant == Combatant.LocalPlayer && local != null && local.AimKeyDown;
            _ready = RifleHold.ReadyStep(_ready, aiming, dt, r);
            float speed = new Vector2(localVelocity.x, localVelocity.z).magnitude;
            var loco = GameData.Rules.Locomotion;
            float sprint = loco.SprintSpeed > loco.WalkSpeed ? Mathf.InverseLerp(loco.WalkSpeed * 1.05f, loco.SprintSpeed * 0.9f, speed) : 0f;
            _sprintShown = Mathf.MoveTowards(_sprintShown, sprint, 5f * dt);
            if (r.KickSeconds > 0f) _kick = Mathf.MoveTowards(_kick, 0f, dt / r.KickSeconds);
            // Acceleration in the character's axes, smoothed: what the torso leans into.
            if (dt > 0f)
            {
                float k = 1f - Mathf.Exp(-AccelSmoothPerSecond * dt);
                var accel = (localVelocity - _smoothVelocity) / dt;
                _smoothVelocity = localVelocity;
                _smoothAccel = Vector3.Lerp(_smoothAccel, accel, k);
            }
            RiflePose = RifleHold.Pose(speed, _sprintShown, _ready, r);
        }

        // The locomotion cycle (0..1, two steps) the base layer is at, for the bob.
        float StridePhase()
        {
            if (_base != Locomotion) return 0f;
            float t = _animator.GetCurrentAnimatorStateInfo(BaseLayer).normalizedTime;
            return t - Mathf.Floor(t);
        }

        // Where this combatant looks, degrees up: the local player's own view, the replicated one for everyone else.
        float LookPitch()
        {
            var local = PlayerController.Local;
            if (_combatant == Combatant.LocalPlayer && local != null) return local.Pitch;
            return _stance != null ? _stance.LookPitch.Value : 0f;
        }

        // LateUpdate, after the torso and leg turns: lean the torso, then place the weapon and both hands.
        void PoseRifle()
        {
            if (_rifle <= 0.001f || _wristR == null || _upperArmR == null || _wrist[0] == null) return;
            var r = GameData.Rules.RifleHold;
            var v = _motion.LocalVelocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            float lean = 1f - _crouch * 0.5f;
            Turn(Torso, RifleHold.TorsoPitch(speed, _sprintShown, _smoothAccel.z, r) * _rifle * lean, transform.right);
            // Rolling right tips the top clockwise seen from behind: a negative turn about the forward axis.
            Turn(Torso, -RifleHold.StrafeRoll(v.x, r) * _rifle, transform.forward);
            // The shoulders to the hold's twist from the facing, measured on the shoulder line itself: the run clip
            // turns the hips (27 degrees) and the chest with the stride. Low ready keeps some of the stride's swing.
            var pose = RiflePose;
            float square = Mathf.Lerp(LowReadySquare, 1f, _ready) * _rifle;
            Turn(Torso, Mathf.DeltaAngle(ShoulderYaw(), _shoulderRestYaw + pose.Twist) * square, transform.up);

            // The look pitch (#121, ADR-096): all of it shouldered, part of it at low ready. The chest takes a share
            // (looking up tips it back: a negative turn about the right axis), the arms and weapon the rest.
            float look = RifleHold.FollowedPitch(LookPitch(), _ready, r) * _rifle;
            Turn(Torso, -look * r.TorsoPitchShare, transform.right);
            var frame = transform.rotation * Quaternion.AngleAxis(-look, Vector3.right);

            float amount = Mathf.Clamp01(speed / Mathf.Max(0.1f, r.MoveFullSpeed)) * (1f - 0.6f * _ready);
            RifleHold.Bob(StridePhase(), amount, r, out float down, out float side, out float bobRoll);
            var turn = frame * Quaternion.Euler(pose.Pitch - r.KickPitchDeg * _kick, pose.Yaw, pose.Roll + bobRoll);
            var axis = turn * Vector3.forward;
            int id = _combatant.ActiveWeaponId;
            if (!LongGunInHand(id)) return;
            var grip = _upperArmR.position + frame * new Vector3(pose.X + side, pose.Y - down, pose.Z)
                - axis * (r.KickBackMeters * _kick);
            var gripTurn = turn * _wristRBind;

            // Right hand: on the grip, turned so the weapon (hanging from it) points along the hold.
            Reach(1, ArmRight, grip, _rifle);
            _wristR.rotation = Quaternion.Slerp(_wristR.rotation, gripTurn, _rifle);
            // Left hand: round the handguard from below, palm up and toward the weapon, fingers closed.
            var up = turn * Vector3.up;
            Grip(0, ArmLeft, grip + axis * _supportAhead[id] - up * PalmBelowBore, axis, up + turn * Vector3.right, GripCurlDeg, _rifle);
        }

        // For the pose review (FeelCapture): heights over the feet of the head joint, the right shoulder and the top of
        // the weapon in hand, meters.
        public string AimGeometry()
        {
            var model = _weapons != null ? _weapons.ModelFor(_combatant.ActiveWeaponId) : null;
            if (model == null || _head == null || _upperArmR == null) return "";
            float top = float.MinValue;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, renderer.bounds.max.y);
            float feet = transform.position.y;
            return $"head {_head.position.y - feet:F3} shoulder {_upperArmR.position.y - feet:F3} weapon top {top - feet:F3}";
        }

        void KickRifle()
        {
            if (LongGunInHand(_combatant.ActiveWeaponId)) _kick = 1f;
        }
    }
}
