using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Terraria;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core.Assets
{
    /// <summary>
    /// Safety patches for custom items (type >= VanillaItemCount) in vanilla Sort/QuickStack.
    ///
    /// Patches:
    ///   1. ItemSorting.Sort — extract custom items before sort, compact after vanilla items
    ///   2. ItemSorting.GetSortingLayerIndex — bounds check for custom types
    ///   3. QuickStacking.MatchingItemTypeDestinationList.firstEntryForType — resize array
    ///
    /// Sort crashes on custom items because they don't match any sorting layer whitelist
    /// (built by SetupWhiteLists which only iterates to ItemID.Count). Uncategorized items
    /// cause ArgumentOutOfRangeException in the placement loop (_sort_counts[0] on empty list).
    ///
    /// QuickStack works natively (arrays resized by TypeExtension + firstEntryForType resize).
    /// </summary>
    internal static class ItemProtectionPatches
    {
        private static Harmony _harmony;
        private static ILogger _log;
        private static bool _applied;

        [ThreadStatic]
        private static List<Item> _sortStash;

        public static void Initialize(ILogger logger)
        {
            _log = logger;
            _harmony = new Harmony("com.terrariamodder.assets.protection");
        }

        public static void ApplyPatches()
        {
            if (_applied) return;

            try
            {
                int patchCount = 0;
                patchCount += PatchSort();
                patchCount += PatchGetSortingLayerIndex();
                ResizeQuickStackArray();

                _applied = true;
                _log?.Info($"[ItemProtectionPatches] Applied {patchCount} patches + array resize");
            }
            catch (Exception ex)
            {
                _log?.Error($"[ItemProtectionPatches] Failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ── 1. Sort Protection ──
        // Custom items crash Sort because they're not in any layer whitelist.
        // Extract before, let vanilla sort vanilla items, then compact custom
        // items right after the last vanilla item (same visual result as if
        // they were sorted together).

        private static int PatchSort()
        {
            try
            {
                var sortingType = typeof(Main).Assembly.GetType("Terraria.UI.ItemSorting");
                if (sortingType == null) return 0;

                var method = sortingType.GetMethod("Sort",
                    BindingFlags.NonPublic | BindingFlags.Static, null,
                    new[] { typeof(bool), typeof(Item[]), typeof(int[]) }, null);

                if (method == null)
                {
                    _log?.Warn("[ItemProtectionPatches] ItemSorting.Sort not found");
                    return 0;
                }

                _harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(ItemProtectionPatches), nameof(Sort_Prefix)),
                    postfix: new HarmonyMethod(typeof(ItemProtectionPatches), nameof(Sort_Postfix)));

                _log?.Debug("[ItemProtectionPatches] Patched ItemSorting.Sort");
                return 1;
            }
            catch (Exception ex)
            {
                _log?.Warn($"[ItemProtectionPatches] Sort patch failed: {ex.Message}");
                return 0;
            }
        }

        private static void Sort_Prefix(Item[] inv, int[] ignoreSlots)
        {
            if (inv == null) return;
            _sortStash = null;

            var ignoreSet = new HashSet<int>();
            if (ignoreSlots != null)
                foreach (int s in ignoreSlots)
                    ignoreSet.Add(s);

            for (int i = 0; i < inv.Length; i++)
            {
                if (ignoreSet.Contains(i)) continue;
                var item = inv[i];
                if (item == null || item.IsAir || item.type < ItemRegistry.VanillaItemCount) continue;

                if (_sortStash == null)
                    _sortStash = new List<Item>();

                _sortStash.Add(item.Clone());
                inv[i] = new Item();
            }

            if (_sortStash != null)
                _log?.Info($"[ItemProtectionPatches] Sort: extracted {_sortStash.Count} custom items");
        }

        private static void Sort_Postfix(Item[] inv, int[] ignoreSlots)
        {
            if (_sortStash == null || inv == null) return;

            var ignoreSet = new HashSet<int>();
            if (ignoreSlots != null)
                foreach (int s in ignoreSlots)
                    ignoreSet.Add(s);

            // Sort custom items by type so identical items group together
            _sortStash.Sort((a, b) => a.type.CompareTo(b.type));

            // Find where vanilla items end (first empty non-ignored slot after sorted items)
            // Vanilla sort compacts items to the start, so empty slots are at the end.
            // Place custom items right after the last vanilla item.
            int firstEmpty = -1;
            for (int i = 0; i < inv.Length; i++)
            {
                if (ignoreSet.Contains(i)) continue;
                if (inv[i] == null || inv[i].IsAir)
                {
                    firstEmpty = i;
                    break;
                }
            }

            // If no empty slot found, try from the end
            if (firstEmpty < 0)
                firstEmpty = 0;

            int restored = 0;
            int slot = firstEmpty;
            for (int si = 0; si < _sortStash.Count; si++)
            {
                // Find next available slot from current position
                while (slot < inv.Length && (ignoreSet.Contains(slot) || (inv[slot] != null && !inv[slot].IsAir)))
                    slot++;

                if (slot < inv.Length)
                {
                    inv[slot] = _sortStash[si];
                    restored++;
                    slot++;
                }
                else
                {
                    _log?.Warn($"[ItemProtectionPatches] Sort: no empty slot for custom item type {_sortStash[si].type}");
                }
            }

            _log?.Info($"[ItemProtectionPatches] Sort: restored {restored}/{_sortStash.Count} custom items (after vanilla items)");
            _sortStash = null;
        }

        // ── 2. GetSortingLayerIndex bounds check ──

        private static int PatchGetSortingLayerIndex()
        {
            try
            {
                var sortingType = typeof(Main).Assembly.GetType("Terraria.UI.ItemSorting");
                if (sortingType == null) return 0;

                var method = sortingType.GetMethod("GetSortingLayerIndex",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(int) }, null);

                if (method == null)
                {
                    _log?.Warn("[ItemProtectionPatches] GetSortingLayerIndex not found");
                    return 0;
                }

                _harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(ItemProtectionPatches), nameof(GetSortingLayerIndex_Prefix)));

                _log?.Debug("[ItemProtectionPatches] Patched GetSortingLayerIndex");
                return 1;
            }
            catch (Exception ex)
            {
                _log?.Warn($"[ItemProtectionPatches] GetSortingLayerIndex patch failed: {ex.Message}");
                return 0;
            }
        }

        private static bool GetSortingLayerIndex_Prefix(int itemType, ref int __result)
        {
            if (itemType >= ItemRegistry.VanillaItemCount)
            {
                __result = 0;
                return false;
            }
            return true;
        }

        // ── 3. Resize QuickStacking firstEntryForType array ──

        private static void ResizeQuickStackArray()
        {
            try
            {
                var quickStackType = typeof(Main).Assembly.GetType("Terraria.GameContent.QuickStacking");
                if (quickStackType == null)
                {
                    _log?.Warn("[ItemProtectionPatches] QuickStacking type not found");
                    return;
                }

                var nestedType = quickStackType.GetNestedType("MatchingItemTypeDestinationList",
                    BindingFlags.NonPublic);
                if (nestedType == null)
                {
                    _log?.Warn("[ItemProtectionPatches] MatchingItemTypeDestinationList not found");
                    return;
                }

                var scratchField = quickStackType.GetField("matchingItemTypeScratch",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (scratchField == null)
                {
                    _log?.Warn("[ItemProtectionPatches] matchingItemTypeScratch field not found");
                    return;
                }

                var scratchInstance = scratchField.GetValue(null);
                if (scratchInstance == null)
                {
                    _log?.Warn("[ItemProtectionPatches] matchingItemTypeScratch is null");
                    return;
                }

                var arrayField = nestedType.GetField("firstEntryForType",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (arrayField == null)
                {
                    _log?.Warn("[ItemProtectionPatches] firstEntryForType field not found");
                    return;
                }

                var currentArray = arrayField.GetValue(scratchInstance) as int[];
                if (currentArray == null)
                {
                    _log?.Warn("[ItemProtectionPatches] firstEntryForType is null");
                    return;
                }

                int needed = ItemRegistry.VanillaItemCount + ItemRegistry.Count + 64;
                if (currentArray.Length >= needed)
                {
                    _log?.Debug($"[ItemProtectionPatches] firstEntryForType already large enough ({currentArray.Length} >= {needed})");
                    return;
                }

                var newArray = new int[needed];
                Array.Copy(currentArray, newArray, currentArray.Length);
                arrayField.SetValue(scratchInstance, newArray);

                _log?.Info($"[ItemProtectionPatches] Resized firstEntryForType: {currentArray.Length} → {needed}");
            }
            catch (Exception ex)
            {
                _log?.Warn($"[ItemProtectionPatches] firstEntryForType resize failed: {ex.Message}");
            }
        }
    }
}
