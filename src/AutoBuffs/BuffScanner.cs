using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AutoBuffs
{
    /// <summary>
    /// Optimized tile scanner for buff-granting furniture.
    /// Uses reflection to avoid XNA assembly dependencies.
    /// </summary>
    public static class BuffScanner
    {
        // Tile IDs (verified for 1.4.5)
        private const ushort TILE_CRYSTAL_BALL = 125;
        private const ushort TILE_AMMO_BOX = 287;
        private const ushort TILE_BEWITCHING_TABLE = 354;
        private const ushort TILE_SHARPENING_STATION = 377;
        private const ushort TILE_WAR_TABLE = 464;
        private const ushort TILE_SLICE_OF_CAKE = 621;

        // Buff IDs (verified for 1.4.5)
        private const int BUFF_CLAIRVOYANCE = 29;
        private const int BUFF_AMMO_BOX = 93;
        private const int BUFF_BEWITCHED = 150;
        private const int BUFF_SHARPENED = 159;
        private const int BUFF_SUGAR_RUSH = 192;
        private const int BUFF_WAR_TABLE = 348;

        // Timing
        private const int SCAN_CADENCE_TICKS = 10;
        private const int EFFECTIVELY_INFINITE_BUFF_TICKS = 60 * 60 * 24;
        private const int SUGAR_RUSH_DURATION_TICKS = 60 * 60 * 2;
        private const int SUGAR_RUSH_REAPPLY_THRESHOLD = 60 * 2;

        private static int _tickCounter = 0;
        private static bool _initialized = false;
        private static bool _hasLoggedFirstScan = false;

        // Fast lookup for tile types we care about
        private static readonly HashSet<ushort> _buffTileTypes = new HashSet<ushort>
        {
            TILE_CRYSTAL_BALL, TILE_AMMO_BOX, TILE_BEWITCHING_TABLE,
            TILE_SHARPENING_STATION, TILE_WAR_TABLE, TILE_SLICE_OF_CAKE
        };

        // Mapping from tile type to buff info
        private struct BuffInfo
        {
            public int BuffId;
            public bool IsSugarRush;
            public Func<bool> IsEnabled;
        }

        private static readonly Dictionary<ushort, BuffInfo> _tileToBuffMap = new Dictionary<ushort, BuffInfo>();

        // Cached reflection - Main
        private static Type _mainType;
        private static FieldInfo _tileArrayField;
        private static FieldInfo _maxTilesXField;
        private static FieldInfo _maxTilesYField;

        // Cached reflection - Player
        private static FieldInfo _positionField;
        private static MethodInfo _addBuffMethod;
        private static MethodInfo _findBuffIndexMethod;
        private static FieldInfo _buffTimeField;

        // Cached reflection - Tile
        private static Type _tileType;
        private static MethodInfo _tileActiveMethod;
        private static FieldInfo _tileTypeField;

        // Cached reflection - Vector2
        private static FieldInfo _xField;
        private static FieldInfo _yField;

        public static void Reset()
        {
            _tickCounter = 0;
            _hasLoggedFirstScan = false;
        }

        private static bool EnsureInitialized()
        {
            if (_initialized) return true;

            try
            {
                // Initialize tile-to-buff mapping
                _tileToBuffMap[TILE_CRYSTAL_BALL] = new BuffInfo
                {
                    BuffId = BUFF_CLAIRVOYANCE,
                    IsSugarRush = false,
                    IsEnabled = () => Mod.EnableCrystalBall
                };
                _tileToBuffMap[TILE_AMMO_BOX] = new BuffInfo
                {
                    BuffId = BUFF_AMMO_BOX,
                    IsSugarRush = false,
                    IsEnabled = () => Mod.EnableAmmoBox
                };
                _tileToBuffMap[TILE_BEWITCHING_TABLE] = new BuffInfo
                {
                    BuffId = BUFF_BEWITCHED,
                    IsSugarRush = false,
                    IsEnabled = () => Mod.EnableBewitchingTable
                };
                _tileToBuffMap[TILE_SHARPENING_STATION] = new BuffInfo
                {
                    BuffId = BUFF_SHARPENED,
                    IsSugarRush = false,
                    IsEnabled = () => Mod.EnableSharpeningStation
                };
                _tileToBuffMap[TILE_WAR_TABLE] = new BuffInfo
                {
                    BuffId = BUFF_WAR_TABLE,
                    IsSugarRush = false,
                    IsEnabled = () => Mod.EnableWarTable
                };
                _tileToBuffMap[TILE_SLICE_OF_CAKE] = new BuffInfo
                {
                    BuffId = BUFF_SUGAR_RUSH,
                    IsSugarRush = true,
                    IsEnabled = () => Mod.EnableSliceOfCake
                };

                // Find Main type (some assemblies throw on GetTypes() - skip them)
                _mainType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                    .FirstOrDefault(t => t.FullName == "Terraria.Main");

                if (_mainType == null)
                {
                    Mod.Log("Could not find Terraria.Main");
                    return false;
                }

                // Find Player type
                var playerType = _mainType.Assembly.GetType("Terraria.Player");

                // Get Main fields
                _tileArrayField = _mainType.GetField("tile", BindingFlags.Public | BindingFlags.Static);
                _maxTilesXField = _mainType.GetField("maxTilesX", BindingFlags.Public | BindingFlags.Static);
                _maxTilesYField = _mainType.GetField("maxTilesY", BindingFlags.Public | BindingFlags.Static);

                // Get Tile type and members
                _tileType = _mainType.Assembly.GetType("Terraria.Tile");
                _tileActiveMethod = _tileType?.GetMethod("active", Type.EmptyTypes);
                _tileTypeField = _tileType?.GetField("type", BindingFlags.Public | BindingFlags.Instance);

                // Get Player position (from Entity base class)
                var entityType = playerType.BaseType;
                _positionField = entityType?.GetField("position", BindingFlags.Public | BindingFlags.Instance)
                              ?? playerType.GetField("position", BindingFlags.Public | BindingFlags.Instance);

                // Get Player buff methods
                _addBuffMethod = playerType.GetMethod("AddBuff",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(int), typeof(int), typeof(bool) },
                    null);
                _findBuffIndexMethod = playerType.GetMethod("FindBuffIndex",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(int) },
                    null);
                _buffTimeField = playerType.GetField("buffTime", BindingFlags.Public | BindingFlags.Instance);

                _initialized = _mainType != null &&
                              _tileArrayField != null &&
                              _tileActiveMethod != null &&
                              _positionField != null &&
                              _addBuffMethod != null;

                if (_initialized)
                {
                    Mod.Log("BuffScanner initialized");
                }
                else
                {
                    Mod.Log($"BuffScanner init failed:");
                    Mod.Log($"  Main: {_mainType != null}");
                    Mod.Log($"  tile: {_tileArrayField != null}");
                    Mod.Log($"  active: {_tileActiveMethod != null}");
                    Mod.Log($"  position: {_positionField != null}");
                    Mod.Log($"  AddBuff: {_addBuffMethod != null}");
                }
            }
            catch (Exception ex)
            {
                Mod.Log($"BuffScanner init error: {ex.Message}");
                _initialized = false;
            }

            return _initialized;
        }

        public static void TryScan(object player, int scanRadius)
        {
            if (!EnsureInitialized()) return;

            // Throttle scans for performance
            if (++_tickCounter < SCAN_CADENCE_TICKS)
                return;
            _tickCounter = 0;

            try
            {
                ScanAndApplyBuffs(player, scanRadius);
            }
            catch (Exception ex)
            {
                Mod.Log($"Scan error: {ex.Message}");
            }
        }

        private static void ScanAndApplyBuffs(object player, int scanRadius)
        {
            // Get player tile position
            var positionObj = _positionField.GetValue(player);
            if (positionObj == null) return;

            // Initialize Vector2 field access
            if (_xField == null)
            {
                var vec2Type = positionObj.GetType();
                _xField = vec2Type.GetField("X");
                _yField = vec2Type.GetField("Y");
            }

            float posX = (float)_xField.GetValue(positionObj);
            float posY = (float)_yField.GetValue(positionObj);

            int playerTileX = (int)(posX / 16f);
            int playerTileY = (int)(posY / 16f);

            // Get world bounds
            int maxTilesX = (int)_maxTilesXField.GetValue(null);
            int maxTilesY = (int)_maxTilesYField.GetValue(null);

            // Calculate scan bounds
            int minX = Math.Max(0, playerTileX - scanRadius);
            int maxX = Math.Min(maxTilesX - 1, playerTileX + scanRadius);
            int minY = Math.Max(0, playerTileY - scanRadius);
            int maxY = Math.Min(maxTilesY - 1, playerTileY + scanRadius);

            int radiusSq = scanRadius * scanRadius;

            // Pre-check which buffs we need
            var neededTiles = new HashSet<ushort>();
            var foundTiles = new HashSet<ushort>();

            foreach (var kvp in _tileToBuffMap)
            {
                if (!kvp.Value.IsEnabled()) continue;

                int buffIndex = (int)_findBuffIndexMethod.Invoke(player, new object[] { kvp.Value.BuffId });

                if (kvp.Value.IsSugarRush)
                {
                    if (buffIndex < 0)
                    {
                        neededTiles.Add(kvp.Key);
                    }
                    else
                    {
                        int[] buffTime = (int[])_buffTimeField.GetValue(player);
                        if (buffTime != null && buffTime[buffIndex] <= SUGAR_RUSH_REAPPLY_THRESHOLD)
                        {
                            neededTiles.Add(kvp.Key);
                        }
                    }
                }
                else
                {
                    if (buffIndex < 0)
                    {
                        neededTiles.Add(kvp.Key);
                    }
                }
            }

            // Early exit if no buffs needed
            if (neededTiles.Count == 0)
            {
                Mod.LogDebug("All buffs active, skipping scan");
                return;
            }

            Mod.LogDebug($"Scanning for {neededTiles.Count} buff tiles around ({playerTileX}, {playerTileY})");

            // Get tile array as 2D array
            var tileArray = _tileArrayField.GetValue(null) as Array;
            if (tileArray == null) return;

            // Scan tiles
            for (int x = minX; x <= maxX && foundTiles.Count < neededTiles.Count; x++)
            {
                int dx = x - playerTileX;
                int dxSq = dx * dx;

                // Skip entire column if too far
                if (dxSq > radiusSq) continue;

                for (int y = minY; y <= maxY; y++)
                {
                    int dy = y - playerTileY;
                    if (dxSq + dy * dy > radiusSq)
                        continue;

                    // Get tile using Array.GetValue for 2D array
                    var tile = tileArray.GetValue(x, y);
                    if (tile == null) continue;

                    // Check if tile is active
                    bool isActive = (bool)_tileActiveMethod.Invoke(tile, null);
                    if (!isActive) continue;

                    // Get tile type
                    ushort tileType = (ushort)_tileTypeField.GetValue(tile);

                    // Check if this is a tile we need and haven't found yet
                    if (neededTiles.Contains(tileType) && !foundTiles.Contains(tileType))
                    {
                        foundTiles.Add(tileType);

                        // Early exit if we found everything
                        if (foundTiles.Count >= neededTiles.Count)
                            break;
                    }
                }
            }

            // Log first scan
            if (!_hasLoggedFirstScan && foundTiles.Count > 0)
            {
                _hasLoggedFirstScan = true;
                Mod.Log($"First scan complete - found {foundTiles.Count} furniture");
            }

            // Apply buffs for found tiles
            foreach (ushort tileType in foundTiles)
            {
                if (_tileToBuffMap.TryGetValue(tileType, out BuffInfo info))
                {
                    ApplyBuff(player, info);
                }
            }

            if (foundTiles.Count > 0)
            {
                Mod.LogDebug($"Applied {foundTiles.Count} buffs");
            }
        }

        private static void ApplyBuff(object player, BuffInfo info)
        {
            int duration = info.IsSugarRush ? SUGAR_RUSH_DURATION_TICKS : EFFECTIVELY_INFINITE_BUFF_TICKS;

            try
            {
                _addBuffMethod.Invoke(player, new object[] { info.BuffId, duration, true });
            }
            catch (Exception ex)
            {
                Mod.Log($"Failed to apply buff {info.BuffId}: {ex.Message}");
            }
        }
    }
}
