using System;
using System.Reflection;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core.Assets
{
    /// <summary>
    /// Extends ItemID.Count and resizes all type-indexed arrays to accommodate custom items.
    /// Must run during initialization BEFORE any custom items are created.
    ///
    /// Arrays resized:
    ///   - ~130 ItemID.Sets arrays (via SetFactory)
    ///   - TextureAssets.Item[] and ItemFlame[]
    ///   - Lang._itemNameCache[] and _itemTooltipCache[]
    ///   - Main.itemAnimations[] and itemAnimationsRegistered[]
    ///   - Item.staff[] and Item.claw[]
    ///   - Any other static array in Terraria assembly matching ItemID.Count size
    ///     (comprehensive scan catches ArmorSetBonuses, QuickStacking, etc.)
    /// </summary>
    public static class TypeExtension
    {
        private static ILogger _log;
        private static bool _applied;

        /// <summary>The original vanilla item count before extension.</summary>
        public static int OriginalCount { get; private set; }

        /// <summary>The new extended count.</summary>
        public static int ExtendedCount { get; private set; }

        /// <summary>
        /// Extend ItemID.Count and resize all type-indexed arrays.
        /// Call once during early initialization (Main constructor postfix).
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="newCount">New maximum item count (default 32767 = max positive short).</param>
        /// <returns>The original vanilla item count, or -1 on failure.</returns>
        public static int Apply(ILogger logger, int newCount = 32767)
        {
            _log = logger;
            if (_applied)
            {
                _log?.Warn("[TypeExtension] Already applied");
                return OriginalCount;
            }

            try
            {
                _log?.Info("[TypeExtension] Extending item type system...");

                // Step 1: Read and save original ItemID.Count
                var itemIdType = typeof(Terraria.ID.ItemID);
                var countField = itemIdType.GetField("Count", BindingFlags.Public | BindingFlags.Static);
                if (countField == null)
                {
                    _log?.Error("[TypeExtension] ItemID.Count field not found");
                    return -1;
                }

                var countVal = countField.GetValue(null);
                if (countVal is short s) OriginalCount = s;
                else if (countVal is int i) OriginalCount = i;
                else
                {
                    _log?.Error($"[TypeExtension] ItemID.Count has unexpected type: {countVal?.GetType()}");
                    return -1;
                }
                ExtendedCount = newCount;
                _log?.Info($"[TypeExtension] Original ItemID.Count = {OriginalCount}, extending to {newCount}");

                // Step 2: Resize ItemID.Sets arrays
                int setsResized = ResizeItemIdSets(itemIdType, OriginalCount, newCount);

                // Step 3: Resize TextureAssets arrays
                int textureResized = ResizeTextureAssets(OriginalCount, newCount);

                // Step 4: Resize Lang caches
                int langResized = ResizeLangCaches(OriginalCount, newCount);

                // Step 5: Resize Main arrays
                int mainResized = ResizeMainArrays(OriginalCount, newCount);

                // Step 6: Resize Item static arrays (staff, claw)
                int itemResized = ResizeItemArrays(OriginalCount, newCount);

                // Step 7: Comprehensive scan — catch any static array in the Terraria assembly
                // that matches ItemID.Count size but wasn't covered by specific resize methods.
                // This catches ArmorSetBonuses, QuickStacking, and any other missed arrays.
                int assemblyResized = ResizeAllAssemblyArrays(OriginalCount, newCount);

                // DO NOT change ItemID.Count — PostSetupContent() iterates 0..Count-1
                // and runs AFTER OnGameReady (during DrawSplash→Initialize_AlmostEverything).
                // It accesses ContentSamples.ItemsByType which won't have entries for custom types yet.
                // Instance arrays sized with new int[ItemID.Count] (QuickStacking, ItemSorting, etc.)
                // are handled by safety finalizers in DrawPatches instead.
                // countField.SetValue(null, (short)newCount);

                _applied = true;
                _log?.Info($"[TypeExtension] Complete: {setsResized} Sets + {textureResized} texture + {langResized} lang + {mainResized} main + {itemResized} item + {assemblyResized} assembly-wide arrays resized");

                return OriginalCount;
            }
            catch (Exception ex)
            {
                _log?.Error($"[TypeExtension] Failed: {ex.Message}\n{ex.StackTrace}");
                return -1;
            }
        }

        private static int ResizeItemIdSets(Type itemIdType, int oldSize, int newSize)
        {
            int count = 0;
            try
            {
                var setsType = itemIdType.GetNestedType("Sets", BindingFlags.Public);
                if (setsType == null)
                {
                    _log?.Warn("[TypeExtension] ItemID.Sets not found");
                    return 0;
                }

                // Update SetFactory._size and clear caches
                var factoryField = setsType.GetField("Factory", BindingFlags.Public | BindingFlags.Static);
                if (factoryField != null)
                {
                    var factory = factoryField.GetValue(null);
                    if (factory != null)
                    {
                        var sizeField = factory.GetType().GetField("_size", BindingFlags.NonPublic | BindingFlags.Instance);
                        sizeField?.SetValue(factory, newSize);

                        // Clear buffer caches so new arrays use correct size
                        foreach (var cacheName in new[] { "_boolBufferCache", "_intBufferCache", "_ushortBufferCache", "_floatBufferCache" })
                        {
                            var cacheField = factory.GetType().GetField(cacheName, BindingFlags.NonPublic | BindingFlags.Instance);
                            if (cacheField != null)
                            {
                                var cache = cacheField.GetValue(factory);
                                cache?.GetType().GetMethod("Clear")?.Invoke(cache, null);
                            }
                        }
                        _log?.Debug("[TypeExtension] Updated SetFactory._size and cleared caches");
                    }
                }

                // Resize all static array fields in Sets
                var fields = setsType.GetFields(BindingFlags.Public | BindingFlags.Static);
                foreach (var field in fields)
                {
                    if (field.Name == "Factory") continue;
                    if (!field.FieldType.IsArray) continue;

                    try
                    {
                        var arr = field.GetValue(null) as Array;
                        if (arr == null) continue;

                        // Only resize arrays that match the old item count
                        if (arr.Length != oldSize) continue;

                        var elemType = field.FieldType.GetElementType();
                        var newArr = Array.CreateInstance(elemType, newSize);
                        Array.Copy(arr, newArr, Math.Min(arr.Length, newSize));

                        // Fill new entries with the correct default value
                        // Many int Sets use -1 as "not set", detect and fill accordingly
                        FillNewEntries(newArr, arr, oldSize, newSize, elemType);

                        field.SetValue(null, newArr);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        _log?.Debug($"[TypeExtension] Failed to resize Sets.{field.Name}: {ex.Message}");
                    }
                }

                _log?.Debug($"[TypeExtension] Resized {count} ItemID.Sets arrays");
            }
            catch (Exception ex)
            {
                _log?.Error($"[TypeExtension] Error resizing Sets: {ex.Message}");
            }
            return count;
        }

        /// <summary>
        /// Fill newly-extended array entries with the appropriate default value.
        /// Detects the default by checking what value most entries in the original array had.
        /// Critical for int arrays from CreateIntSet(-1) which need -1, not 0.
        /// </summary>
        private static void FillNewEntries(Array newArr, Array oldArr, int oldSize, int newSize, Type elemType)
        {
            if (oldSize >= newSize) return;

            if (elemType == typeof(int))
            {
                // Sample the last few entries to detect the default value
                // Most entries will be the default, only a few are overridden
                var intArr = (int[])oldArr;
                int defaultVal = DetectIntDefault(intArr);
                if (defaultVal != 0) // 0 is already the default for new int[]
                {
                    var newIntArr = (int[])newArr;
                    for (int i = oldSize; i < newSize; i++)
                        newIntArr[i] = defaultVal;
                }
            }
            else if (elemType == typeof(short))
            {
                var shortArr = (short[])oldArr;
                short defaultVal = DetectShortDefault(shortArr);
                if (defaultVal != 0)
                {
                    var newShortArr = (short[])newArr;
                    for (int i = oldSize; i < newSize; i++)
                        newShortArr[i] = defaultVal;
                }
            }
            else if (elemType == typeof(float))
            {
                var floatArr = (float[])oldArr;
                float defaultVal = DetectFloatDefault(floatArr);
                if (defaultVal != 0f)
                {
                    var newFloatArr = (float[])newArr;
                    for (int i = oldSize; i < newSize; i++)
                        newFloatArr[i] = defaultVal;
                }
            }
            // bool arrays: default is false, which is already correct
            // Reference arrays: default is null, which is already correct
        }

        private static int DetectIntDefault(int[] arr)
        {
            // Sample last 100 entries — the majority value is the default
            int countNeg1 = 0, countZero = 0;
            int sampleStart = Math.Max(0, arr.Length - 100);
            for (int i = sampleStart; i < arr.Length; i++)
            {
                if (arr[i] == -1) countNeg1++;
                else if (arr[i] == 0) countZero++;
            }
            return countNeg1 > countZero ? -1 : 0;
        }

        private static short DetectShortDefault(short[] arr)
        {
            int countNeg1 = 0, countZero = 0;
            int sampleStart = Math.Max(0, arr.Length - 100);
            for (int i = sampleStart; i < arr.Length; i++)
            {
                if (arr[i] == -1) countNeg1++;
                else if (arr[i] == 0) countZero++;
            }
            return countNeg1 > countZero ? (short)-1 : (short)0;
        }

        private static float DetectFloatDefault(float[] arr)
        {
            int countOne = 0, countZero = 0;
            int sampleStart = Math.Max(0, arr.Length - 100);
            for (int i = sampleStart; i < arr.Length; i++)
            {
                if (Math.Abs(arr[i] - 1f) < 0.001f) countOne++;
                else if (Math.Abs(arr[i]) < 0.001f) countZero++;
            }
            return countOne > countZero ? 1f : 0f;
        }

        private static int ResizeTextureAssets(int oldSize, int newSize)
        {
            int count = 0;
            try
            {
                var texAssetsType = typeof(Terraria.GameContent.TextureAssets);

                foreach (var fieldName in new[] { "Item", "ItemFlame" })
                {
                    var field = texAssetsType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                    if (field == null) continue;

                    var arr = field.GetValue(null) as Array;
                    if (arr == null || arr.Length != oldSize) continue;

                    var elemType = field.FieldType.GetElementType();
                    var newArr = Array.CreateInstance(elemType, newSize);
                    Array.Copy(arr, newArr, arr.Length);

                    // Fill new entries with the "air" item placeholder (slot 0)
                    // to prevent null reference crashes if vanilla code accesses these slots
                    var placeholder = arr.GetValue(0);
                    if (placeholder != null)
                    {
                        for (int i = oldSize; i < newSize; i++)
                            newArr.SetValue(placeholder, i);
                    }

                    field.SetValue(null, newArr);
                    count++;
                    _log?.Debug($"[TypeExtension] Resized TextureAssets.{fieldName}: {oldSize} → {newSize} (filled with placeholder)");
                }
            }
            catch (Exception ex)
            {
                _log?.Error($"[TypeExtension] Error resizing TextureAssets: {ex.Message}");
            }
            return count;
        }

        private static int ResizeLangCaches(int oldSize, int newSize)
        {
            int count = 0;
            try
            {
                var langType = typeof(Terraria.Lang);

                foreach (var fieldName in new[] { "_itemNameCache", "_itemTooltipCache" })
                {
                    var field = langType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
                    if (field == null)
                    {
                        _log?.Debug($"[TypeExtension] Lang.{fieldName} not found");
                        continue;
                    }

                    var arr = field.GetValue(null) as Array;
                    if (arr == null || arr.Length != oldSize) continue;

                    var elemType = field.FieldType.GetElementType();
                    var newArr = Array.CreateInstance(elemType, newSize);
                    Array.Copy(arr, newArr, arr.Length);

                    // Fill new entries with the "air" item entry (slot 0)
                    // to prevent null reference crashes
                    var placeholder = arr.GetValue(0);
                    if (placeholder != null)
                    {
                        for (int i = oldSize; i < newSize; i++)
                            newArr.SetValue(placeholder, i);
                    }

                    field.SetValue(null, newArr);
                    count++;
                    _log?.Debug($"[TypeExtension] Resized Lang.{fieldName}: {oldSize} → {newSize} (filled with placeholder)");
                }
            }
            catch (Exception ex)
            {
                _log?.Error($"[TypeExtension] Error resizing Lang caches: {ex.Message}");
            }
            return count;
        }

        private static int ResizeMainArrays(int oldSize, int newSize)
        {
            int count = 0;
            try
            {
                var mainType = typeof(Terraria.Main);
                // Note: itemAnimationsRegistered is a List<int>, not an array — doesn't need resizing
                var candidates = new[] { "itemAnimations" };

                foreach (var fieldName in candidates)
                {
                    var field = mainType.GetField(fieldName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (field == null) continue;
                    if (!field.FieldType.IsArray) continue;

                    var arr = field.GetValue(null) as Array;
                    if (arr == null || arr.Length != oldSize) continue;

                    var elemType = field.FieldType.GetElementType();
                    var newArr = Array.CreateInstance(elemType, newSize);
                    Array.Copy(arr, newArr, arr.Length);
                    field.SetValue(null, newArr);
                    count++;
                    _log?.Debug($"[TypeExtension] Resized Main.{fieldName}: {oldSize} → {newSize}");
                }
            }
            catch (Exception ex)
            {
                _log?.Error($"[TypeExtension] Error resizing Main arrays: {ex.Message}");
            }
            return count;
        }

        private static int ResizeItemArrays(int oldSize, int newSize)
        {
            int count = 0;
            try
            {
                var itemType = typeof(Terraria.Item);
                // Item.staff and Item.claw are bool[ItemID.Count] indexed by type
                foreach (var fieldName in new[] { "staff", "claw" })
                {
                    var field = itemType.GetField(fieldName,
                        BindingFlags.Public | BindingFlags.Static);
                    if (field == null) continue;
                    if (!field.FieldType.IsArray) continue;

                    var arr = field.GetValue(null) as Array;
                    if (arr == null || arr.Length != oldSize) continue;

                    var elemType = field.FieldType.GetElementType();
                    var newArr = Array.CreateInstance(elemType, newSize);
                    Array.Copy(arr, newArr, arr.Length);
                    field.SetValue(null, newArr);
                    count++;
                    _log?.Debug($"[TypeExtension] Resized Item.{fieldName}: {oldSize} → {newSize}");
                }
            }
            catch (Exception ex)
            {
                _log?.Error($"[TypeExtension] Error resizing Item arrays: {ex.Message}");
            }
            return count;
        }

        /// <summary>
        /// Comprehensive scan: find ALL static array fields in the Terraria assembly
        /// whose length matches the old ItemID.Count, and resize them.
        /// This catches arrays we don't know about (ArmorSetBonuses, QuickStacking, etc.)
        /// that would cause IndexOutOfRangeException for custom item types.
        ///
        /// Safety: 6145 is a very specific number (ItemID.Count). Any array of that
        /// exact size is almost certainly indexed by item type. Making it bigger with
        /// default-filled entries is safe even for false positives.
        /// </summary>
        private static int ResizeAllAssemblyArrays(int oldSize, int newSize)
        {
            int count = 0;
            try
            {
                var asm = typeof(Terraria.Main).Assembly;

                foreach (var type in asm.GetTypes())
                {
                    try
                    {
                        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        foreach (var field in fields)
                        {
                            if (!field.FieldType.IsArray) continue;

                            try
                            {
                                var arr = field.GetValue(null) as Array;
                                if (arr == null || arr.Length != oldSize) continue;

                                // Build a unique key to track what we resize
                                string key = $"{type.FullName}.{field.Name}";

                                var elemType = field.FieldType.GetElementType();
                                var newArr = Array.CreateInstance(elemType, newSize);
                                Array.Copy(arr, newArr, Math.Min(arr.Length, newSize));

                                FillNewEntries(newArr, arr, oldSize, newSize, elemType);

                                field.SetValue(null, newArr);
                                count++;
                                _log?.Info($"[TypeExtension] Assembly scan resized {key} ({elemType.Name}[{oldSize}] → {newSize})");
                            }
                            catch (Exception ex)
                            {
                                _log?.Debug($"[TypeExtension] Skipped {type.FullName}.{field.Name}: {ex.Message}");
                            }
                        }
                    }
                    catch
                    {
                        // Some types may not be loadable (generic constraints, etc.)
                    }
                }

                _log?.Info($"[TypeExtension] Assembly-wide scan resized {count} additional arrays");
            }
            catch (Exception ex)
            {
                _log?.Error($"[TypeExtension] Assembly scan error: {ex.Message}");
            }
            return count;
        }
    }
}
