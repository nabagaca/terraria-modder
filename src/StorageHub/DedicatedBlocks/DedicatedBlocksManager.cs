using System;
using TerrariaModder.Core;
using TerrariaModder.Core.Assets;
using TerrariaModder.Core.Logging;

namespace StorageHub.DedicatedBlocks
{
    /// <summary>
    /// Registers Storage Hub dedicated blocks using Core custom tile IDs.
    /// </summary>
    internal static class DedicatedBlocksManager
    {
        public const string TileHeartId = "storage-hub:storage-heart";
        public const string TileUnitId = "storage-hub:storage-unit";
        public const string ItemHeartId = "storage-hub:storage-heart-item";
        public const string ItemUnitId = "storage-hub:storage-unit-item";

        public static void Register(ModContext context, ILogger log, Func<bool> onStorageHeartRightClick)
        {
            if (context == null) return;

            context.RegisterTile("storage-heart", new TileDefinition
            {
                DisplayName = "Storage Heart",
                Texture = "assets/tiles/storage-heart.png",
                Width = 2,
                Height = 2,
                OriginX = 0,
                OriginY = 1,
                CoordinateHeights = new[] { 16, 16 },
                CoordinateWidth = 16,
                CoordinatePadding = 2,
                FrameImportant = true,
                NoAttach = true,
                Solid = false,
                Lighted = true,
                LavaDeath = true,
                HitSoundStyle = 1,
                MapColorR = 88,
                MapColorG = 190,
                MapColorB = 216,
                DropItemId = ItemHeartId,
                OnRightClick = (x, y, player) =>
                {
                    try
                    {
                        return onStorageHeartRightClick?.Invoke() ?? false;
                    }
                    catch (Exception ex)
                    {
                        log?.Error($"Storage heart right-click error: {ex.Message}");
                        return false;
                    }
                }
            });

            context.RegisterTile("storage-unit", new TileDefinition
            {
                DisplayName = "Storage Unit",
                Texture = "assets/tiles/storage-unit.png",
                Width = 2,
                Height = 2,
                OriginX = 0,
                OriginY = 1,
                CoordinateHeights = new[] { 16, 16 },
                CoordinateWidth = 16,
                CoordinatePadding = 2,
                FrameImportant = true,
                NoAttach = true,
                Solid = false,
                LavaDeath = true,
                HitSoundStyle = 1,
                MapColorR = 120,
                MapColorG = 120,
                MapColorB = 140,
                IsContainer = true,
                ContainerCapacity = 40,
                ContainerName = "Storage Unit",
                DropItemId = ItemUnitId
            });

            context.RegisterItem("storage-heart-item", new ItemDefinition
            {
                DisplayName = "Storage Heart",
                Tooltip = new[] { "Core of your storage network", "Right click the placed block to open Storage Hub" },
                Texture = "assets/items/storage-heart.png",
                CreateTileId = TileHeartId,
                PlaceStyle = 0,
                Width = 24,
                Height = 24,
                MaxStack = 99,
                Consumable = true,
                UseStyle = 1,
                UseTurn = true,
                UseTime = 10,
                UseAnimation = 15,
                AutoReuse = true,
                Rarity = 2,
                Value = 50000
            });

            context.RegisterItem("storage-unit-item", new ItemDefinition
            {
                DisplayName = "Storage Unit",
                Tooltip = new[] { "Dedicated storage block for Storage Hub networks" },
                Texture = "assets/items/storage-unit.png",
                CreateTileId = TileUnitId,
                PlaceStyle = 0,
                Width = 24,
                Height = 24,
                MaxStack = 999,
                Consumable = true,
                UseStyle = 1,
                UseTurn = true,
                UseTime = 10,
                UseAnimation = 15,
                AutoReuse = true,
                Rarity = 1,
                Value = 2500
            });
        }

        public static int ResolveStorageUnitTileType()
        {
            return AssetSystem.GetTileRuntimeType(TileUnitId);
        }
    }
}
