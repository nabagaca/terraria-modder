using System;
using System.Reflection;
using System.Runtime.InteropServices;
using TerrariaModder.Core.Logging;

namespace DebugTools
{
    /// <summary>
    /// Manages game and console window visibility via P/Invoke.
    /// Extracted from the RunHidden mod.
    /// </summary>
    internal static class WindowManager
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private static ILogger _log;
        private static volatile IntPtr _gameWindowHandle;
        private static volatile IntPtr _consoleWindowHandle;
        private static volatile bool _isHidden;
        private static bool _startHidden;

        public static bool IsHidden => _isHidden;

        public static void Initialize(ILogger log, bool startHidden)
        {
            _log = log;
            _startHidden = startHidden;

            // Grab console window handle immediately (always available)
            _consoleWindowHandle = GetConsoleWindow();
            if (_consoleWindowHandle != IntPtr.Zero)
                _log.Debug("[WindowManager] Console window handle acquired");

            // If startHidden, hide console immediately
            if (_startHidden && _consoleWindowHandle != IntPtr.Zero)
            {
                ShowWindow(_consoleWindowHandle, SW_HIDE);
                _log.Info("[WindowManager] Console hidden (startHidden=true)");
            }
        }

        /// <summary>
        /// Called from OnGameReady lifecycle hook when Main.Initialize() completes.
        /// Game window handle is available at this point.
        /// </summary>
        public static void AcquireGameWindowHandle()
        {
            if (_gameWindowHandle != IntPtr.Zero) return;

            var handle = FindGameWindowHandle();
            if (handle != IntPtr.Zero)
            {
                _gameWindowHandle = handle;
                _log.Info("[WindowManager] Game window handle acquired");

                if (_startHidden)
                {
                    ShowWindow(_gameWindowHandle, SW_HIDE);
                    _isHidden = true;
                    _log.Info("[WindowManager] Game window hidden (startHidden=true)");
                }
            }
        }

        public static void Show()
        {
            if (_gameWindowHandle != IntPtr.Zero)
            {
                ShowWindow(_gameWindowHandle, SW_SHOW);
                SetForegroundWindow(_gameWindowHandle);
            }

            if (_consoleWindowHandle != IntPtr.Zero)
                ShowWindow(_consoleWindowHandle, SW_SHOW);

            _isHidden = false;
            _log?.Info("[WindowManager] Windows shown");
        }

        public static void Hide()
        {
            if (_gameWindowHandle != IntPtr.Zero)
                ShowWindow(_gameWindowHandle, SW_HIDE);

            if (_consoleWindowHandle != IntPtr.Zero)
                ShowWindow(_consoleWindowHandle, SW_HIDE);

            _isHidden = true;
            _log?.Info("[WindowManager] Windows hidden");
        }

        /// <summary>
        /// Restore windows if hidden (called during Unload).
        /// </summary>
        public static void RestoreIfHidden()
        {
            if (_isHidden)
            {
                try
                {
                    if (_gameWindowHandle != IntPtr.Zero)
                        ShowWindow(_gameWindowHandle, SW_SHOW);
                    if (_consoleWindowHandle != IntPtr.Zero)
                        ShowWindow(_consoleWindowHandle, SW_SHOW);
                }
                catch { }
            }
        }

        private static IntPtr FindGameWindowHandle()
        {
            try
            {
                var mainType = Type.GetType("Terraria.Main, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Main");
                if (mainType == null) return IntPtr.Zero;

                var instance = mainType.GetField("instance",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (instance == null) return IntPtr.Zero;

                var gameWindow = mainType.GetProperty("Window")?.GetValue(instance);
                if (gameWindow == null) return IntPtr.Zero;

                var handle = gameWindow.GetType().GetProperty("Handle")?.GetValue(gameWindow);
                if (handle is IntPtr h && h != IntPtr.Zero)
                    return h;
            }
            catch { }

            return IntPtr.Zero;
        }
    }
}
