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
        public const string TileComponentId = "storage-hub:storage-component";
        public const string TileConnectorId = "storage-hub:storage-connector";
        public const string TileHeartId = "storage-hub:storage-heart";
        public const string TileUnitId = "storage-hub:storage-unit";
        public const string TileAccessId = "storage-hub:storage-access";
        public const string TileCraftingAccessId = "storage-hub:storage-crafting-access";

        public const string ItemComponentId = "storage-hub:storage-component-item";
        public const string ItemConnectorId = "storage-hub:storage-connector-item";
        public const string ItemHeartId = "storage-hub:storage-heart-item";
        public const string ItemUnitId = "storage-hub:storage-unit-item";
        public const string ItemAccessId = "storage-hub:storage-access-item";
        public const string ItemCraftingAccessId = "storage-hub:storage-crafting-access-item";

        public static void Register(
            ModContext context,
            ILogger log,
            Func<int, int, bool> onStorageHeartRightClick,
            Func<int, int, bool> onStorageAccessRightClick,
            Func<int, int, bool> onStorageCraftingAccessRightClick)
        {
            if (context == null) return;

            context.RegisterTile("storage-component", new TileDefinition
            {
                DisplayName = "Storage Component",
                Texture = "assets/tiles/storage-component.png",
                Width = 2,
                Height = 2,
                OriginX = 1,
                OriginY = 1,
                CoordinateHeights = new[] { 16, 16 },
                CoordinateWidth = 16,
                CoordinatePadding = 2,
                FrameImportant = true,
                NoAttach = false,
                Solid = false,
                SolidTop = true,
                Table = true,
                LavaDeath = true,
                HitSoundStyle = 1,
                MapColorR = 153,
                MapColorG = 107,
                MapColorB = 61,
                DropItemId = ItemComponentId
            });

            context.RegisterTile("storage-connector", new TileDefinition
            {
                DisplayName = "Storage Connector",
                Texture = "assets/tiles/storage-connector.png",
                Width = 1,
                Height = 1,
                OriginX = 0,
                OriginY = 0,
                CoordinateHeights = new[] { 16 },
                CoordinateWidth = 16,
                CoordinatePadding = 2,
                FrameImportant = true,
                NoAttach = true,
                Solid = false,
                LavaDeath = true,
                HitSoundStyle = 1,
                MapColorR = 153,
                MapColorG = 107,
                MapColorB = 61,
                DropItemId = ItemConnectorId
            });

            context.RegisterTile("storage-heart", new TileDefinition
            {
                DisplayName = "Storage Heart",
                Texture = "assets/tiles/storage-heart.png",
                Width = 2,
                Height = 2,
                OriginX = 1,
                OriginY = 1,
                CoordinateHeights = new[] { 16, 16 },
                CoordinateWidth = 16,
                CoordinatePadding = 2,
                FrameImportant = true,
                NoAttach = false,
                Solid = false,
                SolidTop = true,
                Table = true,
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
                        return onStorageHeartRightClick?.Invoke(x, y) ?? false;
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
                OriginX = 1,
                OriginY = 1,
                CoordinateHeights = new[] { 16, 16 },
                CoordinateWidth = 16,
                CoordinatePadding = 2,
                FrameImportant = true,
                NoAttach = false,
                Solid = false,
                SolidTop = true,
                Table = true,
                LavaDeath = true,
                HitSoundStyle = 1,
                MapColorR = 120,
                MapColorG = 120,
                MapColorB = 140,
                IsContainer = true,
                RegisterAsBasicChest = false,
                ContainerInteractable = false,
                ContainerCapacity = 40,
                ContainerName = "Storage Unit",
                DropItemId = ItemUnitId
            });

            context.RegisterTile("storage-access", new TileDefinition
            {
                DisplayName = "Storage Access",
                Texture = "assets/tiles/storage-access.png",
                Width = 2,
                Height = 2,
                OriginX = 1,
                OriginY = 1,
                CoordinateHeights = new[] { 16, 16 },
                CoordinateWidth = 16,
                CoordinatePadding = 2,
                FrameImportant = true,
                NoAttach = false,
                Solid = false,
                SolidTop = true,
                Table = true,
                Lighted = true,
                LavaDeath = true,
                HitSoundStyle = 1,
                MapColorR = 88,
                MapColorG = 190,
                MapColorB = 216,
                DropItemId = ItemAccessId,
                OnRightClick = (x, y, player) =>
                {
                    try
                    {
                        return onStorageAccessRightClick?.Invoke(x, y) ?? false;
                    }
                    catch (Exception ex)
                    {
                        log?.Error($"Storage access right-click error: {ex.Message}");
                        return false;
                    }
                }
            });

            context.RegisterTile("storage-crafting-access", new TileDefinition
            {
                DisplayName = "Storage Crafting Interface",
                Texture = "assets/tiles/storage-crafting-access.png",
                Width = 2,
                Height = 2,
                OriginX = 1,
                OriginY = 1,
                CoordinateHeights = new[] { 16, 16 },
                CoordinateWidth = 16,
                CoordinatePadding = 2,
                FrameImportant = true,
                NoAttach = false,
                Solid = false,
                SolidTop = true,
                Table = true,
                Lighted = true,
                LavaDeath = true,
                HitSoundStyle = 1,
                MapColorR = 127,
                MapColorG = 194,
                MapColorB = 97,
                DropItemId = ItemCraftingAccessId,
                OnRightClick = (x, y, player) =>
                {
                    try
                    {
                        return onStorageCraftingAccessRightClick?.Invoke(x, y) ?? false;
                    }
                    catch (Exception ex)
                    {
                        log?.Error($"Storage crafting access right-click error: {ex.Message}");
                        return false;
                    }
                }
            });

            context.RegisterItem("storage-component-item", new ItemDefinition
            {
                DisplayName = "Storage Component",
                Tooltip = new[] { "Basic structural piece for Storage Hub networks" },
                Texture = "assets/items/storage-component.png",
                CreateTileId = TileComponentId,
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
                Value = 1000
            });

            context.RegisterItem("storage-connector-item", new ItemDefinition
            {
                DisplayName = "Storage Connector",
                Tooltip = new[] { "Connects Storage Hub components across distance" },
                Texture = "assets/items/storage-connector.png",
                CreateTileId = TileConnectorId,
                PlaceStyle = 0,
                Width = 20,
                Height = 20,
                MaxStack = 999,
                Consumable = true,
                UseStyle = 1,
                UseTurn = true,
                UseTime = 10,
                UseAnimation = 15,
                AutoReuse = true,
                Rarity = 1,
                Value = 500
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
                Tooltip = new[] { "Stores items for a connected Storage Heart", "Cannot be opened directly" },
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

            context.RegisterItem("storage-access-item", new ItemDefinition
            {
                DisplayName = "Storage Access",
                Tooltip = new[] { "Extra access point for your Storage Hub network" },
                Texture = "assets/items/storage-access.png",
                CreateTileId = TileAccessId,
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
                Value = 15000
            });

            context.RegisterItem("storage-crafting-access-item", new ItemDefinition
            {
                DisplayName = "Storage Crafting Interface",
                Tooltip = new[] { "Access storage and crafting from one terminal" },
                Texture = "assets/items/storage-crafting-access.png",
                CreateTileId = TileCraftingAccessId,
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
                Rarity = 3,
                Value = 30000
            });
        }

        public static int ResolveStorageComponentTileType()
        {
            return AssetSystem.GetTileRuntimeType(TileComponentId);
        }

        public static int ResolveStorageConnectorTileType()
        {
            return AssetSystem.GetTileRuntimeType(TileConnectorId);
        }

        public static int ResolveStorageHeartTileType()
        {
            return AssetSystem.GetTileRuntimeType(TileHeartId);
        }

        public static int ResolveStorageAccessTileType()
        {
            return AssetSystem.GetTileRuntimeType(TileAccessId);
        }

        public static int ResolveStorageCraftingAccessTileType()
        {
            return AssetSystem.GetTileRuntimeType(TileCraftingAccessId);
        }

        public static int ResolveStorageUnitTileType()
        {
            return AssetSystem.GetTileRuntimeType(TileUnitId);
        }
    }
}
