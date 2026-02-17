namespace TerrariaModder.Core.Events
{
    /// <summary>
    /// Event args for world-related events.
    /// </summary>
    public class WorldEventArgs : ModEventArgs
    {
        /// <summary>World name.</summary>
        public string WorldName { get; set; }

        /// <summary>World ID.</summary>
        public int WorldId { get; set; }

        /// <summary>Is this a new world.</summary>
        public bool IsNewWorld { get; set; }
    }

    /// <summary>
    /// Event args for time-of-day events.
    /// </summary>
    public class TimeEventArgs : ModEventArgs
    {
        /// <summary>Current in-game time.</summary>
        public double Time { get; set; }

        /// <summary>Is it daytime.</summary>
        public bool IsDay { get; set; }
    }
}
