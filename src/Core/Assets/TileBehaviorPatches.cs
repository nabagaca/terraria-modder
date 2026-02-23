using System;
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

                if (def.IsContainer)
                {
                    if (def.ContainerRequiresEmptyToBreak && !WorldGen.destroyObject)
                    {
                        int chestIndex = Chest.FindChest(topX, topY);
                        if (chestIndex >= 0 && ChestHasItems(chestIndex))
                            return false;
                    }

                    _pendingBreak = true;
                    _pendingBreakTopX = topX;
                    _pendingBreakTopY = topY;
                    _pendingBreakDef = def;
                }
                else
                {
                    _pendingBreak = true;
                    _pendingBreakTopX = topX;
                    _pendingBreakTopY = topY;
                    _pendingBreakDef = def;
                }
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
                        Chest.DestroyChest(_pendingBreakTopX, _pendingBreakTopY);
                    }

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

        public static bool TryGetTopLeft(int tileX, int tileY, TileDefinition definition, out int topX, out int topY)
        {
            topX = tileX;
            topY = tileY;

            if (definition == null) return false;
            if (tileX < 0 || tileX >= Main.maxTilesX || tileY < 0 || tileY >= Main.maxTilesY) return false;

            var tile = Main.tile[tileX, tileY];
            if (tile == null) return false;

            int width = Math.Max(1, definition.Width);
            int height = Math.Max(1, definition.Height);

            int frameTileX = tile.frameX / 18;
            int frameTileY = tile.frameY / 18;

            int localX = frameTileX % width;
            int localY = frameTileY % height;

            topX = tileX - localX;
            topY = tileY - localY;
            return true;
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
