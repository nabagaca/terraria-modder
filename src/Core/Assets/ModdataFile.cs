using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core.Assets
{
    /// <summary>
    /// Reads and writes .moddata files (JSON sidecar files alongside .plr/.wld).
    /// Format: { "version": 1, "items": [ { "location": "...", "slot": N, ... } ] }
    /// </summary>
    public static class ModdataFile
    {
        private static ILogger _log;
        private const int FORMAT_VERSION = 1;

        public static void Initialize(ILogger logger)
        {
            _log = logger;
        }

        /// <summary>
        /// An item entry in the moddata file.
        /// </summary>
        public class ItemEntry
        {
            public string Location { get; set; }
            public int Slot { get; set; }
            public string ItemId { get; set; } // "modid:itemname"
            public int Stack { get; set; } = 1;
            public int Prefix { get; set; }
            public bool Favorited { get; set; }
        }

        /// <summary>
        /// Write moddata file atomically (write .tmp, then rename).
        /// </summary>
        public static bool Write(string moddataPath, List<ItemEntry> items)
        {
            if (string.IsNullOrEmpty(moddataPath) || items == null) return false;

            try
            {
                string dir = Path.GetDirectoryName(moddataPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string tmpPath = moddataPath + ".tmp";

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"version\": {FORMAT_VERSION},");
                sb.AppendLine($"  \"itemCount\": {items.Count},");
                sb.AppendLine("  \"items\": [");

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    sb.Append("    { ");
                    sb.Append($"\"location\": \"{Escape(item.Location)}\", ");
                    sb.Append($"\"slot\": {item.Slot}, ");
                    sb.Append($"\"item_id\": \"{Escape(item.ItemId)}\", ");
                    sb.Append($"\"stack\": {item.Stack}, ");
                    sb.Append($"\"prefix\": {item.Prefix}, ");
                    sb.Append($"\"favorited\": {(item.Favorited ? "true" : "false")}");
                    sb.Append(" }");
                    if (i < items.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");

                File.WriteAllText(tmpPath, sb.ToString(), Encoding.UTF8);

                // Backup existing
                if (File.Exists(moddataPath))
                {
                    string bakPath = moddataPath + ".bak";
                    try { File.Copy(moddataPath, bakPath, true); }
                    catch { /* backup failure is non-fatal */ }
                }

                // Atomic rename
                if (File.Exists(moddataPath))
                    File.Delete(moddataPath);
                File.Move(tmpPath, moddataPath);

                return true;
            }
            catch (Exception ex)
            {
                _log?.Error($"[ModdataFile] Failed to write {moddataPath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Read moddata file. Returns empty list if file doesn't exist or is corrupt.
        /// </summary>
        public static List<ItemEntry> Read(string moddataPath)
        {
            var result = new List<ItemEntry>();

            if (string.IsNullOrEmpty(moddataPath) || !File.Exists(moddataPath))
                return result;

            try
            {
                string json = File.ReadAllText(moddataPath, Encoding.UTF8);
                return ParseItems(json);
            }
            catch (Exception ex)
            {
                _log?.Warn($"[ModdataFile] Failed to read {moddataPath}: {ex.Message}");

                // Try backup
                string bakPath = moddataPath + ".bak";
                if (File.Exists(bakPath))
                {
                    try
                    {
                        _log?.Info($"[ModdataFile] Trying backup file...");
                        string json = File.ReadAllText(bakPath, Encoding.UTF8);
                        return ParseItems(json);
                    }
                    catch (Exception bakEx)
                    {
                        _log?.Error($"[ModdataFile] Backup also failed: {bakEx.Message}");
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Get the moddata path for a player file.
        /// </summary>
        public static string GetPlayerModdataPath(string playerPath)
        {
            if (string.IsNullOrEmpty(playerPath)) return null;
            return playerPath + ".moddata";
        }

        /// <summary>
        /// Get the moddata path for a world file.
        /// </summary>
        public static string GetWorldModdataPath(string worldPath)
        {
            if (string.IsNullOrEmpty(worldPath)) return null;
            return worldPath + ".moddata";
        }

        /// <summary>
        /// Delete moddata file and backup (for character deletion).
        /// </summary>
        public static void Delete(string moddataPath)
        {
            try
            {
                if (File.Exists(moddataPath)) File.Delete(moddataPath);
                if (File.Exists(moddataPath + ".bak")) File.Delete(moddataPath + ".bak");
                if (File.Exists(moddataPath + ".tmp")) File.Delete(moddataPath + ".tmp");
            }
            catch (Exception ex)
            {
                _log?.Warn($"[ModdataFile] Error deleting {moddataPath}: {ex.Message}");
            }
        }

        // ── Simple JSON parsing for the specific moddata format ──

        private static List<ItemEntry> ParseItems(string json)
        {
            var items = new List<ItemEntry>();

            // Find items array content
            int arrStart = json.IndexOf("\"items\"");
            if (arrStart < 0) return items;
            arrStart = json.IndexOf('[', arrStart);
            if (arrStart < 0) return items;
            int arrEnd = json.LastIndexOf(']');
            if (arrEnd <= arrStart) return items;

            string arrContent = json.Substring(arrStart + 1, arrEnd - arrStart - 1);

            // Split into individual objects by matching braces
            int depth = 0;
            int objStart = -1;
            for (int i = 0; i < arrContent.Length; i++)
            {
                if (arrContent[i] == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (arrContent[i] == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        string objJson = arrContent.Substring(objStart, i - objStart + 1);
                        var entry = ParseEntry(objJson);
                        if (entry != null) items.Add(entry);
                        objStart = -1;
                    }
                }
            }

            return items;
        }

        private static ItemEntry ParseEntry(string json)
        {
            try
            {
                var entry = new ItemEntry
                {
                    Location = ExtractString(json, "location"),
                    ItemId = ExtractString(json, "item_id"),
                    Slot = ExtractInt(json, "slot"),
                    Stack = ExtractInt(json, "stack", 1),
                    Prefix = ExtractInt(json, "prefix"),
                    Favorited = ExtractBool(json, "favorited")
                };

                if (string.IsNullOrEmpty(entry.ItemId)) return null;
                return entry;
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractString(string json, string key)
        {
            var match = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"([^\"]*)\"");
            return match.Success ? Unescape(match.Groups[1].Value) : null;
        }

        private static int ExtractInt(string json, string key, int defaultVal = 0)
        {
            var match = Regex.Match(json, $"\"{key}\"\\s*:\\s*(-?\\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, out int val) ? val : defaultVal;
        }

        private static bool ExtractBool(string json, string key, bool defaultVal = false)
        {
            var match = Regex.Match(json, $"\"{key}\"\\s*:\\s*(true|false)");
            return match.Success ? match.Groups[1].Value == "true" : defaultVal;
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string Unescape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}
