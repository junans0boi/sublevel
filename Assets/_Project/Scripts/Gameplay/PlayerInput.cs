using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Gameplay
{
    // One frame of player intent. Devices, tests and (later) rebinding all produce this.
    public struct PlayerInputFrame
    {
        public Vector2 Move;       // x = right, y = forward
        public Vector2 Look;       // degrees this frame: x = yaw, y = pitch (up positive)
        public bool Sprint;
        public bool Jump;
        public bool FireHeld;
        public bool FirePressed;
        public bool FireReleased;  // let go this frame (the throwable in hand throws on it, ADR-144)
        public bool Reload;
        public bool Interact;
        public bool Boost;         // J: drink the smallest boost item carried
        public bool GasCover;      // Y: use a gas filter or antidote (ADR-237)
        public bool Inspect;       // I: look the weapon in hand over (#200, cosmetic, own view)
        public bool InteractHeld;  // F held: reviving a knocked teammate (ADR-072)
        public bool ToggleView;
        public bool AimHeld;       // aim key (right button): hold = shoulder, tap = toggle sights
        public bool LeanLeft;
        public bool LeanRight;
        public bool Menu;          // Esc: open or close the pause menu
        public bool Inventory;     // Tab: open or close the inventory
        public bool Heal;
        public bool Restart;
        public int SelectSlot;     // -1 = none; keys 1-5 give 0-4: primary, primary, sidearm, melee, throwable (ADR-058)
        public bool Crouch;        // C: pressed this frame, toggles crouching
        public bool Prone;         // Z: pressed this frame, toggles lying down
        public bool FreeLook;      // Alt held: the mouse orbits the view all the way round, not the character
        public bool SlowWalk;      // Ctrl held: walk slowly and quietly (ADR-065 (3)); Ctrl does nothing else on foot
        public bool AutoRun;       // =: pressed this frame, toggles walking forward on its own (ADR-065 (1))
        public bool FireMode;      // B: pressed this frame, switches single shots / automatic
        public bool ThrowHeld;     // G held: a throw is being aimed
        public bool ThrowPressed;  // G: pressed this frame
        public bool ThrowReleased; // G: let go this frame
        public bool Map;           // M: open or close the full map
        public bool Holster;       // X: pressed this frame, puts the weapon away (empty hands) or takes it out again
        public bool CycleThrowable; // T: pressed this frame, switches the selected throwable (frag / smoke)
        public int ZoomSteps;      // mouse wheel this frame: +1 a notch up (zoom in), -1 down, 0 none (ADR-063)
        public int ZeroSteps;      // PageUp +1 / PageDown -1 this frame: scope zero distance (ADR-103)
        public bool Ping;          // middle mouse: tap (in a team match: released before the wheel opens) marks the point under the crosshair (ADR-065 (6))
        public bool PingHeld;      // the ping button is down: held a moment it opens the marker wheel in a team match (ADR-130)
        public bool EmoteHeld;     // N held: the emote wheel (ADR-131)
        public bool CarryPressed;  // K: pressed this frame, lifts or puts down a knocked teammate (ADR-129)
    }

    public interface IPlayerInput
    {
        PlayerInputFrame Read();
    }

    // Keyboard + mouse through the Input System (ADR-031).
    public class DeviceInput : IPlayerInput
    {
        public const float DefaultMouseSensitivity = 0.12f;
        // Shared by every device input; the settings screen changes it.
        public static float MouseSensitivity = DefaultMouseSensitivity;

        // Held this long, a button opens its wheel instead of acting as a tap.
        public const float WheelHoldSeconds = 0.25f;
        float _pingDownAt;

        public PlayerInputFrame Read()
        {
            var f = new PlayerInputFrame { SelectSlot = -1 };
            var m = Mouse.current;
            // Keys and buttons come from KeyBinds (#140): every action can sit on another key or mouse button.
            if (Keyboard.current != null)
            {
                f.Move = new Vector2((KeyBinds.Held(BindAction.MoveRight) ? 1 : 0) - (KeyBinds.Held(BindAction.MoveLeft) ? 1 : 0),
                    (KeyBinds.Held(BindAction.MoveForward) ? 1 : 0) - (KeyBinds.Held(BindAction.MoveBack) ? 1 : 0));
                f.Restart = Keyboard.current.enterKey.wasPressedThisFrame;
                f.ZeroSteps = (Keyboard.current.pageUpKey.wasPressedThisFrame ? 1 : 0) - (Keyboard.current.pageDownKey.wasPressedThisFrame ? 1 : 0); // fixed keys (ADR-103)
            }
            f.Sprint = KeyBinds.Held(BindAction.Sprint);
            f.Jump = KeyBinds.Pressed(BindAction.Jump);
            f.Reload = KeyBinds.Pressed(BindAction.Reload);
            f.Interact = KeyBinds.Pressed(BindAction.Interact);
            f.InteractHeld = KeyBinds.Held(BindAction.Interact);
            f.ToggleView = KeyBinds.Pressed(BindAction.ToggleView);
            f.LeanLeft = KeyBinds.Held(BindAction.LeanLeft);
            f.LeanRight = KeyBinds.Held(BindAction.LeanRight);
            f.Menu = KeyBinds.Pressed(BindAction.Menu);
            f.Inventory = KeyBinds.Pressed(BindAction.Inventory);
            f.Heal = KeyBinds.Pressed(BindAction.Heal);
            f.Boost = KeyBinds.Pressed(BindAction.Boost);
            f.GasCover = KeyBinds.Pressed(BindAction.GasCover);
            f.Inspect = KeyBinds.Pressed(BindAction.Inspect);
            f.Crouch = KeyBinds.Pressed(BindAction.Crouch);
            f.Prone = KeyBinds.Pressed(BindAction.Prone);
            f.FreeLook = KeyBinds.Held(BindAction.FreeLook);
            f.SlowWalk = KeyBinds.Held(BindAction.SlowWalk);
            f.AutoRun = KeyBinds.Pressed(BindAction.AutoRun);
            f.FireMode = KeyBinds.Pressed(BindAction.FireMode);
            f.ThrowHeld = KeyBinds.Held(BindAction.Throw);
            f.ThrowPressed = KeyBinds.Pressed(BindAction.Throw);
            f.ThrowReleased = KeyBinds.Released(BindAction.Throw);
            f.Map = KeyBinds.Pressed(BindAction.Map);
            f.Holster = KeyBinds.Pressed(BindAction.Holster);
            f.CycleThrowable = KeyBinds.Pressed(BindAction.CycleThrowable);
            f.FireHeld = KeyBinds.Held(BindAction.Fire);
            f.FirePressed = KeyBinds.Pressed(BindAction.Fire);
            f.FireReleased = KeyBinds.Released(BindAction.Fire);
            f.AimHeld = KeyBinds.Held(BindAction.Aim);
            // In a team match a long press is the marker wheel, so the tap counts when the button comes up.
            var me = Net.Combatant.LocalPlayer;
            if (me != null && me.HasTeam)
            {
                if (KeyBinds.Pressed(BindAction.Ping)) _pingDownAt = Time.unscaledTime;
                f.Ping = KeyBinds.Released(BindAction.Ping) && Time.unscaledTime - _pingDownAt < WheelHoldSeconds;
            }
            else f.Ping = KeyBinds.Pressed(BindAction.Ping);
            f.PingHeld = KeyBinds.Held(BindAction.Ping);
            f.EmoteHeld = KeyBinds.Held(BindAction.Emote);
            f.CarryPressed = KeyBinds.Pressed(BindAction.Carry);
            // Ctrl + a number is kept for vehicle seats; on foot it selects nothing.
            if (f.SlowWalk) { }
            else
                for (int slot = 0; slot < 5; slot++)
                    if (KeyBinds.Pressed(BindAction.Slot1 + slot)) { f.SelectSlot = slot; break; }
            if (m != null)
            {
                var delta = m.delta.ReadValue();
                f.Look = new Vector2(delta.x, delta.y) * MouseSensitivity;
                // One notch per frame at most, whatever the device reports per notch (120 on Windows, less on macOS).
                float wheel = m.scroll.ReadValue().y;
                f.ZoomSteps = wheel > 0.01f ? 1 : wheel < -0.01f ? -1 : 0;
            }
            return f;
        }
    }
}
