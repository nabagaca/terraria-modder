namespace StorageHub.Storage
{
    /// <summary>
    /// Immutable snapshot of an item for safe UI display.
    ///
    /// CRITICAL DESIGN DECISION:
    /// This struct contains COPIES of item data, not references to actual Item objects.
    /// This ensures that passive operations (viewing, sorting, filtering) cannot
    /// accidentally modify real items. Even if UI code has bugs, it can't corrupt
    /// items because it has no references to them.
    ///
    /// Why this matters:
    /// - Players will open Storage Hub constantly just to look at their stuff
    /// - If viewing items has ANY chance of corruption, eventually someone loses items
    /// - "Just from looking" item loss is unacceptable
    /// - Active operations (take/deposit/craft) are risky but explicit user actions
    /// </summary>
    public readonly struct ItemSnapshot
    {
        /// <summary>Terraria item type ID.</summary>
        public readonly int ItemId;

        /// <summary>Current stack count.</summary>
        public readonly int Stack;

        /// <summary>Item prefix (reforge/modifier). 0 = no prefix.</summary>
        public readonly int Prefix;

        /// <summary>Display name of the item.</summary>
        public readonly string Name;

        /// <summary>Maximum stack size for this item type.</summary>
        public readonly int MaxStack;

        /// <summary>Rarity value for color coding.</summary>
        public readonly int Rarity;

        /// <summary>
        /// Index of the source chest in Main.chest array.
        /// Special values:
        /// -1 = player inventory
        /// -2 = piggy bank (bank)
        /// -3 = safe (bank2)
        /// -4 = defender's forge (bank3)
        /// -5 = void vault (bank4)
        /// </summary>
        public readonly int SourceChestIndex;

        /// <summary>Slot index within the source container.</summary>
        public readonly int SourceSlot;

        // Category flags for filtering
        /// <summary>Item damage value if it's a weapon.</summary>
        public readonly int? Damage;
        /// <summary>Whether the item has pickaxe power.</summary>
        public readonly bool IsPickaxe;
        /// <summary>Whether the item has axe power.</summary>
        public readonly bool IsAxe;
        /// <summary>Whether the item has hammer power.</summary>
        public readonly bool IsHammer;
        /// <summary>Whether the item is armor.</summary>
        public readonly bool IsArmor;
        /// <summary>Whether the item is an accessory.</summary>
        public readonly bool IsAccessory;
        /// <summary>Whether the item is consumable.</summary>
        public readonly bool IsConsumable;
        /// <summary>Whether the item can be placed as a tile.</summary>
        public readonly bool IsPlaceable;
        /// <summary>Whether the item is a crafting material.</summary>
        public readonly bool IsMaterial;

        /// <summary>
        /// Create a snapshot from item data.
        /// Called by the storage scanner when building the item list.
        /// </summary>
        public ItemSnapshot(
            int itemId,
            int stack,
            int prefix,
            string name,
            int maxStack,
            int rarity,
            int sourceChestIndex,
            int sourceSlot,
            int? damage = null,
            bool isPickaxe = false,
            bool isAxe = false,
            bool isHammer = false,
            bool isArmor = false,
            bool isAccessory = false,
            bool isConsumable = false,
            bool isPlaceable = false,
            bool isMaterial = false)
        {
            ItemId = itemId;
            Stack = stack;
            Prefix = prefix;
            Name = name ?? "";
            MaxStack = maxStack;
            Rarity = rarity;
            SourceChestIndex = sourceChestIndex;
            SourceSlot = sourceSlot;
            Damage = damage;
            IsPickaxe = isPickaxe;
            IsAxe = isAxe;
            IsHammer = isHammer;
            IsArmor = isArmor;
            IsAccessory = isAccessory;
            IsConsumable = isConsumable;
            IsPlaceable = isPlaceable;
            IsMaterial = isMaterial;
        }

        /// <summary>
        /// Whether this snapshot represents an empty slot.
        /// </summary>
        public bool IsEmpty => ItemId <= 0 || Stack <= 0;

        /// <summary>
        /// Whether this item came from player inventory.
        /// </summary>
        public bool IsFromInventory => SourceChestIndex == SourceIndex.PlayerInventory;

        /// <summary>
        /// Whether this item came from a personal bank (piggy/safe/forge/void).
        /// </summary>
        public bool IsFromPersonalBank => SourceChestIndex <= SourceIndex.PiggyBank && SourceChestIndex >= SourceIndex.VoidVault;

        /// <summary>
        /// Create a key for grouping items by type+prefix.
        /// </summary>
        public (int, int) GetGroupKey() => (ItemId, Prefix);

        public override string ToString()
        {
            return $"{Name} x{Stack} (ID:{ItemId}, Prefix:{Prefix})";
        }
    }

    /// <summary>
    /// Constants for special source indices.
    /// </summary>
    public static class SourceIndex
    {
        public const int PlayerInventory = -1;
        public const int PiggyBank = -2;
        public const int Safe = -3;
        public const int DefendersForge = -4;
        public const int VoidVault = -5;

        /// <summary>
        /// Get display name for a source index.
        /// </summary>
        public static string GetSourceName(int sourceIndex)
        {
            return sourceIndex switch
            {
                PlayerInventory => "Inventory",
                PiggyBank => "Piggy Bank",
                Safe => "Safe",
                DefendersForge => "Defender's Forge",
                VoidVault => "Void Vault",
                _ when sourceIndex >= 0 => $"Chest #{sourceIndex}",
                _ => "Unknown"
            };
        }
    }
}
