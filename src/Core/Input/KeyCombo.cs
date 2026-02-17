using System;

namespace TerrariaModder.Core.Input
{
    /// <summary>
    /// Represents a key combination (key + modifiers).
    /// </summary>
    public class KeyCombo
    {
        public int Key { get; set; }
        public bool Ctrl { get; set; }
        public bool Shift { get; set; }
        public bool Alt { get; set; }

        public KeyCombo() { }

        public KeyCombo(int key, bool ctrl = false, bool shift = false, bool alt = false)
        {
            Key = key;
            Ctrl = ctrl;
            Shift = shift;
            Alt = alt;
        }

        /// <summary>
        /// Parse a key combo string like "Ctrl+Shift+F5" or just "F5".
        /// </summary>
        public static KeyCombo Parse(string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return new KeyCombo();

            var combo = new KeyCombo();
            var parts = str.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var trimmed = part.Trim().ToUpperInvariant();

                switch (trimmed)
                {
                    case "CTRL":
                    case "CONTROL":
                        combo.Ctrl = true;
                        break;
                    case "SHIFT":
                        combo.Shift = true;
                        break;
                    case "ALT":
                        combo.Alt = true;
                        break;
                    default:
                        // Must be the actual key
                        combo.Key = KeyCode.Parse(trimmed);
                        break;
                }
            }

            return combo;
        }

        /// <summary>
        /// Check if this combo is currently pressed.
        /// </summary>
        public bool IsPressed()
        {
            if (Key == KeyCode.None) return false;

            // Check modifiers match exactly
            if (Ctrl != InputState.IsCtrlDown()) return false;
            if (Shift != InputState.IsShiftDown()) return false;
            if (Alt != InputState.IsAltDown()) return false;

            // Check main key is pressed
            return InputState.IsKeyDown(Key);
        }

        /// <summary>
        /// Check if this combo was just pressed this frame.
        /// </summary>
        public bool JustPressed()
        {
            if (Key == KeyCode.None) return false;

            // Check modifiers match exactly
            if (Ctrl != InputState.IsCtrlDown()) return false;
            if (Shift != InputState.IsShiftDown()) return false;
            if (Alt != InputState.IsAltDown()) return false;

            // Check main key was just pressed
            return InputState.IsKeyJustPressed(Key);
        }

        public override string ToString()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Shift) parts.Add("Shift");
            if (Alt) parts.Add("Alt");
            if (Key != KeyCode.None) parts.Add(KeyCode.GetName(Key));
            return string.Join("+", parts);
        }

        public override bool Equals(object obj)
        {
            if (obj is KeyCombo other)
            {
                return Key == other.Key && Ctrl == other.Ctrl && Shift == other.Shift && Alt == other.Alt;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Key ^ (Ctrl ? 0x1000 : 0) ^ (Shift ? 0x2000 : 0) ^ (Alt ? 0x4000 : 0);
        }

        public KeyCombo Clone() => new KeyCombo(Key, Ctrl, Shift, Alt);
    }
}
