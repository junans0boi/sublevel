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
        public bool Reload;
        public bool Interact;
        public bool ToggleView;
        public bool AimHeld;       // right button: hold = shoulder, tap = toggle sights
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

        public PlayerInputFrame Read()
        {
            var f = new PlayerInputFrame { SelectSlot = -1 };
            var k = Keyboard.current;
            var m = Mouse.current;
            if (k != null)
            {
                f.Move = new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
                f.Sprint = k.leftShiftKey.isPressed;
                f.Jump = k.spaceKey.wasPressedThisFrame;
                f.Reload = k.rKey.wasPressedThisFrame;
                f.Interact = k.fKey.wasPressedThisFrame;
                f.ToggleView = k.vKey.wasPressedThisFrame;
                f.LeanLeft = k.qKey.isPressed;
                f.LeanRight = k.eKey.isPressed;
                f.Menu = k.escapeKey.wasPressedThisFrame;
                f.Inventory = k.tabKey.wasPressedThisFrame;
                f.Heal = k.hKey.wasPressedThisFrame;
                f.Restart = k.enterKey.wasPressedThisFrame;
                f.Crouch = k.cKey.wasPressedThisFrame;
                f.Prone = k.zKey.wasPressedThisFrame;
                f.FreeLook = k.leftAltKey.isPressed;
                f.SlowWalk = k.ctrlKey.isPressed;
                f.AutoRun = k.equalsKey.wasPressedThisFrame;
                f.FireMode = k.bKey.wasPressedThisFrame;
                f.ThrowHeld = k.gKey.isPressed;
                f.ThrowPressed = k.gKey.wasPressedThisFrame;
                f.ThrowReleased = k.gKey.wasReleasedThisFrame;
                f.Map = k.mKey.wasPressedThisFrame;
                f.Holster = k.xKey.wasPressedThisFrame;
                f.CycleThrowable = k.tKey.wasPressedThisFrame;
                // Ctrl + a number is kept for vehicle seats; on foot it selects nothing.
                if (f.SlowWalk) { }
                else if (k.digit1Key.wasPressedThisFrame) f.SelectSlot = 0;
                else if (k.digit2Key.wasPressedThisFrame) f.SelectSlot = 1;
                else if (k.digit3Key.wasPressedThisFrame) f.SelectSlot = 2;
                else if (k.digit4Key.wasPressedThisFrame) f.SelectSlot = 3;
                else if (k.digit5Key.wasPressedThisFrame) f.SelectSlot = 4;
            }
            if (m != null)
            {
                var delta = m.delta.ReadValue();
                f.Look = new Vector2(delta.x, delta.y) * MouseSensitivity;
                f.FireHeld = m.leftButton.isPressed;
                f.FirePressed = m.leftButton.wasPressedThisFrame;
                f.AimHeld = m.rightButton.isPressed;
                // One notch per frame at most, whatever the device reports per notch (120 on Windows, less on macOS).
                float wheel = m.scroll.ReadValue().y;
                f.ZoomSteps = wheel > 0.01f ? 1 : wheel < -0.01f ? -1 : 0;
            }
            return f;
        }
    }
}
