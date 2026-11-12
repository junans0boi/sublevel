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
        public int SelectSlot;     // -1 = none
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
                if (k.digit1Key.wasPressedThisFrame) f.SelectSlot = 0;
                else if (k.digit2Key.wasPressedThisFrame) f.SelectSlot = 1;
            }
            if (m != null)
            {
                var delta = m.delta.ReadValue();
                f.Look = new Vector2(delta.x, delta.y) * MouseSensitivity;
                f.FireHeld = m.leftButton.isPressed;
                f.FirePressed = m.leftButton.wasPressedThisFrame;
                f.AimHeld = m.rightButton.isPressed;
            }
            return f;
        }
    }
}
