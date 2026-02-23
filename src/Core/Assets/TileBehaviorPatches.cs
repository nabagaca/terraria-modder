using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Terraria;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core.Assets
{
    /// <summary>
    /// Runtime behavior patches for custom tiles (right-click, container open/break, item drops).
    /// </summary>
    internal static class TileBehaviorPatches
    {
        private static Harmony _harmony;
        private static ILogger _log;
        private static bool _applied;

        private static MethodInfo _playerOpenChestMethod;
        private static MethodInfo _inTileEntityInteractionRange;
        private static object _tileReachSimple;
        private static MethodInfo _getItemSourceFromTileBreak;
        private static MethodInfo _newItemMethod;
        private static readonly HashSet<int> _loggedFallbackPlacementTypes = new HashSet<int>();
        private static readonly HashSet<int> _loggedMissingTileObjectDataTypes = new HashSet<int>();

        // Thread-local break context used by KillTile prefix/postfix.
        [ThreadStatic] private static bool _pendingBreak;
        [ThreadStatic] private static int _pendingBreakTopX;
        [ThreadStatic] private static int _pendingBreakTopY;
        [ThreadStatic] private static TileDefinition _pendingBreakDef;

        public static void Initialize(ILogger logger)
        {
            _log = logger;
            _harmony = new Harmony("com.terrariamodder.assets.tiles.behavior");
            CacheReflection();
        }

        public static void ApplyPatches()
        {
            if (_applied) return;

            try
            {
                int patched = 0;
                patched += PatchTileInteractionsUse();
                patched += PatchTileObjectPlace();
                patched += PatchPlaceTile();
                patched += PatchKillTile();
                patched += PatchInteractionRange();

                _applied = true;
                _log?.Info($"[TileBehaviorPatches] Applied {patched} patches");
            }
            catch (Exception ex)
            {
                _log?.Error($"[TileBehaviorPatches] Failed: {ex.Message}");
            }
        }

        private static void CacheReflection()
        {
            try
            {
                var playerType = typeof(Player);
                _playerOpenChestMethod = playerType.GetMethod("OpenChest",
                    BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(int), typeof(int), typeof(int) }, null);

                var tileReachType = playerType.Assembly.GetType("Terraria.DataStructures.TileReachCheckSettings");
                if (tileReachType != null)
                {
                    _tileReachSimple = tileReachType.GetField("Simple", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    _inTileEntityInteractionRange = playerType.GetMethod("InTileEntityInteractionRange",
                        BindingFlags.Public | BindingFlags.Instance, null,
                        new[] { typeof(int), typeof(int), typeof(int), typeof(int), tileReachType }, null);
                }

                _getItemSourceFromTileBreak = typeof(WorldGen).GetMethod("GetItemSource_FromTileBreak",
                    BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int), typeof(int) }, null);

                foreach (var method in typeof(Item).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name != "NewItem") continue;
                    var parms = method.GetParameters();
                    if (parms.Length >= 6 &&
                        parms[1].ParameterType == typeof(int) &&
                        parms[2].ParameterType == typeof(int) &&
                        parms[3].ParameterType == typeof(int) &&
                        parms[4].ParameterType == typeof(int) &&
                        parms[5].ParameterType == typeof(int))
                    {
                        _newItemMethod = method;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Warn($"[TileBehaviorPatches] Reflection cache warning: {ex.Message}");
            }
        }

        private static int PatchTileInteractionsUse()
        {
            try
            {
                var method = typeof(Player).GetMethod("TileInteractionsUse",
                    BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(int), typeof(int) }, null);
                if (method == null)
                {
                    _log?.Warn("[TileBehaviorPatches] Player.TileInteractionsUse not found");
                    return 0;
                }

                _harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(TileBehaviorPatches), nameof(TileInteractionsUse_Prefix)));
                return 1;
            }
            catch (Exception ex)
            {
                _log?.Warn($"[TileBehaviorPatches] TileInteractionsUse patch failed: {ex.Message}");
                return 0;
            }
        }

        private static int PatchTileObjectPlace()
        {
            try
            {
                var tileObjectType = typeof(Main).Assembly.GetType("Terraria.TileObject");
                var placeMethod = tileObjectType?.GetMethod("Place", BindingFlags.Public | BindingFlags.Static);
                if (placeMethod == null)
                {
                    _log?.Warn("[TileBehaviorPatches] TileObject.Place not found");
                    return 0;
                }

                _harmony.Patch(placeMethod,
                    postfix: new HarmonyMethod(typeof(TileBehaviorPatches), nameof(TileObjectPlace_Postfix)));
                return 1;
            }
            catch (Exception ex)
            {
                _log?.Warn($"[TileBehaviorPatches] TileObject.Place patch failed: {ex.Message}");
                return 0;
            }
        }

        private static int PatchKillTile()
        {
            try
            {
                var method = typeof(WorldGen).GetMethod("KillTile",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(bool) }, null);
                if (method == null)
                {
                    _log?.Warn("[TileBehaviorPatches] WorldGen.KillTile not found");
                    return 0;
                }

                _harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(TileBehaviorPatches), nameof(KillTile_Prefix)),
                    postfix: new HarmonyMethod(typeof(TileBehaviorPatches), nameof(KillTile_Postfix)));
                return 1;
            }
            catch (Exception ex)
            {
                _log?.Warn($"[TileBehaviorPatches] KillTile patch failed: {ex.Message}");
                return 0;
            }
        }

        private static int PatchPlaceTile()
        {
            try
            {
                var method = typeof(WorldGen).GetMethod("PlaceTile",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(int), typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(int), typeof(int) }, null);
                if (method == null)
                {
                    _log?.Warn("[TileBehaviorPatches] WorldGen.PlaceTile not found");
                    return 0;
                }

                _harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(TileBehaviorPatches), nameof(PlaceTile_Prefix)));
                return 1;
            }
            catch (Exception ex)
            {
                _log?.Warn($"[TileBehaviorPatches] PlaceTile patch failed: {ex.Message}");
                return 0;
            }
        }

        private static int PatchInteractionRange()
        {
            try
            {
                var method = typeof(Player).GetMethod("IsInInteractionRangeToMultiTileHitbox",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(int), typeof(int) }, null);
                if (method == null || _inTileEntityInteractionRange == null || _tileReachSimple == null)
                    return 0;

                _harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(TileBehaviorPatches), nameof(IsInInteractionRange_Prefix)));
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        // Player.TileInteractionsUse(int myX, int myY)
        private static bool TileInteractionsUse_Prefix(Player __instance, int myX, int myY)
        {
            try
            {
                if (!CustomTileContainers.TryGetTileDefinition(myX, myY, out var def, out int tileType))
                    return true;

                // Respect vanilla click gate fields if present.
                if (!__instance.releaseUseTile || !__instance.tileInteractAttempted)
                    return true;

                if (def.IsContainer)
                {
                    if (CustomTileContainers.TryOpenContainer(__instance, myX, myY, def))
                    {
                        __instance.releaseUseTile = false;
                        return false;
                    }
                }

                if (def.OnRightClick != null)
                {
                    bool handled = false;
                    try { handled = def.OnRightClick(myX, myY, __instance); }
                    catch (Exception ex) { _log?.Error($"[TileBehaviorPatches] OnRightClick error for tile {tileType}: {ex.Message}"); }

                    if (handled)
                    {
                        __instance.releaseUseTile = false;
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Debug($"[TileBehaviorPatches] TileInteractionsUse prefix error: {ex.Message}");
            }

            return true;
        }

        // TileObject.Place(...) postfix
        private static void TileObjectPlace_Postfix(bool __result, object toBePlaced)
        {
            if (!__result || toBePlaced == null) return;

            try
            {
                var toType = toBePlaced.GetType();
                int type = (int)toType.GetField("type")?.GetValue(toBePlaced);
                if (!TileRegistry.IsCustomTile(type)) return;

                var def = TileRegistry.GetDefinition(type);
                if (def == null) return;

                int xCoord = (int)toType.GetField("xCoord")?.GetValue(toBePlaced);
                int yCoord = (int)toType.GetField("yCoord")?.GetValue(toBePlaced);

                if (!CustomTileContainers.TryGetTopLeft(xCoord, yCoord, def, out int topX, out int topY))
                {
                    topX = xCoord;
                    topY = yCoord;
                }

                NormalizeMultiTileFrames(topX, topY, type, def);

                if (def.IsContainer)
                {
                    int chestIndex = Chest.FindChest(topX, topY);
                    if (chestIndex < 0)
                        chestIndex = Chest.CreateChest(topX, topY, -1);

                    if (chestIndex >= 0)
                    {
                        var chest = Main.chest[chestIndex];
                        if (chest != null)
                        {
                            if (def.ContainerCapacity > 0 && chest.maxItems != def.ContainerCapacity)
                            {
                                var resize = chest.GetType().GetMethod("Resize", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
                                resize?.Invoke(chest, new object[] { def.ContainerCapacity });
                            }

                            chest.name = string.IsNullOrEmpty(def.ContainerName) ? def.DisplayName : def.ContainerName;
                        }
                    }
                }

                try { def.OnPlace?.Invoke(topX, topY); }
                catch (Exception ex) { _log?.Debug($"[TileBehaviorPatches] OnPlace hook error: {ex.Message}"); }

                TryPlayTileSound(def.HitSoundStyle, topX, topY);
            }
            catch (Exception ex)
            {
                _log?.Error($"[TileBehaviorPatches] TileObjectPlace postfix error: {ex.Message}");
            }
        }

        // WorldGen.KillTile prefix
        private static bool KillTile_Prefix(int i, int j, bool fail, bool effectOnly, bool noItem)
        {
            _pendingBreak = false;

            try
            {
                if (!CustomTileContainers.TryGetTileDefinition(i, j, out var def, out int tileType))
                    return true;

                if (!CustomTileContainers.TryGetTopLeft(i, j, def, out int topX, out int topY))
                {
                    topX = i;
                    topY = j;
                }

                bool isMultiTile = def.Width > 1 || def.Height > 1;

                if (def.IsContainer)
                {
                    if (def.ContainerRequiresEmptyToBreak && !WorldGen.destroyObject)
                    {
                        int chestIndex = Chest.FindChest(topX, topY);
                        if (chestIndex >= 0 && ChestHasItems(chestIndex))
                            return false;
                    }
                }

                // Always handle custom multi-tile destruction ourselves on actual break calls.
                // This avoids vanilla per-subtile kill flow causing duplicate drops/chest drops.
                if (isMultiTile && !effectOnly && !fail)
                {
                    BreakCustomMultiTile(topX, topY, def, effectOnly, noItem);
                    return false;
                }

                // Fallback-safe behavior for multi-tiles only when object-data metadata is missing.
                if (isMultiTile && !HasTileObjectData(tileType))
                {
                    return true;
                }

                _pendingBreak = true;
                _pendingBreakTopX = topX;
                _pendingBreakTopY = topY;
                _pendingBreakDef = def;
            }
            catch (Exception ex)
            {
                _log?.Debug($"[TileBehaviorPatches] KillTile prefix error: {ex.Message}");
            }

            return true;
        }

        // WorldGen.KillTile postfix
        private static void KillTile_Postfix(int i, int j, bool fail, bool effectOnly, bool noItem)
        {
            if (!_pendingBreak) return;

            try
            {
                if (i < 0 || i >= Main.maxTilesX || j < 0 || j >= Main.maxTilesY) return;

                var tile = Main.tile[i, j];
                bool removed = tile == null || !tile.active() || !TileRegistry.IsCustomTile(tile.type);
                if (!removed) return;

                if (_pendingBreakDef != null)
                {
                    // Only finalize when the top-left anchor no longer has this custom tile.
                    if (_pendingBreakTopX >= 0 && _pendingBreakTopX < Main.maxTilesX &&
                        _pendingBreakTopY >= 0 && _pendingBreakTopY < Main.maxTilesY)
                    {
                        var topLeftTile = Main.tile[_pendingBreakTopX, _pendingBreakTopY];
                        bool topLeftStillPresent = topLeftTile != null && topLeftTile.active() && TileRegistry.IsCustomTile(topLeftTile.type);
                        if (topLeftStillPresent)
                            return;
                    }

                    if (_pendingBreakDef.IsContainer)
                    {
                        RemoveCustomChest(_pendingBreakTopX, _pendingBreakTopY);
                    }

                    TryPlayTileSound(_pendingBreakDef.HitSoundStyle, _pendingBreakTopX, _pendingBreakTopY);

                    if (!effectOnly && !noItem && !string.IsNullOrEmpty(_pendingBreakDef.DropItemId))
                    {
                        int itemType = ItemRegistry.ResolveItemType(_pendingBreakDef.DropItemId);
                        if (itemType > 0)
                            SpawnDropItem(_pendingBreakTopX, _pendingBreakTopY, itemType);
                    }

                    try { _pendingBreakDef.OnBreak?.Invoke(_pendingBreakTopX, _pendingBreakTopY); }
                    catch (Exception ex) { _log?.Debug($"[TileBehaviorPatches] OnBreak hook error: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                _log?.Debug($"[TileBehaviorPatches] KillTile postfix error: {ex.Message}");
            }
            finally
            {
                _pendingBreak = false;
                _pendingBreakDef = null;
            }
        }

        private static bool IsInInteractionRange_Prefix(Player __instance, int chestPointX, int chestPointY, ref bool __result)
        {
            try
            {
                if (!CustomTileContainers.TryGetTileDefinition(chestPointX, chestPointY, out var def, out _))
                    return true;
                if (!def.IsContainer) return true;

                if (!CustomTileContainers.TryGetTopLeft(chestPointX, chestPointY, def, out int topX, out int topY))
                    return true;

                __result = (bool)_inTileEntityInteractionRange.Invoke(__instance,
                    new object[] { topX, topY, Math.Max(1, def.Width), Math.Max(1, def.Height), _tileReachSimple });
                return false;
            }
            catch
            {
                return true;
            }
        }

        // WorldGen.PlaceTile(...) prefix
        // Fallback path for custom multi-tiles when TileObjectData registration is unavailable.
        private static bool PlaceTile_Prefix(int i, int j, int Type, bool mute, bool forced, int plr, int style, ref bool __result)
        {
            try
            {
                if (!TileRegistry.IsCustomTile(Type))
                    return true;

                var def = TileRegistry.GetDefinition(Type);
                if (def == null)
                    return true;

                if (def.Width <= 1 && def.Height <= 1)
                    return true;

                if (HasTileObjectData(Type))
                    return true; // Normal object placement path is available.

                // Re-run registration in case TileObjectData was invalidated after
                // an earlier successful pass.
                TileObjectRegistrar.ApplyDefinitions();
                if (HasTileObjectData(Type))
                    return true;

                if (_loggedFallbackPlacementTypes.Add(Type))
                {
                    _log?.Warn($"[TileBehaviorPatches] Using fallback placement for custom multi-tile {Type} ({def.DisplayName}) because TileObjectData is unavailable");
                }

                bool placed = TryPlaceCustomMultiTile(i, j, Type, def, out int topX, out int topY);
                __result = placed;

                if (placed)
                {
                    if (def.IsContainer)
                        InitializeContainerAt(topX, topY, def);

                    try { def.OnPlace?.Invoke(topX, topY); } catch { }
                    TryPlayTileSound(def.HitSoundStyle, topX, topY);
                }

                return false; // Skip vanilla PlaceTile for this custom multi-tile.
            }
            catch (Exception ex)
            {
                _log?.Debug($"[TileBehaviorPatches] PlaceTile fallback error: {ex.Message}");
                __result = false;
                return false;
            }
        }

        private static bool HasTileObjectData(int tileType)
        {
            try
            {
                var todType = typeof(Main).Assembly.GetType("Terraria.ObjectData.TileObjectData");
                if (todType == null)
                {
                    LogMissingTileObjectData(tileType, "TileObjectData type is unavailable");
                    return false;
                }

                object existing = null;
                var getTileData = todType?.GetMethod("GetTileData", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(int), typeof(int), typeof(int) }, null);
                if (getTileData != null)
                {
                    try { existing = getTileData.Invoke(null, new object[] { tileType, 0, 0 }); }
                    catch (Exception ex)
                    {
                        LogMissingTileObjectData(tileType, $"GetTileData threw: {ex.GetType().Name}: {ex.Message}");
                    }
                    if (existing != null)
                        return true;
                }

                var dataField = todType.GetField("_data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (dataField?.GetValue(null) is IList list &&
                    tileType >= 0 &&
                    tileType < list.Count &&
                    list[tileType] != null)
                {
                    return true;
                }

                string reason;
                if (getTileData == null)
                {
                    reason = "GetTileData method missing";
                }
                else if (!(dataField?.GetValue(null) is IList))
                {
                    reason = "TileObjectData._data missing";
                }
                else
                {
                    var dataList = dataField.GetValue(null) as IList;
                    reason = dataList == null
                        ? "TileObjectData._data null"
                        : $"GetTileData=null and _data[{tileType}] missing (count={dataList.Count})";
                }
                LogMissingTileObjectData(tileType, reason);
                return false;
            }
            catch
            {
                LogMissingTileObjectData(tileType, "unexpected exception in HasTileObjectData");
                return false;
            }
        }

        private static void LogMissingTileObjectData(int tileType, string reason)
        {
            try
            {
                if (!_loggedMissingTileObjectDataTypes.Add(tileType))
                    return;

                string name = TileRegistry.GetDefinition(tileType)?.DisplayName ?? "unknown";
                _log?.Warn($"[TileBehaviorPatches] TileObjectData missing for tile {tileType} ({name}): {reason}");
            }
            catch { }
        }

        private static bool TryPlaceCustomMultiTile(int i, int j, int type, TileDefinition def, out int topX, out int topY)
        {
            topX = i;
            topY = j;

            if (def == null)
                return false;

            int width = Math.Max(1, def.Width);
            int height = Math.Max(1, def.Height);
            int originX = Math.Max(0, Math.Min(width - 1, def.OriginX));
            int originY = Math.Max(0, Math.Min(height - 1, def.OriginY));

            topX = i - originX;
            topY = j - originY;

            if (topX < 0 || topY < 0 || topX + width > Main.maxTilesX || topY + height > Main.maxTilesY)
                return false;

            for (int lx = 0; lx < width; lx++)
            {
                for (int ly = 0; ly < height; ly++)
                {
                    var tile = Framing.GetTileSafely(topX + lx, topY + ly);
                    if (tile == null)
                        return false;

                    // Never place over active tiles in fallback mode; this prevents overlap artifacts.
                    if (tile.active())
                        return false;
                }
            }

            if (!HasBottomSupport(topX, topY, width, height))
                return false;

            int stepX = Math.Max(1, def.CoordinateWidth + Math.Max(0, def.CoordinatePadding));
            int[] rowOffsets = BuildRowFrameOffsets(def, height);

            for (int lx = 0; lx < width; lx++)
            {
                for (int ly = 0; ly < height; ly++)
                {
                    int x = topX + lx;
                    int y = topY + ly;
                    var tile = Framing.GetTileSafely(x, y);
                    tile.active(true);
                    tile.type = (ushort)type;
                    tile.frameX = (short)(lx * stepX);
                    tile.frameY = (short)rowOffsets[ly];
                }
            }

            WorldGen.RangeFrame(topX, topY, topX + width + 1, topY + height + 1);
            return true;
        }

        private static void NormalizeMultiTileFrames(int topX, int topY, int type, TileDefinition def)
        {
            if (def == null)
                return;

            int width = Math.Max(1, def.Width);
            int height = Math.Max(1, def.Height);
            if (width <= 1 && height <= 1)
                return;

            if (topX < 0 || topY < 0 || topX + width > Main.maxTilesX || topY + height > Main.maxTilesY)
                return;

            for (int lx = 0; lx < width; lx++)
            {
                for (int ly = 0; ly < height; ly++)
                {
                    var tile = Main.tile[topX + lx, topY + ly];
                    if (tile == null || !tile.active() || tile.type != type)
                        return;
                }
            }

            int stepX = Math.Max(1, def.CoordinateWidth + Math.Max(0, def.CoordinatePadding));
            int[] rowOffsets = BuildRowFrameOffsets(def, height);

            for (int lx = 0; lx < width; lx++)
            {
                for (int ly = 0; ly < height; ly++)
                {
                    var tile = Main.tile[topX + lx, topY + ly];
                    tile.frameX = (short)(lx * stepX);
                    tile.frameY = (short)rowOffsets[ly];
                }
            }

            WorldGen.RangeFrame(topX, topY, topX + width + 1, topY + height + 1);
        }

        private static bool HasBottomSupport(int topX, int topY, int width, int height)
        {
            int supportY = topY + Math.Max(1, height);
            if (supportY < 0 || supportY >= Main.maxTilesY)
                return false;

            var tileSolid = Main.tileSolid;
            var tileSolidTop = Main.tileSolidTop;
            if (tileSolid == null)
                return false;

            for (int lx = 0; lx < Math.Max(1, width); lx++)
            {
                int x = topX + lx;
                if (x < 0 || x >= Main.maxTilesX)
                    return false;

                var support = Framing.GetTileSafely(x, supportY);
                if (support == null || !support.active())
                    return false;

                int supportType = support.type;
                if (supportType < 0 || supportType >= tileSolid.Length)
                    return false;

                bool solid = tileSolid[supportType];
                bool solidTop = tileSolidTop != null &&
                    supportType >= 0 &&
                    supportType < tileSolidTop.Length &&
                    tileSolidTop[supportType];
                if (!solid && !solidTop)
                    return false;
            }

            return true;
        }

        private static int[] BuildRowFrameOffsets(TileDefinition def, int height)
        {
            int[] result = new int[Math.Max(1, height)];
            int[] coordHeights;
            if (def.CoordinateHeights != null && def.CoordinateHeights.Length > 0)
            {
                coordHeights = def.CoordinateHeights;
            }
            else
            {
                coordHeights = new int[result.Length];
                for (int i = 0; i < coordHeights.Length; i++)
                    coordHeights[i] = 16;
            }

            int padding = Math.Max(0, def.CoordinatePadding);
            int acc = 0;
            for (int row = 0; row < result.Length; row++)
            {
                result[row] = acc;
                int h = coordHeights[Math.Min(row, coordHeights.Length - 1)];
                acc += Math.Max(1, h) + padding;
            }

            return result;
        }

        private static void InitializeContainerAt(int topX, int topY, TileDefinition def)
        {
            if (def == null || !def.IsContainer)
                return;

            int chestIndex = Chest.FindChest(topX, topY);
            if (chestIndex < 0)
                chestIndex = Chest.CreateChest(topX, topY, -1);

            if (chestIndex < 0)
                return;

            var chest = Main.chest[chestIndex];
            if (chest == null)
                return;

            if (def.ContainerCapacity > 0 && chest.maxItems != def.ContainerCapacity)
            {
                var resize = chest.GetType().GetMethod("Resize", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
                resize?.Invoke(chest, new object[] { def.ContainerCapacity });
            }

            chest.name = string.IsNullOrEmpty(def.ContainerName) ? def.DisplayName : def.ContainerName;
        }

        private static bool ChestHasItems(int chestIndex)
        {
            if (chestIndex < 0 || chestIndex >= Main.maxChests) return false;
            var chest = Main.chest[chestIndex];
            if (chest?.item == null) return false;

            for (int i = 0; i < chest.maxItems && i < chest.item.Length; i++)
            {
                var item = chest.item[i];
                if (item != null && !item.IsAir && item.stack > 0)
                    return true;
            }

            return false;
        }

        private static void SpawnDropItem(int tileX, int tileY, int itemType)
        {
            try
            {
                if (_newItemMethod == null) return;

                object source = _getItemSourceFromTileBreak?.Invoke(null, new object[] { tileX, tileY });
                object[] args = new object[] { source, tileX * 16, tileY * 16, 16, 16, itemType, 1, false, 0, false };
                _newItemMethod.Invoke(null, args);
            }
            catch (Exception ex)
            {
                _log?.Debug($"[TileBehaviorPatches] Drop spawn failed: {ex.Message}");
            }
        }

        private static void BreakCustomMultiTile(int topX, int topY, TileDefinition def, bool effectOnly, bool noItem)
        {
            if (def == null) return;
            if (effectOnly) return;

            int width = Math.Max(1, def.Width);
            int height = Math.Max(1, def.Height);
            bool removedAny = false;

            for (int lx = 0; lx < width; lx++)
            {
                for (int ly = 0; ly < height; ly++)
                {
                    int x = topX + lx;
                    int y = topY + ly;
                    if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY)
                        continue;

                    var tile = Main.tile[x, y];
                    if (tile == null || !tile.active())
                        continue;

                    if (!TileRegistry.IsCustomTile(tile.type))
                        continue;

                    tile.active(false);
                    tile.type = 0;
                    tile.frameX = 0;
                    tile.frameY = 0;
                    removedAny = true;
                }
            }

            if (!removedAny)
                return;

            if (def.IsContainer)
            {
                RemoveCustomChest(topX, topY);
            }

            TryPlayTileSound(def.HitSoundStyle, topX, topY);

            if (!effectOnly && !noItem && !string.IsNullOrEmpty(def.DropItemId))
            {
                int itemType = ItemRegistry.ResolveItemType(def.DropItemId);
                if (itemType > 0)
                    SpawnDropItem(topX, topY, itemType);
            }

            try { def.OnBreak?.Invoke(topX, topY); } catch { }
        }

        private static void RemoveCustomChest(int topX, int topY)
        {
            try
            {
                int chestIndex = Chest.FindChest(topX, topY);
                if (chestIndex < 0)
                    return;

                Chest.RemoveChest(chestIndex);

                if (Main.player != null)
                {
                    for (int p = 0; p < Main.player.Length; p++)
                    {
                        var player = Main.player[p];
                        if (player != null && player.chest == chestIndex)
                            player.chest = -1;
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Debug($"[TileBehaviorPatches] RemoveCustomChest failed: {ex.Message}");
            }
        }

        private static void TryPlayTileSound(int soundId, int tileX, int tileY)
        {
            if (soundId < 0) return;

            try
            {
                var seType = typeof(Main).Assembly.GetType("Terraria.Audio.SoundEngine");
                if (seType == null)
                    return;

                foreach (var m in seType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "PlaySound") continue;
                    var p = m.GetParameters();
                    if (p.Length == 6 && p[0].ParameterType == typeof(int))
                    {
                        m.Invoke(null, new object[] { soundId, tileX * 16, tileY * 16, 1, 1f, 0f });
                        return;
                    }
                }
            }
            catch { }
        }

        internal static MethodInfo PlayerOpenChestMethod => _playerOpenChestMethod;
    }

    /// <summary>
    /// Public helper API for custom container-like tiles.
    /// </summary>
    public static class CustomTileContainers
    {
        public static bool IsCustomContainerTile(int tileType)
        {
            var def = TileRegistry.GetDefinition(tileType);
            return def != null && def.IsContainer;
        }

        public static bool TryGetTileDefinition(int tileX, int tileY, out TileDefinition definition, out int tileType)
        {
            definition = null;
            tileType = -1;

            if (tileX < 0 || tileX >= Main.maxTilesX || tileY < 0 || tileY >= Main.maxTilesY)
                return false;

            var tile = Main.tile[tileX, tileY];
            if (tile == null || !tile.active())
                return false;

            tileType = tile.type;
            if (!TileRegistry.IsCustomTile(tileType))
                return false;

            definition = TileRegistry.GetDefinition(tileType);
            return definition != null;
        }

        private struct FrameLayout
        {
            public int CoordinateWidth;
            public int CoordinatePadding;
            public int[] CoordinateHeights;
        }

        public static bool TryGetTopLeft(int tileX, int tileY, TileDefinition definition, out int topX, out int topY)
        {
            topX = tileX;
            topY = tileY;

            if (definition == null) return false;
            if (tileX < 0 || tileX >= Main.maxTilesX || tileY < 0 || tileY >= Main.maxTilesY) return false;

            var tile = Main.tile[tileX, tileY];
            if (tile == null) return false;

            int tileType = tile.type;
            int width = Math.Max(1, definition.Width);
            int height = Math.Max(1, definition.Height);

            var layouts = BuildFrameLayouts(tileType, definition, height);
            for (int i = 0; i < layouts.Count; i++)
            {
                if (TryResolveTopLeftFromLayout(
                    tileX, tileY, tile.frameX, tile.frameY, tileType, width, height, layouts[i], out topX, out topY))
                {
                    return true;
                }
            }

            // Last-resort best effort based on definition values.
            int fallbackStepX = Math.Max(1, definition.CoordinateWidth + Math.Max(0, definition.CoordinatePadding));
            int fallbackLocalX = PositiveModulo(tile.frameX / fallbackStepX, width);
            int fallbackLocalY = GetLocalFrameRow(
                tile.frameY,
                definition.CoordinateHeights,
                Math.Max(0, definition.CoordinatePadding),
                height);

            topX = tileX - fallbackLocalX;
            topY = tileY - fallbackLocalY;
            return true;
        }

        private static List<FrameLayout> BuildFrameLayouts(int tileType, TileDefinition definition, int height)
        {
            var layouts = new List<FrameLayout>(4);

            if (TryGetRuntimeFrameLayout(tileType, height, out var runtimeLayout))
            {
                layouts.Add(runtimeLayout);

                if (runtimeLayout.CoordinatePadding > 0)
                {
                    var noPaddingRuntimeLayout = runtimeLayout;
                    noPaddingRuntimeLayout.CoordinatePadding = 0;
                    if (!ContainsEquivalentLayout(layouts, noPaddingRuntimeLayout))
                        layouts.Add(noPaddingRuntimeLayout);
                }
            }

            var defLayout = new FrameLayout
            {
                CoordinateWidth = Math.Max(1, definition?.CoordinateWidth ?? 16),
                CoordinatePadding = Math.Max(0, definition?.CoordinatePadding ?? 0),
                CoordinateHeights = definition?.CoordinateHeights != null && definition.CoordinateHeights.Length > 0
                    ? (int[])definition.CoordinateHeights.Clone()
                    : BuildDefaultCoordinateHeights(height)
            };

            if (!ContainsEquivalentLayout(layouts, defLayout))
                layouts.Add(defLayout);

            if (defLayout.CoordinatePadding > 0)
            {
                var noPaddingDefLayout = defLayout;
                noPaddingDefLayout.CoordinatePadding = 0;
                if (!ContainsEquivalentLayout(layouts, noPaddingDefLayout))
                    layouts.Add(noPaddingDefLayout);
            }

            return layouts;
        }

        private static bool TryGetRuntimeFrameLayout(int tileType, int height, out FrameLayout layout)
        {
            layout = default;

            try
            {
                var todType = typeof(Main).Assembly.GetType("Terraria.ObjectData.TileObjectData");
                if (todType == null)
                    return false;

                object data = TryGetTileObjectData(todType, tileType);
                if (data == null)
                    return false;

                int coordinateWidth = ReadIntMember(data, "CoordinateWidth");
                int coordinatePadding = ReadIntMember(data, "CoordinatePadding");
                int[] coordinateHeights = ReadIntArrayMember(data, "CoordinateHeights");

                layout = new FrameLayout
                {
                    CoordinateWidth = coordinateWidth > 0 ? coordinateWidth : 16,
                    CoordinatePadding = Math.Max(0, coordinatePadding),
                    CoordinateHeights = coordinateHeights != null && coordinateHeights.Length > 0
                        ? coordinateHeights
                        : BuildDefaultCoordinateHeights(height)
                };

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object TryGetTileObjectData(Type todType, int tileType)
        {
            try
            {
                var getTileData = todType.GetMethod("GetTileData", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(int), typeof(int), typeof(int) }, null);
                if (getTileData != null)
                {
                    object data = null;
                    try { data = getTileData.Invoke(null, new object[] { tileType, 0, 0 }); }
                    catch { }
                    if (data != null)
                        return data;
                }

                var dataField = todType.GetField("_data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (dataField?.GetValue(null) is IList list &&
                    tileType >= 0 &&
                    tileType < list.Count)
                {
                    return list[tileType];
                }
            }
            catch { }

            return null;
        }

        private static int ReadIntMember(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return 0;

            try
            {
                var type = instance.GetType();
                var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                    return Convert.ToInt32(field.GetValue(instance));

                var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null && prop.CanRead)
                    return Convert.ToInt32(prop.GetValue(instance, null));
            }
            catch { }

            return 0;
        }

        private static int[] ReadIntArrayMember(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

            try
            {
                object raw = null;
                var type = instance.GetType();
                var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    raw = field.GetValue(instance);
                }
                else
                {
                    var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null && prop.CanRead)
                        raw = prop.GetValue(instance, null);
                }

                if (raw is int[] ints)
                    return ints.Length > 0 ? (int[])ints.Clone() : null;

                if (raw is short[] shorts)
                {
                    if (shorts.Length == 0) return null;
                    var arr = new int[shorts.Length];
                    for (int i = 0; i < shorts.Length; i++) arr[i] = shorts[i];
                    return arr;
                }

                if (raw is IList list)
                {
                    if (list.Count == 0) return null;
                    var arr = new int[list.Count];
                    for (int i = 0; i < list.Count; i++)
                        arr[i] = Convert.ToInt32(list[i]);
                    return arr;
                }
            }
            catch { }

            return null;
        }

        private static bool ContainsEquivalentLayout(List<FrameLayout> layouts, FrameLayout candidate)
        {
            if (layouts == null)
                return false;

            for (int i = 0; i < layouts.Count; i++)
            {
                var existing = layouts[i];
                if (existing.CoordinateWidth != candidate.CoordinateWidth)
                    continue;
                if (existing.CoordinatePadding != candidate.CoordinatePadding)
                    continue;
                if (!AreIntArraysEqual(existing.CoordinateHeights, candidate.CoordinateHeights))
                    continue;

                return true;
            }

            return false;
        }

        private static bool AreIntArraysEqual(int[] a, int[] b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null)
                return false;
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        private static bool TryResolveTopLeftFromLayout(
            int tileX,
            int tileY,
            int frameX,
            int frameY,
            int tileType,
            int width,
            int height,
            FrameLayout layout,
            out int topX,
            out int topY)
        {
            topX = tileX;
            topY = tileY;

            int stepX = Math.Max(1, layout.CoordinateWidth + Math.Max(0, layout.CoordinatePadding));
            int frameTileX = frameX / stepX;
            int localX = PositiveModulo(frameTileX, width);
            int localY = GetLocalFrameRow(frameY, layout.CoordinateHeights, layout.CoordinatePadding, height);

            int candidateTopX = tileX - localX;
            int candidateTopY = tileY - localY;

            if (!IsTopLeftCandidate(candidateTopX, candidateTopY, tileType, width, height, layout))
                return false;

            topX = candidateTopX;
            topY = candidateTopY;
            return true;
        }

        private static bool IsTopLeftCandidate(int topX, int topY, int tileType, int width, int height, FrameLayout layout)
        {
            if (topX < 0 || topY < 0 || topX + width > Main.maxTilesX || topY + height > Main.maxTilesY)
                return false;

            int stepX = Math.Max(1, layout.CoordinateWidth + Math.Max(0, layout.CoordinatePadding));
            int patternWidth = Math.Max(1, stepX * Math.Max(1, width));
            int patternHeight = ComputePatternHeight(layout.CoordinateHeights, layout.CoordinatePadding, height);
            int[] rowOffsets = BuildRowOffsets(layout.CoordinateHeights, layout.CoordinatePadding, height);

            for (int lx = 0; lx < width; lx++)
            {
                for (int ly = 0; ly < height; ly++)
                {
                    var tile = Main.tile[topX + lx, topY + ly];
                    if (tile == null || !tile.active() || tile.type != tileType)
                        return false;

                    int expectedX = lx * stepX;
                    int actualX = PositiveModulo(tile.frameX, patternWidth);
                    if (actualX != expectedX)
                        return false;

                    int expectedY = rowOffsets[ly];
                    int actualY = PositiveModulo(tile.frameY, patternHeight);
                    if (actualY != expectedY)
                        return false;
                }
            }

            return true;
        }

        private static int GetLocalFrameRow(int frameY, int[] coordinateHeights, int coordinatePadding, int height)
        {
            if (height <= 1)
                return 0;

            int patternHeight = ComputePatternHeight(coordinateHeights, coordinatePadding, height);
            if (patternHeight <= 0)
                return 0;

            int wrapped = PositiveModulo(frameY, patternHeight);
            int[] rowHeights = coordinateHeights != null && coordinateHeights.Length > 0
                ? coordinateHeights
                : BuildDefaultCoordinateHeights(height);
            int padding = Math.Max(0, coordinatePadding);
            int acc = 0;
            for (int row = 0; row < height; row++)
            {
                int rowHeight = rowHeights[Math.Min(row, rowHeights.Length - 1)];
                int rowStep = Math.Max(1, rowHeight) + padding;
                if (wrapped < acc + rowStep)
                    return row;
                acc += rowStep;
            }

            return 0;
        }

        private static int[] BuildRowOffsets(int[] coordinateHeights, int coordinatePadding, int height)
        {
            int rowCount = Math.Max(1, height);
            int[] rowHeights = coordinateHeights != null && coordinateHeights.Length > 0
                ? coordinateHeights
                : BuildDefaultCoordinateHeights(rowCount);
            int padding = Math.Max(0, coordinatePadding);

            int[] offsets = new int[rowCount];
            int acc = 0;
            for (int row = 0; row < rowCount; row++)
            {
                offsets[row] = acc;
                int rowHeight = rowHeights[Math.Min(row, rowHeights.Length - 1)];
                acc += Math.Max(1, rowHeight) + padding;
            }

            return offsets;
        }

        private static int ComputePatternHeight(int[] coordinateHeights, int coordinatePadding, int height)
        {
            int rowCount = Math.Max(1, height);
            int[] rowHeights = coordinateHeights != null && coordinateHeights.Length > 0
                ? coordinateHeights
                : BuildDefaultCoordinateHeights(rowCount);
            int padding = Math.Max(0, coordinatePadding);

            int total = 0;
            for (int row = 0; row < rowCount; row++)
            {
                int rowHeight = rowHeights[Math.Min(row, rowHeights.Length - 1)];
                total += Math.Max(1, rowHeight) + padding;
            }

            return Math.Max(1, total);
        }

        private static int PositiveModulo(int value, int modulus)
        {
            if (modulus <= 0)
                return 0;

            int result = value % modulus;
            if (result < 0)
                result += modulus;

            return result;
        }

        private static int[] BuildDefaultCoordinateHeights(int height)
        {
            int[] arr = new int[Math.Max(1, height)];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = 16;
            return arr;
        }

        public static bool TryOpenContainer(Player player, int tileX, int tileY, TileDefinition definition)
        {
            if (player == null || definition == null || !definition.IsContainer)
                return false;

            if (!TryGetTopLeft(tileX, tileY, definition, out int topX, out int topY))
                return false;

            int chestIndex = Chest.FindChest(topX, topY);
            if (chestIndex < 0)
                chestIndex = Chest.CreateChest(topX, topY, -1);
            if (chestIndex < 0)
                return false;

            var chest = Main.chest[chestIndex];
            if (chest != null)
            {
                if (definition.ContainerCapacity > 0 && chest.maxItems != definition.ContainerCapacity)
                {
                    var resize = chest.GetType().GetMethod("Resize", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
                    resize?.Invoke(chest, new object[] { definition.ContainerCapacity });
                }

                chest.name = string.IsNullOrEmpty(definition.ContainerName) ? definition.DisplayName : definition.ContainerName;
            }

            Main.stackSplit = 600;
            int previousChest = player.chest;

            if (player.chest == chestIndex)
            {
                player.chest = -1;
                TryPlayChestSound(11); // close
                return true;
            }

            if (TileBehaviorPatches.PlayerOpenChestMethod != null)
            {
                TileBehaviorPatches.PlayerOpenChestMethod.Invoke(player, new object[] { topX, topY, chestIndex });
            }
            else
            {
                player.chest = chestIndex;
                player.chestX = topX;
                player.chestY = topY;
                Main.playerInventory = true;
            }

            TryPlayChestSound(previousChest == -1 ? 10 : 12); // open/switch
            return true;
        }

        private static void TryPlayChestSound(int soundId)
        {
            try
            {
                var seType = typeof(Main).Assembly.GetType("Terraria.Audio.SoundEngine");
                foreach (var m in seType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "PlaySound") continue;
                    var p = m.GetParameters();
                    if (p.Length == 6 && p[0].ParameterType == typeof(int))
                    {
                        m.Invoke(null, new object[] { soundId, -1, -1, 1, 1f, 0f });
                        break;
                    }
                }
            }
            catch { }
        }
    }
}
