using System;

namespace Game.Gameplay
{
    public enum UiScreen { None, Inventory, Menu }

    // Which full-screen UI, if any, currently owns the mouse and keyboard. The player controller opens and closes
    // screens from input and ignores gameplay input while one is open; the screens watch this to show themselves.
    public static class UiFocus
    {
        public static UiScreen Current { get; private set; }
        public static event Action Changed;

        // Opens the screen, or closes whatever is open (Esc and Tab both back out of the other screen).
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
