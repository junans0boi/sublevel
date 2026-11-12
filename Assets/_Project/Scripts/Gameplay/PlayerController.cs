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

        LocomotionParams _locomotion;
        GameRules _rules;
        AimInput _aim;
        readonly ViewRecoil _recoil = new ViewRecoil();
        readonly ShotGrouper _ownShots = new ShotGrouper(0.03);
        const float StanceResendSeconds = 0.3f;
        const float MaxViewPitch = 89f;

        int _sentAim, _sentLean;
        float _stanceSentAt;
        System.Random _recoilRng;
        bool _bodyHidden;
        CharacterController _controller;
        Combatant _combatant;
        IPlayerInput _input;
        float _verticalVelocity;
        float _nextLocalFire;
        bool _owner;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _combatant = GetComponent<Combatant>();
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
            _input = InputOverride ?? new DeviceInput();
            _controller.enabled = true;
            Camera = Camera.main;
            if (Camera != null) Camera.fieldOfView = _rules.Aim.Hip.Fov;
            _combatant.ShotFired += OnOwnShot;
            int matchSeed = MatchDirector.Instance != null ? MatchDirector.Instance.Seed : 0;
            _recoilRng = new System.Random(unchecked(matchSeed * 131 + ObjectId * 31 + 7));
            Yaw = transform.eulerAngles.y;
            if (!Application.isBatchMode && InputOverride == null)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
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
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Update()
        {
            if (!_owner) return;
            var f = _input.Read();
            if (f.Quit) Application.Quit(); // until there is a menu (M1.6c)
            _recoil.Tick(Time.deltaTime, _rules.Recoil.RecoverDegPerSecond);

            if (_combatant.Dead.Value)
            {
                _aim.Update(false, Time.timeAsDouble, false);
                Lean = 0;
                SendStance();
                if (f.Restart) _combatant.CmdRequestRestart();
                PlaceCamera();
                return;
            }

            Yaw += f.Look.x;
            Pitch = Mathf.Clamp(Pitch + f.Look.y, -PitchLimit, PitchLimit);
            transform.rotation = Quaternion.Euler(0f, Yaw, 0f);

            // Aiming needs a weapon and wins over sprinting; leaning is for standing and walking only.
            var aim = _aim.Update(f.AimHeld, Time.timeAsDouble, _combatant.ActiveWeapon != null);
            bool sprint = f.Sprint && aim == AimMode.Hip;
            Lean = sprint && f.Move.y > 0.1f ? 0 : (f.LeanRight ? 1 : 0) - (f.LeanLeft ? 1 : 0);
            SendStance();

            float speedFactor = _rules.Aim.For(aim).MoveFactor * (Lean != 0 ? _rules.Lean.MoveFactor : 1f);
            Locomotion.PlanarVelocity(f.Move.x, f.Move.y, Yaw, sprint, _locomotion, out var vx, out var vz, speedFactor);
            _verticalVelocity = Locomotion.VerticalVelocity(_verticalVelocity, _controller.isGrounded, f.Jump, Time.deltaTime, _locomotion);
            _controller.Move(new Vector3(vx, _verticalVelocity, vz) * Time.deltaTime);

            if (f.ToggleView) Rig.ToggleMode();
            PlaceCamera();

            NearbyLoot = FindNearbyLoot();
            if (f.Interact && NearbyLoot != null) _combatant.CmdPickup(NearbyLoot.NetworkObject);
            if (f.SelectSlot >= 0) _combatant.CmdSelectSlot(f.SelectSlot);
            if (f.Reload) _combatant.CmdReload();
            if (f.Heal) _combatant.CmdUseMedkit();
            TryFire(f);
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
            var weapon = _combatant.ActiveWeapon;
            if (weapon == null || _combatant.Reloading.Value || _combatant.ActiveMag <= 0) return;
            if (!(weapon.Auto ? f.FireHeld : f.FirePressed) || Time.time < _nextLocalFire) return;
            _nextLocalFire = Time.time + weapon.FireInterval;
            var eye = _combatant.EyePosition;
            var aim = Camera != null ? Rig.AimPoint(Camera.transform, transform, AimDistance) : eye + transform.forward * AimDistance;
            _combatant.CmdFire((aim - eye).normalized);
        }

        void PlaceCamera()
        {
            if (Camera == null) return;
            var aimRules = _rules.Aim;
            var view = new ViewState
            {
                Aim = _aim.Mode,
                Lean = _combatant.LeanAmount,
                LeanRollDeg = _rules.Lean.CameraRollDeg,
                RecoilPitch = Mathf.Min(_recoil.Pitch, MaxViewPitch - Pitch), // never past straight up
                RecoilYaw = _recoil.Yaw,
                ShoulderAimDistance = aimRules.ShoulderCameraDistance,
            };
            Rig.Place(Camera.transform, transform, _combatant.EyePosition, Yaw, Pitch, view, Time.deltaTime);
            float ease = 1f - Mathf.Exp(-aimRules.FovChangePerSecond * Time.deltaTime);
            Camera.fieldOfView = Mathf.Lerp(Camera.fieldOfView, aimRules.For(_aim.Mode).Fov, ease);

            // Hide our own body whenever the camera sits at the eyes so it does not block the view.
            bool hide = Rig.IsEyeView(_aim.Mode);
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
        }

        public void LookAt(Vector3 worldPoint)
        {
            var to = worldPoint - _combatant.EyePosition;
            Yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            Pitch = Mathf.Clamp(Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, -PitchLimit, PitchLimit);
        }
    }
}
