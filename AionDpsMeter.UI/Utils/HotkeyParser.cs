using System.Windows.Input;

namespace AionDpsMeter.UI.Utils
{
    public static class HotkeyParser
    {
        // MOD_NONE=0, MOD_ALT=1, MOD_CTRL=2, MOD_SHIFT=4, MOD_WIN=8
        public const uint MOD_NONE  = 0;
        public const uint MOD_ALT   = 0x0001;
        public const uint MOD_CTRL  = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN   = 0x0008;

        /// <summary>
        /// Parses a hotkey string like "Ctrl+Shift+D" into (modifiers, virtualKey).
        /// Returns (0,0) if parsing fails or no main key found.
        /// </summary>
        public static (uint modifiers, uint vk) Parse(string? hotkey)
        {
            if (string.IsNullOrWhiteSpace(hotkey))
                return (0, 0);

            var parts = hotkey.Split('+');
            uint mods = 0;
            Key mainKey = Key.None;

            foreach (var raw in parts)
            {
                var part = raw.Trim();
                switch (part.ToUpperInvariant())
                {
                    case "CTRL":  mods |= MOD_CTRL;  break;
                    case "SHIFT": mods |= MOD_SHIFT; break;
                    case "ALT":   mods |= MOD_ALT;   break;
                    case "WIN":   mods |= MOD_WIN;   break;
                    default:
                        mainKey = ParseKey(part);
                        break;
                }
            }

            if (mainKey == Key.None)
                return (0, 0);

            var vk = (uint)KeyInterop.VirtualKeyFromKey(mainKey);
            return (mods, vk);
        }

        // Browser (KeyboardEvent.key / .code) names that differ from WPF's Key enum names.
        private static readonly Dictionary<string, Key> BrowserKeyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ArrowUp"] = Key.Up, ["ArrowDown"] = Key.Down, ["ArrowLeft"] = Key.Left, ["ArrowRight"] = Key.Right,
            [" "] = Key.Space, ["Minus"] = Key.OemMinus, ["Equal"] = Key.OemPlus, ["Comma"] = Key.OemComma,
            ["Period"] = Key.OemPeriod, ["Slash"] = Key.OemQuestion, ["Semicolon"] = Key.OemSemicolon,
            ["Quote"] = Key.OemQuotes, ["BracketLeft"] = Key.OemOpenBrackets, ["BracketRight"] = Key.OemCloseBrackets,
            ["Backslash"] = Key.OemPipe, ["Backquote"] = Key.OemTilde, ["NumpadAdd"] = Key.Add,
            ["NumpadSubtract"] = Key.Subtract, ["NumpadMultiply"] = Key.Multiply, ["NumpadDivide"] = Key.Divide,
            ["NumpadDecimal"] = Key.Decimal, ["NumpadEnter"] = Key.Enter,
        };

        private static Key ParseKey(string name)
        {
            if (BrowserKeyNames.TryGetValue(name, out var key)) return key;

            // "1" would otherwise parse as the enum's numeric value (Key 1 = Cancel), not the 1 key.
            if (name.Length == 1 && char.IsDigit(name[0])) return Key.D0 + (name[0] - '0');

            // "Numpad1" -> NumPad1 (Enum.TryParse is case-insensitive).
            if (name.All(char.IsDigit)) return Key.None;
            return Enum.TryParse<Key>(name, ignoreCase: true, out var k) ? k : Key.None;
        }
    }
}
