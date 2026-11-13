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

        [SerializeField] Renderer[] _bodyRenderers = new Renderer[0];

        // Tests and tools replace the device input here before the player spawns.
        public static IPlayerInput InputOverride;
        public static PlayerController Local { get; private set; }

        public PlayerCameraRig Rig { get; } = new PlayerCameraRig();
        public Camera Camera { get; private set; }
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public LootItem NearbyLoot { get; private set; }

        public AimMode Aim => _aim.Mode;
        public int Lean { get; private set; }
        // The stance this player has asked for; the replicated one is on CombatantStance.
        public Stance BodyStance { get; private set; }
        public bool Sprinting { get; private set; }
        // Free look (Alt held): the view orbits away from where the character faces by these angles (ADR-065 (2)).
        public bool FreeLooking { get; private set; }
        // Auto-run (=): walking forward on its own until W or S is pressed, aiming starts or a screen opens (ADR-065 (1)).
        public bool AutoRunning => _autoRun;
        // Slow walk (Ctrl held, ADR-065 (3)).
        public bool SlowWalking { get; private set; }
        // A vehicle (or anything else) moves the body: no walking, jumping or parkour of our own; the view still
        // turns. Firing is left to whoever set it.
        public bool ExternalControl;
        // Parkour (Space at an obstacle, ADR-065 (4)): the move under way, and what the last Space at an obstacle decided.
        public bool InParkour => _parkour.Move != ParkourMove.None;
        public ParkourMove ParkourUnderWay => _parkour.Move;
        public ParkourPlan LastParkourPlan { get; private set; }
        public bool ParkourGoingBack => _parkourBack;
        public float ViewYaw => Yaw + _freeYaw;
        public float ViewPitch => Pitch + _freePitch;
        // This frame's input as the character got it (empty while a screen is open), for other owner-side
        // components that act on keys of their own (fire mode, throwing, slots 3-5).
        public PlayerInputFrame LastInput { get; private set; }

        LocomotionParams _locomotion;
        GameRules _rules;
        AimInput _aim;
        readonly ViewRecoil _recoil = new ViewRecoil();
        readonly ShotGrouper _ownShots = new ShotGrouper(0.03);
        const float StanceResendSeconds = 0.3f;
        const float MaxViewPitch = 89f;

        bool _autoRun;
        float _lastMoveY;
        bool _sentSlowWalk;
        float _slowWalkSentAt;
        ParkourPlan _parkour;
        float _parkourT;
        bool _parkourBack;
        int _sentAim, _sentLean;
        float _stanceSentAt;
        int _sentBodyStance;
        float _bodyStanceSentAt;
        float _freeYaw, _freePitch;
        readonly FallTracker _fall = new FallTracker();
        CombatantStance _stance; // missing on a prefab built before stances existed: the player then only stands
        System.Random _recoilRng;
        bool _bodyHidden;
        CharacterController _controller;
        Combatant _combatant;
        WeaponVisual _weaponVisual;
        IPlayerInput _input;
        float _verticalVelocity;
        readonly FireCadence _fireCadence = new FireCadence();
        readonly FireCadence _punchCadence = new FireCadence();
        double _clickUntil = -1;
        bool _owner;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _combatant = GetComponent<Combatant>();
            _weaponVisual = GetComponent<WeaponVisual>();
            _stance = GetComponent<CombatantStance>();
            _controller.enabled = false;
            _rules = GameData.Rules;
            _locomotion = _rules.Locomotion;
            _aim = new AimInput(_rules.Aim.TapSeconds);
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
            if (_stance != null) _stance.ParkourRejected += OnParkourRejected;
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
            screens.AddComponent<MapScreen>();
        }

        // The mouse steers the view while playing and becomes a pointer while a screen is open.
        void UpdateCursor()
        {
            if (Application.isBatchMode || InputOverride != null) return;
            bool playing = UiFocus.Current == UiScreen.None && !ResultScreen.BlocksGameplay;
            Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !playing;
        }

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
            var f = _input.Read();
            // Screens that take over the mouse (pause menu, inventory): the keys that open and close them still
            // work, everything else is ignored so the character stands still and does not shoot.
            if (f.Menu) UiFocus.Toggle(UiScreen.Menu);
            else if (f.Inventory && !_combatant.Dead.Value && !ResultScreen.BlocksGameplay) UiFocus.Toggle(UiScreen.Inventory);
            else if (f.Map && UiFocus.MapAvailable && !_combatant.Dead.Value) UiFocus.Toggle(UiScreen.Map);
            if (UiFocus.Current != UiScreen.None || (ResultScreen.BlocksGameplay && !_combatant.Dead.Value))
                f = new PlayerInputFrame { SelectSlot = -1, AimHeld = false };
            // In the elevator until the doors open: no moving, firing or picking up; the view and the screens work.
            if (SpawnProtection.InputBlocked)
                f = new PlayerInputFrame { SelectSlot = -1, Look = f.Look, Menu = f.Menu, Inventory = f.Inventory, Map = f.Map, FreeLook = f.FreeLook, Ping = f.Ping };
            // Scopes slow the mouse by their zoom; the settings add aim/scope factors and the vertical invert.
            f.Look = GameSettings.AdjustLook(f.Look, _aim.Mode != AimMode.Hip, ScopeView.SensitivityFactor);
            LastInput = f;
            UpdateCursor();
            _recoil.Tick(Time.deltaTime, _rules.Recoil.RecoverDegPerSecond);

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

            // Aiming needs a weapon and wins over sprinting; leaning is for standing and walking only.
            bool armed = _combatant.ActiveWeapon != null;
            var aim = _aim.Update(f.AimHeld, Time.timeAsDouble, armed);

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
            transform.rotation = Quaternion.Euler(0f, Yaw, 0f);

            // Auto-run: = toggles it; a fresh W or S, aiming, a screen or anything else taking the body ends it.
            if (f.AutoRun) _autoRun = !_autoRun;
            bool freshForwardBack = Mathf.Abs(f.Move.y) > 0.1f && Mathf.Abs(_lastMoveY) <= 0.1f;
            _lastMoveY = f.Move.y;
            if (freshForwardBack || aim != AimMode.Hip || UiFocus.Current != UiScreen.None || SpawnProtection.InputBlocked
                || ExternalControl || ResultScreen.BlocksGameplay) _autoRun = false;
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
            bool sprint = f.Sprint && f.Move.y > 0.1f && aim == AimMode.Hip && !firing && !SlowWalking && !ExternalControl;

            // Space in front of an obstacle vaults or climbs instead of jumping (standing or crouched, on the ground).
            if (f.Jump && !ExternalControl && _stance != null && _controller.isGrounded && BodyStance != Stance.Prone && TryStartParkour())
            {
                Sprinting = false;
                SendSlowWalk();
                PlaceCamera();
                return;
            }

            // C toggles the crouch and Z lying down; jumping or sprinting gets up instead. Rising (lying to crouching,
            // anything to standing) needs room overhead for the taller capsule.
            bool wasStanding = BodyStance == Stance.Stand;
            var stanceRules = StanceRules.Current;
            if (_stance == null) BodyStance = Stance.Stand;
            else if (!ExternalControl)
            {
                var next = Stances.Next(BodyStance, f.Crouch, f.Prone, f.Jump, sprint, true, stanceRules);
                if (Stances.Rises(BodyStance, next, stanceRules) && !_stance.CanReach(stanceRules.For(next).CapsuleHeight)) next = BodyStance;
                BodyStance = next;
            }
            bool jump = f.Jump && wasStanding && !ExternalControl; // out of a crouch the key only stands up
            sprint &= BodyStance == Stance.Stand;
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
            // Riding the elevator car up (ADR-065 (1)): the car carries the feet, nothing else moves the body.
            else if (SpawnProtection.InputBlocked && ElevatorCar.TryRideFloor(transform.position, out float rideFeet))
                Teleport(new Vector3(transform.position.x, rideFeet, transform.position.z));
            else
            {
                // Empty hands (nothing held, or put away with X) are faster; the other factors slow the weapon down.
                // Slow walking replaces the empty-hands bonus with its own share of the walking speed.
                float speedFactor = Locomotion.MoveSpeedFactor(_rules.Aim.For(aim).MoveFactor, Lean != 0 ? _rules.Lean.MoveFactor : 1f,
                    stanceRules.For(BodyStance).MoveFactor, armed, _locomotion, SlowWalking);
                // Slower while a bandage or kit is being used.
                speedFactor *= CombatantHealing.MoveFactorOf(_combatant);
                Locomotion.PlanarVelocity(f.Move.x, f.Move.y, Yaw, sprint, _locomotion, out var vx, out var vz, speedFactor);
                _verticalVelocity = Locomotion.VerticalVelocity(_verticalVelocity, _controller.isGrounded, jump, Time.deltaTime, _locomotion);
                _controller.Move(new Vector3(vx, _verticalVelocity, vz) * Time.deltaTime);
                ReportFall();
            }

            if (f.ToggleView) Rig.ToggleMode();
            PlaceCamera();

            NearbyLoot = FindNearbyLoot();
            if (f.Interact && NearbyLoot != null) _combatant.CmdPickup(NearbyLoot.NetworkObject);
            if (f.SelectSlot >= 0) _combatant.CmdSelectSlot(f.SelectSlot);
            if (f.Reload) _combatant.CmdReload();
            if (f.Holster) SetHolstered(!_combatant.Holstered.Value);
            if (f.FireMode) _combatant.CmdToggleFireMode();
            if (f.Heal) _combatant.CmdUseMedkit();
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

        // One frame along the path. S in the first part of the move runs it back to the start.
        void TickParkour(in PlayerInputFrame f)
        {
            var rules = ParkourRules.Current;
            if (!_parkourBack && f.Move.y < -0.5f && Parkour.CanCancel(_parkourT, rules)) _parkourBack = true;
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
            if (_stance == null || dropped <= FallRules.Current.SafeMeters) return;
            if (IsServerInitialized) _stance.ServerReportFall(dropped);
            else _stance.CmdReportFall(dropped);
        }

        // Our own shots kick the view; the kicked view is what the next shot aims with.
        void OnOwnShot(Vector3 origin, Vector3 end)
        {
            var weapon = _combatant.ActiveWeapon;
            if (weapon == null || !_ownShots.IsNewShot(Time.timeAsDouble)) return;
            float factor = _rules.Aim.For(_aim.Mode).RecoilFactor;
            // Seeded per match and player so a replayed match kicks the same way.
            float side = (float)(_recoilRng.NextDouble() * 2.0 - 1.0) * weapon.RecoilYawDeg;
            _recoil.Kick(weapon.RecoilPitchDeg * factor, side * factor, _rules.Recoil.MaxPitchDeg);
        }

        void TryFire(in PlayerInputFrame f)
        {
            double now = Time.timeAsDouble;
            var fire = _rules.Fire;
            // A click that comes a moment too soon is remembered, so quick semi-automatic fire does not swallow clicks.
            if (f.FirePressed) _clickUntil = now + fire.ClickBufferSeconds;
            // Nothing in hand (none carried, or put away with X): the button punches instead (ADR-059 (1)).
            if (_combatant.ActiveWeaponId == 0)
            {
                TryPunch(f, now);
                return;
            }
            var weapon = _combatant.ActiveWeapon;
            if (weapon == null || _combatant.Reloading.Value || _combatant.ActiveMag <= 0) return;
            // Automatic weapons set to single shots (B) fire once per click, and no faster than the server allows.
            if (!(_combatant.ActiveAutoFire ? f.FireHeld : now <= _clickUntil) || !_fireCadence.Ready(now)) return;
            _clickUntil = -1;
            double interval = _combatant.FireInterval(weapon);
            // Scheduled from when the shot was due, not from this frame: a frame always lands a little late, and
            // adding the interval to that would make the rhythm follow the frame times and run slow.
            _fireCadence.Fired(now, interval, fire.ClientCatchUp(interval, Time.deltaTime));
            var direction = AimDirection();
            // The host is the server: fire now instead of waiting for the next network tick to carry the request.
            if (IsServerInitialized) _combatant.ServerFire(direction);
            else _combatant.CmdFire(direction);
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

        // Held or clicked, at most one punch per PunchInterval; the server keeps the same limit.
        void TryPunch(in PlayerInputFrame f, double now)
        {
            if (!(f.FireHeld || now <= _clickUntil) || !_punchCadence.Ready(now)) return;
            _clickUntil = -1;
            double interval = MeleeRules.Current.PunchInterval;
            _punchCadence.Fired(now, interval, _rules.Fire.ClientCatchUp(interval, Time.deltaTime));
            var direction = AimDirection();
            if (IsServerInitialized) _combatant.ServerPunch(direction);
            else _combatant.CmdPunch(direction);
        }

        void PlaceCamera()
        {
            if (Camera == null) return;
            var aimRules = _rules.Aim;
            var view = new ViewState
            {
                Aim = _aim.Mode,
                Lean = _combatant.LeanAmount,
                LeanRollDeg = Rig.IsEyeView(_aim.Mode) ? _rules.Lean.EyeViewRollDeg : _rules.Lean.ThirdPersonRollDeg,
                RecoilPitch = Mathf.Min(_recoil.Pitch, MaxViewPitch - ViewPitch), // never past straight up
                RecoilYaw = _recoil.Yaw,
                ShoulderAimDistance = aimRules.ShoulderCameraDistance,
            };
            var eye = _combatant.EyePosition + Vector3.down * ParkourEyeDrop();
            Rig.Place(Camera.transform, transform, eye, ViewYaw, ViewPitch, view, Time.deltaTime);
            float ease = 1f - Mathf.Exp(-aimRules.FovChangePerSecond * Time.deltaTime);
            float fovTarget = ScopeView.FovOverride > 0f ? ScopeView.FovOverride
                : _aim.Mode == AimMode.Hip && Rig.Mode == ViewMode.FirstPerson ? GameSettings.FirstPersonFov
                : aimRules.For(_aim.Mode).Fov;
            Camera.fieldOfView = Mathf.Lerp(Camera.fieldOfView, fovTarget, ease);

            // Hide our own body whenever the camera sits at the eyes so it does not block the view; the weapon
            // moves in front of the camera instead.
            bool hide = Rig.IsEyeView(_aim.Mode);
            if (_weaponVisual != null)
            {
                // The dead hold nothing up: the weapon stays with the body.
                if (hide && !_combatant.Dead.Value) _weaponVisual.ShowAtEye(Camera.transform, _aim.Mode == AimMode.Sights);
                else _weaponVisual.ShowInHand();
            }
            if (hide == _bodyHidden) return;
            _bodyHidden = hide;
            foreach (var r in _bodyRenderers) r.enabled = !hide;
        }

        LootItem FindNearbyLoot()
        {
            LootItem best = null;
            float bestSqr = Combatant.PickupRange * Combatant.PickupRange;
            foreach (var item in LootItem.All)
            {
                float sqr = (item.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = item; }
            }
            return best;
        }

        // Puts the map marker on the first surface (ground, building) under the crosshair within PingDistance.
        // Local only for now: team pings come with team mode (the marker already carries an owner and colour).
        // Returns false when nothing is in reach.
        public bool PingAtCrosshair()
        {
            if (Camera == null || !Rig.TryAimHit(Camera.transform, transform, PingDistance, out var point)) return false;
            MapMarker.Set(point);
            return true;
        }

        // ---- Hooks for tests and scripted control ----

        public void Teleport(Vector3 position)
        {
            // Being moved ends a move under way.
            if (InParkour) AbortParkour();
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
