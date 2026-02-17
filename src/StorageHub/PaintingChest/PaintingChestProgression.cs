using System;
using System.Collections.Generic;
using System.Reflection;

namespace StorageHub.PaintingChest
{
    /// <summary>
    /// Painting chest capacity progression tiers and chest item ID scanning.
    /// </summary>
    public static class PaintingChestProgression
    {
        public class CapacityTier
        {
            public int TargetLevel { get; set; }
            public int RequiredCount { get; set; }
            public int Capacity { get; set; }
            public int RequiredGameTier { get; set; }
        }

        public static readonly CapacityTier[] Tiers =
        {
            new CapacityTier { TargetLevel = 1, RequiredCount = 1, Capacity = 80, RequiredGameTier = 0 },
            new CapacityTier { TargetLevel = 2, RequiredCount = 5, Capacity = 200, RequiredGameTier = 0 },
            new CapacityTier { TargetLevel = 3, RequiredCount = 20, Capacity = 1000, RequiredGameTier = 2 },
            new CapacityTier { TargetLevel = 4, RequiredCount = 50, Capacity = 5000, RequiredGameTier = 3 },
        };

        public static int GetCapacity(int level)
        {
            switch (level)
            {
                case 0: return 40;
                case 1: return 80;
                case 2: return 200;
                case 3: return 1000;
                case 4: return 5000;
                default: return 40;
            }
        }

        public static CapacityTier GetNextTier(int currentLevel)
        {
            if (currentLevel < 0 || currentLevel >= Tiers.Length) return null;
            return Tiers[currentLevel];
        }

        // Cached chest item IDs (any item whose createTile is in BasicChest)
        private static int[] _chestItemIds;
        private static bool _chestItemIdsComputed;

        /// <summary>
        /// Get all item IDs that represent "chests" (items whose createTile is in TileID.Sets.BasicChest).
        /// Computed once and cached.
        /// </summary>
        public static int[] GetChestItemIds()
        {
            if (_chestItemIdsComputed) return _chestItemIds;
            _chestItemIdsComputed = true;

            try
            {
                var terrariaAsm = Assembly.Load("Terraria");

                // Get TileID.Sets.BasicChest (bool[])
                var setsType = terrariaAsm.GetType("Terraria.ID.TileID+Sets");
                var basicChestField = setsType?.GetField("BasicChest", BindingFlags.Public | BindingFlags.Static);
                var basicChest = basicChestField?.GetValue(null) as bool[];
                if (basicChest == null)
                {
                    _chestItemIds = Array.Empty<int>();
                    return _chestItemIds;
                }

                // Get ContentSamples.ItemsByType (Dictionary<int, Item>)
                var contentSamplesType = terrariaAsm.GetType("Terraria.ID.ContentSamples");
                var itemsByTypeField = contentSamplesType?.GetField("ItemsByType", BindingFlags.Public | BindingFlags.Static);
                var itemsByType = itemsByTypeField?.GetValue(null);
                if (itemsByType == null)
                {
                    _chestItemIds = Array.Empty<int>();
                    return _chestItemIds;
                }

                // Item.createTile field
                var itemType = terrariaAsm.GetType("Terraria.Item");
                var createTileField = itemType?.GetField("createTile", BindingFlags.Public | BindingFlags.Instance);
                var typeField = itemType?.GetField("type", BindingFlags.Public | BindingFlags.Instance);
                if (createTileField == null || typeField == null)
                {
                    _chestItemIds = Array.Empty<int>();
                    return _chestItemIds;
                }

                // Iterate dictionary
                var result = new List<int>();
                var enumerator = itemsByType.GetType().GetMethod("GetEnumerator")?.Invoke(itemsByType, null);
                if (enumerator == null)
                {
                    _chestItemIds = Array.Empty<int>();
                    return _chestItemIds;
                }

                var moveNext = enumerator.GetType().GetMethod("MoveNext");
                var currentProp = enumerator.GetType().GetProperty("Current");

                while ((bool)moveNext.Invoke(enumerator, null))
                {
                    var kvp = currentProp.GetValue(enumerator);
                    var valueProp = kvp.GetType().GetProperty("Value");
                    var item = valueProp.GetValue(kvp);
                    if (item == null) continue;

                    int createTile = (int)createTileField.GetValue(item);
                    if (createTile >= 0 && createTile < basicChest.Length && basicChest[createTile])
                    {
                        int id = (int)typeField.GetValue(item);
                        if (id > 0) result.Add(id);
                    }
                }

                _chestItemIds = result.ToArray();
            }
            catch
            {
                _chestItemIds = Array.Empty<int>();
            }

            return _chestItemIds;
        }

        /// <summary>
        /// Count total chest items available from an item counts dictionary.
        /// </summary>
        public static int CountChestItems(Dictionary<int, int> itemCounts)
        {
            var chestIds = GetChestItemIds();
            long total = 0;
            foreach (int id in chestIds)
            {
                if (itemCounts.TryGetValue(id, out int count))
                    total += count;
            }
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }
    }
}
