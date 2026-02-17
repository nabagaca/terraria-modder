using System;

namespace TerrariaModder.Core.Events
{
    /// <summary>
    /// Base class for all event arguments.
    /// </summary>
    public class ModEventArgs
    {
        /// <summary>
        /// The time when the event was fired.
        /// </summary>
        public DateTime Timestamp { get; } = DateTime.Now;
    }

    /// <summary>
    /// Base class for cancellable event arguments.
    /// </summary>
    public class CancellableEventArgs : ModEventArgs
    {
        /// <summary>
        /// Set to true to cancel this event.
        /// What "cancel" means depends on the event.
        /// </summary>
        public bool Cancelled { get; set; }

        /// <summary>
        /// Cancel this event.
        /// </summary>
        public void Cancel() => Cancelled = true;
    }
}
