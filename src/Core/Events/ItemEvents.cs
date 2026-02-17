using System;

namespace TerrariaModder.Core.Events
{
    /// <summary>
    /// Events related to items.
    /// </summary>
    public static class ItemEvents
    {
        /// <summary>Fired when a player picks up an item. Cancellable.</summary>
        public static event Action<ItemPickupEventArgs> OnItemPickup;

        /// <summary>Fired when a player uses an item. Cancellable.</summary>
        public static event Action<ItemUseEventArgs> OnItemUse;

        /// <summary>Fired when an item is dropped into the world. Cancellable.</summary>
        public static event Action<ItemDropEventArgs> OnItemDrop;

        // Internal firing methods
        internal static bool FireItemPickup(ItemPickupEventArgs args)
            => EventDispatcher.FireCancellable(OnItemPickup, args, "ItemEvents.OnItemPickup");

        internal static bool FireItemUse(ItemUseEventArgs args)
            => EventDispatcher.FireCancellable(OnItemUse, args, "ItemEvents.OnItemUse");

        internal static bool FireItemDrop(ItemDropEventArgs args)
            => EventDispatcher.FireCancellable(OnItemDrop, args, "ItemEvents.OnItemDrop");

        /// <summary>Clear all event subscriptions.</summary>
        internal static void ClearAll()
        {
            OnItemPickup = null;
            OnItemUse = null;
            OnItemDrop = null;
        }
    }
}
