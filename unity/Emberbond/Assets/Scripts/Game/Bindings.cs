using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Emberbond
{
    /// <summary>The actions a player can rebind. Menus, dialogue choices 1-3 and Escape stay fixed.</summary>
    public enum GameAction { Forward, Back, Left, Right, Jump, Sprint, Crouch, Interact, Summon, Attack, Block }

    /// <summary>One physical control: a keyboard key or a mouse button.</summary>
    public struct Bind
    {
        public bool Mouse;
        public int Code;                 // Key enum value, or mouse button 0 left, 1 right, 2 middle, 3 back, 4 forward

        public static Bind K(Key k) { return new Bind { Mouse = false, Code = (int)k }; }
        public static Bind M(int b) { return new Bind { Mouse = true, Code = b }; }
        public bool Equals(Bind o) { return Mouse == o.Mouse && Code == o.Code; }
        public string Serialise() { return (Mouse ? "m:" : "k:") + Code; }

        public static bool TryParse(string s, out Bind b)
        {
            b = default(Bind);
            if (string.IsNullOrEmpty(s) || s.Length < 3 || s[1] != ':') return false;
            int code;
            if (!int.TryParse(s.Substring(2), out code)) return false;
            if (s[0] == 'm') { if (code < 0 || code > 4) return false; b = M(code); return true; }
            if (s[0] == 'k')
            {
                if (!Enum.IsDefined(typeof(Key), code) || code == (int)Key.None || code == (int)Key.Escape || code == (int)Key.IMESelected) return false;
                b = K((Key)code); return true;
            }
            return false;
        }
    }

    /// <summary>Rebindable controls, stored in PlayerPrefs. Rebinding onto a control already in use swaps the two.</summary>
    public static class Bindings
    {
        public static readonly GameAction[] All = (GameAction[])Enum.GetValues(typeof(GameAction));
        private static readonly Dictionary<GameAction, Bind> _map = new Dictionary<GameAction, Bind>();
        private const string Prefix = "emberbond.bind.";

        static Bindings() { ResetDefaults(); }

        public static Bind Default(GameAction a)
        {
            switch (a)
            {
                case GameAction.Forward: return Bind.K(Key.W);
                case GameAction.Back: return Bind.K(Key.S);
                case GameAction.Left: return Bind.K(Key.A);
                case GameAction.Right: return Bind.K(Key.D);
                case GameAction.Jump: return Bind.K(Key.Space);
                case GameAction.Sprint: return Bind.K(Key.LeftShift);
                case GameAction.Crouch: return Bind.K(Key.LeftCtrl);
                case GameAction.Interact: return Bind.K(Key.E);
                case GameAction.Summon: return Bind.K(Key.G);
                case GameAction.Attack: return Bind.M(0);
                default: return Bind.M(1);
            }
        }

        public static void ResetDefaults()
        {
            _map.Clear();
            foreach (GameAction a in All) _map[a] = Default(a);
        }

        public static Bind Get(GameAction a) { return _map[a]; }

        /// <summary>Binds an action to a control; if another action had it, that action takes this action's old control.</summary>
        public static void Set(GameAction a, Bind b)
        {
            Bind old = _map[a];
            foreach (GameAction other in All)
            {
                if (other != a && _map[other].Equals(b)) { _map[other] = old; break; }
            }
            _map[a] = b;
        }

        /// <summary>Which keys may be assigned (not Escape, the digits used for dialogue, or odd system keys).</summary>
        public static bool Assignable(Key k)
        {
            if (k == Key.None || k == Key.Escape || k == Key.IMESelected) return false;
            if (k >= Key.Digit1 && k <= Key.Digit3) return false;
            return Enum.IsDefined(typeof(Key), (int)k);
        }

        public static bool Held(GameAction a)
        {
            Bind b = _map[a];
            if (b.Mouse)
            {
                Mouse m = Mouse.current;
                ButtonControl c = MouseButton(m, b.Code);
                return c != null && c.isPressed;
            }
            Keyboard k = Keyboard.current;
            if (k == null) return false;
            return k[(Key)b.Code].isPressed;
        }

        public static bool Pressed(GameAction a)
        {
            Bind b = _map[a];
            if (b.Mouse)
            {
                ButtonControl c = MouseButton(Mouse.current, b.Code);
                return c != null && c.wasPressedThisFrame;
            }
            Keyboard k = Keyboard.current;
            if (k == null) return false;
            return k[(Key)b.Code].wasPressedThisFrame;
        }

        private static ButtonControl MouseButton(Mouse m, int code)
        {
            if (m == null) return null;
            switch (code)
            {
                case 0: return m.leftButton;
                case 1: return m.rightButton;
                case 2: return m.middleButton;
                case 3: return m.backButton;
                case 4: return m.forwardButton;
            }
            return null;
        }

        // ------------------------------------------------------------ names

        public static string ActionName(GameAction a)
        {
            switch (a)
            {
                case GameAction.Forward: return "Move forward";
                case GameAction.Back: return "Move back";
                case GameAction.Left: return "Strafe left";
                case GameAction.Right: return "Strafe right";
                case GameAction.Jump: return "Jump / climb";
                case GameAction.Sprint: return "Sprint / boost";
                case GameAction.Crouch: return "Crouch / dive";
                case GameAction.Interact: return "Mount / land / interact";
                case GameAction.Summon: return "Summon wyrm";
                case GameAction.Attack: return "Strike / breathe fire";
                default: return "Parry";
            }
        }

        public static string Label(Bind b)
        {
            if (b.Mouse)
            {
                switch (b.Code)
                {
                    case 0: return "Left Mouse";
                    case 1: return "Right Mouse";
                    case 2: return "Middle Mouse";
                    case 3: return "Mouse Back";
                    default: return "Mouse Forward";
                }
            }
            return KeyName((Key)b.Code);
        }

        public static string Label(GameAction a) { return Label(_map[a]); }

        /// <summary>A short label for on-screen prompts: "E", "LMB", "Shift".</summary>
        public static string Short(GameAction a)
        {
            Bind b = _map[a];
            if (b.Mouse)
            {
                switch (b.Code) { case 0: return "LMB"; case 1: return "RMB"; case 2: return "MMB"; case 3: return "Mouse 4"; default: return "Mouse 5"; }
            }
            string n = KeyName((Key)b.Code);
            return n.Replace("Left ", "L-").Replace("Right ", "R-").Replace("Shift", "Shift").Replace("Control", "Ctrl");
        }

        private static string KeyName(Key k)
        {
            switch (k)
            {
                case Key.LeftShift: return "Left Shift";
                case Key.RightShift: return "Right Shift";
                case Key.LeftCtrl: return "Left Ctrl";
                case Key.RightCtrl: return "Right Ctrl";
                case Key.LeftAlt: return "Left Alt";
                case Key.RightAlt: return "Right Alt";
                case Key.Space: return "Space";
                case Key.Tab: return "Tab";
                case Key.Enter: return "Enter";
                case Key.Backspace: return "Backspace";
                case Key.UpArrow: return "Up Arrow";
                case Key.DownArrow: return "Down Arrow";
                case Key.LeftArrow: return "Left Arrow";
                case Key.RightArrow: return "Right Arrow";
                case Key.CapsLock: return "Caps Lock";
            }
            string s = k.ToString();
            if (s.StartsWith("Digit")) return s.Substring(5);
            if (s.StartsWith("Numpad")) return "Num " + s.Substring(6);
            return s;
        }

        // ------------------------------------------------------------ persistence

        public static void Save()
        {
            foreach (GameAction a in All) PlayerPrefs.SetString(Prefix + a, _map[a].Serialise());
            PlayerPrefs.Save();
        }

        /// <summary>Loads saved controls, ignoring damaged values and refusing duplicates.</summary>
        public static void Load()
        {
            ResetDefaults();
            Dictionary<GameAction, Bind> loaded = new Dictionary<GameAction, Bind>();
            HashSet<string> used = new HashSet<string>();
            foreach (GameAction a in All)
            {
                Bind b;
                if (!Bind.TryParse(PlayerPrefs.GetString(Prefix + a, ""), out b)) b = Default(a);
                if (!Assignable((Key)(b.Mouse ? (int)Key.A : b.Code))) b = Default(a);
                string id = b.Serialise();
                if (used.Contains(id)) { loaded.Clear(); break; }          // a clash means the data is untrustworthy: use defaults
                used.Add(id);
                loaded[a] = b;
            }
            if (loaded.Count == All.Length) foreach (KeyValuePair<GameAction, Bind> kv in loaded) _map[kv.Key] = kv.Value;
        }

        /// <summary>Compact text form used by the self-test to check round trips.</summary>
        public static string Dump()
        {
            List<string> parts = new List<string>();
            foreach (GameAction a in All) parts.Add(a + "=" + _map[a].Serialise());
            return string.Join(",", parts.ToArray());
        }
    }
}
