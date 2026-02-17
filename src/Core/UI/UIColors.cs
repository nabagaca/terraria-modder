using System;
using System.IO;
using System.Text.RegularExpressions;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core.UI
{
    /// <summary>
    /// RGBA color value for UI rendering.
    /// </summary>
    public struct Color4
    {
        public readonly byte R, G, B, A;

        public Color4(byte r, byte g, byte b, byte a = 255)
        {
            R = r; G = g; B = b; A = a;
        }

        /// <summary>
        /// Create a copy with a different alpha value.
        /// </summary>
        public Color4 WithAlpha(byte a) => new Color4(R, G, B, a);
    }

    /// <summary>
    /// Centralized UI color palette with colorblind theme support.
    /// All mod UIs should use these semantic colors instead of hardcoded RGB values.
    /// </summary>
    public static class UIColors
    {
        private static ILogger _log;
        private static string _prefsPath;
        private static string _currentTheme = "normal";

        // --- Panel backgrounds ---
        public static Color4 PanelBg;
        public static Color4 HeaderBg;
        public static Color4 SectionBg;
        public static Color4 TooltipBg;

        // --- Item list / slots ---
        public static Color4 ItemBg;
        public static Color4 ItemHoverBg;
        public static Color4 ItemActiveBg;

        // --- Text ---
        public static Color4 Text;
        public static Color4 TextDim;
        public static Color4 TextHint;
        public static Color4 TextTitle;

        // --- Status indicators ---
        public static Color4 Success;
        public static Color4 Error;
        public static Color4 Warning;
        public static Color4 Info;

        // --- Interactive elements ---
        public static Color4 Button;
        public static Color4 ButtonHover;
        public static Color4 Accent;
        public static Color4 AccentText;

        // --- UI chrome ---
        public static Color4 Divider;
        public static Color4 ScrollTrack;
        public static Color4 ScrollThumb;
        public static Color4 Border;
        public static Color4 InputBg;
        public static Color4 InputFocusBg;

        // --- Slider ---
        public static Color4 SliderTrack;
        public static Color4 SliderThumb;
        public static Color4 SliderThumbHover;

        // --- Close button ---
        public static Color4 CloseBtn;
        public static Color4 CloseBtnHover;

        // --- Console-specific ---
        public static Color4 ConsoleCommand;
        public static Color4 ConsolePrompt;

        /// <summary>Current theme name.</summary>
        public static string CurrentTheme => _currentTheme;

        /// <summary>Available theme names.</summary>
        public static readonly string[] ThemeNames = { "normal", "red-green", "blue-yellow", "high-contrast" };

        /// <summary>
        /// Initialize colors from preferences file.
        /// </summary>
        public static void Initialize(string corePath, ILogger log)
        {
            _log = log;
            _prefsPath = Path.Combine(corePath, "preferences.json");

            // Load saved theme
            string savedTheme = "normal";
            if (File.Exists(_prefsPath))
            {
                try
                {
                    string json = File.ReadAllText(_prefsPath);
                    var match = Regex.Match(json, @"""colorTheme""\s*:\s*""([^""]*)""", RegexOptions.IgnoreCase);
                    if (match.Success)
                        savedTheme = match.Groups[1].Value;
                }
                catch (Exception ex)
                {
                    _log?.Error($"[UIColors] Failed to load preferences: {ex.Message}");
                }
            }

            ApplyTheme(savedTheme);
            _log?.Info($"[UIColors] Initialized with theme: {_currentTheme}");
        }

        /// <summary>
        /// Switch to a different theme and persist the choice.
        /// </summary>
        public static void SetTheme(string themeName)
        {
            ApplyTheme(themeName);
            SavePreferences();
            _log?.Info($"[UIColors] Theme changed to: {_currentTheme}");
        }

        private static void ApplyTheme(string themeName)
        {
            // Validate theme name
            bool valid = false;
            foreach (var name in ThemeNames)
            {
                if (string.Equals(name, themeName, StringComparison.OrdinalIgnoreCase))
                {
                    themeName = name; // normalize case
                    valid = true;
                    break;
                }
            }
            if (!valid) themeName = "normal";
            _currentTheme = themeName;

            // Apply base colors first, then override for specific themes
            ApplyNormalTheme();

            switch (themeName)
            {
                case "red-green":
                    ApplyRedGreenTheme();
                    break;
                case "blue-yellow":
                    ApplyBlueYellowTheme();
                    break;
                case "high-contrast":
                    ApplyHighContrastTheme();
                    break;
            }
        }

        private static void ApplyNormalTheme()
        {
            // Panel backgrounds
            PanelBg = new Color4(30, 30, 45, 240);
            HeaderBg = new Color4(50, 50, 70);
            SectionBg = new Color4(35, 35, 55);
            TooltipBg = new Color4(20, 20, 30, 240);

            // Item list / slots
            ItemBg = new Color4(45, 45, 65, 200);
            ItemHoverBg = new Color4(60, 60, 90, 220);
            ItemActiveBg = new Color4(70, 70, 100);

            // Text
            Text = new Color4(255, 255, 255);
            TextDim = new Color4(180, 180, 180);
            TextHint = new Color4(120, 120, 120);
            TextTitle = new Color4(255, 255, 100);

            // Status
            Success = new Color4(100, 255, 100);
            Error = new Color4(255, 100, 100);
            Warning = new Color4(255, 200, 100);
            Info = new Color4(100, 150, 200);

            // Interactive
            Button = new Color4(60, 60, 80);
            ButtonHover = new Color4(80, 80, 110);
            Accent = new Color4(100, 150, 200);
            AccentText = new Color4(255, 255, 180);

            // Chrome
            Divider = new Color4(60, 60, 80);
            ScrollTrack = new Color4(30, 30, 40, 200);
            ScrollThumb = new Color4(80, 80, 100);
            Border = new Color4(100, 100, 140);
            InputBg = new Color4(35, 35, 50);
            InputFocusBg = new Color4(50, 50, 70);

            // Slider
            SliderTrack = new Color4(40, 40, 60);
            SliderThumb = new Color4(80, 80, 100);
            SliderThumbHover = new Color4(120, 120, 150);

            // Close button
            CloseBtn = new Color4(60, 40, 40);
            CloseBtnHover = new Color4(100, 50, 50);

            // Console
            ConsoleCommand = new Color4(180, 180, 255);
            ConsolePrompt = new Color4(100, 200, 100);
        }

        /// <summary>
        /// Deuteranopia + Protanopia: swap green→blue, red→orange for status colors.
        /// </summary>
        private static void ApplyRedGreenTheme()
        {
            Success = new Color4(80, 180, 255);       // blue instead of green
            Error = new Color4(255, 165, 0);           // orange instead of red
            Warning = new Color4(255, 255, 80);        // bright yellow
            ConsolePrompt = new Color4(80, 180, 255);  // blue prompt
        }

        /// <summary>
        /// Tritanopia: swap blue→magenta, keep red/green.
        /// </summary>
        private static void ApplyBlueYellowTheme()
        {
            Info = new Color4(230, 100, 180);           // magenta instead of blue
            Accent = new Color4(230, 100, 180);         // magenta accent
            AccentText = new Color4(255, 200, 200);     // pinkish instead of yellow-tint
            Error = new Color4(255, 80, 120);           // red/magenta (more distinct from green)
            ConsoleCommand = new Color4(230, 150, 200); // pink-tinted command echo
        }

        /// <summary>
        /// High contrast: stronger, purer colors with more background contrast.
        /// </summary>
        private static void ApplyHighContrastTheme()
        {
            // Darker backgrounds for more contrast
            PanelBg = new Color4(10, 10, 20, 250);
            HeaderBg = new Color4(30, 30, 50);
            SectionBg = new Color4(20, 20, 35);
            TooltipBg = new Color4(5, 5, 15, 250);
            ItemBg = new Color4(25, 25, 40, 220);
            ItemHoverBg = new Color4(50, 50, 80, 240);
            ItemActiveBg = new Color4(60, 60, 100);
            InputBg = new Color4(15, 15, 30);

            // Brighter text
            TextDim = new Color4(200, 200, 200);
            TextHint = new Color4(150, 150, 150);

            // Pure status colors
            Success = new Color4(0, 255, 0);
            Error = new Color4(255, 50, 50);
            Warning = new Color4(255, 255, 0);
            Info = new Color4(0, 200, 255);

            // Stronger interactive contrast
            Button = new Color4(40, 40, 70);
            ButtonHover = new Color4(80, 80, 130);
            Accent = new Color4(0, 200, 255);
        }

        /// <summary>
        /// Get Terraria's rarity color for item display. Not themed - matches vanilla.
        /// </summary>
        public static Color4 GetRarityColor(int rarity)
        {
            switch (rarity)
            {
                case -1: return new Color4(130, 130, 130);  // Gray
                case 0:  return new Color4(255, 255, 255);  // White
                case 1:  return new Color4(150, 150, 255);  // Blue
                case 2:  return new Color4(150, 255, 150);  // Green
                case 3:  return new Color4(255, 200, 150);  // Orange
                case 4:  return new Color4(255, 150, 150);  // Light Red
                case 5:  return new Color4(255, 150, 255);  // Pink
                case 6:  return new Color4(210, 160, 255);  // Light Purple
                case 7:  return new Color4(150, 255, 10);   // Lime
                case 8:  return new Color4(255, 255, 10);   // Yellow
                case 9:  return new Color4(5, 200, 255);    // Cyan
                case 10: return new Color4(255, 40, 100);   // Red
                case 11: return new Color4(180, 40, 255);   // Purple
                default: return new Color4(255, 255, 255);  // Default white
            }
        }

        private static void SavePreferences()
        {
            if (string.IsNullOrEmpty(_prefsPath)) return;

            try
            {
                string dir = Path.GetDirectoryName(_prefsPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = "{\n    \"colorTheme\": \"" + _currentTheme + "\"\n}\n";
                File.WriteAllText(_prefsPath, json);
            }
            catch (Exception ex)
            {
                _log?.Error($"[UIColors] Failed to save preferences: {ex.Message}");
            }
        }
    }
}
