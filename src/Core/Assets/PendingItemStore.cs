using System.Collections.Generic;

namespace TerrariaModder.Core.Assets
{
    /// <summary>
    /// Holds custom items that couldn't be placed on load (all storage full).
    /// Items persist here until the player withdraws or deletes them via the UI.
    /// On save, pending items are written back to moddata so they survive save/quit.
    /// </summary>
    public static class PendingItemStore
    {
        public class PendingItem
        {
            public string ItemId { get; set; }     // "modid:itemname"
            public int RuntimeType { get; set; }
            public int Stack { get; set; } = 1;
            public int Prefix { get; set; }
            public bool Favorited { get; set; }
        }

        private static readonly List<PendingItem> _playerItems = new List<PendingItem>();
        private static readonly List<PendingItem> _worldItems = new List<PendingItem>();

        /// <summary>Current pending player items (inventory overflow).</summary>
        public static IReadOnlyList<PendingItem> PlayerItems => _playerItems;

        /// <summary>Current pending world items (chest overflow).</summary>
        public static IReadOnlyList<PendingItem> WorldItems => _worldItems;

        /// <summary>Total pending items across player + world.</summary>
        public static int TotalCount => _playerItems.Count + _worldItems.Count;

        public static void AddPlayerItem(PendingItem item)
        {
            if (item != null) _playerItems.Add(item);
        }

        public static void AddWorldItem(PendingItem item)
        {
            if (item != null) _worldItems.Add(item);
        }

        public static void RemovePlayerItem(PendingItem item)
        {
            _playerItems.Remove(item);
        }

        public static void RemoveWorldItem(PendingItem item)
        {
            _worldItems.Remove(item);
        }

        public static void ClearPlayer() => _playerItems.Clear();
        public static void ClearWorld() => _worldItems.Clear();
        public static void ClearAll()
        {
            _playerItems.Clear();
            _worldItems.Clear();
        }

        /// <summary>
        /// Convert pending player items to moddata entries for persistence.
        /// Uses location "pending" so they're recognized on next load.
        /// </summary>
        public static List<ModdataFile.ItemEntry> GetPlayerModdataEntries()
        {
            var entries = new List<ModdataFile.ItemEntry>();
            for (int i = 0; i < _playerItems.Count; i++)
            {
                var p = _playerItems[i];
                entries.Add(new ModdataFile.ItemEntry
                {
                    Location = "pending",
                    Slot = i,
                    ItemId = p.ItemId,
                    Stack = p.Stack,
                    Prefix = p.Prefix,
                    Favorited = p.Favorited
                });
            }
            return entries;
        }

        /// <summary>
        /// Convert pending world items to moddata entries for persistence.
        /// Uses location "pending_world" so they're recognized on next load.
        /// </summary>
        public static List<ModdataFile.ItemEntry> GetWorldModdataEntries()
        {
            var entries = new List<ModdataFile.ItemEntry>();
            for (int i = 0; i < _worldItems.Count; i++)
            {
                var w = _worldItems[i];
                entries.Add(new ModdataFile.ItemEntry
                {
                    Location = "pending_world",
                    Slot = i,
                    ItemId = w.ItemId,
                    Stack = w.Stack,
                    Prefix = w.Prefix,
                    Favorited = w.Favorited
                });
            }
            return entries;
        }
    }
}
