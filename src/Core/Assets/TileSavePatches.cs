using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Terraria;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core.Assets
{
    /// <summary>
    /// Save/load interception for custom tiles.
    /// Strategy: extract custom tiles to sidecar moddata on save, restore on load.
    /// This keeps worlds safe when custom tile mods are missing.
    /// </summary>
    internal static class TileSavePatches
    {
        private static Harmony _harmony;
        private static ILogger _log;
        private static bool _applied;

        private static readonly Dictionary<int, TileSnapshot> _extractedSnapshots = new Dictionary<int, TileSnapshot>();
        private static readonly HashSet<string> _worldBackupDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private class TileEntry
        {
            public int X;
            public int Y;
            public string TileId;
            public short FrameX;
            public short FrameY;
            public int Slope;
            public bool HalfBrick;
            public ushort Wall;
            public byte Liquid;
            public int LiquidType;
            public byte Color;
            public byte WallColor;
            public bool Actuator;
            public bool InActive;
        }

        private struct TileSnapshot
        {
            public ushort Type;
            public short FrameX;
            public short FrameY;
            public int Slope;
            public bool HalfBrick;
            public ushort Wall;
            public byte Liquid;
            public int LiquidType;
            public byte Color;
            public byte WallColor;
            public bool Actuator;
            public bool InActive;
            public bool Active;
        }

        public static void Initialize(ILogger logger)
        {
            _log = logger;
            _harmony = new Harmony("com.terrariamodder.assets.tiles.save");
        }

        public static void ApplyPatches()
        {
            if (_applied) return;

            try
            {
                PatchSaveWorld();
                PatchLoadWorld();
                _applied = true;
                _log?.Info("[TileSavePatches] Applied successfully");
            }
            catch (Exception ex)
            {
                _log?.Error($"[TileSavePatches] Failed: {ex.Message}");
            }
        }

        private static void PatchSaveWorld()
        {
            var worldFileType = typeof(Terraria.IO.WorldFile);
            var saveMethod = worldFileType.GetMethod("SaveWorld",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(bool), typeof(bool) }, null)
                ?? worldFileType.GetMethod("SaveWorld",
                    BindingFlags.Public | BindingFlags.Static, null,
                    Type.EmptyTypes, null);

            if (saveMethod == null)
            {
                _log?.Warn("[TileSavePatches] WorldFile.SaveWorld not found");
                return;
            }

            _harmony.Patch(saveMethod,
                prefix: new HarmonyMethod(typeof(TileSavePatches), nameof(SaveWorld_Prefix)),
                postfix: new HarmonyMethod(typeof(TileSavePatches), nameof(SaveWorld_Postfix)));
        }

        private static void PatchLoadWorld()
        {
            var worldFileType = typeof(Terraria.IO.WorldFile);
            var loadMethod = worldFileType.GetMethod("LoadWorld",
                BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (loadMethod == null)
            {
                _log?.Warn("[TileSavePatches] WorldFile.LoadWorld() not found");
                return;
            }

            _harmony.Patch(loadMethod,
                prefix: new HarmonyMethod(typeof(TileSavePatches), nameof(LoadWorld_Prefix)),
                postfix: new HarmonyMethod(typeof(TileSavePatches), nameof(LoadWorld_Postfix)));
        }

        private static void SaveWorld_Prefix()
        {
            _extractedSnapshots.Clear();

            if (TileRegistry.Count == 0) return;

            try
            {
                string worldPath = GetCurrentWorldPath();
                if (string.IsNullOrEmpty(worldPath)) return;

                var entries = new List<TileEntry>();
                int extracted = 0;

                int width = Main.maxTilesX;
                int height = Main.maxTilesY;

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        var tile = Main.tile[x, y];
                        if (tile == null || !tile.active()) continue;

                        int tileType = tile.type;
                        if (!TileRegistry.IsCustomTile(tileType)) continue;

                        string fullId = TileRegistry.GetFullId(tileType);
                        if (string.IsNullOrEmpty(fullId))
                            continue;

                        int key = ToKey(x, y);
                        _extractedSnapshots[key] = new TileSnapshot
                        {
                            Type = tile.type,
                            FrameX = tile.frameX,
                            FrameY = tile.frameY,
                            Slope = tile.slope(),
                            HalfBrick = tile.halfBrick(),
                            Wall = tile.wall,
                            Liquid = tile.liquid,
                            LiquidType = tile.liquidType(),
                            Color = tile.color(),
                            WallColor = tile.wallColor(),
                            Actuator = tile.actuator(),
                            InActive = tile.inActive(),
                            Active = tile.active()
                        };

                        entries.Add(new TileEntry
                        {
                            X = x,
                            Y = y,
                            TileId = fullId,
                            FrameX = tile.frameX,
                            FrameY = tile.frameY,
                            Slope = tile.slope(),
                            HalfBrick = tile.halfBrick(),
                            Wall = tile.wall,
                            Liquid = tile.liquid,
                            LiquidType = tile.liquidType(),
                            Color = tile.color(),
                            WallColor = tile.wallColor(),
                            Actuator = tile.actuator(),
                            InActive = tile.inActive()
                        });

                        // Clear to safe vanilla air tile for world save
                        tile.active(false);
                        tile.type = 0;
                        tile.frameX = 0;
                        tile.frameY = 0;
                        tile.liquid = 0;
                        tile.actuator(false);
                        tile.inActive(false);
                        extracted++;
                    }
                }

                string path = GetTileModdataPath(worldPath);
                if (entries.Count == 0)
                {
                    TryDelete(path);
                    return;
                }

                if (!WriteEntries(path, entries))
                {
                    _log?.Error("[TileSavePatches] Failed to write tile moddata; restoring tiles before vanilla save");
                    RestoreAllSnapshots();
                    return;
                }

                _log?.Info($"[TileSavePatches] Extracted {extracted} custom tiles into moddata");
            }
            catch (Exception ex)
            {
                _log?.Error($"[TileSavePatches] Save prefix error: {ex.Message}");
                RestoreAllSnapshots();
            }
        }

        private static void SaveWorld_Postfix()
        {
            if (_extractedSnapshots.Count == 0) return;

            try
            {
                RestoreAllSnapshots();
            }
            catch (Exception ex)
            {
                _log?.Error($"[TileSavePatches] Save postfix restore error: {ex.Message}");
            }
            finally
            {
                _extractedSnapshots.Clear();
            }
        }

        private static void LoadWorld_Prefix()
        {
            if (!AssetSystem.ExperimentalCustomTiles || TileRegistry.Count == 0)
                return;

            try
            {
                string worldPath = GetCurrentWorldPath();
                if (string.IsNullOrEmpty(worldPath)) return;
                if (_worldBackupDone.Contains(worldPath)) return;

                string backupPath = worldPath + ".before-custom-tiles.bak";
                if (!File.Exists(backupPath) && File.Exists(worldPath))
                {
                    File.Copy(worldPath, backupPath, overwrite: false);
                    _log?.Info($"[TileSavePatches] World backup created: {backupPath}");
                }

                _worldBackupDone.Add(worldPath);
            }
            catch (Exception ex)
            {
                _log?.Warn($"[TileSavePatches] World backup failed: {ex.Message}");
            }
        }

        private static void LoadWorld_Postfix()
        {
            if (TileRegistry.Count == 0) return;

            try
            {
                string worldPath = GetCurrentWorldPath();
                if (string.IsNullOrEmpty(worldPath)) return;

                string path = GetTileModdataPath(worldPath);
                var entries = ReadEntries(path);
                if (entries.Count == 0) return;

                int restored = 0;
                int missing = 0;

                foreach (var entry in entries)
                {
                    if (entry.X < 0 || entry.X >= Main.maxTilesX || entry.Y < 0 || entry.Y >= Main.maxTilesY)
                    {
                        missing++;
                        continue;
                    }

                    int runtimeType = TileRegistry.GetRuntimeType(entry.TileId);
                    if (runtimeType < 0)
                    {
                        // Missing-mod case: keep air tile; sidecar remains for later recovery.
                        missing++;
                        continue;
                    }

                    var tile = Main.tile[entry.X, entry.Y];
                    if (tile == null)
                    {
                        tile = new Tile();
                        Main.tile[entry.X, entry.Y] = tile;
                    }

                    tile.active(true);
                    tile.type = (ushort)runtimeType;
                    tile.frameX = entry.FrameX;
                    tile.frameY = entry.FrameY;
                    tile.wall = entry.Wall;
                    tile.liquid = entry.Liquid;
                    tile.liquidType((byte)entry.LiquidType);
                    tile.slope((byte)entry.Slope);
                    tile.halfBrick(entry.HalfBrick);
                    tile.color(entry.Color);
                    tile.wallColor(entry.WallColor);
                    tile.actuator(entry.Actuator);
                    tile.inActive(entry.InActive);

                    restored++;
                }

                _log?.Info($"[TileSavePatches] Restored {restored} custom tiles from moddata, unresolved={missing}");
            }
            catch (Exception ex)
            {
                _log?.Error($"[TileSavePatches] Load postfix error: {ex.Message}");
            }
        }

        private static void RestoreAllSnapshots()
        {
            foreach (var kvp in _extractedSnapshots)
            {
                FromKey(kvp.Key, out int x, out int y);
                if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY)
                    continue;

                var tile = Main.tile[x, y];
                if (tile == null)
                {
                    tile = new Tile();
                    Main.tile[x, y] = tile;
                }

                var snap = kvp.Value;
                tile.active(snap.Active);
                tile.type = snap.Type;
                tile.frameX = snap.FrameX;
                tile.frameY = snap.FrameY;
                tile.wall = snap.Wall;
                tile.liquid = snap.Liquid;
                tile.liquidType((byte)snap.LiquidType);
                tile.slope((byte)snap.Slope);
                tile.halfBrick(snap.HalfBrick);
                tile.color(snap.Color);
                tile.wallColor(snap.WallColor);
                tile.actuator(snap.Actuator);
                tile.inActive(snap.InActive);
            }
        }

        private static string GetCurrentWorldPath()
        {
            try
            {
                var worldFileData = Main.ActiveWorldFileData;
                if (worldFileData != null)
                {
                    var pathProp = worldFileData.GetType().GetProperty("Path");
                    var p = pathProp?.GetValue(worldFileData) as string;
                    if (!string.IsNullOrEmpty(p)) return p;
                }

                var worldPathProp = typeof(Main).GetProperty("worldPathName", BindingFlags.Public | BindingFlags.Static);
                return worldPathProp?.GetValue(null) as string;
            }
            catch
            {
                return null;
            }
        }

        private static string GetTileModdataPath(string worldPath) => worldPath + ".tiles.moddata";

        private static int ToKey(int x, int y) => (x << 16) ^ (y & 0xFFFF);

        private static void FromKey(int key, out int x, out int y)
        {
            x = (key >> 16) & 0xFFFF;
            y = key & 0xFFFF;
        }

        private static bool WriteEntries(string path, List<TileEntry> entries)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string tmp = path + ".tmp";
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"version\": 1,");
                sb.AppendLine($"  \"count\": {entries.Count},");
                sb.AppendLine("  \"tiles\": [");

                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    sb.Append("    { ");
                    sb.Append($"\"x\": {e.X}, ");
                    sb.Append($"\"y\": {e.Y}, ");
                    sb.Append($"\"tile_id\": \"{Escape(e.TileId)}\", ");
                    sb.Append($"\"fx\": {e.FrameX}, ");
                    sb.Append($"\"fy\": {e.FrameY}, ");
                    sb.Append($"\"slope\": {e.Slope}, ");
                    sb.Append($"\"half\": {(e.HalfBrick ? "true" : "false")}, ");
                    sb.Append($"\"wall\": {e.Wall}, ");
                    sb.Append($"\"liquid\": {e.Liquid}, ");
                    sb.Append($"\"liquid_type\": {e.LiquidType}, ");
                    sb.Append($"\"color\": {e.Color}, ");
                    sb.Append($"\"wall_color\": {e.WallColor}, ");
                    sb.Append($"\"actuator\": {(e.Actuator ? "true" : "false")}, ");
                    sb.Append($"\"inactive\": {(e.InActive ? "true" : "false")}");
                    sb.Append(" }");
                    if (i < entries.Count - 1) sb.Append(',');
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.AppendLine("}");

                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(path))
                {
                    try { File.Copy(path, path + ".bak", overwrite: true); }
                    catch { }
                    File.Delete(path);
                }
                File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                _log?.Error($"[TileSavePatches] Failed writing moddata {path}: {ex.Message}");
                return false;
            }
        }

        private static List<TileEntry> ReadEntries(string path)
        {
            if (!File.Exists(path)) return new List<TileEntry>();

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                return ParseEntries(json);
            }
            catch (Exception ex)
            {
                _log?.Warn($"[TileSavePatches] Failed reading {path}: {ex.Message}");
                string bak = path + ".bak";
                if (File.Exists(bak))
                {
                    try
                    {
                        string json = File.ReadAllText(bak, Encoding.UTF8);
                        return ParseEntries(json);
                    }
                    catch { }
                }
                return new List<TileEntry>();
            }
        }

        private static List<TileEntry> ParseEntries(string json)
        {
            var list = new List<TileEntry>();

            int arrStart = json.IndexOf("\"tiles\"", StringComparison.OrdinalIgnoreCase);
            if (arrStart < 0) return list;
            arrStart = json.IndexOf('[', arrStart);
            if (arrStart < 0) return list;
            int arrEnd = json.LastIndexOf(']');
            if (arrEnd <= arrStart) return list;

            string content = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
            int depth = 0;
            int objStart = -1;
            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (content[i] == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        string obj = content.Substring(objStart, i - objStart + 1);
                        var entry = ParseEntry(obj);
                        if (entry != null) list.Add(entry);
                        objStart = -1;
                    }
                }
            }

            return list;
        }

        private static TileEntry ParseEntry(string json)
        {
            try
            {
                var entry = new TileEntry
                {
                    X = ExtractInt(json, "x"),
                    Y = ExtractInt(json, "y"),
                    TileId = ExtractString(json, "tile_id"),
                    FrameX = (short)ExtractInt(json, "fx"),
                    FrameY = (short)ExtractInt(json, "fy"),
                    Slope = ExtractInt(json, "slope"),
                    HalfBrick = ExtractBool(json, "half"),
                    Wall = (ushort)ExtractInt(json, "wall"),
                    Liquid = (byte)ExtractInt(json, "liquid"),
                    LiquidType = ExtractInt(json, "liquid_type"),
                    Color = (byte)ExtractInt(json, "color"),
                    WallColor = (byte)ExtractInt(json, "wall_color"),
                    Actuator = ExtractBool(json, "actuator"),
                    InActive = ExtractBool(json, "inactive")
                };

                return string.IsNullOrEmpty(entry.TileId) ? null : entry;
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractString(string json, string key)
        {
            var m = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? Unescape(m.Groups[1].Value) : null;
        }

        private static int ExtractInt(string json, string key, int defaultValue = 0)
        {
            var m = Regex.Match(json, $"\"{key}\"\\s*:\\s*(-?\\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out int val) ? val : defaultValue;
        }

        private static bool ExtractBool(string json, string key, bool defaultValue = false)
        {
            var m = Regex.Match(json, $"\"{key}\"\\s*:\\s*(true|false)");
            return m.Success ? m.Groups[1].Value == "true" : defaultValue;
        }

        private static string Escape(string value)
        {
            if (value == null) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string Unescape(string value)
        {
            if (value == null) return string.Empty;
            return value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            }
            catch { }
        }
    }
}
