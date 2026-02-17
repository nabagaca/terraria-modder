using System;
using TerrariaModder.Core;
using TerrariaModder.Core.Debug;
using TerrariaModder.Core.Logging;

namespace DebugTools
{
    public class Mod : IMod
    {
        public string Id => "debug-tools";
        public string Name => "Debug Tools";
        public string Version => "1.0.0";

        private static Mod _instance;
        private ILogger _log;
        private DebugHttpServer _httpServer;
        private ConsoleUI _console;
        private bool _httpEnabled;

        public void Initialize(ModContext context)
        {
            _log = context.Logger;
            _instance = this;

            if (!context.Config.Get<bool>("enabled", true))
            {
                _log.Info("Debug Tools disabled in config");
                return;
            }

            _httpEnabled = context.Config.Get("httpServer", true);
            bool startHidden = context.Config.Get("startHidden", false);

            // Initialize window manager (grabs console handle, hides if startHidden)
            WindowManager.Initialize(_log, startHidden);

            // Initialize virtual input subsystems
            VirtualInputManager.Initialize(_log);
            VirtualInputActions.Initialize(_log);
            InputLogger.Initialize(_log);
            VirtualInputPatches.Initialize(_log);

            // Initialize console UI
            _console = new ConsoleUI();
            _console.Initialize(context);

            // Register menu navigation commands (backward-compatible names)
            RegisterMenuCommands();

            // Start HTTP server
            if (_httpEnabled)
            {
                try
                {
                    _httpServer = new DebugHttpServer(_log);
                    _httpServer.Start();
                }
                catch (Exception ex)
                {
                    _log.Error($"[DebugHttpServer] Failed to initialize: {ex.Message}");
                }
            }
            else
            {
                _log.Info("[DebugHttpServer] Disabled via config");
            }

            _log.Info("Debug Tools initialized");
        }

        /// <summary>
        /// Called by injector lifecycle scan when Main.Initialize() completes.
        /// </summary>
        public static void OnGameReady()
        {
            var inst = _instance;
            if (inst == null) return;

            try
            {
                VirtualInputPatches.ApplyPatches();
            }
            catch (Exception ex)
            {
                inst._log?.Error($"[DebugTools] VirtualInputPatches failed: {ex.Message}");
            }

            try
            {
                WindowManager.AcquireGameWindowHandle();
            }
            catch (Exception ex)
            {
                inst._log?.Error($"[DebugTools] Window handle acquisition failed: {ex.Message}");
            }
        }

        public void OnWorldLoad() { }

        public void OnWorldUnload()
        {
            try
            {
                VirtualInputManager.ReleaseAll();
            }
            catch (Exception ex)
            {
                _log?.Error($"[VirtualInput] Error releasing input on world unload: {ex.Message}");
            }

            _console?.Close();
        }

        public void Unload()
        {
            // Release virtual input
            try
            {
                VirtualInputManager.ReleaseAll();
                VirtualInputPatches.Cleanup();
            }
            catch (Exception ex)
            {
                _log?.Error($"[VirtualInput] Error during shutdown: {ex.Message}");
            }

            // Stop HTTP server
            try
            {
                _httpServer?.Dispose();
                _httpServer = null;
            }
            catch (Exception ex)
            {
                _log?.Error($"[DebugHttpServer] Error during shutdown: {ex.Message}");
            }

            // Restore windows if hidden
            WindowManager.RestoreIfHidden();

            // Clean up console
            _console?.Cleanup();
            _console = null;

            _instance = null;
            _log?.Info("Debug Tools unloaded");
        }

        private void RegisterMenuCommands()
        {
            var menuNav = new MenuNavigator(_log);

            CommandRegistry.Register("menu.state", "Show current menu state, available characters and worlds", args =>
            {
                var state = menuNav.GetMenuState();
                if (state.InWorld)
                {
                    CommandRegistry.Write($"In world: {state.WorldName}");
                    return;
                }
                if (!state.InMenu)
                {
                    CommandRegistry.Write("Unknown state (not in menu or world)");
                    return;
                }
                CommandRegistry.Write($"Menu: {state.MenuDescription} (mode {state.MenuMode})");
                if (state.Players != null && state.Players.Count > 0)
                {
                    CommandRegistry.Write($"Characters ({state.PlayerCount}):");
                    foreach (var p in state.Players)
                        CommandRegistry.Write($"  [{p.Index}] {p.Name}");
                }
                if (state.Worlds != null && state.Worlds.Count > 0)
                {
                    CommandRegistry.Write($"Worlds ({state.WorldCount}):");
                    foreach (var w in state.Worlds)
                        CommandRegistry.Write($"  [{w.Index}] {w.Name}");
                }
            });

            CommandRegistry.Register("menu.select", "Navigate to a menu target. Usage: menu.select <target> (singleplayer|character_N|world_N|play|back)", args =>
            {
                if (args.Length == 0)
                {
                    CommandRegistry.Write("Usage: menu.select <target>");
                    CommandRegistry.Write("Targets: singleplayer, character_N, world_N, play, back, title");
                    return;
                }
                var result = menuNav.Navigate(args[0]);
                CommandRegistry.Write(result.Success ? $"OK: {result.Message}" : $"FAIL: {result.Message}");
            });

            CommandRegistry.Register("menu.back", "Go back to title screen (Escape equivalent)", args =>
            {
                var result = menuNav.Navigate("back");
                CommandRegistry.Write(result.Success ? $"OK: {result.Message}" : $"FAIL: {result.Message}");
            });

            CommandRegistry.Register("menu.enter", "Enter a world. Usage: menu.enter [character] [world]", args =>
            {
                int charIdx = args.Length > 0 && int.TryParse(args[0], out int c) ? c : 0;
                int worldIdx = args.Length > 1 && int.TryParse(args[1], out int w) ? w : 0;
                CommandRegistry.Write($"Entering world: character={charIdx}, world={worldIdx}...");
                var result = menuNav.EnterWorld(charIdx, worldIdx);
                CommandRegistry.Write(result.Success ? $"OK: {result.Message}" : $"FAIL: {result.Message}");
            });
        }
    }
}
