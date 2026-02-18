using System;
using System.Reflection;
using Terraria;
using Terraria.GameContent;
using TerrariaModder.Core.Assets;
using TerrariaModder.Core.Logging;

namespace StorageHub.PaintingChest
{
    internal static class TileTextureExtender
    {
        private static ILogger _log;
        private static bool _tileExtended;
        private static bool _itemExtended;
        private static bool _failed;

        private static Type _texture2dType;
        private static object _graphicsDevice;
        private static MethodInfo _getDataMethod;
        private static MethodInfo _setDataMethod;
        private static ConstructorInfo _texture2dCtor;
        private static bool _reflectionReady;

        private const int SOURCE_STYLE = 3; // "Good Morning" painting
        private const int FRAME_WIDTH = 54;
        private const int FRAME_HEIGHT = 36;

        public static void Initialize(ILogger logger)
        {
            _log = logger;
        }

        public static void TryExtend()
        {
            if ((_tileExtended && _itemExtended) || _failed) return;

            try
            {
                if (!InitReflection()) return;

                if (!_tileExtended)
                    _tileExtended = ExtendTileSpritesheet();
                if (!_itemExtended)
                    _itemExtended = GenerateItemTexture();
            }
            catch (Exception ex)
            {
                _log?.Error($"TileTextureExtender failed: {ex.Message}");
                _failed = true;
            }
        }

        private static bool InitReflection()
        {
            if (_reflectionReady) return true;

            _texture2dType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                _texture2dType = asm.GetType("Microsoft.Xna.Framework.Graphics.Texture2D");
                if (_texture2dType != null) break;
            }
            if (_texture2dType == null) return false;

            var instField = typeof(Main).GetField("instance", BindingFlags.Public | BindingFlags.Static);
            var inst = instField?.GetValue(null);
            if (inst != null)
            {
                var gdProp = inst.GetType().GetProperty("GraphicsDevice");
                _graphicsDevice = gdProp?.GetValue(inst);
            }
            if (_graphicsDevice == null) return false;

            var gdType = _graphicsDevice.GetType();
            _texture2dCtor = _texture2dType.GetConstructor(new[] { gdType, typeof(int), typeof(int) });
            if (_texture2dCtor == null) return false;

            foreach (var m in _texture2dType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name == "GetData" && m.IsGenericMethod)
                {
                    var parms = m.GetParameters();
                    if (parms.Length == 1 && parms[0].ParameterType.IsArray)
                    {
                        _getDataMethod = m.MakeGenericMethod(typeof(uint));
                        break;
                    }
                }
            }
            foreach (var m in _texture2dType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name == "SetData" && m.IsGenericMethod)
                {
                    var parms = m.GetParameters();
                    if (parms.Length == 1 && parms[0].ParameterType.IsArray)
                    {
                        _setDataMethod = m.MakeGenericMethod(typeof(uint));
                        break;
                    }
                }
            }

            if (_getDataMethod == null || _setDataMethod == null) return false;

            _reflectionReady = true;
            return true;
        }

        private static object GetTexture2DFromAsset(object asset)
        {
            if (asset == null) return null;
            return asset.GetType().GetProperty("Value")?.GetValue(asset);
        }

        private static int GetTextureWidth(object texture) => (int)texture.GetType().GetProperty("Width").GetValue(texture);
        private static int GetTextureHeight(object texture) => (int)texture.GetType().GetProperty("Height").GetValue(texture);

        private static uint[] GetPixelData(object texture)
        {
            int w = GetTextureWidth(texture);
            int h = GetTextureHeight(texture);
            var data = new uint[w * h];
            _getDataMethod.Invoke(texture, new object[] { data });
            return data;
        }

        private static object ForceLoadAsset(string assetName)
        {
            try
            {
                object repo = typeof(Main).GetField("Assets", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    ?? typeof(Main).GetProperty("Assets", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (repo == null) return null;

                MethodInfo requestMethod = null;
                foreach (var m in repo.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name == "Request" && m.IsGenericMethod) { requestMethod = m; break; }
                }
                if (requestMethod == null) return null;

                var requestGeneric = requestMethod.MakeGenericMethod(_texture2dType);
                var modeType = requestGeneric.GetParameters()[1].ParameterType;
                var immediateLoad = Enum.ToObject(modeType, 1);

                var asset = requestGeneric.Invoke(repo, new object[] { assetName, immediateLoad });
                return asset == null ? null : GetTexture2DFromAsset(asset);
            }
            catch { return null; }
        }

        private static bool ExtendTileSpritesheet()
        {
            var tileArray = typeof(TextureAssets).GetField("Tile", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) as Array;
            if (tileArray == null || PaintingChestManager.TILE_TYPE >= tileArray.Length) return false;

            var existingAsset = tileArray.GetValue(PaintingChestManager.TILE_TYPE);
            var existingTexture = ForceLoadAsset("Images/Tiles_" + PaintingChestManager.TILE_TYPE);
            if (existingTexture == null) return false;

            int origWidth = GetTextureWidth(existingTexture);
            int origHeight = GetTextureHeight(existingTexture);
            uint[] origPixels = GetPixelData(existingTexture);

            int gmFrameY = SOURCE_STYLE * FRAME_HEIGHT;
            uint[] framePixels = new uint[FRAME_WIDTH * FRAME_HEIGHT];
            for (int row = 0; row < FRAME_HEIGHT; row++)
                for (int col = 0; col < FRAME_WIDTH; col++)
                {
                    int srcIdx = (gmFrameY + row) * origWidth + col;
                    if (srcIdx < origPixels.Length)
                        framePixels[row * FRAME_WIDTH + col] = origPixels[srcIdx];
                }

            RecolorFrame(framePixels, FRAME_WIDTH, FRAME_HEIGHT);

            int newHeight = origHeight + FRAME_HEIGHT;
            var newTexture = _texture2dCtor.Invoke(new object[] { _graphicsDevice, origWidth, newHeight });
            uint[] newPixels = new uint[origWidth * newHeight];
            Array.Copy(origPixels, 0, newPixels, 0, origPixels.Length);

            for (int row = 0; row < FRAME_HEIGHT; row++)
                for (int col = 0; col < FRAME_WIDTH; col++)
                    newPixels[(origHeight + row) * origWidth + col] = framePixels[row * FRAME_WIDTH + col];

            _setDataMethod.Invoke(newTexture, new object[] { newPixels });

            var originalName = GetAssetName(existingAsset) ?? "Images/Tiles_" + PaintingChestManager.TILE_TYPE;
            var newAsset = CreateAssetWrapper(existingAsset.GetType(), newTexture, originalName);
            if (newAsset != null)
            {
                tileArray.SetValue(newAsset, PaintingChestManager.TILE_TYPE);
                _log?.Info($"Extended tile 246 texture to {origWidth}x{newHeight}");
                return true;
            }
            return false;
        }

        private static void RecolorFrame(uint[] pixels, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    uint pixel = pixels[y * width + x];
                    uint alpha = (pixel >> 24) & 0xFF;
                    if (alpha == 0) continue;

                    // XNA SurfaceFormat.Color stores as ABGR in uint32:
                    // bits 0-7 = R, bits 8-15 = G, bits 16-23 = B, bits 24-31 = A
                    uint r = pixel & 0xFF;
                    uint g = (pixel >> 8) & 0xFF;
                    uint b = (pixel >> 16) & 0xFF;

                    bool isInterior = x >= 6 && x < width - 6 && y >= 5 && y < height - 5;
                    if (isInterior)
                    {
                        // Blue tint — reduce red, keep some green, boost blue
                        uint newR = r * 80 / 255;
                        uint newG = g * 150 / 255;
                        uint newB = (uint)Math.Min(255, b * 200 / 255 + g * 80 / 255);
                        r = newR; g = newG; b = newB;
                    }
                    else
                    {
                        // Frame: dark cool blue-grey
                        r = r * 80 / 255;
                        g = g * 90 / 255;
                        b = b * 130 / 255;
                    }

                    pixels[y * width + x] = (alpha << 24) | (b << 16) | (g << 8) | r;
                }
            }
        }

        private static bool GenerateItemTexture()
        {
            int ourType = ItemRegistry.GetRuntimeType(PaintingChestManager.FULL_ITEM_ID);
            if (ourType < 0) return false;

            var itemArray = typeof(TextureAssets).GetField("Item", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) as Array;
            if (itemArray == null) return false;

            const int SOURCE_ITEM_ID = 1482; // "Good Morning"
            if (SOURCE_ITEM_ID >= itemArray.Length || ourType >= itemArray.Length) return false;

            var gmTexture = ForceLoadAsset("Images/Item_" + SOURCE_ITEM_ID);
            if (gmTexture == null) return false;

            int itemW = GetTextureWidth(gmTexture);
            int itemH = GetTextureHeight(gmTexture);
            uint[] pixels = GetPixelData(gmTexture);
            uint[] newPixels = new uint[pixels.Length];
            Array.Copy(pixels, newPixels, pixels.Length);

            for (int i = 0; i < newPixels.Length; i++)
            {
                uint pixel = newPixels[i];
                uint alpha = (pixel >> 24) & 0xFF;
                if (alpha == 0) continue;
                // XNA ABGR: bits 0-7 = R, 8-15 = G, 16-23 = B
                uint origR = pixel & 0xFF;
                uint origG = (pixel >> 8) & 0xFF;
                uint origB = (pixel >> 16) & 0xFF;
                // Blue tint matching tile
                uint r = origR * 80 / 255;
                uint g = origG * 150 / 255;
                uint b = (uint)Math.Min(255, origB * 200 / 255 + origG * 80 / 255);
                newPixels[i] = (alpha << 24) | (b << 16) | (g << 8) | r;
            }

            var newTexture = _texture2dCtor.Invoke(new object[] { _graphicsDevice, itemW, itemH });
            _setDataMethod.Invoke(newTexture, new object[] { newPixels });

            var existingItemAsset = itemArray.GetValue(SOURCE_ITEM_ID);
            var originalItemName = GetAssetName(existingItemAsset) ?? "Images/Item_" + SOURCE_ITEM_ID;
            var newAsset = CreateAssetWrapper(existingItemAsset.GetType(), newTexture, originalItemName);
            if (newAsset != null)
            {
                itemArray.SetValue(newAsset, ourType);
                _log?.Info($"Generated painting chest item texture (type {ourType})");
                return true;
            }
            return false;
        }

        private static string GetAssetName(object asset)
        {
            return asset?.GetType().GetProperty("Name")?.GetValue(asset) as string;
        }

        private static object CreateAssetWrapper(Type assetType, object texture, string assetName)
        {
            try
            {
                var instance = Activator.CreateInstance(assetType, BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new object[] { assetName }, null)
                    ?? Activator.CreateInstance(assetType, true);
                if (instance == null) return null;

                var valueField = assetType.GetField("<Value>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? assetType.GetField("_value", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? assetType.GetField("ownValue", BindingFlags.NonPublic | BindingFlags.Instance);
                valueField?.SetValue(instance, texture);

                var stateField = assetType.GetField("<State>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? assetType.GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? assetType.GetField("state", BindingFlags.NonPublic | BindingFlags.Instance);

                if (stateField != null)
                {
                    if (stateField.FieldType.IsEnum) stateField.SetValue(instance, Enum.ToObject(stateField.FieldType, 2));
                    else if (stateField.FieldType == typeof(int)) stateField.SetValue(instance, 2);
                }
                else
                {
                    foreach (var field in assetType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (field.FieldType.IsEnum) { field.SetValue(instance, Enum.ToObject(field.FieldType, 2)); break; }
                    }
                }

                return instance;
            }
            catch { return null; }
        }
    }
}
