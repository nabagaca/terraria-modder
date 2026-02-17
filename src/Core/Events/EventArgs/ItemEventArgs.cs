namespace TerrariaModder.Core.Events
{
    /// <summary>
    /// Base class for item-related events.
    /// </summary>
    public class ItemEventArgs : ModEventArgs
    {
        /// <summary>The item type ID.</summary>
        public int ItemType { get; set; }

        /// <summary>The item object (Terraria.Item).</summary>
        public object Item { get; set; }

        /// <summary>The stack size.</summary>
        public int Stack { get; set; }
    }

    /// <summary>
    /// Event args for item pickup events.
    /// </summary>
    public class ItemPickupEventArgs : CancellableEventArgs
    {
        /// <summary>The player index who picked up the item.</summary>
        public int PlayerIndex { get; set; }

        /// <summary>The player object.</summary>
        public object Player { get; set; }

        /// <summary>The item type ID.</summary>
        public int ItemType { get; set; }

        /// <summary>The item object.</summary>
        public object Item { get; set; }

        /// <summary>The stack size.</summary>
        public int Stack { get; set; }
    }

    /// <summary>
    /// Event args for item use events.
    /// </summary>
    public class ItemUseEventArgs : CancellableEventArgs
    {
        /// <summary>The player index using the item.</summary>
        public int PlayerIndex { get; set; }

        /// <summary>The player object.</summary>
        public object Player { get; set; }

        /// <summary>The item type ID.</summary>
        public int ItemType { get; set; }

        /// <summary>The item object being used.</summary>
        public object Item { get; set; }
    }

    /// <summary>
    /// Event args for item drop events.
    /// </summary>
    public class ItemDropEventArgs : CancellableEventArgs
    {
        /// <summary>The item type ID.</summary>
        public int ItemType { get; set; }

        /// <summary>The stack size.</summary>
        public int Stack { get; set; }

        /// <summary>Drop X position.</summary>
        public float X { get; set; }

        /// <summary>Drop Y position.</summary>
        public float Y { get; set; }
    }
}
