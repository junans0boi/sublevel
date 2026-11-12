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

        readonly LocomotionParams _locomotion = new LocomotionParams();
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
        }

        public override void OnStartClient()
        {
            if (!IsOwner) return;
            _owner = true;
            Local = this;
            _input = InputOverride ?? new DeviceInput();
            _controller.enabled = true;
            Camera = Camera.main;
            Yaw = transform.eulerAngles.y;
            if (!Application.isBatchMode && InputOverride == null)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public override void OnStopClient()
        {
            if (Local == this) Local = null;
            _owner = false;
        }

        void Update()
        {
            if (!_owner) return;
            var f = _input.Read();

            if (_combatant.Dead.Value)
            {
                if (f.Restart && MatchDirector.Instance != null) MatchDirector.Instance.Restart();
                PlaceCamera();
                return;
            }

            Yaw += f.Look.x;
            Pitch = Mathf.Clamp(Pitch + f.Look.y, -PitchLimit, PitchLimit);
            transform.rotation = Quaternion.Euler(0f, Yaw, 0f);

            Locomotion.PlanarVelocity(f.Move.x, f.Move.y, Yaw, f.Sprint, _locomotion, out var vx, out var vz);
            _verticalVelocity = Locomotion.VerticalVelocity(_verticalVelocity, _controller.isGrounded, f.Jump, Time.deltaTime, _locomotion);
            _controller.Move(new Vector3(vx, _verticalVelocity, vz) * Time.deltaTime);

            if (f.ToggleView) SetViewMode(Rig.Mode == ViewMode.ThirdPerson ? ViewMode.FirstPerson : ViewMode.ThirdPerson);
            if (f.SwapShoulder) Rig.SwapShoulder();
            PlaceCamera();

            NearbyLoot = FindNearbyLoot();
            if (f.Interact && NearbyLoot != null) _combatant.CmdPickup(NearbyLoot.NetworkObject);
            if (f.SelectSlot >= 0) _combatant.CmdSelectSlot(f.SelectSlot);
            if (f.Reload) _combatant.CmdReload();
            if (f.Heal) _combatant.CmdUseMedkit();
            TryFire(f);
        }

        void TryFire(in PlayerInputFrame f)
        {
            var weapon = _combatant.ActiveWeapon;
            if (weapon == null || _combatant.Reloading.Value || _combatant.ActiveMag <= 0) return;
            if (!(weapon.Auto ? f.FireHeld : f.FirePressed) || Time.time < _nextLocalFire) return;
            _nextLocalFire = Time.time + weapon.FireInterval;
            var eye = _combatant.EyePosition;
            var aim = Camera != null ? Rig.AimPoint(Camera.transform, transform, AimDistance) : eye + transform.forward * AimDistance;
            _combatant.CmdFire(eye, (aim - eye).normalized);
        }

        void PlaceCamera()
        {
            if (Camera != null) Rig.Place(Camera.transform, transform, _combatant.EyePosition, Yaw, Pitch);
        }

        public void SetViewMode(ViewMode mode)
        {
            if (Rig.Mode != mode) Rig.ToggleMode();
            // Hide our own body in first person so it does not block the view.
            foreach (var r in _bodyRenderers) r.enabled = mode == ViewMode.ThirdPerson;
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
