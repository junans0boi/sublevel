using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Game.Gameplay
{
    // Every action the player can put on another key (#140). Enter (restart after death) stays fixed.
    public enum BindAction
    {
        MoveForward, MoveBack, MoveLeft, MoveRight, Sprint, Jump, AutoRun, SlowWalk, Crouch, Prone, LeanLeft, LeanRight, FreeLook,
        Fire, Aim, Reload, FireMode, Slot1, Slot2, Slot3, Slot4, Slot5, Holster, Throw, CycleThrowable, Heal, Boost,
        Interact, ToggleView, Inventory, Map, Ping, Menu, NetStats, Carry, Emote,
    }

    // Which key or mouse button each action uses. A binding is a code: an Input System `Key` value, or MouseBase + the
    // mouse button (0 left, 1 right, 2 middle, 3 back, 4 forward). Changes are saved at once (PlayerPrefs "keys.<action>");
    // devices read them through Held / Pressed / Released.
    public static class KeyBinds
    {
        public const int MouseBase = 1000;
        public const int None = 0;
        const string Prefix = "keys.";

        static readonly int[] Defaults = BuildDefaults();
        static readonly int[] Current = (int[])Defaults.Clone();
        static bool _loaded;

        public static int Count => Defaults.Length;
        public static event Action Changed;

        static int[] BuildDefaults()
        {
            var d = new int[Enum.GetValues(typeof(BindAction)).Length];
            void Set(BindAction a, Key k) => d[(int)a] = (int)k;
            Set(BindAction.MoveForward, Key.W); Set(BindAction.MoveBack, Key.S); Set(BindAction.MoveLeft, Key.A); Set(BindAction.MoveRight, Key.D);
            Set(BindAction.Sprint, Key.LeftShift); Set(BindAction.Jump, Key.Space); Set(BindAction.AutoRun, Key.Equals);
            Set(BindAction.SlowWalk, Key.LeftCtrl); Set(BindAction.Crouch, Key.C); Set(BindAction.Prone, Key.Z);
            Set(BindAction.LeanLeft, Key.Q); Set(BindAction.LeanRight, Key.E); Set(BindAction.FreeLook, Key.LeftAlt);
            d[(int)BindAction.Fire] = MouseBase; d[(int)BindAction.Aim] = MouseBase + 1;
            Set(BindAction.Reload, Key.R); Set(BindAction.FireMode, Key.B);
            Set(BindAction.Slot1, Key.Digit1); Set(BindAction.Slot2, Key.Digit2); Set(BindAction.Slot3, Key.Digit3);
            Set(BindAction.Slot4, Key.Digit4); Set(BindAction.Slot5, Key.Digit5);
            Set(BindAction.Holster, Key.X); Set(BindAction.Throw, Key.G); Set(BindAction.CycleThrowable, Key.T);
            Set(BindAction.Heal, Key.H); Set(BindAction.Boost, Key.J); Set(BindAction.Interact, Key.F);
            Set(BindAction.ToggleView, Key.V); Set(BindAction.Inventory, Key.Tab); Set(BindAction.Map, Key.M);
            d[(int)BindAction.Ping] = MouseBase + 2; Set(BindAction.Menu, Key.Escape); Set(BindAction.NetStats, Key.F3);
            Set(BindAction.Carry, Key.Y); Set(BindAction.Emote, Key.N);
            return d;
        }

        static string PrefKey(BindAction a) => Prefix + a;

        // Reads the saved bindings (the defaults where nothing is saved). Called lazily and by tests after they change prefs.
        public static void Load()
        {
            for (int i = 0; i < Current.Length; i++) Current[i] = Valid(PlayerPrefs.GetInt(PrefKey((BindAction)i), Defaults[i]), Defaults[i]);
            _loaded = true;
            Changed?.Invoke();
        }

        static int Valid(int code, int fallback) => IsBindable(code) ? code : fallback;

        public static bool IsBindable(int code) =>
            code >= MouseBase ? code <= MouseBase + 4 : code > (int)Key.None && code <= (int)Key.OEM5 && code != 111;  // 111 = the dummy IMESelected key

        // The code of a letter key 'A'..'Z' (the letter keys are consecutive in Key).
        public static int Letter(char c) => (int)Key.A + (char.ToUpperInvariant(c) - 'A');

        public static int DefaultOf(BindAction a) => Defaults[(int)a];

        public static int Get(BindAction a)
        {
            if (!_loaded) Load();
            return Current[(int)a];
        }

        // Puts `a` on `code` and saves it. Returns the other action already on that code (it keeps it: the screen warns).
        public static BindAction? Set(BindAction a, int code)
        {
            if (!_loaded) Load();
            if (!IsBindable(code)) return null;
            Current[(int)a] = code;
            PlayerPrefs.SetInt(PrefKey(a), code);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return ConflictOf(a);
        }

        // Another action on the same code as `a`, or null.
        public static BindAction? ConflictOf(BindAction a)
        {
            int code = Get(a);
            for (int i = 0; i < Current.Length; i++)
                if (i != (int)a && Current[i] == code) return (BindAction)i;
            return null;
        }

        // 기본: every action back on its default key (also removes the saved values).
        public static void ResetAll()
        {
            for (int i = 0; i < Current.Length; i++)
            {
                Current[i] = Defaults[i];
                PlayerPrefs.DeleteKey(PrefKey((BindAction)i));
            }
            _loaded = true;
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        // Test support: the developer's own saved keys must survive test runs (-1 = nothing saved for that action).
        public static int[] SnapshotPrefs()
        {
            var saved = new int[Defaults.Length];
            for (int i = 0; i < saved.Length; i++) saved[i] = PlayerPrefs.GetInt(PrefKey((BindAction)i), -1);
            return saved;
        }

        public static void RestorePrefs(int[] saved)
        {
            for (int i = 0; i < saved.Length; i++)
            {
                if (saved[i] < 0) PlayerPrefs.DeleteKey(PrefKey((BindAction)i));
                else PlayerPrefs.SetInt(PrefKey((BindAction)i), saved[i]);
            }
            PlayerPrefs.Save();
            Load();
        }

        // ---- reading the devices ----

        static ButtonControl Control(int code)
        {
            if (code >= MouseBase)
            {
                var m = Mouse.current;
                if (m == null) return null;
                switch (code - MouseBase)
                {
                    case 0: return m.leftButton;
                    case 1: return m.rightButton;
                    case 2: return m.middleButton;
                    case 3: return m.backButton;
                    default: return m.forwardButton;
                }
            }
            var k = Keyboard.current;
            if (k == null || !IsBindable(code)) return null;
            if (code == (int)Key.LeftCtrl) return k.ctrlKey; // either Ctrl, as before
            return k[(Key)code];
        }

        public static bool Held(BindAction a) { var c = Control(Get(a)); return c != null && c.isPressed; }
        public static bool Pressed(BindAction a) { var c = Control(Get(a)); return c != null && c.wasPressedThisFrame; }
        public static bool Released(BindAction a) { var c = Control(Get(a)); return c != null && c.wasReleasedThisFrame; }

        // The key or button pressed this frame (rebinding listens with it), or false.
        public static bool TryCapture(out int code)
        {
            code = None;
            var m = Mouse.current;
            if (m != null)
            {
                if (m.leftButton.wasPressedThisFrame) { code = MouseBase; return true; }
                if (m.rightButton.wasPressedThisFrame) { code = MouseBase + 1; return true; }
                if (m.middleButton.wasPressedThisFrame) { code = MouseBase + 2; return true; }
                if (m.backButton.wasPressedThisFrame) { code = MouseBase + 3; return true; }
                if (m.forwardButton.wasPressedThisFrame) { code = MouseBase + 4; return true; }
            }
            var k = Keyboard.current;
            if (k == null) return false;
            foreach (var key in k.allKeys)
                if (key.wasPressedThisFrame && IsBindable((int)key.keyCode)) { code = (int)key.keyCode; return true; }
            return false;
        }

        // ---- names ----

        public static string Name(int code)
        {
            if (code >= MouseBase)
            {
                switch (code - MouseBase)
                {
                    case 0: return "좌클릭";
                    case 1: return "우클릭";
                    case 2: return "휠 클릭";
                    case 3: return "마우스 뒤로";
                    case 4: return "마우스 앞으로";
                }
            }
            if (!IsBindable(code)) return "없음";
            var key = (Key)code;
            switch (key)
            {
                case Key.LeftShift: return "Shift";
                case Key.RightShift: return "Right Shift";
                case Key.LeftCtrl: return "Ctrl";
                case Key.RightCtrl: return "Right Ctrl";
                case Key.LeftAlt: return "Alt";
                case Key.RightAlt: return "Right Alt";
                case Key.Equals: return "=";
                case Key.Minus: return "-";
                case Key.Backquote: return "`";
                case Key.Backslash: return "\\";
                case Key.Slash: return "/";
                case Key.Comma: return ",";
                case Key.Period: return ".";
                case Key.Semicolon: return ";";
                case Key.Quote: return "'";
                case Key.LeftBracket: return "[";
                case Key.RightBracket: return "]";
                case Key.Escape: return "Esc";
                case Key.UpArrow: return "↑";
                case Key.DownArrow: return "↓";
                case Key.LeftArrow: return "←";
                case Key.RightArrow: return "→";
            }
            string name = key.ToString();
            if (name.StartsWith("Digit")) return name.Substring(5);
            if (name.StartsWith("Numpad")) return "Num " + name.Substring(6);
            return name;
        }

        public static string Name(BindAction a) => Name(Get(a));
    }
}
