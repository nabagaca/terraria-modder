using System;
using System.Collections;
using System.Reflection;
using Terraria;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core.Assets
{
    /// <summary>
    /// Applies tile flags and object-data metadata for custom tile runtime IDs.
    /// </summary>
    internal static class TileObjectRegistrar
    {
        private sealed class TileObjectDataNotReadyException : Exception
        {
            public TileObjectDataNotReadyException(string message) : base(message) { }
        }

        private static ILogger _log;
        private static bool _applied;
        private static int _lastFailureCount = -1;

        public static void Initialize(ILogger logger)
        {
            _log = logger;
        }

        public static void ApplyDefinitions()
        {
            if (TileRegistry.Count == 0) return;
            if (_applied)
            {
                if (!NeedsReapply())
                    return;

                _applied = false;
                _lastFailureCount = -1;
                _log?.Warn("[TileObjectRegistrar] Detected missing TileObjectData entries after prior apply; reapplying");
            }

            int applied = 0;
            int failed = 0;
            int deferred = 0;
            foreach (var fullId in TileRegistry.AllIds)
            {
                int runtimeType = TileRegistry.GetRuntimeType(fullId);
                if (runtimeType < 0) continue;

                var def = TileRegistry.GetDefinition(runtimeType);
                if (def == null) continue;

                try
                {
                    ApplyTileFlags(runtimeType, def);
                    RegisterTileObjectData(runtimeType, def);
                    RegisterMapEntry(runtimeType, def);
                    applied++;
                }
                catch (Exception ex)
                {
                    Exception root = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                    if (root is TileObjectDataNotReadyException)
                    {
                        deferred++;
                        continue;
                    }

                    failed++;
                    _log?.Error($"[TileObjectRegistrar] Failed applying {fullId} (type {runtimeType}): {root.GetType().Name}: {root.Message}");
                }
            }

            if (failed == 0 && deferred == 0)
            {
                _applied = true;
                _log?.Info($"[TileObjectRegistrar] Applied metadata for {applied} custom tiles");
            }
            else
            {
                // Avoid spamming identical retry logs every frame.
                if (_lastFailureCount != failed)
                {
                    _lastFailureCount = failed;
                    if (failed > 0)
                    {
                        _log?.Warn($"[TileObjectRegistrar] Applied metadata for {applied}/{TileRegistry.Count} custom tiles; will retry failed registrations");
                    }
                    else if (deferred > 0)
                    {
                        _log?.Info($"[TileObjectRegistrar] TileObjectData not ready yet ({deferred} pending); will retry registration");
                    }
                }
            }
        }

        private static void ApplyTileFlags(int runtimeType, TileDefinition def)
        {
            SetMainBool("tileSolid", runtimeType, def.Solid);
            SetMainBool("tileSolidTop", runtimeType, def.SolidTop);
            SetMainBool("tileBrick", runtimeType, def.Brick);
            SetMainBool("tileNoAttach", runtimeType, def.NoAttach);
            SetMainBool("tileTable", runtimeType, def.Table);
            SetMainBool("tileLighted", runtimeType, def.Lighted);
            SetMainBool("tileLavaDeath", runtimeType, def.LavaDeath);
            SetMainBool("tileFrameImportant", runtimeType, def.FrameImportant);
            SetMainBool("tileNoFail", runtimeType, def.NoFail);
            SetMainBool("tileCut", runtimeType, def.Cut);
            SetMainBool("tileMergeDirt", runtimeType, def.MergeDirt);
            SetMainBool("tileContainer", runtimeType, def.IsContainer);

            // TileID.Sets flags
            SetTileSetBool("DisableSmartCursor", runtimeType, def.DisableSmartCursor);
            SetTileSetBool("BasicChest", runtimeType, def.IsContainer);
        }

        private static void RegisterTileObjectData(int runtimeType, TileDefinition def)
        {
            if (def.Width == 1 && def.Height == 1)
                return; // 1x1 can use raw tile placement

            var asm = typeof(Main).Assembly;
            var todType = asm.GetType("Terraria.ObjectData.TileObjectData");
            if (todType == null)
            {
                _log?.Warn("[TileObjectRegistrar] TileObjectData type not found");
                return;
            }

            // If already registered, skip re-adding (important for retry path).
            if (HasTileObjectData(todType, runtimeType))
                return;

            var newTileField = todType.GetField("newTile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            object newTile = newTileField?.GetValue(null);

            // Copy from closest built-in style template when available.
            object styleTemplate = ResolveStyleTemplate(todType, def);
            if (newTile == null && styleTemplate == null)
                throw new TileObjectDataNotReadyException("TileObjectData templates are not initialized yet");

            if (newTile == null && styleTemplate != null)
            {
                newTile = CreateTileObjectDataCopy(todType, styleTemplate);
                if (newTile != null)
                    newTileField?.SetValue(null, newTile);
            }

            bool registered = false;

            if (newTile != null)
            {
                try
                {
                    WithTileObjectDataWriteAccess(todType, () =>
                    {
                        ApplyTileObjectDataFields(newTile, def);

                        // TileObjectData.addTile(type)
                        var addTile = todType.GetMethod("addTile", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null)
                            ?? todType.GetMethod("AddTile", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
                        addTile?.Invoke(null, new object[] { runtimeType });
                    });

                    if (HasTileObjectData(todType, runtimeType))
                    {
                        registered = true;
                    }
                    else
                    {
                        _log?.Warn($"[TileObjectRegistrar] addTile path did not produce visible TileObjectData for tile {runtimeType} ({def.DisplayName}); retrying fallback");
                    }
                }
                catch (Exception ex)
                {
                    Exception root = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                    _log?.Warn($"[TileObjectRegistrar] addTile path failed for tile {runtimeType} ({def.DisplayName}): {root.GetType().Name}: {root.Message}");
                }
            }

            if (!registered)
            {
                if (newTile == null)
                {
                    _log?.Warn($"[TileObjectRegistrar] TileObjectData.newTile unavailable for tile {runtimeType} ({def.DisplayName}) - using direct registration fallback");
                }

                if (TryRegisterTileObjectDataDirect(todType, runtimeType, def, styleTemplate, out string fallbackReason))
                {
                    if (HasTileObjectData(todType, runtimeType))
                    {
                        registered = true;
                    }
                    else
                    {
                        _log?.Warn($"[TileObjectRegistrar] Direct registration path completed but TileObjectData still missing for tile {runtimeType} ({def.DisplayName})");
                    }
                }
                else
                {
                    _log?.Warn($"[TileObjectRegistrar] Direct registration failed for tile {runtimeType} ({def.DisplayName}): {fallbackReason}");
                }
            }

            if (!registered)
                throw new InvalidOperationException($"Could not register TileObjectData for tile {runtimeType} ({def.DisplayName})");
        }

        private static void ApplyTileObjectDataFields(object tileObjectData, TileDefinition def)
        {
            if (tileObjectData == null || def == null) return;

            SetMember(tileObjectData, "Width", def.Width);
            SetMember(tileObjectData, "Height", def.Height);
            SetMember(tileObjectData, "CoordinateWidth", def.CoordinateWidth);
            SetMember(tileObjectData, "CoordinatePadding", def.CoordinatePadding);
            SetMember(tileObjectData, "StyleHorizontal", def.StyleHorizontal);
            if (def.StyleWrapLimit > 0) SetMember(tileObjectData, "StyleWrapLimit", def.StyleWrapLimit);
            if (def.StyleMultiplier > 0) SetMember(tileObjectData, "StyleMultiplier", def.StyleMultiplier);

            var coordHeights = def.CoordinateHeights != null && def.CoordinateHeights.Length > 0
                ? def.CoordinateHeights
                : BuildDefaultCoordinateHeights(def.Height);
            SetMember(tileObjectData, "CoordinateHeights", coordHeights);

            object origin = CreatePoint16(def.OriginX, def.OriginY);
            if (origin != null) SetMember(tileObjectData, "Origin", origin);

            if (def.IsContainer)
                SetMember(tileObjectData, "LavaDeath", def.LavaDeath);
        }

        private static bool TryRegisterTileObjectDataDirect(Type todType, int runtimeType, TileDefinition def, object styleTemplate, out string reason)
        {
            reason = null;
            try
            {
                object data = CreateTileObjectDataCopy(todType, styleTemplate);
                if (data == null)
                {
                    reason = "CreateTileObjectDataCopy returned null (style template unavailable)";
                    return false;
                }

                var dataField = todType.GetField("_data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (!(dataField?.GetValue(null) is IList list))
                {
                    reason = "TileObjectData._data static list unavailable";
                    return false;
                }

                WithTileObjectDataWriteAccess(todType, () =>
                {
                    ApplyTileObjectDataFields(data, def);

                    while (list.Count <= runtimeType)
                        list.Add(null);

                    list[runtimeType] = data;
                });

                _log?.Debug($"[TileObjectRegistrar] Direct-registered TileObjectData for tile {runtimeType} ({def.DisplayName})");
                return true;
            }
            catch (Exception ex)
            {
                Exception root = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                reason = $"{root.GetType().Name}: {root.Message}";
                return false;
            }
        }

        private static bool HasTileObjectData(Type todType, int runtimeType)
        {
            try
            {
                var getTileData = todType.GetMethod("GetTileData", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(int), typeof(int), typeof(int) }, null);
                if (getTileData != null)
                {
                    object existing = null;
                    try { existing = getTileData.Invoke(null, new object[] { runtimeType, 0, 0 }); }
                    catch { }
                    if (existing != null) return true;
                }

                var dataField = todType.GetField("_data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (dataField?.GetValue(null) is IList list &&
                    runtimeType >= 0 &&
                    runtimeType < list.Count &&
                    list[runtimeType] != null)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool NeedsReapply()
        {
            try
            {
                var todType = typeof(Main).Assembly.GetType("Terraria.ObjectData.TileObjectData");
                if (todType == null)
                    return false;

                foreach (var fullId in TileRegistry.AllIds)
                {
                    int runtimeType = TileRegistry.GetRuntimeType(fullId);
                    if (runtimeType < 0) continue;

                    var def = TileRegistry.GetDefinition(runtimeType);
                    if (def == null) continue;
                    if (def.Width <= 1 && def.Height <= 1) continue;

                    if (!HasTileObjectData(todType, runtimeType))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static object GetStaticFieldValue(Type type, string fieldName)
        {
            try
            {
                var field = type?.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return field?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static object ResolveStyleTemplate(Type todType, TileDefinition def)
        {
            int desiredWidth = Math.Max(1, def?.Width ?? 1);
            int desiredHeight = Math.Max(1, def?.Height ?? 1);

            // Preferred: static style templates.
            object template = GetStaticFieldValue(todType, $"Style{def.Width}x{def.Height}")
                ?? GetStaticFieldValue(todType, "Style1x1")
                ?? GetStaticFieldValue(todType, "_baseObject");
            if (template != null)
                return template;

            // Fallback: clone object data from known vanilla tiles via GetTileData(type, style, alternate).
            // This works even when Style* static fields are null in this runtime.
            int[] candidates;
            if (def != null && def.IsContainer)
            {
                candidates = new[]
                {
                    (int)Terraria.ID.TileID.Containers,   // chest
                    (int)Terraria.ID.TileID.Dressers,     // dresser
                    (int)Terraria.ID.TileID.Furnaces      // generic 2x2 station fallback
                };
            }
            else
            {
                candidates = new[]
                {
                    (int)Terraria.ID.TileID.Containers,   // chest 2x2
                    (int)Terraria.ID.TileID.Furnaces,     // 3x2 station
                    (int)Terraria.ID.TileID.WorkBenches   // 2x1
                };
            }

            foreach (int typeId in candidates)
            {
                object data = TryGetTileData(todType, typeId, 0, 0);
                if (data != null && MatchesTileObjectDataSize(data, desiredWidth, desiredHeight))
                {
                    _log?.Debug($"[TileObjectRegistrar] Using TileObjectData.GetTileData({typeId},0,0) as template");
                    return data;
                }
            }

            // Last resort: use an existing registered object-data entry (prefer same dimensions).
            object fromDataList = ResolveTemplateFromDataList(todType, def);
            if (fromDataList != null)
                return fromDataList;

            return null;
        }

        private static object TryGetTileData(Type todType, int type, int style, int alternate)
        {
            try
            {
                var getTileData = todType?.GetMethod("GetTileData", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(int), typeof(int), typeof(int) }, null);
                return getTileData?.Invoke(null, new object[] { type, style, alternate });
            }
            catch
            {
                return null;
            }
        }

        private static object ResolveTemplateFromDataList(Type todType, TileDefinition def)
        {
            try
            {
                var dataField = todType?.GetField("_data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (!(dataField?.GetValue(null) is IList list) || list.Count == 0)
                    return null;

                int desiredWidth = Math.Max(1, def?.Width ?? 1);
                int desiredHeight = Math.Max(1, def?.Height ?? 1);

                for (int i = 0; i < list.Count; i++)
                {
                    object entry = list[i];
                    if (entry == null)
                        continue;

                    if (MatchesTileObjectDataSize(entry, desiredWidth, desiredHeight))
                    {
                        _log?.Debug($"[TileObjectRegistrar] Using TileObjectData._data[{i}] as template");
                        return entry;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool MatchesTileObjectDataSize(object tileObjectData, int width, int height)
        {
            int? entryWidth = ReadIntMember(tileObjectData, "Width");
            int? entryHeight = ReadIntMember(tileObjectData, "Height");
            if (!entryWidth.HasValue || !entryHeight.HasValue)
                return false;

            return entryWidth.Value == width && entryHeight.Value == height;
        }

        private static int? ReadIntMember(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

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
            catch
            {
                return null;
            }

            return null;
        }

        private static void WithTileObjectDataWriteAccess(Type todType, Action action)
        {
            if (action == null) return;

            FieldInfo roField = null;
            bool originalReadOnly = true;
            bool restore = false;
            try
            {
                roField = todType?.GetField("readOnlyData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (roField != null && roField.FieldType == typeof(bool))
                {
                    originalReadOnly = (bool)roField.GetValue(null);
                    if (originalReadOnly)
                    {
                        roField.SetValue(null, false);
                        restore = true;
                    }
                }

                action();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("TileObjectData mutation failed", ex);
            }
            finally
            {
                if (restore && roField != null)
                {
                    try { roField.SetValue(null, originalReadOnly); }
                    catch { }
                }
            }
        }

        private static object CreateTileObjectDataCopy(Type todType, object template)
        {
            if (todType == null || template == null) return null;

            try
            {
                var ctor = todType.GetConstructor(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { todType },
                    null);
                return ctor?.Invoke(new[] { template });
            }
            catch (Exception ex)
            {
                _log?.Debug($"[TileObjectRegistrar] Failed to create TileObjectData copy: {ex.Message}");
                return null;
            }
        }

        private static void RegisterMapEntry(int runtimeType, TileDefinition def)
        {
            // Best effort map lookup fallback: map custom tile types to existing lookup index 0.
            TrySetArrayEntry("Terraria.Map.MapHelper", "TileToLookup", runtimeType, 0);
            TrySetArrayEntry("Terraria.Map.MapHelper", "tileLookup", runtimeType, 0);

            // Best effort legend cache label replacement for custom tiles.
            try
            {
                var langType = typeof(Lang);
                foreach (var fieldName in new[] { "_tileNameCache", "_mapLegendCache" })
                {
                    var field = langType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
                    var arr = field?.GetValue(null) as Array;
                    if (arr == null || runtimeType < 0 || runtimeType >= arr.Length) continue;

                    object text = CreateLocalizedText($"TileName.Custom_{runtimeType}", def.DisplayName ?? $"Custom Tile {runtimeType}");
                    if (text != null)
                        arr.SetValue(text, runtimeType);
                }
            }
            catch
            {
                // Non-fatal.
            }
        }

        private static void SetMainBool(string fieldName, int index, bool value)
        {
            try
            {
                var field = typeof(Main).GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var arr = field?.GetValue(null) as bool[];
                if (arr != null && index >= 0 && index < arr.Length)
                    arr[index] = value;
            }
            catch { }
        }

        private static void SetTileSetBool(string fieldName, int index, bool value)
        {
            try
            {
                var setsType = typeof(Terraria.ID.TileID).GetNestedType("Sets", BindingFlags.Public);
                var field = setsType?.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                var arr = field?.GetValue(null) as bool[];
                if (arr != null && index >= 0 && index < arr.Length)
                    arr[index] = value;
            }
            catch { }
        }

        private static void TrySetArrayEntry(string typeName, string fieldName, int index, int value)
        {
            try
            {
                var type = typeof(Main).Assembly.GetType(typeName);
                var field = type?.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var arr = field?.GetValue(null) as Array;
                if (arr == null || index < 0 || index >= arr.Length) return;

                Type elemType = arr.GetType().GetElementType();
                object converted = Convert.ChangeType(value, elemType);
                arr.SetValue(converted, index);
            }
            catch { }
        }

        private static void SetMember(object instance, string memberName, object value)
        {
            if (instance == null) return;

            var type = instance.GetType();
            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(instance, value);
                return;
            }

            var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
                prop.SetValue(instance, value, null);
        }

        private static int[] BuildDefaultCoordinateHeights(int height)
        {
            var arr = new int[Math.Max(1, height)];
            for (int i = 0; i < arr.Length; i++) arr[i] = 16;
            return arr;
        }

        private static object CreatePoint16(int x, int y)
        {
            try
            {
                var point16Type = typeof(Main).Assembly.GetType("Terraria.DataStructures.Point16");
                if (point16Type == null) return null;

                var ctor = point16Type.GetConstructor(new[] { typeof(short), typeof(short) });
                if (ctor != null) return ctor.Invoke(new object[] { (short)x, (short)y });

                ctor = point16Type.GetConstructor(new[] { typeof(int), typeof(int) });
                if (ctor != null) return ctor.Invoke(new object[] { x, y });

                return Activator.CreateInstance(point16Type);
            }
            catch
            {
                return null;
            }
        }

        private static object CreateLocalizedText(string key, string value)
        {
            try
            {
                var type = typeof(Terraria.Localization.LocalizedText);
                var ctor = type.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string), typeof(string) }, null);
                return ctor?.Invoke(new object[] { key, value ?? string.Empty });
            }
            catch
            {
                return null;
            }
        }
    }
}
