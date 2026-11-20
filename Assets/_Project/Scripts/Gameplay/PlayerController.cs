using System.Collections.Generic;
using FishNet.Object;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Owner-side control of a player: movement (owner-authoritative until M6, ADR-029), camera, and requests to the server.
    [RequireComponent(typeof(CharacterController), typeof(Combatant))]
    public class PlayerController : NetworkBehaviour
    {
        const float AimDistance = 300f;
        public const float PingDistance = 600f; // middle-mouse ping reach (ADR-065 (6))
        const float PitchLimit = 80f;
        const float TeleportTellMeters = 1.5f; // a Teleport this far is announced to the server (MoveGuard)

        [SerializeField] Renderer[] _bodyRenderers = new Renderer[0];

        // Tests and tools replace the device input here before the player spawns.
        public static IPlayerInput InputOverride;
        public static PlayerController Local { get; private set; }

        public PlayerCameraRig Rig { get; } = new PlayerCameraRig();
        public Camera Camera { get; private set; }
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public bool LiftCallInReach { get; private set; }   // the waiting island's lift button is what F would press (the HUD prompt)
        public LootItem NearbyLoot { get; private set; }

        public AimMode Aim => _aim.Mode;
        public int Lean { get; private set; }
        // The stance this player has asked for; the replicated one is on CombatantStance.
        public Stance BodyStance { get; private set; }
        public bool Sprinting { get; private set; }
        // In deep water, swimming (ADR-112).
        public bool Swimming => _swimming;
        bool _swimming;
        float _waterSurface;
        // Free look (Alt held): the view orbits away from where the character faces by these angles (ADR-065 (2)).
        public bool FreeLooking { get; private set; }
        // Auto-run (=): walking forward on its own until W or S is pressed, aiming starts or a screen opens (ADR-065 (1)).
        public bool AutoRunning => _autoRun;
        // Slow walk (Ctrl held, ADR-065 (3)).
        public bool SlowWalking { get; private set; }
        // The aim key is down with a gun in hand, whether it turns out a tap (sights) or a hold (shoulder).
        public bool AimKeyDown { get; private set; }
        // A vehicle (or anything else) moves the body: no walking, jumping or parkour of our own; the view still
        // turns. Firing is left to whoever set it.
        public bool ExternalControl;
        // Parkour (Space at an obstacle, ADR-065 (4)): the move under way, and what the last Space at an obstacle decided.
        public bool InParkour => _parkour.Move != ParkourMove.None;
        public ParkourMove ParkourUnderWay => _parkour.Move;
        public ParkourPlan LastParkourPlan { get; private set; }
        public bool ParkourGoingBack => _parkourBack;
        // The last move that finished was run back to its start (S early in it).
        public bool LastParkourTakenBack { get; private set; }
        // Hanging from a high edge caught in the air, or pulling up from it (#115, ADR-087).
        public bool OnLedge => Parkour.IsLedge(_parkour.Move);
        public float ViewYaw => Yaw + _freeYaw;
        public float ViewPitch => Pitch + _freePitch;
        // This frame's input as the character got it (empty while a screen is open), for other owner-side
        // components that act on keys of their own (fire mode, throwing, slots 3-5).
        public PlayerInputFrame LastInput { get; private set; }

        LocomotionParams _locomotion;
        float _moveX, _moveZ; int _moveFrame; // planar velocity actually moved (#111: starts and stops take a moment)
        GameRules _rules;
        AimInput _aim;
        readonly ViewRecoil _recoil = new ViewRecoil();
        // Camera trauma (ADR-471): added on top of the placed camera after Update and taken off again before the next,
        // so nothing that aims from the camera ever sees it.
        readonly CameraShake _shake = new CameraShake();
        Quaternion _shakeApplied = Quaternion.identity;
        bool _shakeOn;
        readonly ShotGrouper _ownShots = new ShotGrouper(0.03);
        const float StanceResendSeconds = 0.3f;
        const float MaxViewPitch = 89f;
        // The ride view (ADR-079 car, about 2.2 m square inside): near the door, just under the ceiling, looking back at the rider.
        public const float RideCameraForward = 0.75f, RideCameraHeight = 1.95f;

        bool _autoRun;
        bool _sprintOverruled; // C or Z pressed during this sprint: it no longer gets the body up
        float _lastMoveY;
        bool _sentSlowWalk;
        float _slowWalkSentAt;
        float _sentPitch, _pitchSentAt = float.NegativeInfinity;
        ParkourPlan _parkour;
        float _parkourT;
        bool _parkourBack;
        float _hangYaw;          // the body faces the wall while on a ledge
        bool _jumpedThisAir;     // a jump (not just walking off something) put the body in the air
        bool _ledgeLetGo;        // S let go of an edge: no catching another until the feet are down
        int _sentAim, _sentLean;
        float _stanceSentAt;
        int _sentBodyStance;
        float _bodyStanceSentAt;
        float _freeYaw, _freePitch;
        readonly FallTracker _fall = new FallTracker();
        CombatantStance _stance; // missing on a prefab built before stances existed: the player then only stands
        System.Random _recoilRng;
        int _bodyShown; // 0 all, 1 none (eye view), 2 all but the head (eye view, seated, empty hands)
        CharacterController _controller;
        Combatant _combatant;
        ThrowableView _throwView;
        WeaponVisual _weaponVisual;
        IPlayerInput _input;
        float _verticalVelocity;
        readonly FireCadence _fireCadence = new FireCadence();
        readonly FireCadence _punchCadence = new FireCadence();
        double _clickUntil = -1;
        int _burstLeft; // shots still to fire of a burst (ADR-089)
        bool _owner;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _combatant = GetComponent<Combatant>();
            _throwView = GetComponent<ThrowableView>();
            _weaponVisual = GetComponent<WeaponVisual>();
            _stance = GetComponent<CombatantStance>();
            _controller.enabled = false;
            _rules = GameData.Rules;
            _locomotion = _rules.Locomotion;
            _aim = new AimInput(_rules.Aim.TapSeconds);
        }

        // The server's copy of a player some other peer owns is still something to hit: its capsule stays on, otherwise
        // shots and blasts pass through it (ADR-130). The owner turns its own on in OnStartClient and is the only one that
        // moves it. Only the server judges hits, so client replicas keep theirs off. Hit-only: queries see the capsule,
        // but no body is blocked or pushed by it (the copy lags its owner, and in one test process it overlaps the
        // owner's own body) (#161).
        public override void OnStartServer()
        {
            if (Owner.IsLocalClient) return;
            _controller.enabled = true;
            _controller.excludeLayers = ~0;
        }

        public override void OnStartClient()
        {
            if (!IsOwner) return;
            _owner = true;
            Local = this;
            if (GameSettings.DefaultViewFirstPerson && Rig.Mode == ViewMode.ThirdPerson) Rig.ToggleMode();
            _input = InputOverride ?? new DeviceInput();
            _controller.enabled = true;
            Camera = Camera.main;
            if (Camera != null) Camera.fieldOfView = _rules.Aim.Hip.Fov;
            _combatant.ShotFired += OnOwnShot;
            if (GetComponent<BungeeRide>() == null) gameObject.AddComponent<BungeeRide>(); // the waiting island's jump (ADR-386); idle everywhere else
            if (_stance != null) { _stance.ParkourRejected += OnParkourRejected; _stance.Corrected += OnParkourRejected; } // both: end any move, go back
            int matchSeed = MatchDirector.Instance != null ? MatchDirector.Instance.Seed : 0;
            _recoilRng = new System.Random(unchecked(matchSeed * 131 + ObjectId * 31 + 7));
            Yaw = transform.eulerAngles.y;
            UiFocus.Close();
            UpdateCursor();
            // The screens only the local player uses. They live in the match scene and go away with it.
            var screens = new GameObject("Screens");
            screens.AddComponent<InventoryScreen>();
            screens.AddComponent<PauseMenu>();
            screens.AddComponent<ScopeView>();
            screens.AddComponent<ScopeControl>(); // G10b (ADR-103)
            screens.AddComponent<SwimHud>();      // G12 (ADR-112)
            screens.AddComponent<MapScreen>();
            screens.AddComponent<PickupList>(); // G5 (ADR-093)
        }

        // The mouse steers the view while playing and becomes a pointer while a screen is open.
        void UpdateCursor()
        {
            if (Application.isBatchMode || InputOverride != null) return;
            bool playing = UiFocus.Current == UiScreen.None && !ResultScreen.BlocksGameplay;
            Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !playing;
        }

        // Tests: a second local player in the same process (remote client harness) takes its own input, not the shared override.
        public void UseInput(IPlayerInput input) => _input = input;

        // An owner that returns after a reconnect (#166) gets the object after it already started as an observer.
        public override void OnOwnershipClient(FishNet.Connection.NetworkConnection prev) { if (!_owner) OnStartClient(); }

        public override void OnStopClient() => ReleaseOwnership();

        // Scene unloads destroy objects without the network callbacks.
        void OnDestroy() => ReleaseOwnership();

        void ReleaseOwnership()
        {
            if (!_owner) return;
            _owner = false;
            _combatant.ShotFired -= OnOwnShot;
            if (_stance != null) _stance.ParkourRejected -= OnParkourRejected;
            if (Local == this) Local = null;
            UiFocus.Close();
            if (_weaponVisual != null) _weaponVisual.ShowInHand();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Update()
        {
            if (!_owner) return;
            RemoveShake();
            var f = _input.Read();
            if (SettingsScreen.KeyCaptureActive) f = new PlayerInputFrame { SelectSlot = -1 }; // the key being rebound is not a command
            // Screens that take over the mouse: the keys that open and close them still work; over the inventory and
            // the map the movement keys keep working too, everything else is ignored (no look, aim or fire).
            if (f.Menu) UiFocus.Toggle(UiScreen.Menu);
            else if (f.Inventory && !_combatant.Dead.Value && !ResultScreen.BlocksGameplay) UiFocus.Toggle(UiScreen.Inventory);
            else if (f.Map && UiFocus.MapAvailable && !_combatant.Dead.Value) UiFocus.Toggle(UiScreen.Map);
            _jumpCancels = f.Jump; // a jump key still ends a use while a screen is open (ADR-144)
            f = UiFocus.GameplayInput(UiFocus.Current, f);
            if (ResultScreen.BlocksGameplay && !_combatant.Dead.Value) f = new PlayerInputFrame { SelectSlot = -1 };
            // In the elevator until the doors open: no moving, firing or picking up; the view and the screens work.
            if (SpawnProtection.InputBlocked || ElevatorPanel.Riding)
                f = new PlayerInputFrame { SelectSlot = -1, Look = f.Look, Menu = f.Menu, Inventory = f.Inventory, Map = f.Map, FreeLook = f.FreeLook, Ping = f.Ping };
            // An open wheel takes the mouse (ADR-130): its cursor moves, the view and the weapon stay still.
            if (RadialWheel.AnyOpen)
            {
                RadialWheel.FeedOpen(f.Look);
                f.Look = Vector2.zero;
                f.FirePressed = f.FireHeld = f.AimHeld = f.Ping = false;
            }
            // PUBG throwing (ADR-144): with the throwable in hand the fire button is the throw key (hold = arc, release =
            // throw) and no longer shoots or punches. G still throws from any slot.
            if (ThrowableInHand)
            {
                f.ThrowPressed |= f.FirePressed; f.ThrowHeld |= f.FireHeld; f.ThrowReleased |= f.FireReleased;
                f.FirePressed = f.FireHeld = false;
            }
            // Scopes slow the mouse by their zoom; the settings add aim/scope factors and the vertical invert.
            f.Look = GameSettings.AdjustLook(f.Look, _aim.Mode != AimMode.Hip, ScopeView.SensitivityFactor, ScopeView.CurrentZoom);
            LastInput = f;
            // Moving, firing or changing something ends an emote (ADR-133).
            if (_combatant.Emoting && (f.Move.sqrMagnitude > 0.01f || f.Jump || f.FireHeld || f.FirePressed || f.AimHeld || f.Crouch || f.Prone
                || f.Reload || f.Holster || f.Heal || f.ThrowPressed || f.SelectSlot >= 0)) _combatant.CmdEmote(0);
            UpdateCursor();
            _recoil.Tick(Time.deltaTime, _rules.Recoil.RecoverRate(_combatant.ActiveWeapon) * FittedMods.Recover, _rules.Recoil.RecoverDelaySeconds);

            if (_combatant.Dead.Value)
            {
                if (InParkour) AbortParkour();
                _aim.Update(false, Time.timeAsDouble, false);
                Lean = 0;
                BodyStance = Stance.Stand;
                Sprinting = false;
                FreeLooking = false;
                _autoRun = false;
                SlowWalking = false;
                _freeYaw = _freePitch = 0f;
                SendStance();
                SendBodyStance();
                SendSlowWalk();
                if (f.Restart) _combatant.CmdRequestRestart();
                PlaceCamera();
                return;
            }

            // Teams (ADR-072): knocked down = crawl only (lying, no weapon, no jumping, sprinting or leaning);
            // reviving a teammate = kneel still. F held stays in the frame for the revive (TeamHud).
            bool knocked = _combatant.Knocked.Value;
            bool carrying = _combatant.IsCarrying;
            if (knocked || _combatant.Reviving || carrying)
            {
                if (InParkour) AbortParkour();
                f.Sprint = f.Jump = f.Crouch = f.Prone = f.AimHeld = f.FireHeld = f.FirePressed = f.LeanLeft = f.LeanRight = false;
                f.Interact = f.Reload = f.Heal = f.Boost = f.GasCover = f.Inspect = f.Holster = f.FireMode = f.CycleThrowable = f.AutoRun = false;
                f.ThrowHeld = f.ThrowPressed = f.ThrowReleased = false;
                f.SelectSlot = -1;
                // Carried on a teammate's back (ADR-129): no crawling of its own, it rides where the carrier goes.
                if (!knocked && !carrying || _combatant.BeingCarried) f.Move = Vector2.zero;
                _autoRun = false;
                LastInput = f;
                if (_combatant.TryCarryPose(out var carriedAt, out float carrierYaw))
                {
                    Teleport(carriedAt);
                    Yaw = carrierYaw;
                }
            }

            // Aiming needs a gun (not a melee weapon, ADR-073) and wins over sprinting; leaning is for standing and walking only.
            bool armed = !Melee.CanStrike(_combatant.ActiveWeapon);
            _aim.SightsHold = GameSettings.SightsHold;
            _aim.ShoulderToggle = GameSettings.ShoulderToggle;
            var aim = Molotov.LimitAim(_aim.Update(f.AimHeld, Time.timeAsDouble, armed), _combatant.Burning.Value); // burning: no steady aim (ADR-110)
            // The aim key raises the weapon on the press, before the tap/hold decision (#121): no sprint under it either.
            AimKeyDown = armed && f.AimHeld;
            SendLookPitch();

            FollowVehicleHeading();

            // Free look turns only the view, and only from the hip: the character keeps facing and moving where it
            // did while the camera orbits all the way round. Letting go swings the view back the short way.
            FreeLooking = f.FreeLook && aim == AimMode.Hip;
            if (FreeLooking)
            {
                _freeYaw = Locomotion.WrapFreeYaw(_freeYaw, f.Look.x);
                _freePitch = Mathf.Clamp(_freePitch + f.Look.y, -PitchLimit - Pitch, PitchLimit - Pitch);
            }
            else
            {
                var freeLook = FreeLookRules.Current;
                _freeYaw = Locomotion.FreeLookReturn(_freeYaw, Time.deltaTime, freeLook);
                _freePitch = Locomotion.FreeLookReturn(_freePitch, Time.deltaTime, freeLook);
                Yaw += f.Look.x;
                Pitch = Mathf.Clamp(Pitch + f.Look.y, -PitchLimit, PitchLimit);
            }
            // On a ledge the body keeps facing the wall and the view turns only so far from it.
            if (OnLedge) Yaw = _hangYaw + Mathf.Clamp(Mathf.DeltaAngle(_hangYaw, Yaw), -ParkourRules.Current.HangLookDeg, ParkourRules.Current.HangLookDeg);
            // Seated, the vehicle sets the body's rotation (tilted with it): the eyes and shots go by that.
            if (!VehicleSeat.IsAttached(_combatant)) transform.rotation = Quaternion.Euler(0f, OnLedge ? _hangYaw : Yaw, 0f);

            // Auto-run: = toggles it; a fresh W or S, aiming, a screen or anything else taking the body ends it.
            if (f.AutoRun)
            {
                _autoRun = !_autoRun;
            }
            bool freshForwardBack = Mathf.Abs(f.Move.y) > 0.1f && Mathf.Abs(_lastMoveY) <= 0.1f;
            _lastMoveY = f.Move.y;
            if (freshForwardBack || aim != AimMode.Hip || UiFocus.Current == UiScreen.Menu || SpawnProtection.InputBlocked
                || ExternalControl || ResultScreen.BlocksGameplay) _autoRun = false;
            float inputY = f.Move.y;
            if (_autoRun) f.Move.y = 1f;

            if (InParkour)
            {
                if (ExternalControl) AbortParkour();
                else
                {
                    // The body is on its scripted path: no walking, jumping, stance changes or firing meanwhile.
                    TickParkour(f);
                    Sprinting = false;
                    SendStance();
                    SendBodyStance();
                    SendSlowWalk();
                    if (f.ToggleView) Rig.ToggleMode();
                    PlaceCamera();
                    NearbyLoot = FindNearbyLoot();
                    return;
                }
            }

            // Sprinting is forward only, and neither aiming nor firing goes with it: pulling the trigger or
            // raising the weapon drops back to a walk, so no shot ever leaves a sprint. Slow walking never sprints.
            bool firing = armed && (f.FireHeld || f.FirePressed);
            SlowWalking = f.SlowWalk && !ExternalControl;
            // Healing is walk-only, and a jump ends it (ADR-099).
            bool healing = CombatantHealing.IsUsing(_combatant);
            if (healing && (f.Jump || _jumpCancels)) GetComponent<CombatantHealing>().RequestCancel();
            bool canSprint = AutoRunRules.CanSprint(aim == AimMode.Hip, AimKeyDown, firing, SlowWalking, ExternalControl, healing);
            // Auto-run is always a sprint (ADR-350): when a sprint is not allowed it ends instead of walking on.
            if (_autoRun && !canSprint) { _autoRun = false; f.Move.y = inputY; }
            bool sprint = (f.Sprint || _autoRun) && f.Move.y > 0.1f && canSprint;

            // Space in front of an obstacle vaults or climbs instead of jumping (standing or crouched, on the ground).
            if (f.Jump && !ExternalControl && _stance != null && _controller.isGrounded && BodyStance != Stance.Prone && TryStartParkour())
            {
                Sprinting = false;
                SendSlowWalk();
                PlaceCamera();
                return;
            }

            // C toggles the crouch and Z lying down; jumping or sprinting gets up instead, but not the sprint that was
            // held when C or Z took the body down (#105: C mid-run crouch-walks on until Shift or W is pressed again).
            // Rising (lying to crouching, anything to standing) needs room overhead for the taller capsule.
            bool wasStanding = BodyStance == Stance.Stand;
            var stanceRules = StanceRules.Current;
            if (!sprint) _sprintOverruled = false;
            else if (f.Crouch || f.Prone) _sprintOverruled = true;
            if (_stance == null) BodyStance = Stance.Stand;
            else if (!ExternalControl)
            {
                var next = Stances.Next(BodyStance, f.Crouch, f.Prone, f.Jump, sprint && !_sprintOverruled, true, stanceRules);
                if (Stances.Rises(BodyStance, next, stanceRules) && !_stance.CanReach(stanceRules.For(next).CapsuleHeight)) next = BodyStance;
                BodyStance = next;
            }
            if (knocked && _stance != null) BodyStance = Stance.Prone;
            // Deep water (G12, ADR-112): swim lying on the surface; out of it the body stands again.
            float waterDepth = WaterVolume.DepthAt(transform.position, out float waterSurface, out float waterFloor);
            bool wasSwimming = _swimming;
            _waterSurface = waterSurface;
            _swimming = !ExternalControl && !knocked && !SpawnProtection.InputBlocked && !ElevatorPanel.Riding && !ElevatorCar.Carries(transform.position)
                && Swim.Swimming(_swimming, waterDepth, SwimRules.Current); // a car below the sea level is dry (it swam, prone, in the shaft)
            if (_swimming) BodyStance = Stance.Prone;
            else if (wasSwimming && BodyStance == Stance.Prone) BodyStance = Stance.Stand;
            bool jump = f.Jump && wasStanding && !ExternalControl; // out of a crouch the key only stands up
            // Crouched, Shift sprints crouched (ADR-095), except the sprint held when C took the body down (#105).
            sprint &= BodyStance == Stance.Stand || (BodyStance == Stance.Crouch && !_sprintOverruled);
            Sprinting = sprint;
            Lean = sprint ? 0 : (f.LeanRight ? 1 : 0) - (f.LeanLeft ? 1 : 0);
            SendStance();
            SendBodyStance();
            SendSlowWalk();

            if (ExternalControl)
            {
                // Whatever carries the body moves it; ours stays put and nothing it does is a fall.
                _verticalVelocity = 0f;
                _fall.Reset();
            }
            // Riding to another region (ADR-076/079): the feet stay on the moving car floor, departure then arrival car.
            else if (ElevatorPanel.TryHold(transform.position, out var rideAt, out bool rideArrived, out float rideYaw))
            {
                Teleport(rideAt);
                if (rideArrived) LookAt(rideAt + Quaternion.Euler(0f, rideYaw, 0f) * Vector3.forward * 20f + Vector3.up * 1.6f);
            }
            // Riding the elevator car up (ADR-065 (1)): the car carries the feet, nothing else moves the body.
            else if (SpawnProtection.InputBlocked && ElevatorCar.TryRideFloor(transform.position, out float rideFeet))
                Teleport(new Vector3(transform.position.x, rideFeet, transform.position.z));
            else if (_swimming)
            {
                // Swimming (ADR-112): about 46% of the sprint speed, floating to the surface (Ctrl held: diving), no jumping.
                var swim = SwimRules.Current;
                float swimFactor = Swim.Speed(_locomotion.SprintSpeed, swim) / _locomotion.WalkSpeed;
                Locomotion.PlanarVelocity(f.Move.x, f.Move.y, Yaw, false, _locomotion, out var swx, out var swz, swimFactor);
                if (_moveFrame != Time.frameCount - 1) _moveX = _moveZ = 0f;
                _moveFrame = Time.frameCount;
                Locomotion.Accelerate(ref _moveX, ref _moveZ, swx, swz, Time.deltaTime, _locomotion);
                float eyeAboveFeet = Stances.EyeHeight(_combatant.UprightEyeHeight, Stance.Prone, stanceRules);
                float targetFeet = Swim.TargetFeetY(waterSurface, waterFloor, eyeAboveFeet, SlowWalking, swim);
                float rise = Mathf.Clamp((targetFeet - transform.position.y) / Mathf.Max(Time.deltaTime, 1e-3f), -swim.VerticalSpeed, swim.VerticalSpeed);
                _verticalVelocity = 0f;
                _fall.Reset();
                _controller.Move(new Vector3(_moveX, rise, _moveZ) * Time.deltaTime);
            }
            else
            {
                // A long gun slows walking and sprinting by its kind (ADR-095); the other factors slow it further.
                // Slow walking replaces the weapon's factor with its own share of the walking speed.
                float speedFactor = Locomotion.MoveSpeedFactor(_rules.Aim.For(aim).MoveFactor, Lean != 0 ? _rules.Lean.MoveFactor : 1f,
                    stanceRules.For(BodyStance).MoveFactor, armed ? Locomotion.CarryFactor(_combatant.ActiveWeapon, _locomotion) : 1f, _locomotion, SlowWalking);
                // Slower while a bandage or kit is being used.
                speedFactor *= CombatantHealing.MoveFactorOf(_combatant) * CombatantHealing.BoostSpeedOf(_combatant);
                speedFactor *= Combatant.StaggerMoveFactorOf(_combatant); // stopping power (ADR-182, ADR-240)
                if (knocked) speedFactor *= TeamRules.Current.KnockedMoveFactor;
                if (carrying) speedFactor *= TeamRules.Current.CarryMoveFactor;
                Locomotion.PlanarVelocity(f.Move.x, f.Move.y, Yaw, sprint, _locomotion, out var vx, out var vz, speedFactor);
                // Coming from anything else (a seat, a ride, a vault) the body starts from standing.
                if (_moveFrame != Time.frameCount - 1) _moveX = _moveZ = 0f;
                _moveFrame = Time.frameCount;
                Locomotion.Accelerate(ref _moveX, ref _moveZ, vx, vz, Time.deltaTime, _locomotion);
                vx = _moveX;
                vz = _moveZ;
                _verticalVelocity = Locomotion.VerticalVelocity(_verticalVelocity, _controller.isGrounded, jump, Time.deltaTime, _locomotion);
                _jumpedThisAir |= jump;
                _controller.Move(new Vector3(vx, _verticalVelocity, vz) * Time.deltaTime);
                ReportFall();
                if (_controller.isGrounded) _jumpedThisAir = _ledgeLetGo = false;
                else if (!knocked && TryGrabLedge((f.Jump && !jump) || f.JumpHeld)) // a tap that takes off is a jump; the hang then catches (ADR-087)
                {
                    PlaceCamera();
                    return;
                }
            }

            if (f.ToggleView) Rig.ToggleMode();
            PlaceCamera();

            NearbyLoot = FindNearbyLoot();
            // Next to a knocked teammate F revives instead (ADR-072).
            // In a station F is the elevator's (ADR-076): tap = next destination, held = ride.
            bool elevatorKey = ElevatorPanel.HandleInteract(_combatant, f.Interact, f.InteractHeld, Time.deltaTime);
            // A door in reach and in view opens or shuts (G11, ADR-104); otherwise F picks up.
            // F held next to a landed supply crate opens it (#144, ADR-113); a tap still picks up.
            bool supplyKey = !elevatorKey && SupplyView.HandleInteract(_combatant, f.InteractHeld, Time.deltaTime);
            // One press, one target (ADR-276): the pickup, unless a door lies clearly nearer the middle of the view.
            var look = Camera != null ? Camera.transform.forward : transform.forward;
            // On the waiting island F at the bungee lift's call button calls it (ADR-490); it comes before loot and doors there.
            bool liftTop = false;
            LiftCallInReach = !elevatorKey && !supplyKey && WaitingHud.CallInReach(_combatant, look, out liftTop);
            if (LiftCallInReach) NearbyLoot = null;
            bool liftKey = LiftCallInReach && f.Interact && WaitingHud.Call(_combatant, liftTop);
            float doorBearing = DoorSystem.Bearing(_combatant.EyePosition, look);
            bool doorFirst = doorBearing != float.MaxValue && (NearbyLoot == null || !Interact.PickupBeatsDoor(PickupList.Instance != null ? PickupList.Instance.CurrentBearing : 0f, doorBearing, InteractRules.Current));
            if (doorFirst) NearbyLoot = null; // the prompt and list show what F will do
            PickupList.DoorFirst = doorFirst && doorBearing != float.MaxValue;
            bool doorKey = !elevatorKey && !supplyKey && !LiftCallInReach && f.Interact && doorFirst && TeamHud.ReviveCandidate(_combatant) == null
                && DoorSystem.TryInteract(_combatant, _combatant.EyePosition, look);
            if (!elevatorKey && !supplyKey && !doorKey && f.Interact && NearbyLoot != null && TeamHud.ReviveCandidate(_combatant) == null) PickupList.Take(_combatant, NearbyLoot);
            // The hand reaches for the door or the loot (#212, own view only).
            if (doorKey || liftKey || (f.Interact && !elevatorKey && !supplyKey && NearbyLoot != null))
                if (TryGetComponent(out CombatantAnimation reachAnim)) reachAnim.StartGesture(doorKey || liftKey ? HandGesture.Door : HandGesture.Pickup);
            if (f.SelectSlot >= 0) _combatant.CmdSelectSlot(f.SelectSlot);
            if (f.Reload && !ThrowableInHand && !(_throwView != null && _throwView.Aiming)) _combatant.CmdReload(); // R pulls the pin then (ThrowableView)
            if (f.Holster) SetHolstered(!_combatant.Holstered.Value);
            if (f.FireMode) _combatant.CmdToggleFireMode();
            if (f.Heal) _combatant.CmdUseMedkit();
            if (f.Boost) GetComponent<CombatantHealing>()?.RequestBoost();
            if (f.GasCover) GetComponent<KeyHolder>()?.RequestGasCover();
            if (f.Inspect && TryGetComponent(out CombatantAnimation inspectAnim)) inspectAnim.StartInspect();
            if (f.CycleThrowable) GetComponent<ThrowableUser>()?.RequestCycle();
            if (f.Ping) PingAtCrosshair();
            // No shooting (or punching) while free looking: the camera is not where the weapon points (PUBG-like).
            if (!FreeLooking) TryFire(f);
        }

        // ---- Parkour (ADR-065 (4)) ----

        // Measures what is ahead and, when it is something to get over, starts the move. False: Space jumps (or
        // stands up) as usual.
        bool TryStartParkour()
        {
            var plan = ParkourProbe.Plan(transform, _controller.radius, _controller.height, Yaw, BodyStance == Stance.Crouch, ParkourRules.Current);
            LastParkourPlan = plan;
            if (!Parkour.IsMotion(plan.Move)) return false;
            _parkour = plan;
            _parkourT = 0f;
            _parkourBack = false;
            _autoRun = false;
            // The body goes up and over on its path, through the edge of the obstacle: no collisions meanwhile.
            _controller.enabled = false;
            _verticalVelocity = 0f;
            int glass = plan.Glass != null ? plan.Glass.Id : -1;
            if (IsServerInitialized) _stance.ServerStartParkour((int)plan.Move, plan.Start, plan.End, glass);
            else _stance.CmdStartParkour((int)plan.Move, plan.Start, plan.End, glass);
            return true;
        }

        // In the air facing a high edge the hands can reach (#115): catch it and hang. After a jump, or when falling
        // past it; standing only (no weapon fire, no stance change while hanging). With Space down (`key`), an edge the
        // hands reach is mantled onto at once, whatever is below (ADR-492).
        bool TryGrabLedge(bool key)
        {
            var rules = ParkourRules.Current;
            if (_stance == null || _ledgeLetGo || _combatant.Reviving || BodyStance != Stance.Stand) return false;
            var plan = key ? ParkourProbe.PlanMantle(transform, _controller.radius, _controller.height, Yaw, rules) : default;
            if (plan.Move != ParkourMove.LedgeClimb)
            {
                if (!(_jumpedThisAir || _verticalVelocity < -rules.LedgeFallSpeed)) return false;
                plan = ParkourProbe.PlanLedge(transform, _controller.radius, _controller.height, Yaw, rules);
                if (plan.Move != ParkourMove.Hang) return false;
            }
            LastParkourPlan = plan;
            _parkour = plan;
            _parkourT = 0f;
            _parkourBack = false;
            _autoRun = false;
            _hangYaw = Yaw;
            _controller.enabled = false;
            _verticalVelocity = 0f;
            // Hanging: the body holds under the edge. Mantling: it starts against the wall and climbs from there.
            transform.position = plan.Move == ParkourMove.Hang ? plan.End : plan.Start;
            if (IsServerInitialized) _stance.ServerStartParkour((int)plan.Move, plan.Start, plan.End, -1);
            else _stance.CmdStartParkour((int)plan.Move, plan.Start, plan.End, -1);
            return true;
        }

        // Hanging: Space pulls up onto the top, S lets go and drops.
        void TickHang(in PlayerInputFrame f)
        {
            transform.position = _parkour.End;
            if (f.Jump)
            {
                var hang = _parkour;
                _parkour = new ParkourPlan
                {
                    Move = ParkourMove.LedgeClimb, Profile = hang.Profile, Start = hang.End, End = hang.ClimbTo, TopY = hang.TopY,
                    Seconds = hang.Seconds, ClimbTo = hang.ClimbTo, Face = hang.Face,
                };
                _parkourT = 0f;
                if (IsServerInitialized) _stance.ServerStartParkour((int)ParkourMove.LedgeClimb, _parkour.Start, _parkour.End, -1);
                else _stance.CmdStartParkour((int)ParkourMove.LedgeClimb, _parkour.Start, _parkour.End, -1);
            }
            else if (f.Move.y < -0.5f)
            {
                _ledgeLetGo = true;
                AbortParkour();
            }
        }

        // One frame along the path. S in the first part of the move runs it back to the start.
        void TickParkour(in PlayerInputFrame f)
        {
            if (_parkour.Move == ParkourMove.Hang)
            {
                TickHang(f);
                return;
            }
            var rules = ParkourRules.Current;
            // A pull-up from a ledge is not taken back: back at the start is hanging in the air.
            if (_parkour.Move != ParkourMove.LedgeClimb && !_parkourBack && f.Move.y < -0.5f && Parkour.CanCancel(_parkourT, rules)) _parkourBack = true;
            float step = Time.deltaTime / Mathf.Max(0.05f, _parkour.Seconds);
            _parkourT += _parkourBack ? -step : step;
            bool done = _parkourBack ? _parkourT <= 0f : _parkourT >= 1f;
            _parkourT = Mathf.Clamp01(_parkourT);
            transform.position = ParkourPosition(_parkourT);
            if (done) FinishParkour();
        }

        Vector3 ParkourPosition(float t)
        {
            var p = _parkour;
            Parkour.PathPoint(p.Move, t, p.Start.y, p.End.y, p.TopY, ParkourRules.Current.LiftMeters, out float along, out float height);
            var flat = Vector3.Lerp(p.Start, p.End, along);
            return new Vector3(flat.x, height, flat.z);
        }

        // Going through a window the head ducks under the lintel: the eyes drop by what the body would otherwise
        // poke above the opening, most at the middle of the move.
        float ParkourEyeDrop()
        {
            if (_parkour.Move != ParkourMove.Window) return 0f;
            var profile = _parkour.Profile;
            float eyeOverFeet = _combatant.EyePosition.y - transform.position.y;
            float poke = profile.TopHeight + ParkourRules.Current.LiftMeters + eyeOverFeet + 0.15f - (profile.TopHeight + profile.OpeningHeight);
            return Mathf.Max(0f, poke) * Mathf.Sin(Mathf.PI * _parkourT);
        }

        void FinishParkour()
        {
            bool cancelled = _parkourBack;
            LastParkourTakenBack = cancelled;
            transform.position = cancelled ? _parkour.Start : _parkour.End;
            EndParkourLocally();
            if (IsServerInitialized) _stance.ServerEndParkour(transform.position, cancelled);
            else _stance.CmdEndParkour(transform.position, cancelled);
        }

        // Something else takes over in the middle of a move (death, a vehicle, a teleport): stop where the body is.
        void AbortParkour()
        {
            EndParkourLocally();
            if (IsServerInitialized) _stance.ServerStopParkour();
            else _stance.CmdStopParkour();
        }

        void EndParkourLocally()
        {
            _parkour = default;
            _parkourT = 0f;
            _parkourBack = false;
            _controller.enabled = true;
            _verticalVelocity = 0f;
            _fall.Reset(); // the path is not a fall; dropping off the far side afterwards is measured from here
        }

        // The server did not believe the move: back to where it started.
        void OnParkourRejected(Vector3 back)
        {
            if (InParkour) EndParkourLocally();
            Teleport(back);
        }

        // Tells the server about slow walking when it changes, and again if its copy still disagrees a moment later.
        // The look pitch for the third-person weapon hold (#121): only when it moved a little, at most ten times a second.
        void SendLookPitch()
        {
            if (_stance == null || Mathf.Abs(Pitch - _sentPitch) < 1.5f || Time.time - _pitchSentAt < 0.1f) return;
            _sentPitch = Pitch;
            _pitchSentAt = Time.time;
            if (IsServerInitialized) _stance.ServerSetLookPitch(Pitch);
            else _stance.CmdSetLookPitch(Pitch);
        }

        void SendSlowWalk()
        {
            if (_stance == null) return;
            bool changed = SlowWalking != _sentSlowWalk;
            bool stale = SlowWalking != _stance.SlowWalk.Value && Time.time - _slowWalkSentAt > StanceResendSeconds;
            if (!changed && !stale) return;
            _sentSlowWalk = SlowWalking;
            _slowWalkSentAt = Time.time;
            if (IsServerInitialized) _stance.ServerSetSlowWalk(SlowWalking);
            else _stance.CmdSetSlowWalk(SlowWalking);
        }

        // The host is the server and acts at once; a client asks.
        void SetHolstered(bool holstered)
        {
            if (IsServerInitialized) _combatant.ServerSetHolstered(holstered);
            else _combatant.CmdSetHolstered(holstered);
        }

        // Tells the server when the stance changes, and again if the server's copy still disagrees a moment later
        // (it may have refused or reset it), so the two cannot drift apart for good.
        void SendStance()
        {
            int aim = (int)_aim.Mode;
            bool changed = aim != _sentAim || Lean != _sentLean;
            bool stale = (aim != _combatant.Aim.Value || Lean != _combatant.Lean.Value) && Time.time - _stanceSentAt > StanceResendSeconds;
            if (!changed && !stale) return;
            _sentAim = aim;
            _sentLean = Lean;
            _stanceSentAt = Time.time;
            _combatant.CmdSetStance(aim, Lean);
        }

        // The same for crouching and standing up. The host is the server and sets it without a request.
        void SendBodyStance()
        {
            if (_stance == null) return;
            int stance = (int)BodyStance;
            bool changed = stance != _sentBodyStance;
            bool stale = stance != _stance.State.Value && Time.time - _bodyStanceSentAt > StanceResendSeconds;
            if (!changed && !stale) return;
            _sentBodyStance = stance;
            _bodyStanceSentAt = Time.time;
            if (IsServerInitialized) _stance.ServerSetStance(BodyStance);
            else _stance.CmdSetStance(stance);
        }

        // Movement is ours (ADR-029), so we measure our own drops and tell the server about the ones that can
        // hurt; it checks the number against what it saw before applying damage.
        void ReportFall()
        {
            float dropped = _fall.Tick(_controller.isGrounded, transform.position.y);
            if (dropped > 0f) _shake.Add(CameraShake.Landing(dropped));
            if (_stance == null || dropped <= FallRules.Current.SafeMeters) return;
            if (IsServerInitialized) _stance.ServerReportFall(dropped);
            else _stance.CmdReportFall(dropped);
        }

        // ADR-100: what the grips and stock on the weapon in hand do to its recoil.
        AttachMods FittedMods => (_fitted != null ? _fitted : (_fitted = GetComponent<CombatantOptics>()))?.ActiveMods ?? AttachMods.None;
        CombatantOptics _fitted;

        // Our own shots kick the view; the kicked view is what the next shot aims with.
        void OnOwnShot(Vector3 origin, Vector3 end)
        {
            var weapon = _combatant.ActiveWeapon;
            if (weapon == null || !_ownShots.IsNewShot(Time.timeAsDouble)) return;
            // The weapon's pattern (ADR-089), seeded per match and player so a replayed match kicks the same way.
            float factor = _rules.Aim.For(_aim.Mode).RecoilFactor * _rules.Recoil.StanceFactor(BodyStance);
            RecoilPattern.Kick(_recoil, weapon, _recoilRng, factor, _rules.Recoil, FittedMods);
            _shake.Add(CameraShake.OwnShot(weapon.Damage, weapon.Pellets) * factor);
        }

        // Adds camera trauma to the local player's view (a hit taken, a punch).
        public static void Shake(float trauma)
        {
            if (Local != null) Local._shake.Add(trauma);
        }

        // A blast somewhere: the local view shakes by how near it is (ADR-471). `scale` 1 = a frag grenade.
        public static void ShakeFromBlast(Vector3 centre, float scale)
        {
            if (Local == null) return;
            var from = Local.Camera != null ? Local.Camera.transform.position : Local.transform.position;
            Local._shake.Add(CameraShake.Blast(Vector3.Distance(from, centre), scale));
        }

        public float ShakeTrauma => _shake.Trauma;

        void RemoveShake()
        {
            if (!_shakeOn) return;
            _shakeOn = false;
            if (Camera != null) Camera.transform.rotation *= Quaternion.Inverse(_shakeApplied);
        }

        // After every Update has placed the camera and aimed from it.
        void LateUpdate()
        {
            if (!_owner || Camera == null) return;
            _shake.Tick(Time.deltaTime);
            if (_shake.Trauma <= 0f) return;
            _shake.Offset(Time.timeAsDouble, GameSettings.CameraShake, out float pitch, out float yaw, out float roll);
            _shakeApplied = Quaternion.Euler(-pitch, yaw, roll);
            Camera.transform.rotation *= _shakeApplied;
            _shakeOn = true;
        }

        // ADR-090: a hit on this player throws the view; a vest makes it smaller. Called once per shot that lands.
        public void TakeAimPunch()
        {
            if (_recoilRng == null) return;
            var gear = GetComponent<CombatantGear>();
            RecoilPattern.Punch(_recoil, _recoilRng, gear != null ? gear.VestTier.Value : 0, _rules.Recoil);
        }

        bool _jumpCancels;
        bool ThrowableInHand => _combatant.ActiveSlot.Value == Loadout.ThrowableSlot && !_combatant.Holstered.Value;

        void TryFire(in PlayerInputFrame f)
        {
            double now = Time.timeAsDouble;
            var fire = _rules.Fire;
            // A click that comes a moment too soon is remembered, so quick semi-automatic fire does not swallow clicks.
            if (f.FirePressed) _clickUntil = now + fire.ClickBufferSeconds;
            // Nothing in hand (none carried, or put away with X): the button punches instead (ADR-059 (1)); a melee
            // weapon in hand swings the same way (ADR-073).
            if (Melee.CanStrike(_combatant.ActiveWeapon))
            {
                TryPunch(f, now);
                return;
            }
            // No shooting from the water (ADR-112).
            if (_swimming) { _burstLeft = 0; return; }
            var weapon = _combatant.ActiveWeapon;
            // A shell-by-shell reload gives way to a shot (ADR-089).
            if (weapon == null || (_combatant.Reloading.Value && !weapon.ReloadsByShell) || _combatant.ActiveMag <= 0)
            {
                // A trigger pulled on an empty magazine clicks (#288, own ears: the press is not sent anywhere).
                if (f.FirePressed && weapon != null && _combatant.ActiveMag <= 0 && !_combatant.Reloading.Value && TryGetComponent(out CombatantAudio dry)) dry.PlayDryFire();
                _burstLeft = 0;
                return;
            }
            // Automatic weapons set to single shots (B) fire once per click, and no faster than the server allows;
            // in burst mode a click fires the whole burst.
            var mode = _combatant.ActiveFireMode;
            if (!(mode == FireMode.Auto ? f.FireHeld : _burstLeft > 0 || now <= _clickUntil) || !_fireCadence.Ready(now)) return;
            if (_burstLeft <= 0) { _clickUntil = -1; _burstLeft = mode == FireMode.Burst ? weapon.Burst : 1; }
            int burstShot = mode == FireMode.Burst ? weapon.Burst - _burstLeft + 1 : 1;
            _burstLeft--;
            double interval = Loadout.IntervalAfter(weapon, mode, burstShot, _rules.Slots.SingleModeMinInterval);
            // Scheduled from when the shot was due, not from this frame: a frame always lands a little late, and
            // adding the interval to that would make the rhythm follow the frame times and run slow.
            _fireCadence.Fired(now, interval, fire.ClientCatchUp(interval, Time.deltaTime));
            var direction = AimDirection();
            // The host is the server: fire now instead of waiting for the next network tick to carry the request.
            if (IsServerInitialized) _combatant.ServerFire(direction);
            else _combatant.CmdFire(direction, _combatant.ViewTick); // what the shooter was looking at (#162)
        }

        // Where a shot or a punch goes from the eyes. While the view is turned away in free look it goes where the
        // character faces, not where the camera does.
        Vector3 AimDirection()
        {
            var eye = _combatant.EyePosition;
            bool viewTurned = _freeYaw != 0f || _freePitch != 0f;
            var aim = Camera != null && !viewTurned
                ? Rig.AimPoint(Camera.transform, transform, AimDistance)
                : eye + Quaternion.Euler(-(Pitch + _recoil.Pitch), Yaw + _recoil.Yaw, 0f) * Vector3.forward * AimDistance;
            return (aim - eye).normalized;
        }

        // Held or clicked, at most one punch (or swing) per interval; the server keeps the same limit.
        void TryPunch(in PlayerInputFrame f, double now)
        {
            if (!(f.FireHeld || now <= _clickUntil) || !_punchCadence.Ready(now)) return;
            _clickUntil = -1;
            double interval = Melee.StrikeOf(_combatant.ActiveWeapon, MeleeRules.Current).Interval;
            _punchCadence.Fired(now, interval, _rules.Fire.ClientCatchUp(interval, Time.deltaTime));
            var direction = AimDirection();
            if (IsServerInitialized) _combatant.ServerPunch(direction);
            else _combatant.CmdPunch(direction);
        }

        // The ride view's spot toward `side` from the head, stopping short of walls, door and ceiling.
        Vector3 RideCameraSpot(Vector3 head, Vector3 side)
        {
            var offset = side * RideCameraForward + Vector3.up * (RideCameraHeight - 1.6f);
            float reach = offset.magnitude;
            var dir = offset / reach;
            var hits = Physics.SphereCastAll(head, 0.12f, dir, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
                if (hit.distance > 0f && !hit.transform.IsChildOf(transform)) reach = Mathf.Min(reach, Mathf.Max(0.05f, hit.distance - 0.05f));
            return head + dir * reach;
        }

        void PlaceCamera()
        {
            if (Camera == null) return;
            var aimRules = _rules.Aim;
            StaggerWander.Offset(Combatant.StaggerShakeDegOf(_combatant), Time.timeAsDouble, out float wanderPitch, out float wanderYaw);
            var view = new ViewState
            {
                Aim = _aim.Mode,
                Lean = _combatant.LeanAmount,
                LeanRollDeg = Rig.IsEyeView(_aim.Mode) ? _rules.Lean.EyeViewRollDeg : _rules.Lean.ThirdPersonRollDeg,
                RecoilPitch = Mathf.Min(_recoil.Pitch + ScopeControl.SwayPitch + wanderPitch, MaxViewPitch - ViewPitch), // never past straight up
                RecoilYaw = _recoil.Yaw + ScopeControl.SwayYaw + wanderYaw,
                ShoulderAimDistance = aimRules.ShoulderCameraDistance,
            };
            var eye = _combatant.EyePosition + Vector3.down * ParkourEyeDrop();
            Rig.Place(Camera.transform, transform, eye, ViewYaw, ViewPitch, view, Time.deltaTime);
            // In a riding elevator car the chase camera is pushed into the shut car and shows a wall: the view looks
            // down from the door corner of the car at the rider instead (input is blocked during the ride anyway).
            if (ElevatorPanel.Riding && !Rig.IsEyeView(_aim.Mode))
            {
                var feet = transform.position;
                var forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                // From the head toward that corner, stopping short of the car's walls, door and ceiling: a rider standing
                // by the door put the camera outside the shut door.
                var head = feet + Vector3.up * 1.6f;
                var spot = RideCameraSpot(head, forward);
                // Pressed against the door: the far corner of the car instead.
                if ((spot - head).sqrMagnitude < 0.45f * 0.45f) { var back = RideCameraSpot(head, -forward); if ((back - head).sqrMagnitude > (spot - head).sqrMagnitude) spot = back; }
                Camera.transform.position = spot;
                Camera.transform.rotation = Quaternion.LookRotation(feet + Vector3.up * 1.0f - Camera.transform.position);
            }
            if (_swimming)
            {
                // The chase camera follows the head: over the water while the head is up, under it while diving (#184).
                var at = Camera.transform.position;
                at.y = Swim.HeadUnder(eye.y, _waterSurface, SwimRules.Current) ? Mathf.Min(at.y, _waterSurface - 0.1f) : Mathf.Max(at.y, _waterSurface + 0.12f);
                Camera.transform.position = at;
            }
            float ease = 1f - Mathf.Exp(-aimRules.FovChangePerSecond * Time.deltaTime);
            float fovTarget = ScopeView.FovOverride > 0f ? ScopeView.FovOverride
                : _aim.Mode == AimMode.Hip && Rig.Mode == ViewMode.FirstPerson ? GameSettings.FirstPersonFov
                : aimRules.For(_aim.Mode).Fov;
            Camera.fieldOfView = Mathf.Lerp(Camera.fieldOfView, fovTarget + BungeeRide.ExtraFov, ease);

            // Hide our own body whenever the camera sits at the eyes so it does not block the view; the weapon
            // moves in front of the camera instead.
            bool hide = Rig.IsEyeView(_aim.Mode);
            if (_weaponVisual != null)
            {
                // The dead hold nothing up: the weapon stays with the body.
                if (hide && !_combatant.Dead.Value) _weaponVisual.ShowAtEye(Camera.transform, _aim.Mode == AimMode.Sights);
                else _weaponVisual.ShowInHand();
            }
            // Seated with empty hands, only the head goes (its shadow stays): the own arms on the wheel or the bars
            // and the legs show in the eye view.
            int shown = !hide ? 0 : VehicleSeat.IsAttached(_combatant) && _combatant.ActiveWeaponId == 0 ? 2 : 1;
            // Every frame, not only on a change: something turned the body mesh back on in the crouched eye view (the camera sat inside it).
            if (shown == 1) foreach (var r in _bodyRenderers) if (r != null) { r.enabled = false; r.forceRenderingOff = true; }
            if (shown == _bodyShown) return;
            _bodyShown = shown;
            foreach (var r in _bodyRenderers)
            {
                if (r == null) continue;
                bool head = r.name.Contains("Head");
                r.enabled = shown != 1;
                r.forceRenderingOff = false;
                r.shadowCastingMode = shown == 2 && head ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            // #179: the worn helmet and vest go with the body (the eye sits inside the helmet).
            var armor = GetComponent<ArmorVisual>();
            if (armor != null) armor.SetVisible(shown == 0);
        }

        // Seated, the view turns with the vehicle (the mouse looks around relative to its heading), and getting in
        // faces the view the vehicle's way: the third-person camera starts behind it and stays there through turns.
        bool _inVehicle;
        float _vehicleHeading;
        const float EnterPitchCarDeg = 8f, EnterPitchBikeDeg = 12f;

        void FollowVehicleHeading()
        {
            var vehicle = VehicleSeat.VehicleOf(_combatant);
            if (vehicle == null) { _inVehicle = false; return; }
            var forward = vehicle.transform.forward;
            if (new Vector2(forward.x, forward.z).sqrMagnitude < 1e-4f) return; // nose straight up or down: keep the view
            float heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            if (!_inVehicle)
            {
                Yaw = heading;
                Pitch = vehicle.IsMotorcycle ? -EnterPitchBikeDeg : -EnterPitchCarDeg; // a little down: the bars / dash in view
            }
            else Yaw += Mathf.DeltaAngle(_vehicleHeading, heading);
            _inVehicle = true;
            _vehicleHeading = heading;
        }

        readonly List<LootItem> _sightItems = new List<LootItem>();
        readonly List<float> _sightBearings = new List<float>();

        // The pickup F takes (ADR-276): in reach, on this storey, in front and in sight; the one nearest the middle of the view.
        LootItem FindNearbyLoot()
        {
            PickupSight.Gather(_combatant, Camera != null ? Camera.transform.forward : transform.forward, _sightItems, _sightBearings);
            // G5 (ADR-093): the pickup list's selection; the wheel is the scope's while looking down the sights.
            if (PickupList.Instance != null) return PickupList.Instance.Refresh(transform.position, _sightItems, _sightBearings, _aim.Mode == AimMode.Sights ? 0 : LastInput.ZoomSteps);
            LootItem best = null;
            float bestRank = float.MaxValue;
            for (int i = 0; i < _sightItems.Count; i++)
            {
                float rank = Interact.Rank(_sightBearings[i], (_sightItems[i].transform.position - transform.position).magnitude);
                if (rank < bestRank) { bestRank = rank; best = _sightItems[i]; }
            }
            return best;
        }

        // Puts the map marker on the first surface (ground, building) under the crosshair within PingDistance.
        // In a team match TeamHud shares it with the team as a "go" ping (ADR-130).
        // Returns false when nothing is in reach.
        public bool PingAtCrosshair()
        {
            if (!TryPingPoint(out var point)) return false;
            MapMarker.Set(point);
            return true;
        }

        // The emote wheel's choice (ADR-133): empty hands first (the weapon is put away), then the server plays it for everyone.
        public void StartEmote(Emote emote)
        {
            if (_combatant.Dead.Value || _combatant.TeamBlocksAction) return;
            if (_combatant.ActiveWeaponId != 0) SetHolstered(true);
            _combatant.CmdEmote((byte)emote);
        }

        // The first surface under the crosshair within PingDistance.
        public bool TryPingPoint(out Vector3 point)
        {
            point = default;
            return Camera != null && Rig.TryAimHit(Camera.transform, transform, PingDistance, out point);
        }

        // ---- Hooks for tests and scripted control ----

        public void Teleport(Vector3 position)
        {
            // Being moved ends a move under way.
            if (InParkour) AbortParkour();
            // A far jump is on purpose: tell the server so it does not take it for a cheat (#161).
            if (_owner && !IsServerInitialized && _stance != null && (position - transform.position).sqrMagnitude > TeleportTellMeters * TeleportTellMeters) _stance.CmdTeleported(position);
            _controller.enabled = false;
            transform.position = position;
            _controller.enabled = true;
            _verticalVelocity = 0f;
            _fall.Reset(); // being moved is not a fall
        }

        public void LookAt(Vector3 worldPoint)
        {
            var to = worldPoint - _combatant.EyePosition;
            Yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            Pitch = Mathf.Clamp(Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, -PitchLimit, PitchLimit);
        }
    }
}
