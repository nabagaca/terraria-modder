using System;
using Terraria;
using TerrariaModder.Core;
using TerrariaModder.Core.Assets;
using TerrariaModder.Core.Logging;
using TerrariaModder.Core.UI;
using StorageHub.Config;

namespace StorageHub.PaintingChest
{
    public static class PaintingChestManager
    {
        public const int TILE_TYPE = 246;
        public const int OUR_PLACE_STYLE = 37;
        public const string FULL_ITEM_ID = "storage-hub:painting-chest";

        public static bool Enabled { get; private set; }

        private static ILogger _log;
        private static StorageHubConfig _config;
        private static int _patchDelayFrames = -1; // Frame countdown for deferred patching (-1 = inactive)

        public static void Initialize(ILogger logger, ModContext context)
        {
            _log = logger;
            Enabled = true;

            context.RegisterItem("painting-chest", new ItemDefinition
            {
                DisplayName = "Mysterious Painting",
                Tooltip = new[] { "A painting that hides a secret compartment", "'Good Morning'" },
                CreateTile = TILE_TYPE,
                PlaceStyle = OUR_PLACE_STYLE,
                Width = 30,
                Height = 30,
                MaxStack = 99,
                Consumable = true,
                UseStyle = 1,
                UseTime = 10,
                UseAnimation = 15,
                AutoReuse = true,
                Rarity = 2,
                Value = 10000
            });

            context.AddShopItem(new ShopDefinition
            {
                NpcType = 1,
                ItemId = FULL_ITEM_ID,
                Price = 10000
            });

            UIRenderer.RegisterPanelDraw("painting-chest-label", Draw);

            // Set tileContainer[246] = true immediately — vanilla's DrawInventory
            // force-closes chests every frame if the tile type isn't in tileContainer.
            // Must be set before any world load, not deferred to the 5s patch timer.
            PaintingChestPatches.SetTileContainer();

            _patchDelayFrames = 300; // ~5 seconds at 60fps, runs on game thread
            _log.Info("Painting chest initialized, patches scheduled");
        }

        /// <summary>
        /// Called each frame from the game thread. Handles deferred patch application.
        /// Must be called from the parent mod's OnUpdate.
        /// </summary>
        public static void Update()
        {
            if (_patchDelayFrames > 0)
            {
                _patchDelayFrames--;
            }
            else if (_patchDelayFrames == 0)
            {
                _patchDelayFrames = -1; // Disable further ticks
                try
                {
                    PaintingChestPatches.ApplyPatches(_log);
                    TileTextureExtender.Initialize(_log);
                    TileTextureExtender.TryExtend();
                    _log?.Info("Painting chest patches applied");
                }
                catch (Exception ex)
                {
                    _log?.Error($"Painting chest patch error: {ex.Message}");
                }
            }
        }

        public static void OnWorldLoad(StorageHubConfig config)
        {
            _config = config;
            PaintingChestPatches.SetTileContainer();
            TileTextureExtender.TryExtend();
            ResizeAllPaintingChests(GetCurrentCapacity());
        }

        public static void Unload()
        {
            _patchDelayFrames = -1;
            PaintingChestPatches.Unpatch();
            UIRenderer.UnregisterPanelDraw("painting-chest-label");
            Enabled = false;
            _config = null;
        }

        public static int GetCurrentCapacity()
        {
            int level = _config?.PaintingChestLevel ?? 0;
            return PaintingChestProgression.GetCapacity(level);
        }

        private static void Draw()
        {
            if (!Enabled) return;

            // Check if a painting chest is currently open
            int myPlayer = Main.myPlayer;
            var player = Main.player[myPlayer];
            int chestIdx = player.chest;
            if (chestIdx < 0) return;

            var chest = Main.chest[chestIdx];
            if (chest == null) return;

            // Verify it's our painting chest tile
            try
            {
                var tile = Main.tile[chest.x, chest.y];
                if (tile == null || !tile.active() || tile.type != TILE_TYPE) return;
                int style = tile.frameY / 36;
                if (style != OUR_PLACE_STYLE) return;
            }
            catch { return; }

            // Count non-empty slots
            int usedSlots = 0;
            for (int i = 0; i < chest.maxItems; i++)
            {
                if (chest.item[i] != null && chest.item[i].type > 0 && chest.item[i].stack > 0)
                    usedSlots++;
            }

            // Position: below the 4 visible chest rows
            // ChestUI.DrawSlots sets inventoryScale = 0.755f, slots are 56px apart
            const float chestScale = 0.755f;
            const int visibleRows = 4;
            int labelX = 73;
            int labelY = (int)(Main.instance.invBottom + visibleRows * 56 * chestScale) + 8;

            string text = $"Capacity  {usedSlots} / {chest.maxItems}";
            UIRenderer.DrawText(text, labelX, labelY, UIColors.TextDim);
        }

        public static void ResizeAllPaintingChests(int capacity)
        {
            int resized = 0;
            for (int i = 0; i < 8000; i++)
            {
                var chest = Main.chest[i];
                if (chest == null) continue;

                try
                {
                    var tile = Main.tile[chest.x, chest.y];
                    if (tile == null || !tile.active() || tile.type != TILE_TYPE) continue;

                    int style = tile.frameY / 36;
                    if (style != OUR_PLACE_STYLE) continue;

                    if (chest.maxItems != capacity)
                    {
                        chest.Resize(capacity);
                        resized++;
                    }
                }
                catch { }
            }

            if (resized > 0)
                _log?.Info($"Resized {resized} painting chests to {capacity} slots");
        }
    }
}
