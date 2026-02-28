using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows.Forms;
using Terraria;
using TerrariaModder.Core.Logging;

namespace WidescreenTools.Patches
{
    internal static class WidescreenResolutionOverride
    {
        private const int EnumCurrentSettings = -1;

        private static ILogger _log;
        private static FieldInfo _maxScreenWField;
        private static FieldInfo _maxScreenHField;
        private static FieldInfo _renderTargetMaxSizeField;
        private static FieldInfo _displayWidthField;
        private static FieldInfo _displayHeightField;
        private static FieldInfo _numDisplayModesField;
        private static FieldInfo _minScreenWField;
        private static FieldInfo _minScreenHField;
        private static MethodInfo _registerDisplayResolutionMethod;
        private static bool _initialized;

        public static void Initialize(ILogger log)
        {
            _log = log;

            if (_initialized)
            {
                return;
            }

            Type mainType = typeof(Main);
            _maxScreenWField = mainType.GetField("maxScreenW", BindingFlags.Public | BindingFlags.Static);
            _maxScreenHField = mainType.GetField("maxScreenH", BindingFlags.Public | BindingFlags.Static);
            _renderTargetMaxSizeField = mainType.GetField("_renderTargetMaxSize", BindingFlags.NonPublic | BindingFlags.Static);
            _displayWidthField = mainType.GetField("displayWidth", BindingFlags.Public | BindingFlags.Static);
            _displayHeightField = mainType.GetField("displayHeight", BindingFlags.Public | BindingFlags.Static);
            _numDisplayModesField = mainType.GetField("numDisplayModes", BindingFlags.Public | BindingFlags.Static);
            _minScreenWField = mainType.GetField("minScreenW", BindingFlags.Public | BindingFlags.Static);
            _minScreenHField = mainType.GetField("minScreenH", BindingFlags.Public | BindingFlags.Static);
            _registerDisplayResolutionMethod = mainType.GetMethod("RegisterDisplayResolution", BindingFlags.NonPublic | BindingFlags.Static);
            _initialized = true;
        }

        public static bool Apply()
        {
            if (!_initialized)
            {
                Initialize(_log);
            }

            if (_maxScreenWField == null || _maxScreenHField == null || _displayWidthField == null || _displayHeightField == null)
            {
                _log?.Warn("[WidescreenTools] Could not resolve Terraria resolution fields");
                return false;
            }

            try
            {
                HashSet<(int width, int height)> modes = GetMonitorModes();
                if (modes.Count == 0)
                {
                    _log?.Warn("[WidescreenTools] No monitor modes were discovered");
                    return false;
                }

                int startingCount = GetSafeInt(_numDisplayModesField);
                int maxWidth = GetSafeInt(_maxScreenWField);
                int maxHeight = GetSafeInt(_maxScreenHField);
                int minWidth = GetSafeInt(_minScreenWField);
                int minHeight = GetSafeInt(_minScreenHField);

                foreach (var mode in modes)
                {
                    maxWidth = Math.Max(maxWidth, mode.width);
                    maxHeight = Math.Max(maxHeight, mode.height);
                }

                _maxScreenWField.SetValue(null, maxWidth);
                _maxScreenHField.SetValue(null, maxHeight);

                if (_renderTargetMaxSizeField != null)
                {
                    int renderTargetMax = maxWidth + 200 * 2 * maxWidth / 1920;
                    int current = GetSafeInt(_renderTargetMaxSizeField);
                    if (renderTargetMax > current)
                    {
                        _renderTargetMaxSizeField.SetValue(null, renderTargetMax);
                    }
                }

                int registered = 0;
                foreach (var mode in modes)
                {
                    if (mode.width < minWidth || mode.height < minHeight)
                    {
                        continue;
                    }

                    if (RegisterMode(mode.width, mode.height))
                    {
                        registered++;
                    }
                }

                int endingCount = GetSafeInt(_numDisplayModesField);
                _log?.Info($"[WidescreenTools] Discovered {modes.Count} monitor mode(s); display list {startingCount} -> {endingCount}");
                _log?.Info($"[WidescreenTools] Resolution caps set to {maxWidth}x{maxHeight}; added {registered} display mode(s)");
                return true;
            }
            catch (Exception ex)
            {
                _log?.Error($"[WidescreenTools] Failed to unlock high resolutions: {ex.Message}");
                return false;
            }
        }

        private static HashSet<(int width, int height)> GetMonitorModes()
        {
            var result = new HashSet<(int width, int height)>();

            foreach (Screen screen in Screen.AllScreens)
            {
                foreach (var mode in EnumerateModes(screen.DeviceName))
                {
                    result.Add(mode);
                }

                // Keep the current bounds as a fallback if enumeration returns nothing.
                result.Add((screen.Bounds.Width, screen.Bounds.Height));
            }

            return result;
        }

        private static IEnumerable<(int width, int height)> EnumerateModes(string deviceName)
        {
            var seen = new HashSet<(int width, int height)>();
            var mode = CreateDevMode();
            int index = 0;

            while (EnumDisplaySettings(deviceName, index, ref mode))
            {
                if (mode.dmPelsWidth > 0 && mode.dmPelsHeight > 0)
                {
                    var key = ((int)mode.dmPelsWidth, (int)mode.dmPelsHeight);
                    if (seen.Add(key))
                    {
                        yield return key;
                    }
                }

                index++;
                mode = CreateDevMode();
            }

            mode = CreateDevMode();
            if (EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode))
            {
                var current = ((int)mode.dmPelsWidth, (int)mode.dmPelsHeight);
                if (current.Item1 > 0 && current.Item2 > 0 && seen.Add(current))
                {
                    yield return current;
                }
            }
        }

        private static bool RegisterMode(int width, int height)
        {
            int[] widths = _displayWidthField?.GetValue(null) as int[];
            int[] heights = _displayHeightField?.GetValue(null) as int[];
            if (widths == null || heights == null || _numDisplayModesField == null || _registerDisplayResolutionMethod == null)
            {
                return false;
            }

            int count = GetSafeInt(_numDisplayModesField);
            for (int i = 0; i < count && i < widths.Length && i < heights.Length; i++)
            {
                if (widths[i] == width && heights[i] == height)
                {
                    return false;
                }
            }

            _registerDisplayResolutionMethod.Invoke(null, new object[] { width, height });
            return true;
        }

        private static int GetSafeInt(FieldInfo field)
        {
            if (field == null)
            {
                return 0;
            }

            object value = field.GetValue(null);
            if (value is int i)
            {
                return i;
            }

            return 0;
        }

        private static NativeDevMode CreateDevMode()
        {
            var mode = new NativeDevMode();
            mode.dmDeviceName = new string('\0', 32);
            mode.dmFormName = new string('\0', 32);
            mode.dmSize = (short)Marshal.SizeOf(typeof(NativeDevMode));
            return mode;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref NativeDevMode devMode);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NativeDevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;

            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;

            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }
    }
}
