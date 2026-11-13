using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Gameplay
{
    // One frame of what the player asks of a vehicle.
    public struct VehicleInputFrame
    {
        public float Throttle;    // W +1 / S -1: drive, brake, reverse (W also restarts a stopped engine)
        public float Steer;       // D +1 / A -1
        public bool Handbrake;    // Space held (on the ground)
        public bool Boost;        // Left Shift held
        public bool EngineToggle; // Z pressed: engine off / on
        public float Pitch;       // motorcycle in the air: Space +1 nose up, Ctrl -1 nose down
        public float Roll;        // motorcycle in the air: E +1 right, Q -1 left
        public bool Interact;     // F pressed: get in (nearest free seat, the driver's if free) or out
        public int Seat;          // Ctrl+1..4 pressed: 0..3; -1 = none
        public bool ToggleView;   // V pressed: the seat's eye view or the vehicle camera
    }

    public interface IVehicleInput
    {
        VehicleInputFrame Read();
    }

    // Keyboard through the Input System (ADR-065 (5)).
    public class DeviceVehicleInput : IVehicleInput
    {
        public VehicleInputFrame Read()
        {
            var f = new VehicleInputFrame { Seat = -1 };
            var k = Keyboard.current;
            if (k == null) return f;
            bool ctrl = k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed;
            f.Throttle = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
            f.Steer = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
            f.Handbrake = k.spaceKey.isPressed;
            f.Boost = k.leftShiftKey.isPressed;
            f.EngineToggle = k.zKey.wasPressedThisFrame;
            f.Pitch = (k.spaceKey.isPressed ? 1f : 0f) - (ctrl ? 1f : 0f);
            f.Roll = (k.eKey.isPressed ? 1f : 0f) - (k.qKey.isPressed ? 1f : 0f);
            f.Interact = k.fKey.wasPressedThisFrame;
            f.ToggleView = k.vKey.wasPressedThisFrame;
            if (ctrl)
            {
                if (k.digit1Key.wasPressedThisFrame) f.Seat = 0;
                else if (k.digit2Key.wasPressedThisFrame) f.Seat = 1;
                else if (k.digit3Key.wasPressedThisFrame) f.Seat = 2;
                else if (k.digit4Key.wasPressedThisFrame) f.Seat = 3;
            }
            return f;
        }
    }

    // The local player's vehicle controls (ADR-065 (5)). Sits on the player prefab next to PlayerController and acts
    // only for the local player. While attached to a vehicle the character's own locomotion is off
    // (PlayerController.ExternalControl); the look, aiming and firing stay with PlayerController, and the server
    // decides what a seat may fire. Runs after PlayerController so the look of this frame is already applied.
    [DefaultExecutionOrder(90)]
    public class VehicleInput : MonoBehaviour
    {
        const float EngineRequestSeconds = 0.5f;

        // Tests replace the device input here.
        public static IVehicleInput InputOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => InputOverride = null;

        readonly DeviceVehicleInput _device = new DeviceVehicleInput();
        PlayerController _player;
        Combatant _combatant;
        bool _attached;
        ViewMode _modeSeen;
        float _engineRequestedAt = float.NegativeInfinity;

        // The view in the vehicle: the seat's eyes (true) or the vehicle camera. V switches; shared with the on-foot view mode.
        public bool EyeView => _player != null && _player.Rig.Mode == ViewMode.FirstPerson;

        bool IsLocal => _player != null && PlayerController.Local == _player;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _combatant = GetComponent<Combatant>();
        }

        void OnEnable() => VehicleSeat.Released += OnReleased;

        void OnDisable() => VehicleSeat.Released -= OnReleased;

        void Update()
        {
            if (!IsLocal || _combatant == null) return;
            var f = (InputOverride ?? _device).Read();
            // Screens that take the mouse, the elevator wait and death leave the vehicle alone (it rolls on its own).
            bool blocked = UiFocus.Current != UiScreen.None || SpawnProtection.InputBlocked || _combatant.Dead.Value;
            if (blocked) f = new VehicleInputFrame { Seat = -1 };

            bool attached = VehicleSeat.IsAttached(_combatant);
            SetAttached(attached);

            // V: PlayerController switches its own view mode with the same key; if it did not this frame (its input
            // may be off while the character is driven from here) switch it here, so V works either way.
            if (attached && f.ToggleView && _player.Rig.Mode == _modeSeen) _player.Rig.ToggleMode();
            _modeSeen = _player.Rig.Mode;

            if (VehicleSeat.TryGet(_combatant, out var vehicle, out int seat))
            {
                if (f.Interact) vehicle.RequestExit(_combatant);
                else if (f.Seat >= 0 && f.Seat != seat) vehicle.RequestSwitch(_combatant, f.Seat);
                if (seat == Vehicles.DriverSeat) Drive(vehicle, f);
                return;
            }
            if (attached || !f.Interact) return;
            // F near a vehicle gets in, unless there is loot right here to pick up (that keeps the F key).
            if (_player.NearbyLoot != null) return;
            var near = Vehicle.NearestEnterable(transform.position, VehicleRules.Current.EnterRange, out _);
            if (near != null) near.RequestEnter(_combatant);
        }

        void Drive(Vehicle vehicle, in VehicleInputFrame f)
        {
            // Z switches the engine; W starts a stopped one.
            bool on = vehicle.EngineOn.Value;
            bool wanted = f.EngineToggle ? !on : on || f.Throttle > 0.1f;
            if (wanted != on && Time.time - _engineRequestedAt > EngineRequestSeconds)
            {
                _engineRequestedAt = Time.time;
                vehicle.RequestEngine(_combatant, wanted);
            }
            vehicle.SetDriverInput(new DriverInput
            {
                Throttle = f.Throttle,
                Steer = f.Steer,
                Handbrake = f.Handbrake,
                Boost = f.Boost,
                // Holding a pistol the driver aims with the mouse: the wheels go back to straight.
                SteerLocked = _combatant.ActiveWeaponId != 0,
                Pitch = f.Pitch,
                Roll = f.Roll,
            });
        }

        // The character's own movement is off from getting in until the way out has finished.
        void SetAttached(bool attached)
        {
            if (attached == _attached) return;
            _attached = attached;
            _player.ExternalControl = attached;
        }

        // The way out has finished on this side: the owner puts its character on the exit point (movement is the
        // owner's, ADR-029) and takes back its own movement.
        void OnReleased(Combatant c, Vector3 exit)
        {
            if (c != _combatant || !IsLocal) return;
            _player.Teleport(exit);
            SetAttached(false);
        }
    }
}
