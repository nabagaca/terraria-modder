using System;
using System.Reflection;
using HarmonyLib;
using Terraria;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core.Assets
{
    /// <summary>
    /// Safety patches for custom item types (>= ItemID.Count).
    ///
    /// Patches:
    ///   1. Item.GetDrawHitbox — return default rect for custom types with null textures
    ///   2. ArmorSetBonuses.GetCompleteSet — bounds-check SetsContaining array
    ///      (allocated fresh in BuildLookup() before ItemID.Count is updated)
    ///   3. Main.DrawItems — finalizer catches null texture crashes from world items
    ///   4. ItemSlot.DrawItemIcon — finalizer catches null texture in inventory slots
    ///   5. Main.LoadItem — skip if texture entry is null (safety net)
    /// </summary>
    internal static class DrawPatches
    {
        private static Harmony _harmony;
        private static ILogger _log;
        private static bool _applied;

        // Cached reflection fields
        private static FieldInfo _textureItemField;
        private static FieldInfo _setsContainingField;
        private static FieldInfo _headItemField;
        private static FieldInfo _bodyItemField;

        public static void Initialize(ILogger logger)
        {
            _log = logger;
            _harmony = new Harmony("com.terrariamodder.assets.draw");

            // Cache TextureAssets.Item field reference
            try
            {
                var texAssetsType = typeof(Terraria.GameContent.TextureAssets);
                _textureItemField = texAssetsType.GetField("Item", BindingFlags.Public | BindingFlags.Static);
            }
            catch (Exception ex)
            {
                _log?.Warn($"[DrawPatches] Failed to cache TextureAssets.Item: {ex.Message}");
            }
        }

        public static void ApplyPatches()
        {
            if (_applied) return;

            try
            {
                int patchCount = 0;
                patchCount += PatchGetDrawHitbox();
                patchCount += PatchGetCompleteSet();
                patchCount += PatchLoadItem();
                patchCount += PatchDrawItem();
                patchCount += PatchDrawItemIcon();
                patchCount += PatchItemSorting();
                patchCount += PatchQuickStacking();

                _applied = true;
                _log?.Info($"[DrawPatches] Applied {patchCount} patches");
            }
            catch (Exception ex)
            {
                _log?.Error($"[DrawPatches] Failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Check if TextureAssets.Item[type] is non-null (safe to access).
        /// </summary>
        private static bool IsTextureAvailable(int type)
        {
            try
            {
                // Re-fetch the array reference each time since TypeExtension may replace it
                var arr = _textureItemField?.GetValue(null) as Array;
                if (arr == null || type < 0 || type >= arr.Length)
                    return false;

                return arr.GetValue(type) != null;
            }
            catch
            {
                return false;
            }
        }

        // ── 1. Item.GetDrawHitbox — safe fallback for types with null textures ──

        private static int PatchGetDrawHitbox()
        {
            try
            {
                var method = typeof(Item).GetMethod("GetDrawHitbox",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(int), typeof(Player) }, null);

                if (method == null)
                {
                    _log?.Warn("[DrawPatches] Item.GetDrawHitbox not found");
                    return 0;
                }

                _harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(DrawPatches), nameof(GetDrawHitbox_Prefix)));

                _log?.Debug("[DrawPatches] Patched Item.GetDrawHitbox");
                return 1;
            }
            catch (Exception ex)
            {
                _log?.Warn($"[DrawPatches] GetDrawHitbox patch failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Prefix for Item.GetDrawHitbox. If the texture for this type is null,
        /// return a default rectangle to prevent NullReferenceException in Main.LoadItem.
        /// Works for both custom items (before deferred texture load) and any null entry.
        /// </summary>
        private static bool GetDrawHitbox_Prefix(int type, ref object __result)
        {
            if (IsTextureAvailable(type))
                return true; // texture exists, let vanilla handle

            // Texture is null — return a default Rectangle(0, 0, width, height)
            try
            {
                var def = type >= ItemRegistry.VanillaItemCount
                    ? ItemRegistry.GetDefinition(type)
                    : null;
                int w = def?.Width ?? 20;
                int h = def?.Height ?? 20;

                // Create Rectangle via reflection (avoids XNA compile dependency)
                var rectType = typeof(Item).GetMethod("GetDrawHitbox",
                    BindingFlags.Public | BindingFlags.Static)?.ReturnType;

                if (rectType != null)
                {
                    __result = Activator.CreateInstance(rectType, new object[] { 0, 0, w, h });
                    return false;
                }
            }
            catch
            {
                // Last resort: let vanilla run and hope for the best
            }

            return true;
        }

        // ── 2. ArmorSetBonuses.GetCompleteSet — bounds protection ──

        private static int PatchGetCompleteSet()
        {
            try
            {
                var asbType = typeof(Main).Assembly.GetType("Terraria.DataStructures.ArmorSetBonuses");
                if (asbType == null)
                {
                    _log?.Warn("[DrawPatches] ArmorSetBonuses type not found");
                    return 0;
                }

                var method = asbType.GetMethod("GetCompleteSet",
                    BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    _log?.Warn("[DrawPatches] ArmorSetBonuses.GetCompleteSet not found");
                    return 0;
                }

                // Cache reflection fields for the hot-path prefix
                _setsContainingField = asbType.GetField("SetsContaining", BindingFlags.Public | BindingFlags.Static);
                var queryContextType = method.GetParameters()[0].ParameterType;
                _headItemField = queryContextType.GetField("HeadItem");
                _bodyItemField = queryContextType.GetField("BodyItem");

                _harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(DrawPatches), nameof(GetCompleteSet_Prefix)));
                _log?.Debug("[DrawPatches] Patched ArmorSetBonuses.GetCompleteSet");
                return 1;
            }
            catch (Exception ex)
            {
                _log?.Warn($"[DrawPatches] GetCompleteSet patch failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Prefix for ArmorSetBonuses.GetCompleteSet. The SetsContaining array is
        /// allocated fresh in BuildLookup() with size ItemID.Count (6145). Custom items
        /// have type >= 6145 and cause IndexOutOfRangeException. Since custom items
        /// never have vanilla armor set bonuses, return null immediately if any
        /// equipped armor piece has a type out of bounds.
        /// </summary>
        private static bool GetCompleteSet_Prefix(ref object __result, object context)
        {
            try
            {
                int headItem = (int)_headItemField.GetValue(context);
                int bodyItem = (int)_bodyItemField.GetValue(context);

                var arr = _setsContainingField?.GetValue(null) as Array;
                if (arr != null &&
                    (headItem < 0 || headItem >= arr.Length ||
                     bodyItem < 0 || bodyItem >= arr.Length))
                {
                    __result = null;
                    return false; // Skip vanilla — would crash
                }
            }
            catch
            {
                // If reflection fails, let vanilla run
            }
            return true;
        }

        // ── 3. Main.DrawItem — skip items with null textures ──

        private static int PatchDrawItem()
        {
            try
            {
                // DrawItems is a public instance method on Main
                var method = typeof(Main).GetMethod("DrawItems",
                    BindingFlags.Public | BindingFlags.Instance);

                if (method == null)
                {
                    _log?.Warn("[DrawPatches] Main.DrawItems not found");
                    return 0;
                }

                _harmony.Patch(method,
                    finalizer: new HarmonyMethod(typeof(DrawPatches), nameof(DrawItems_Finalizer)));

                _log?.Debug("[DrawPatches] Patched Main.DrawItems (finalizer)");
                return 1;
            }
            catch (Exception ex)
            {
                _log?.Warn($"[DrawPatches] DrawItems patch failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Finalizer for Main.DrawItems. If the method throws (null texture on a world item),
        /// swallow the exception to prevent it from crashing the entire draw frame.
        /// Without this, one bad item on the ground kills inventory/equipment rendering too.
        /// </summary>
        private static Exception DrawItems_Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                // Suppress during texture stabilization — expected transient null textures
                if (!AssetSystem.TexturesStable) return null;

                if (_drawItemErrorCount < 5)
                {
                    _drawItemErrorCount++;
                    _log?.Warn($"[DrawPatches] DrawItems crash caught ({_drawItemErrorCount}): {__exception.GetType().Name}: {__exception.Message}");
                }
            }
            return null; // Swallow — world items just won't draw this frame
        }

        private static int _drawItemErrorCount = 0;

        // ── 4. ItemSlot.DrawItemIcon — null texture safety ──

        private static int PatchDrawItemIcon()
        {
            try
            {
                var itemSlotType = typeof(Main).Assembly.GetType("Terraria.UI.ItemSlot");
                if (itemSlotType == null)
                {
                    _log?.Warn("[DrawPatches] ItemSlot type not found");
                    return 0;
                }

                // Find DrawItemIcon by name — it's a public static method with many overloads
                MethodInfo method = null;
                foreach (var m in itemSlotType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name == "DrawItemIcon")
                    {
                        method = m;
                        break;
                    }
                }

                if (method == null)
                {
                    _log?.Warn("[DrawPatches] ItemSlot.DrawItemIcon not found");
                    return 0;
                }

                _harmony.Patch(method,
                    finalizer: new HarmonyMethod(typeof(DrawPatches), nameof(DrawItemIcon_Finalizer)));

                _log?.Debug("[DrawPatches] Patched ItemSlot.DrawItemIcon (finalizer)");
                return 1;
            }
            catch (Exception ex)
            {
                _log?.Warn($"[DrawPatches] DrawItemIcon patch failed: {ex.Message}");
                return 0;
            }
        }

        private static Exception DrawItemIcon_Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                // Suppress during texture stabilization — expected transient null textures
                if (!AssetSystem.TexturesStable) return null;

                if (_drawIconErrorCount < 5)
                {
                    _drawIconErrorCount++;
                    _log?.Warn($"[DrawPatches] DrawItemIcon error ({_drawIconErrorCount}): {__exception.GetType().Name}: {__exception.Message}");
                }
            }
            return null; // Swallow — slot just won't draw icon
        }

        private static int _drawIconErrorCount = 0;

        // ── 5. ItemSorting — catch crashes from undersized _layerIndexForItemType ──

        private static int PatchItemSorting()
        {
            try
            {
                var sortType = typeof(Main).Assembly.GetType("Terraria.UI.ItemSorting");
                if (sortType == null)
                {
                    _log?.Debug("[DrawPatches] ItemSorting type not found (non-critical)");
                    return 0;
                }

                // Patch Sort method — it calls SetupSortingPriorities which creates undersized array
                MethodInfo method = null;
                foreach (var m in sortType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name == "Sort")
                    {
                        method = m;
                        break;
                    }
                }

                if (method == null)
                {
                    _log?.Debug("[DrawPatches] ItemSorting.Sort not found");
                    return 0;
                }

                _harmony.Patch(method,
                    finalizer: new HarmonyMethod(typeof(DrawPatches), nameof(ItemSorting_Finalizer)));

                _log?.Debug("[DrawPatches] Patched ItemSorting.Sort (finalizer)");
                return 1;
            }
            catch (Exception ex)
            {
                _log?.Debug($"[DrawPatches] ItemSorting patch failed: {ex.Message}");
                return 0;
            }
        }

        private static Exception ItemSorting_Finalizer(Exception __exception)
        {
            if (__exception != null && _sortErrorCount < 3)
            {
                _sortErrorCount++;
                _log?.Warn($"[DrawPatches] ItemSorting crash caught ({_sortErrorCount}): {__exception.GetType().Name}: {__exception.Message}");
            }
            return null;
        }

        private static int _sortErrorCount = 0;

        // ── 6. QuickStacking — catch crashes from undersized firstEntryForType ──

        private static int PatchQuickStacking()
        {
            try
            {
                var qsType = typeof(Main).Assembly.GetType("Terraria.GameContent.QuickStacking");
                if (qsType == null)
                {
                    _log?.Debug("[DrawPatches] QuickStacking type not found (non-critical)");
                    return 0;
                }

                // Patch the Sort method which triggers quick-stack with custom items
                MethodInfo method = null;
                foreach (var m in qsType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name == "Sort")
                    {
                        method = m;
                        break;
                    }
                }

                if (method == null)
                {
                    _log?.Debug("[DrawPatches] QuickStacking.Sort not found");
                    return 0;
                }

                _harmony.Patch(method,
                    finalizer: new HarmonyMethod(typeof(DrawPatches), nameof(QuickStacking_Finalizer)));

                _log?.Debug("[DrawPatches] Patched QuickStacking.Sort (finalizer)");
                return 1;
            }
            catch (Exception ex)
            {
                _log?.Debug($"[DrawPatches] QuickStacking patch failed: {ex.Message}");
                return 0;
            }
        }

        private static Exception QuickStacking_Finalizer(Exception __exception)
        {
            if (__exception != null && _quickStackErrorCount < 3)
            {
                _quickStackErrorCount++;
                _log?.Warn($"[DrawPatches] QuickStacking crash caught ({_quickStackErrorCount}): {__exception.GetType().Name}: {__exception.Message}");
            }
            return null;
        }

        private static int _quickStackErrorCount = 0;

        // ── 7. Main.LoadItem — null safety net ──

        private static int PatchLoadItem()
        {
            try
            {
                var method = typeof(Main).GetMethod("LoadItem",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(int) }, null);

                if (method == null)
                {
                    _log?.Warn("[DrawPatches] Main.LoadItem not found");
                    return 0;
                }

                _harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(DrawPatches), nameof(LoadItem_Prefix)));

                _log?.Debug("[DrawPatches] Patched Main.LoadItem");
                return 1;
            }
            catch (Exception ex)
            {
                _log?.Warn($"[DrawPatches] LoadItem patch failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Prefix for Main.LoadItem. Skip for custom types only — their textures are
        /// managed by TextureLoader. Without this, vanilla LoadItem tries to load
        /// "Images/Item_6154" from disk and crashes.
        /// Vanilla items always pass through to the original method.
        /// </summary>
        private static bool LoadItem_Prefix(int i)
        {
            // Skip vanilla LoadItem for custom types — textures managed by TextureLoader
            if (i >= ItemRegistry.VanillaItemCount)
                return false;

            return true; // Let vanilla handle its own items
        }
    }
}
