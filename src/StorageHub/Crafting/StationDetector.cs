using System;
using System.Collections.Generic;
using System.Reflection;
using TerrariaModder.Core.Logging;
using StorageHub.Config;

namespace StorageHub.Crafting
{
    /// <summary>
    /// Detects nearby crafting stations by scanning tiles around the player.
    ///
    /// Design:
    /// - Fast path: Reads Player.adjTile[] which Terraria maintains every frame
    ///   with all TileCountsAs equivalences resolved (free, covers vanilla range)
    /// - Extended scan: For tier ranges beyond vanilla (~5 tiles), scans tiles
    ///   with cached reflection and applies TileCountsAs equivalences
    /// - At Tier 3+, detected stations are remembered
    /// - Environment conditions: Reads Player.adjWaterSource/adjHoney/adjLava/ZoneSnow/ZoneGraveyard
    /// </summary>
    public class StationDetector
    {
        private readonly ILogger _log;
        private readonly StorageHubConfig _config;

        // Reflection cache - initialized once
        private static Type _mainType;
        private static Type _tileType;
        private static FieldInfo _playerArrayField;
        private static FieldInfo _myPlayerField;
        private static FieldInfo _playerPositionField;
        private static FieldInfo _adjTileField;
        private static FieldInfo _tileArrayField;
        private static FieldInfo _tileTypeField;
        private static FieldInfo _maxTilesXField;
        private static FieldInfo _maxTilesYField;
        private static MethodInfo _tileIndexerMethod;
        private static MethodInfo _tileActiveMethod;
        private static FieldInfo _tileCountsAsField;

        // Environment condition reflection cache
        private static FieldInfo _adjWaterSourceField;
        private static FieldInfo _adjHoneyField;
        private static FieldInfo _adjLavaField;
        private static PropertyInfo _zoneSnowProp;
        private static PropertyInfo _zoneGraveyardProp;

        private static bool _initialized;
        private static bool _initFailed;

        // Crafting station tile IDs — verified against TileID.cs and Recipe.cs decomp.
        // Includes all tile IDs used as Recipe.requiredTile, plus physical tiles
        // that resolve to recipe tiles via Recipe.TileCountsAs equivalences.
        public static readonly HashSet<int> CraftingStationTiles = new HashSet<int>
        {
            // Tiles that appear directly in Recipe.requiredTile assignments
            13,   // Bottles (placed bottle on furniture)
            16,   // Anvils
            17,   // Furnaces
            18,   // WorkBenches
            26,   // DemonAltar (also Crimson Altar — same tile type, different style)
            77,   // Hellforge
            86,   // Loom
            94,   // Kegs
            96,   // CookingPots
            101,  // Bookcases
            106,  // Sawmill
            114,  // TinkerersWorkbench
            125,  // CrystalBall
            133,  // AdamantiteForge
            134,  // MythrilAnvil
            215,  // Campfire
            217,  // Blendomatic
            218,  // MeatGrinder
            220,  // Solidifier
            228,  // DyeVat
            243,  // ImbuingStation
            247,  // Autohammer
            283,  // HeavyWorkBench
            300,  // BoneWelder
            301,  // FleshCloningVat
            302,  // GlassKiln
            303,  // LihzahrdFurnace
            304,  // LivingLoom
            305,  // SkyMill
            306,  // IceMachine
            307,  // SteampunkBoiler
            308,  // HoneyDispenser
            412,  // LunarCraftingStation (Ancient Manipulator)
            622,  // TeaKettle
            // Physical tiles that aren't direct recipe requirements but resolve
            // to recipe tiles via TileCountsAs (needed for extended scan detection)
            355,  // AlchemyTable → Bottles (13)
            699,  // DeadCellsPotionStation → Bottles (13)
        };

        // Vanilla adjTile scan range is ~5 tiles (Player.tileRangeX/Y)
        private const int VanillaRangeTiles = 5;

        // Maximum scan range in tiles (capped for performance)
        private const int MaxScanRange = 100;

        /// <summary>
        /// Get the effective scan range for station detection (in tiles).
        /// Uses tier-based range, capped at MaxScanRange for performance.
        /// </summary>
        private int GetScanRange()
        {
            int tierRange = ProgressionTier.GetRange(_config.Tier);

            // Convert pixels to tiles
            int rangeInTiles = tierRange == int.MaxValue ? MaxScanRange : tierRange / 16;

            // Cap at max for performance
            return Math.Min(rangeInTiles, MaxScanRange);
        }

        public StationDetector(ILogger log, StorageHubConfig config)
        {
            _log = log;
            _config = config;
            if (!_initialized && !_initFailed)
            {
                InitReflection();
            }
        }

        private void InitReflection()
        {
            try
            {
                _mainType = Type.GetType("Terraria.Main, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Main");
                _tileType = Type.GetType("Terraria.Tile, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Tile");

                if (_mainType != null)
                {
                    _playerArrayField = _mainType.GetField("player", BindingFlags.Public | BindingFlags.Static);
                    _myPlayerField = _mainType.GetField("myPlayer", BindingFlags.Public | BindingFlags.Static);
                    _tileArrayField = _mainType.GetField("tile", BindingFlags.Public | BindingFlags.Static);
                    _maxTilesXField = _mainType.GetField("maxTilesX", BindingFlags.Public | BindingFlags.Static);
                    _maxTilesYField = _mainType.GetField("maxTilesY", BindingFlags.Public | BindingFlags.Static);
                }

                var playerType = Type.GetType("Terraria.Player, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Player");
                if (playerType != null)
                {
                    _playerPositionField = playerType.GetField("position", BindingFlags.Public | BindingFlags.Instance);
                    _adjTileField = playerType.GetField("adjTile", BindingFlags.Public | BindingFlags.Instance);

                    // Environment condition fields/properties
                    _adjWaterSourceField = playerType.GetField("adjWaterSource", BindingFlags.Public | BindingFlags.Instance);
                    _adjHoneyField = playerType.GetField("adjHoney", BindingFlags.Public | BindingFlags.Instance);
                    _adjLavaField = playerType.GetField("adjLava", BindingFlags.Public | BindingFlags.Instance);
                    _zoneSnowProp = playerType.GetProperty("ZoneSnow", BindingFlags.Public | BindingFlags.Instance);
                    _zoneGraveyardProp = playerType.GetProperty("ZoneGraveyard", BindingFlags.Public | BindingFlags.Instance);
                }

                if (_tileType != null)
                {
                    _tileTypeField = _tileType.GetField("type", BindingFlags.Public | BindingFlags.Instance);
                    _tileActiveMethod = _tileType.GetMethod("active", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                }

                // Cache tile array indexer
                // Tile[,] is a 2D array — .NET uses Get(int,int), NOT get_Item
                var tiles = _tileArrayField?.GetValue(null);
                if (tiles != null)
                {
                    _tileIndexerMethod = tiles.GetType().GetMethod("Get", new[] { typeof(int), typeof(int) });
                    if (_tileIndexerMethod == null)
                    {
                        // Fallback: try get_Item in case it's a custom indexer type
                        _tileIndexerMethod = tiles.GetType().GetMethod("get_Item", new[] { typeof(int), typeof(int) });
                    }
                }

                // Cache Recipe.TileCountsAs
                var recipeType = Type.GetType("Terraria.Recipe, Terraria")
                    ?? Assembly.Load("Terraria").GetType("Terraria.Recipe");
                if (recipeType != null)
                {
                    _tileCountsAsField = recipeType.GetField("TileCountsAs", BindingFlags.Public | BindingFlags.Static);
                }

                _initialized = true;
                _log.Debug($"StationDetector initialized (adjTile={_adjTileField != null}, tileIndexer={_tileIndexerMethod != null}, tileActive={_tileActiveMethod != null}, tileType={_tileTypeField != null}, tileCountsAs={_tileCountsAsField != null})");
                if (_tileIndexerMethod != null)
                    _log.Debug($"  Tile indexer method: {_tileIndexerMethod.DeclaringType?.Name}.{_tileIndexerMethod.Name}");
                if (_tileIndexerMethod == null)
                    _log.Warn("  Extended range station detection DISABLED (tile indexer not found)");
            }
            catch (Exception ex)
            {
                _initFailed = true;
                _log.Error($"StationDetector init failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Scan tiles around the player and return detected crafting stations.
        /// Uses two strategies:
        /// 1. Read Player.adjTile[] for vanilla-range stations (free, includes equivalences)
        /// 2. Extended tile scan for tier ranges beyond vanilla (cached reflection)
        /// </summary>
        public HashSet<int> ScanNearbyStations()
        {
            var stations = new HashSet<int>();

            try
            {
                var player = GetLocalPlayer();
                if (player == null) return stations;

                // Fast path: read Player.adjTile[] (maintained by Terraria every frame)
                // This covers vanilla range (~5 tiles) with ALL equivalences resolved
                GetAdjTileStations(player, stations);

                // Extended scan: only if tier range exceeds vanilla range
                int scanRange = GetScanRange();
                if (scanRange > VanillaRangeTiles)
                {
                    ScanExtendedRange(player, stations, scanRange);
                }

                // Remember stations for Tier 3+
                if (stations.Count > 0 && ProgressionTier.HasStationMemory(_config.Tier) && _config.StationMemoryEnabled)
                {
                    foreach (var tileId in stations)
                    {
                        if (_config.RememberedStations.Add(tileId))
                        {
                            _log.Debug($"Remembered crafting station tile: {tileId}");
                        }
                    }
                }

                _log.Debug($"[StationDetector] Scan complete: {stations.Count} station types (Tier {_config.Tier}, range {scanRange} tiles, adjTile=fast, extended={scanRange > VanillaRangeTiles && _tileIndexerMethod != null})");
                if (stations.Count > 0)
                {
                    _log.Debug($"[StationDetector] Stations found: [{string.Join(", ", stations)}]");
                }
            }
            catch (Exception ex)
            {
                _log.Error($"ScanNearbyStations failed: {ex.Message}");
            }

            return stations;
        }

        /// <summary>
        /// Scan environment conditions from the local player.
        /// Reads adjWater, adjHoney, adjLava, ZoneSnow, ZoneGraveyard.
        /// </summary>
        public EnvironmentState ScanEnvironmentConditions()
        {
            var state = new EnvironmentState();
            try
            {
                var player = GetLocalPlayer();
                if (player == null) return state;

                if (_adjWaterSourceField != null)
                    state.HasWater = _adjWaterSourceField.GetValue(player) is bool b1 && b1;
                if (_adjHoneyField != null)
                    state.HasHoney = _adjHoneyField.GetValue(player) is bool b2 && b2;
                if (_adjLavaField != null)
                    state.HasLava = _adjLavaField.GetValue(player) is bool b3 && b3;
                if (_zoneSnowProp != null)
                    state.InSnow = _zoneSnowProp.GetValue(player) is bool b4 && b4;
                if (_zoneGraveyardProp != null)
                    state.InGraveyard = _zoneGraveyardProp.GetValue(player) is bool b5 && b5;
            }
            catch (Exception ex)
            {
                _log.Error($"ScanEnvironmentConditions failed: {ex.Message}");
            }
            return state;
        }

        /// <summary>
        /// Read Player.adjTile[] to get all stations within vanilla range.
        /// Terraria resolves TileCountsAs equivalences in SetAdjTile(), so
        /// adjTile[17] (Furnace) will be true if an Adamantite Forge is nearby.
        /// </summary>
        private void GetAdjTileStations(object player, HashSet<int> stations)
        {
            if (_adjTileField == null) return;

            try
            {
                var adjTile = _adjTileField.GetValue(player) as bool[];
                if (adjTile == null) return;

                for (int i = 0; i < adjTile.Length; i++)
                {
                    if (adjTile[i] && CraftingStationTiles.Contains(i))
                    {
                        stations.Add(i);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"GetAdjTileStations failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Scan tiles beyond vanilla range using cached reflection.
        /// Applies TileCountsAs equivalences for found stations.
        /// </summary>
        private void ScanExtendedRange(object player, HashSet<int> stations, int scanRange)
        {
            if (_tileArrayField == null || _tileIndexerMethod == null) return;

            try
            {
                var pos = GetPlayerPosition(player);
                int playerTileX = (int)(pos.x / 16);
                int playerTileY = (int)(pos.y / 16);

                var tiles = _tileArrayField.GetValue(null);
                if (tiles == null) return;

                // Re-cache indexer if needed (tile array may have been recreated)
                if (_tileIndexerMethod == null)
                {
                    _tileIndexerMethod = tiles.GetType().GetMethod("get_Item", new[] { typeof(int), typeof(int) });
                    if (_tileIndexerMethod == null) return;
                }

                int maxTilesX = 8400;
                int maxTilesY = 2400;
                if (_maxTilesXField != null)
                {
                    var xVal = _maxTilesXField.GetValue(null);
                    if (xVal != null) maxTilesX = (int)xVal;
                }
                if (_maxTilesYField != null)
                {
                    var yVal = _maxTilesYField.GetValue(null);
                    if (yVal != null) maxTilesY = (int)yVal;
                }

                // Load TileCountsAs for equivalence resolution
                Array tileCountsAs = null;
                if (_tileCountsAsField != null)
                {
                    tileCountsAs = _tileCountsAsField.GetValue(null) as Array;
                }

                // Only scan the extended area (skip vanilla range already covered by adjTile)
                int innerRange = VanillaRangeTiles;
                var invokeArgs = new object[2];

                for (int x = playerTileX - scanRange; x <= playerTileX + scanRange; x++)
                {
                    if (x < 0 || x >= maxTilesX) continue;

                    for (int y = playerTileY - scanRange; y <= playerTileY + scanRange; y++)
                    {
                        if (y < 0 || y >= maxTilesY) continue;

                        // Skip vanilla range (already covered by adjTile)
                        if (x >= playerTileX - innerRange && x <= playerTileX + innerRange &&
                            y >= playerTileY - innerRange && y <= playerTileY + innerRange)
                            continue;

                        try
                        {
                            invokeArgs[0] = x;
                            invokeArgs[1] = y;
                            object tile = _tileIndexerMethod.Invoke(tiles, invokeArgs);
                            if (tile == null) continue;

                            if (!CheckTileActive(tile)) continue;

                            int tileType = GetTileType(tile);
                            if (tileType <= 0) continue;

                            if (CraftingStationTiles.Contains(tileType))
                            {
                                stations.Add(tileType);
                                // Apply equivalences (e.g., Adamantite Forge → Hellforge → Furnace → Campfire)
                                ApplyEquivalences(tileType, stations, tileCountsAs);
                            }
                        }
                        catch
                        {
                            // Skip tiles that cause errors
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"ScanExtendedRange failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Recursively apply TileCountsAs equivalences (mirrors Player.SetAdjTile logic).
        /// E.g., Adamantite Forge (133) → Hellforge (77) → Furnace (17) → Campfire (215)
        /// </summary>
        private void ApplyEquivalences(int tileType, HashSet<int> stations, Array tileCountsAs)
        {
            if (tileCountsAs == null || tileType < 0 || tileType >= tileCountsAs.Length) return;

            try
            {
                // TileCountsAs is List<int>[] — each element is a List<int> or null
                var list = tileCountsAs.GetValue(tileType) as System.Collections.IList;
                if (list == null) return;

                foreach (var item in list)
                {
                    int equivType = (int)item;
                    if (stations.Add(equivType))
                    {
                        // Recurse for chained equivalences
                        ApplyEquivalences(equivType, stations, tileCountsAs);
                    }
                }
            }
            catch
            {
                // Equivalence lookup failure is non-fatal
            }
        }

        private int GetTileType(object tile)
        {
            if (_tileTypeField == null) return 0;
            try
            {
                var typeVal = _tileTypeField.GetValue(tile);
                if (typeVal is ushort ushortVal) return ushortVal;
                if (typeVal is int intVal) return intVal;
                if (typeVal != null) return Convert.ToInt32(typeVal);
            }
            catch { }
            return 0;
        }

        private bool CheckTileActive(object tile)
        {
            try
            {
                if (_tileActiveMethod != null)
                {
                    var result = _tileActiveMethod.Invoke(tile, null);
                    if (result is bool b) return b;
                }

                // Fallback: assume active if type > 0
                return GetTileType(tile) > 0;
            }
            catch { }
            return false;
        }

        private object GetLocalPlayer()
        {
            try
            {
                if (_myPlayerField == null || _playerArrayField == null)
                    return null;

                var myPlayerVal = _myPlayerField.GetValue(null);
                if (myPlayerVal == null) return null;

                int myPlayer = (int)myPlayerVal;
                var players = _playerArrayField.GetValue(null) as Array;
                if (players == null) return null;

                if (myPlayer < 0 || myPlayer >= players.Length)
                    return null;

                return players.GetValue(myPlayer);
            }
            catch
            {
                return null;
            }
        }

        private (float x, float y) GetPlayerPosition(object player)
        {
            try
            {
                if (player == null) return (0, 0);

                var position = _playerPositionField?.GetValue(player);
                if (position != null)
                {
                    var posType = position.GetType();
                    var xField = posType.GetField("X");
                    var yField = posType.GetField("Y");

                    if (xField == null || yField == null) return (0, 0);

                    var xVal = xField.GetValue(position);
                    var yVal = yField.GetValue(position);

                    if (xVal == null || yVal == null) return (0, 0);

                    float x = Convert.ToSingle(xVal);
                    float y = Convert.ToSingle(yVal);
                    return (x, y);
                }
            }
            catch { }
            return (0, 0);
        }
    }

    /// <summary>
    /// Environment conditions near the player (proximity-based).
    /// </summary>
    public struct EnvironmentState
    {
        public bool HasWater;
        public bool HasHoney;
        public bool HasLava;
        public bool InSnow;
        public bool InGraveyard;
    }
}
