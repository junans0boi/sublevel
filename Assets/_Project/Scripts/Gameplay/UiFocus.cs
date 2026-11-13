using System;

namespace Game.Gameplay
{
    public enum UiScreen { None, Inventory, Menu, Map }

    // Which full-screen UI, if any, currently owns the mouse and keyboard. The player controller opens and closes
    // screens from input and ignores gameplay input while one is open; the screens watch this to show themselves.
    public static class UiFocus
    {
        public static UiScreen Current { get; private set; }
        // Set by the map screen once one exists in the scene; until then the map key does nothing.
        public static bool MapAvailable;
        public static event Action Changed;

        // Opens the screen, or closes whatever is open (Esc, Tab and M each back out of the other screens).
        public static void Toggle(UiScreen screen) => Set(Current == UiScreen.None ? screen : UiScreen.None);

        public static void Close() => Set(UiScreen.None);

        public static void Set(UiScreen screen)
        {
            if (Current == screen) return;
            Current = screen;
            Changed?.Invoke();
        }
    }
}
