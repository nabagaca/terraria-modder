using System;
using System.IO;
using System.Reflection;
using System.Text;
using TerrariaModder.Core.Logging;

namespace StorageHub.Debug
{
    /// <summary>
    /// Captures debug state to a single session log file.
    ///
    /// Design for user bug reports:
    /// - Single file per game session (overwrites previous)
    /// - Records important events with timestamps
    /// - Captures errors/failures prominently
    /// - User can send this file when reporting issues
    ///
    /// File location: TerrariaModder/mods/storage-hub/debug/session.log
    /// </summary>
    public class DebugDumper
    {
        private readonly ILogger _log;
        private readonly string _logPath;
        private readonly StringBuilder _sessionLog = new StringBuilder();
        private DateTime _sessionStart;
        private int _errorCount = 0;
        private int _warningCount = 0;

        // Reflection cache
        private static Type _mainType;
        private static Type _itemType;
        private static Type _chestType;
        private static Type _playerType;
        private static FieldInfo _chestArrayField;
        private static FieldInfo _playerArrayField;
        private static FieldInfo _myPlayerField;
        private static FieldInfo _playerInventoryField;
        private static FieldInfo _chestItemField;
        private static FieldInfo _chestXField;
        private static FieldInfo _chestYField;
        private static FieldInfo _itemTypeField;
        private static FieldInfo _itemStackField;
        private static PropertyInfo _itemNameProp;
        private static FieldInfo _mouseItemField;
        private static bool _initialized = false;

        public DebugDumper(ILogger log, string modFolder)
        {
            _log = log;
            _sessionStart = DateTime.Now;

            // Ensure debug folder exists
            var debugFolder = Path.Combine(modFolder, "debug");
            if (!Directory.Exists(debugFolder))
                Directory.CreateDirectory(debugFolder);

            _logPath = Path.Combine(debugFolder, "session.log");

            if (!_initialized)
            {
                InitReflection();
            }

            // Start fresh session log
            _sessionLog.Clear();
            _sessionLog.AppendLine("================================================================================");
            _sessionLog.AppendLine("STORAGE HUB DEBUG SESSION");
            _sessionLog.AppendLine($"Started: {_sessionStart:yyyy-MM-dd HH:mm:ss}");
            _sessionLog.AppendLine($"Framework: TerrariaModder");
            _sessionLog.AppendLine("================================================================================");
            _sessionLog.AppendLine();

            WriteToFile();
        }

        private void InitReflection()
        {
            try
            {
                _mainType = Type.GetType("Terraria.Main, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Main");
                _itemType = Type.GetType("Terraria.Item, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Item");
                _chestType = Type.GetType("Terraria.Chest, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Chest");
                _playerType = Type.GetType("Terraria.Player, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Player");

                if (_mainType != null)
                {
                    _chestArrayField = _mainType.GetField("chest", BindingFlags.Public | BindingFlags.Static);
                    _playerArrayField = _mainType.GetField("player", BindingFlags.Public | BindingFlags.Static);
                    _myPlayerField = _mainType.GetField("myPlayer", BindingFlags.Public | BindingFlags.Static);
                    _mouseItemField = _mainType.GetField("mouseItem", BindingFlags.Public | BindingFlags.Static);
                }

                if (_chestType != null)
                {
                    _chestItemField = _chestType.GetField("item", BindingFlags.Public | BindingFlags.Instance);
                    _chestXField = _chestType.GetField("x", BindingFlags.Public | BindingFlags.Instance);
                    _chestYField = _chestType.GetField("y", BindingFlags.Public | BindingFlags.Instance);
                }

                if (_itemType != null)
                {
                    _itemTypeField = _itemType.GetField("type", BindingFlags.Public | BindingFlags.Instance);
                    _itemStackField = _itemType.GetField("stack", BindingFlags.Public | BindingFlags.Instance);
                    _itemNameProp = _itemType.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                }

                if (_playerType != null)
                {
                    _playerInventoryField = _playerType.GetField("inventory", BindingFlags.Public | BindingFlags.Instance);
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                _log.Error($"DebugDumper init failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Log an event (no state dump, just a timestamped message).
        /// </summary>
        public void LogEvent(string message)
        {
            var timestamp = GetTimestamp();
            _sessionLog.AppendLine($"[{timestamp}] {message}");
            WriteToFile();
        }

        /// <summary>
        /// Log a world load event with config summary.
        /// </summary>
        public void DumpState(string actionLabel)
        {
            var timestamp = GetTimestamp();
            _sessionLog.AppendLine();
            _sessionLog.AppendLine($"[{timestamp}] === {actionLabel} ===");

            // Just log the event, not full inventory spam
            var cursorInfo = GetCursorItemSummary();
            if (!string.IsNullOrEmpty(cursorInfo))
            {
                _sessionLog.AppendLine($"  Cursor: {cursorInfo}");
            }

            WriteToFile();
        }

        /// <summary>
        /// Log a shimmer operation with before/after context.
        /// Only logs summary, not full inventory dumps.
        /// </summary>
        public void DumpShimmerOperation(string phase, int inputItemId, int inputStack, int outputItemId, int outputStack, int sourceChest, int sourceSlot)
        {
            var timestamp = GetTimestamp();

            if (phase == "BEFORE")
            {
                _sessionLog.AppendLine();
                _sessionLog.AppendLine($"[{timestamp}] SHIMMER: Taking {inputStack}x ID:{inputItemId} from chest {sourceChest} slot {sourceSlot}");
                _sessionLog.AppendLine($"           Expected output: {outputStack}x ID:{outputItemId}");
            }
            else if (phase == "AFTER")
            {
                _sessionLog.AppendLine($"[{timestamp}] SHIMMER: Complete");
            }

            WriteToFile();
        }

        /// <summary>
        /// Log a state dump with chest context (for errors).
        /// </summary>
        public void DumpStateWithChest(string actionLabel, int chestIndex)
        {
            var timestamp = GetTimestamp();

            bool isError = actionLabel.Contains("FAILED");
            if (isError) _errorCount++;

            _sessionLog.AppendLine();
            _sessionLog.AppendLine($"[{timestamp}] {(isError ? "ERROR" : "EVENT")}: {actionLabel}");

            // Only dump detailed state on errors
            if (isError)
            {
                _sessionLog.AppendLine($"  Chest {chestIndex}: {GetChestSummary(chestIndex)}");

                var cursorInfo = GetCursorItemSummary();
                if (!string.IsNullOrEmpty(cursorInfo))
                {
                    _sessionLog.AppendLine($"  Cursor: {cursorInfo}");
                }
            }

            WriteToFile();
        }

        /// <summary>
        /// Log an error with full context for debugging.
        /// </summary>
        public void LogError(string operation, string details, Exception ex = null)
        {
            _errorCount++;
            var timestamp = GetTimestamp();

            _sessionLog.AppendLine();
            _sessionLog.AppendLine($"[{timestamp}] *** ERROR in {operation} ***");
            _sessionLog.AppendLine($"  Details: {details}");

            if (ex != null)
            {
                _sessionLog.AppendLine($"  Exception: {ex.Message}");
            }

            // Capture cursor state on error
            var cursorInfo = GetCursorItemSummary();
            if (!string.IsNullOrEmpty(cursorInfo))
            {
                _sessionLog.AppendLine($"  Cursor: {cursorInfo}");
            }

            WriteToFile();
        }

        /// <summary>
        /// Write session summary (call on world unload).
        /// </summary>
        public void WriteSessionSummary()
        {
            var duration = DateTime.Now - _sessionStart;

            _sessionLog.AppendLine();
            _sessionLog.AppendLine("================================================================================");
            _sessionLog.AppendLine("SESSION SUMMARY");
            _sessionLog.AppendLine($"Duration: {duration.TotalMinutes:F1} minutes");
            _sessionLog.AppendLine($"Errors: {_errorCount}");
            _sessionLog.AppendLine($"Warnings: {_warningCount}");
            _sessionLog.AppendLine("================================================================================");

            WriteToFile();
        }

        private string GetTimestamp()
        {
            var elapsed = DateTime.Now - _sessionStart;
            return $"{elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
        }

        private string GetCursorItemSummary()
        {
            try
            {
                var mouseItem = _mouseItemField?.GetValue(null);
                if (mouseItem == null) return null;

                var typeVal = _itemTypeField?.GetValue(mouseItem);
                int type = typeVal != null ? (int)typeVal : 0;
                if (type == 0) return null;

                var stackVal = _itemStackField?.GetValue(mouseItem);
                int stack = stackVal != null ? (int)stackVal : 0;
                var name = _itemNameProp?.GetValue(mouseItem)?.ToString() ?? "Unknown";

                return $"{stack}x {name} (ID:{type})";
            }
            catch
            {
                return null;
            }
        }

        private string GetChestSummary(int chestIndex)
        {
            try
            {
                var chests = _chestArrayField?.GetValue(null) as Array;
                if (chests == null || chestIndex < 0 || chestIndex >= chests.Length)
                    return "(invalid)";

                var chest = chests.GetValue(chestIndex);
                if (chest == null) return "(null)";

                var x = _chestXField?.GetValue(chest);
                var y = _chestYField?.GetValue(chest);

                var items = _chestItemField?.GetValue(chest) as Array;
                int itemCount = 0;
                if (items != null)
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        var item = items.GetValue(i);
                        if (item == null) continue;
                        var typeVal = _itemTypeField?.GetValue(item);
                        int type = typeVal != null ? (int)typeVal : 0;
                        if (type > 0) itemCount++;
                    }
                }

                return $"pos=({x},{y}), items={itemCount}/40";
            }
            catch
            {
                return "(error reading)";
            }
        }

        private void WriteToFile()
        {
            try
            {
                File.WriteAllText(_logPath, _sessionLog.ToString());
            }
            catch
            {
                // Silent fail - don't break gameplay for logging
            }
        }
    }
}
