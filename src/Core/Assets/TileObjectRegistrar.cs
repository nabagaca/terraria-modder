using System;
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
        private static ILogger _log;
        private static bool _applied;

        public static void Initialize(ILogger logger)
        {
            _log = logger;
        }

        public static void ApplyDefinitions()
        {
            if (_applied) return;
            if (TileRegistry.Count == 0) return;

            int applied = 0;
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
                    _log?.Error($"[TileObjectRegistrar] Failed applying {fullId} (type {runtimeType}): {ex.Message}");
                }
            }

            _applied = true;
            _log?.Info($"[TileObjectRegistrar] Applied metadata for {applied} custom tiles");
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

            var newTileField = todType.GetField("newTile", BindingFlags.Public | BindingFlags.Static);
            var newTile = newTileField?.GetValue(null);
            if (newTile == null)
            {
                _log?.Warn("[TileObjectRegistrar] TileObjectData.newTile not available");
                return;
            }

            // Copy from closest built-in style template when available.
            object styleTemplate = todType.GetField($"Style{def.Width}x{def.Height}", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? todType.GetField("Style1x1", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (styleTemplate != null)
            {
                var copyFrom = newTile.GetType().GetMethod("CopyFrom", BindingFlags.Public | BindingFlags.Instance);
                copyFrom?.Invoke(newTile, new[] { styleTemplate });
            }

            SetMember(newTile, "Width", def.Width);
            SetMember(newTile, "Height", def.Height);
            SetMember(newTile, "CoordinateWidth", def.CoordinateWidth);
            SetMember(newTile, "CoordinatePadding", def.CoordinatePadding);
            SetMember(newTile, "StyleHorizontal", def.StyleHorizontal);
            if (def.StyleWrapLimit > 0) SetMember(newTile, "StyleWrapLimit", def.StyleWrapLimit);
            if (def.StyleMultiplier > 0) SetMember(newTile, "StyleMultiplier", def.StyleMultiplier);

            var coordHeights = def.CoordinateHeights != null && def.CoordinateHeights.Length > 0
                ? def.CoordinateHeights
                : BuildDefaultCoordinateHeights(def.Height);
            SetMember(newTile, "CoordinateHeights", coordHeights);

            object origin = CreatePoint16(def.OriginX, def.OriginY);
            if (origin != null) SetMember(newTile, "Origin", origin);

            if (def.IsContainer)
            {
                SetMember(newTile, "LavaDeath", def.LavaDeath);
            }

            // TileObjectData.addTile(type)
            var addTile = todType.GetMethod("addTile", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null)
                ?? todType.GetMethod("AddTile", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
            addTile?.Invoke(null, new object[] { runtimeType });
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
