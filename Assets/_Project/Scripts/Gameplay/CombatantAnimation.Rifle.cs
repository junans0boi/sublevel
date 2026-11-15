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
        const float MinPistolMeters = 0.1f, PistolSupportAhead = 0.02f, PistolCupSide = 0.025f;
        const float LowReadySquare = 0.7f;     // share of the shoulders' turn away from the hold taken out at low ready   // shorter (the pistol) keeps the one-handed clip pose
        const float StockPocketZ = 0.03f;      // the butt's spot in front of the right shoulder joint when shouldered
        const float AccelSmoothPerSecond = 8f;
        const float PalmBelowBore = 0.03f;     // the left palm holds the handguard this far under the bore line

        Transform _upperArmR, _wristR;
        float _shoulderRestYaw;      // shoulder line's turn from the facing in the saved pose (degrees, right positive)
        Quaternion _wristRBind;      // right wrist relative to the character in the saved pose, weapon pointing forward
        WeaponVisual _weapons;
        float[] _supportAhead;       // per weapon id: the left hand this far along the weapon from the grip; 0 = not held in both hands
        float[] _rearBack;           // per weapon id: how far the rear of the model is behind the right hand (m), for seating the stock (#226)
        bool[] _isPistol;            // per weapon id: a hand gun, held in both hands with PistolHold's numbers (#218)
        bool _rifleWanted;           // a long gun in hand and nothing else posing the arms
        float _rifle;                // 0..1: how much of the two-handed hold is shown
        float _ready;                // 0 low ready .. 1 shouldered
        float _sprintShown;          // 0..1, eased
        float _kick;                 // 0..1 after a shot
        Vector3 _smoothVelocity, _smoothAccel;
        // Reload and draw (#200): the replicated Reloading flag starts the clock on every side; the weapon's own times set the pace.
        bool _wasReloading;
        float _reloadStart = -1f, _reloadSeconds = 1f, _drawStart = -10f, _drawSeconds = 0.4f;
        int _lastDrawnId;
        ReloadKind _reloadKind;
        GameObject _magModel;

        // How far through the reload (0..1) and the draw (0..1; 1 when not drawing) this combatant is, for the review shots and first person.
        public float ReloadProgress { get; private set; } = -1f;
        public float DrawProgress { get; private set; } = 1f;
        // 0..1 while a long gun is being lowered after the holster key (1 when not), and the weapon id still held for it.
        public float HolsterProgress { get; private set; } = 1f;
        public const float HolsterSeconds = 0.3f;
        int _holsterId;
        float _holsterStart = -10f;
        // The weapon in the hands for the poses: the active one, or the one being put away.
        public int HeldId => _itemHidesWeapon ? 0 : _combatant.ActiveWeaponId != 0 ? _combatant.ActiveWeaponId : _holsterId;
        public ActionPose ActionShown { get; private set; }

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
            _isPistol = new bool[MaxWeaponId + 1];
            _rearBack = new float[MaxWeaponId + 1];
            for (int id = 1; id <= MaxWeaponId; id++)
            {
                var model = _weapons.ModelFor(id);
                var def = GameData.Weapons.Find(id);
                if (model == null || def == null || def.IsMelee || !Span(model, out float back, out float front)) continue;
                if (front - back < MinLongGunMeters)
                {
                    // A hand gun: the left hand cups the right (no handguard to hold).
                    if (front - back >= MinPistolMeters) { _supportAhead[id] = PistolSupportAhead; _isPistol[id] = true; }
                    continue;
                }
                _rearBack[id] = Mathf.Max(0f, -back);
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

        // Held in both hands: a long gun, or a pistol (#218).
        bool LongGunInHand(int id) => _supportAhead != null && id > 0 && id < _supportAhead.Length && _supportAhead[id] > 0f;
        bool PistolInHand(int id) => LongGunInHand(id) && _isPistol[id];
        RifleHoldRules HoldRules(int id) => PistolInHand(id) ? GameData.Rules.PistolHold : GameData.Rules.RifleHold;

        // Update: how much of the hold to show and how far raised. `armed` = a gun (not melee) in hand, alive.
        void TrackRifle(bool armed, bool aiming, Vector3 localVelocity)
        {
            var r = HoldRules(HeldId);
            float dt = Time.deltaTime;
            _rifleWanted = armed && LongGunInHand(HeldId) && _prone < 0.5f && _seated < 0.5f && _parkour < 0.5f;
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
            TrackAction();
        }

        void TrackAction()
        {
            int id = _combatant.ActiveWeaponId;
            var def = id != 0 ? GameData.Weapons.Find(id) : null;
            bool reloading = _combatant.Reloading.Value && def != null;
            if (reloading && !_wasReloading)
            {
                _reloadStart = Time.time;
                _reloadKind = def.ReloadsByShell ? ReloadKind.Shell : PistolInHand(id) ? ReloadKind.Pistol : ReloadKind.Magazine;
                _reloadSeconds = Mathf.Max(0.3f, def.ReloadSeconds(_combatant.ActiveMag));
            }
            _wasReloading = reloading;
            // A shell reload runs as long as the flag does; the rest end at their time (or earlier if cancelled).
            ReloadProgress = reloading ? (_reloadKind == ReloadKind.Shell ? 0.5f : Mathf.Clamp01((Time.time - _reloadStart) / _reloadSeconds)) : -1f;
            if (id != _lastDrawnId)
            {
                if (id != 0) { _drawStart = Time.time; _drawSeconds = Mathf.Max(0.25f, def != null ? def.DrawSeconds : 0.4f); _holsterId = 0; }
                // Putting a long gun away: it stays in the hand and lowers for HolsterSeconds instead of vanishing (#200).
                else if (_lastDrawnId != 0 && !_combatant.Dead.Value && LongGunInHand(_lastDrawnId)) { _holsterId = _lastDrawnId; _holsterStart = Time.time; }
                _lastDrawnId = id;
            }
            DrawProgress = Mathf.Clamp01((Time.time - _drawStart) / _drawSeconds);
            HolsterProgress = _holsterId != 0 && id == 0 ? Mathf.Clamp01((Time.time - _holsterStart) / HolsterSeconds) : 1f;
            if (HolsterProgress >= 1f) _holsterId = 0;
        }

        // The reload or draw pose for this frame (identity when neither is on).
        ActionPose CurrentAction()
        {
            ActionPose a = default;
            if (ReloadProgress >= 0f)
                a = ReloadMotion.Reload(_reloadKind == ReloadKind.Shell ? Mathf.Clamp01(Mathf.Min(1f, (Time.time - _reloadStart) / 0.3f)) : ReloadProgress, _reloadKind, Time.time - _reloadStart);
            else if (HolsterProgress < 1f) a = ReloadMotion.Draw(1f - HolsterProgress);
            else if (DrawProgress < 1f) a = ReloadMotion.Draw(DrawProgress);
            ActionShown = a;
            return a;
        }

        void ShowMag(bool visible, Vector3 at, Quaternion turn, bool pistol)
        {
            if (visible && _magModel == null)
            {
                var model = _weapons != null ? _weapons.ModelFor(HeldId) : null;
                var source = model != null ? model.GetComponentInChildren<Renderer>() : null;
                _magModel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _magModel.name = "ReloadMagazine";
                Destroy(_magModel.GetComponent<Collider>());
                if (source != null) _magModel.GetComponent<Renderer>().sharedMaterial = source.sharedMaterial;
                _magModel.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (_magModel == null) return;
            _magModel.SetActive(visible);
            if (!visible) return;
            _magModel.transform.localScale = pistol ? new Vector3(0.025f, 0.07f, 0.04f) : new Vector3(0.035f, 0.11f, 0.06f);
            _magModel.transform.SetPositionAndRotation(at, turn);
        }

        // First person: the magazine rides in a spot low on the left of the view while the reload swaps it.
        public void ShowMagAtEye(Transform cam, bool visible) =>
            ShowMag(visible, cam.TransformPoint(new Vector3(-0.1f, -0.2f, 0.35f)), cam.rotation, PistolInHand(HeldId));

        // First person with arms: the magazine rides in the support hand's spot (#216).
        public void ShowMagAt(bool visible, Vector3 at, Quaternion turn) => ShowMag(visible, at, turn, PistolInHand(HeldId));

        // How far along the weapon the support hand holds from the grip (m), 0 = not held in two hands; pistols cup the right hand.
        public float SupportAheadOf(int id) => LongGunInHand(id) ? _supportAhead[id] : 0f;
        public bool IsPistolGrip(int id) => PistolInHand(id);
        public const float SupportPalmBelowBore = PalmBelowBore, SupportPistolCupSide = PistolCupSide;

        void OnDisable() { if (_magModel != null) Destroy(_magModel); }

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
            var r = HoldRules(HeldId);
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
            var act = CurrentAction();
            // Breathing lifts and lowers the hands a little (#231).
            down -= Breath * 0.004f * (1f - amount);
            var turn = frame * Quaternion.Euler(pose.Pitch - r.KickPitchDeg * _kick + act.PitchDeg, pose.Yaw, pose.Roll + bobRoll + act.RollDeg);
            var axis = turn * Vector3.forward;
            int id = HeldId;
            if (!LongGunInHand(id)) return;
            // Shouldered, the butt goes into the shoulder pocket: a long stock (the bullpup's) pushes the grip forward until it does (#226).
            float stock = _isPistol[id] ? 0f : Mathf.Max(0f, StockPocketZ + _rearBack[id] - pose.Z) * _ready;
            var grip = _upperArmR.position + frame * new Vector3(pose.X + side, pose.Y - down - act.Down, pose.Z + stock)
                - axis * (r.KickBackMeters * _kick);
            var gripTurn = turn * _wristRBind;

            // Right hand: on the grip, turned so the weapon (hanging from it) points along the hold.
            Reach(1, ArmRight, grip, _rifle);
            _wristR.rotation = Quaternion.Slerp(_wristR.rotation, gripTurn, _rifle);
            // Left hand: round the handguard from below, palm up and toward the weapon, fingers closed.
            var up = turn * Vector3.up;
            var across = turn * Vector3.right;
            // Sprinting the left hand lets go and swings with the run clip (#222): the weapon is carried by the right hand.
            float support = _rifle * (1f - r.SprintSupportRelease * _sprintShown * (1f - _ready));
            if (PistolInHand(id))
                // The left hand wraps the right from the left, palm in, fingers over the right fingers.
                Grip(0, ArmLeft, grip - across * PistolCupSide + axis * _supportAhead[id] - up * 0.01f, axis, across, GripCurlDeg, support);
            else
                Grip(0, ArmLeft, grip + axis * _supportAhead[id] - up * PalmBelowBore, axis, up + across, GripCurlDeg, support);
            if (act.LeftBlend > 0.001f)
            {
                // Reload: the left hand leaves the hold for the magazine well, the belt and the charging handle.
                var spot = grip + axis * act.LeftA + up * act.LeftU + across * act.LeftC;
                Grip(0, ArmLeft, spot, axis, up + across, GripCurlDeg * 0.6f, _rifle * act.LeftBlend * (1f - r.SprintSupportRelease * _sprintShown * (1f - _ready)));
                ShowMag(act.MagInHand && !_weapons.AtEye, spot - up * 0.02f, turn, PistolInHand(id));
            }
            else ShowMag(false, Vector3.zero, Quaternion.identity, false);
        }

        // For the pose review (FeelCapture): heights over the feet of the head joint, the right shoulder and the top of
        // the weapon in hand, meters.
        public string AimGeometry()
        {
            var model = _weapons != null ? _weapons.ModelFor(HeldId) : null;
            if (model == null || _head == null || _upperArmR == null) return "";
            float top = float.MinValue;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, renderer.bounds.max.y);
            float feet = transform.position.y;
            return $"head {_head.position.y - feet:F3} shoulder {_upperArmR.position.y - feet:F3} weapon top {top - feet:F3}";
        }

        void KickRifle()
        {
            if (LongGunInHand(HeldId)) _kick = 1f;
        }
    }
}
