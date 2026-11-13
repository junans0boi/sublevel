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
        // Free look: the view is turned away from where the character faces by these angles.
        public bool FreeLooking { get; private set; }
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
                f = new PlayerInputFrame { SelectSlot = -1, Look = f.Look, Menu = f.Menu, Inventory = f.Inventory, Map = f.Map, FreeLook = f.FreeLook };
            // Scopes slow the mouse by their zoom; the settings add a factor of their own while aiming.
            float lookFactor = ScopeView.SensitivityFactor;
            if (_aim.Mode != AimMode.Hip) lookFactor *= GameSettings.AimSensitivityFactor;
            f.Look *= lookFactor;
            LastInput = f;
            UpdateCursor();
            _recoil.Tick(Time.deltaTime, _rules.Recoil.RecoverDegPerSecond);

            if (_combatant.Dead.Value)
            {
                _aim.Update(false, Time.timeAsDouble, false);
                Lean = 0;
                BodyStance = Stance.Stand;
                Sprinting = false;
                FreeLooking = false;
                _freeYaw = _freePitch = 0f;
                SendStance();
                SendBodyStance();
                if (f.Restart) _combatant.CmdRequestRestart();
                PlaceCamera();
                return;
            }

            // Aiming needs a weapon and wins over sprinting; leaning is for standing and walking only.
            bool armed = _combatant.ActiveWeapon != null;
            var aim = _aim.Update(f.AimHeld, Time.timeAsDouble, armed);

            // Free look turns only the view, and only from the hip: the character keeps facing (and shooting)
            // where it did. Letting go swings the view back.
            FreeLooking = f.FreeLook && aim == AimMode.Hip;
            if (FreeLooking)
            {
                var freeLook = FreeLookRules.Current;
                _freeYaw = Mathf.Clamp(_freeYaw + f.Look.x, -freeLook.MaxYawDeg, freeLook.MaxYawDeg);
                _freePitch = Mathf.Clamp(_freePitch + f.Look.y, -PitchLimit - Pitch, PitchLimit - Pitch);
            }
            else
            {
                float back = FreeLookRules.Current.ReturnDegPerSecond * Time.deltaTime;
                _freeYaw = Mathf.MoveTowards(_freeYaw, 0f, back);
                _freePitch = Mathf.MoveTowards(_freePitch, 0f, back);
                Yaw += f.Look.x;
                Pitch = Mathf.Clamp(Pitch + f.Look.y, -PitchLimit, PitchLimit);
            }
            transform.rotation = Quaternion.Euler(0f, Yaw, 0f);

            // Sprinting is forward only, and neither aiming nor firing goes with it: pulling the trigger or
            // raising the weapon drops back to a walk, so no shot ever leaves a sprint.
            bool firing = armed && (f.FireHeld || f.FirePressed);
            bool sprint = f.Sprint && f.Move.y > 0.1f && aim == AimMode.Hip && !firing;

            // C toggles the crouch and Z lying down; jumping or sprinting gets up instead. Rising (lying to crouching,
            // anything to standing) needs room overhead for the taller capsule.
            bool wasStanding = BodyStance == Stance.Stand;
            var stanceRules = StanceRules.Current;
            if (_stance == null) BodyStance = Stance.Stand;
            else
            {
                var next = Stances.Next(BodyStance, f.Crouch, f.Prone, f.Jump, sprint, true, stanceRules);
                if (Stances.Rises(BodyStance, next, stanceRules) && !_stance.CanReach(stanceRules.For(next).CapsuleHeight)) next = BodyStance;
                BodyStance = next;
            }
            bool jump = f.Jump && wasStanding; // out of a crouch the key only stands up
            sprint &= BodyStance == Stance.Stand;
            Sprinting = sprint;
            Lean = sprint ? 0 : (f.LeanRight ? 1 : 0) - (f.LeanLeft ? 1 : 0);
            SendStance();
            SendBodyStance();

            // Empty hands (nothing held, or put away with X) are faster; the other factors slow the weapon down.
            float speedFactor = Locomotion.MoveSpeedFactor(_rules.Aim.For(aim).MoveFactor, Lean != 0 ? _rules.Lean.MoveFactor : 1f,
                stanceRules.For(BodyStance).MoveFactor, armed, _locomotion);
            Locomotion.PlanarVelocity(f.Move.x, f.Move.y, Yaw, sprint, _locomotion, out var vx, out var vz, speedFactor);
            _verticalVelocity = Locomotion.VerticalVelocity(_verticalVelocity, _controller.isGrounded, jump, Time.deltaTime, _locomotion);
            _controller.Move(new Vector3(vx, _verticalVelocity, vz) * Time.deltaTime);
            ReportFall();

            if (f.ToggleView) Rig.ToggleMode();
            PlaceCamera();

            NearbyLoot = FindNearbyLoot();
            if (f.Interact && NearbyLoot != null) _combatant.CmdPickup(NearbyLoot.NetworkObject);
            if (f.SelectSlot >= 0) _combatant.CmdSelectSlot(f.SelectSlot);
            if (f.Reload) _combatant.CmdReload();
            if (f.Holster) SetHolstered(!_combatant.Holstered.Value);
            if (f.FireMode) _combatant.CmdToggleFireMode();
            if (f.Heal) _combatant.CmdUseMedkit();
            TryFire(f);
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
            var weapon = _combatant.ActiveWeapon;
            if (weapon == null || _combatant.Reloading.Value || _combatant.ActiveMag <= 0) return;
            // Automatic weapons set to single shots (B) fire once per click, and no faster than the server allows.
            if (!(_combatant.ActiveAutoFire ? f.FireHeld : now <= _clickUntil) || !_fireCadence.Ready(now)) return;
            _clickUntil = -1;
            double interval = _combatant.FireInterval(weapon);
            // Scheduled from when the shot was due, not from this frame: a frame always lands a little late, and
            // adding the interval to that would make the rhythm follow the frame times and run slow.
            _fireCadence.Fired(now, interval, fire.ClientCatchUp(interval, Time.deltaTime));
            var eye = _combatant.EyePosition;
            // While the view is turned away in free look the shot goes where the character faces, not where the camera does.
            bool viewTurned = _freeYaw != 0f || _freePitch != 0f;
            var aim = Camera != null && !viewTurned
                ? Rig.AimPoint(Camera.transform, transform, AimDistance)
                : eye + Quaternion.Euler(-(Pitch + _recoil.Pitch), Yaw + _recoil.Yaw, 0f) * Vector3.forward * AimDistance;
            var direction = (aim - eye).normalized;
            // The host is the server: fire now instead of waiting for the next network tick to carry the request.
            if (IsServerInitialized) _combatant.ServerFire(direction);
            else _combatant.CmdFire(direction);
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
            Rig.Place(Camera.transform, transform, _combatant.EyePosition, ViewYaw, ViewPitch, view, Time.deltaTime);
            float ease = 1f - Mathf.Exp(-aimRules.FovChangePerSecond * Time.deltaTime);
            float fovTarget = ScopeView.FovOverride > 0f ? ScopeView.FovOverride : aimRules.For(_aim.Mode).Fov;
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

        // ---- Hooks for tests and scripted control ----

        public void Teleport(Vector3 position)
        {
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
