using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core.Debug
{
    /// <summary>
    /// Metadata for a registered debug command.
    /// </summary>
    public class CommandInfo
    {
        /// <summary>
        /// Full command name (e.g., "help" for core, "quick-keys.status" for mod commands).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Human-readable description of what the command does.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// The callback to execute when the command is invoked.
        /// </summary>
        internal Action<string[]> Callback { get; }

        /// <summary>
        /// The mod ID that registered this command, or null for core commands.
        /// </summary>
        public string ModId { get; }

        public CommandInfo(string name, string description, Action<string[]> callback, string modId = null)
        {
            Name = name;
            Description = description;
            Callback = callback;
            ModId = modId;
        }
    }

    /// <summary>
    /// Central registry for debug commands. Thread-safe.
    /// Core commands have no namespace prefix. Mod commands are prefixed with "modid.".
    /// </summary>
    public static class CommandRegistry
    {
        private static readonly ConcurrentDictionary<string, CommandInfo> _commands =
            new ConcurrentDictionary<string, CommandInfo>(StringComparer.OrdinalIgnoreCase);

        private static ILogger _log;

        /// <summary>
        /// Event fired when a command writes output via <see cref="Write"/>.
        /// Subscribe to this to display command output in a console UI.
        /// </summary>
        public static event Action<string> OnOutput;

        /// <summary>
        /// Event fired when the console output should be cleared.
        /// </summary>
        public static event Action OnClearOutput;

        /// <summary>
        /// Write output from a command. This fires the <see cref="OnOutput"/> event
        /// and also logs the message. Commands should use this instead of logging directly.
        /// Subscriber exceptions are caught to prevent crashing the caller.
        /// </summary>
        public static void Write(string message)
        {
            var safeMessage = message ?? "";
            try
            {
                OnOutput?.Invoke(safeMessage);
            }
            catch (Exception ex)
            {
                _log?.Error($"[CommandRegistry] OnOutput subscriber threw: {ex.Message}");
            }
            _log?.Info(safeMessage);
        }

        /// <summary>
        /// Request that the console UI clear its output.
        /// Subscriber exceptions are caught to prevent crashing the caller.
        /// </summary>
        public static void Clear()
        {
            try
            {
                OnClearOutput?.Invoke();
            }
            catch (Exception ex)
            {
                _log?.Error($"[CommandRegistry] OnClearOutput subscriber threw: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize the command registry.
        /// </summary>
        public static void Initialize(ILogger logger)
        {
            _log = logger;
            _log.Info("[CommandRegistry] Initialized");
        }

        /// <summary>
        /// Register a core command (no namespace prefix).
        /// </summary>
        /// <param name="name">Command name (e.g., "help"). Must be lowercase, no spaces.</param>
        /// <param name="description">Human-readable description.</param>
        /// <param name="callback">Callback invoked with parsed arguments.</param>
        /// <returns>True if registered, false if name already taken.</returns>
        public static bool Register(string name, string description, Action<string[]> callback)
        {
            return RegisterInternal(name, description, callback, null);
        }

        /// <summary>
        /// Register a mod command with automatic namespacing.
        /// The full command name will be "modId.name".
        /// </summary>
        /// <param name="modId">The mod's ID.</param>
        /// <param name="name">Command name within the mod (e.g., "status"). Must be lowercase, no spaces.</param>
        /// <param name="description">Human-readable description.</param>
        /// <param name="callback">Callback invoked with parsed arguments.</param>
        /// <returns>True if registered, false if name already taken or invalid.</returns>
        public static bool RegisterForMod(string modId, string name, string description, Action<string[]> callback)
        {
            if (string.IsNullOrEmpty(modId))
            {
                _log?.Warn("[CommandRegistry] Cannot register command with empty mod ID");
                return false;
            }

            string fullName = $"{modId}.{name}";
            return RegisterInternal(fullName, description, callback, modId);
        }

        private static bool RegisterInternal(string name, string description, Action<string[]> callback, string modId)
        {
            if (string.IsNullOrEmpty(name))
            {
                _log?.Warn("[CommandRegistry] Cannot register command with empty name");
                return false;
            }

            if (callback == null)
            {
                _log?.Warn($"[CommandRegistry] Cannot register command '{name}' with null callback");
                return false;
            }

            // Validate name: no spaces
            if (name.Contains(" "))
            {
                _log?.Warn($"[CommandRegistry] Command name '{name}' contains spaces");
                return false;
            }

            // Normalize to lowercase for consistent storage and display
            name = name.ToLowerInvariant();

            var info = new CommandInfo(name, description ?? "", callback, modId);

            if (_commands.TryAdd(name, info))
            {
                string source = modId != null ? $"[{modId}]" : "[core]";
                _log?.Info($"[CommandRegistry] Registered command: {name} {source}");
                return true;
            }

            _log?.Warn($"[CommandRegistry] Command '{name}' already registered");
            return false;
        }

        /// <summary>
        /// Execute a command from raw input string.
        /// Splits input into command name and arguments.
        /// </summary>
        /// <param name="input">Raw input (e.g., "help mods" or "quick-keys.status").</param>
        /// <returns>True if command was found and executed, false otherwise.</returns>
        public static bool Execute(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            // Split into command and args
            var parts = input.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string commandName = parts[0];
            string[] args = parts.Length > 1 ? parts.Skip(1).ToArray() : Array.Empty<string>();

            if (!_commands.TryGetValue(commandName, out var command))
            {
                _log?.Warn($"[CommandRegistry] Unknown command: {commandName}");
                return false;
            }

            try
            {
                _log?.Debug($"[CommandRegistry] Executing: {commandName} {string.Join(" ", args)}");
                command.Callback(args);
                return true;
            }
            catch (Exception ex)
            {
                _log?.Error($"[CommandRegistry] Error executing '{commandName}': {ex.Message}");
                Write($"Error in '{commandName}': {ex.Message}");
                return true; // Command was found but errored - return true so UI doesn't show "unknown command"
            }
        }

        /// <summary>
        /// Get all registered commands.
        /// </summary>
        public static IReadOnlyList<CommandInfo> GetCommands()
        {
            return _commands.Values.OrderBy(c => c.Name).ToList();
        }

        /// <summary>
        /// Get commands registered by a specific mod.
        /// </summary>
        public static IReadOnlyList<CommandInfo> GetCommandsForMod(string modId)
        {
            return _commands.Values
                .Where(c => c.ModId != null && c.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Name)
                .ToList();
        }

        /// <summary>
        /// Get core commands (no mod namespace).
        /// </summary>
        public static IReadOnlyList<CommandInfo> GetCoreCommands()
        {
            return _commands.Values
                .Where(c => c.ModId == null)
                .OrderBy(c => c.Name)
                .ToList();
        }

        /// <summary>
        /// Get help text for a specific command.
        /// </summary>
        /// <param name="commandName">The command name to look up.</param>
        /// <returns>The description, or null if command not found.</returns>
        public static string GetHelp(string commandName)
        {
            if (_commands.TryGetValue(commandName, out var command))
                return command.Description;
            return null;
        }

        /// <summary>
        /// Check if a command exists.
        /// </summary>
        public static bool HasCommand(string commandName)
        {
            return _commands.ContainsKey(commandName);
        }

        /// <summary>
        /// Unregister all commands for a mod (used during mod unload).
        /// </summary>
        public static void UnregisterMod(string modId)
        {
            var toRemove = _commands.Values
                .Where(c => c.ModId != null && c.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name)
                .ToList();

            foreach (var name in toRemove)
            {
                _commands.TryRemove(name, out _);
            }

            if (toRemove.Count > 0)
            {
                _log?.Info($"[CommandRegistry] Unregistered {toRemove.Count} command(s) for mod '{modId}'");
            }
        }
    }
}
