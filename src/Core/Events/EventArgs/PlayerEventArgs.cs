namespace TerrariaModder.Core.Events
{
    /// <summary>
    /// Base class for player-related events.
    /// </summary>
    public class PlayerEventArgs : ModEventArgs
    {
        /// <summary>The player index.</summary>
        public int PlayerIndex { get; set; }

        /// <summary>The player object (Terraria.Player).</summary>
        public object Player { get; set; }
    }

    /// <summary>
    /// Event args for player spawn events.
    /// </summary>
    public class PlayerSpawnEventArgs : PlayerEventArgs
    {
        /// <summary>Spawn X position.</summary>
        public int SpawnX { get; set; }

        /// <summary>Spawn Y position.</summary>
        public int SpawnY { get; set; }
    }

    /// <summary>
    /// Event args for player death events.
    /// </summary>
    public class PlayerDeathEventArgs : PlayerEventArgs
    {
        /// <summary>Damage that killed the player.</summary>
        public int Damage { get; set; }

        /// <summary>Death reason text.</summary>
        public string DeathReason { get; set; }
    }

    /// <summary>
    /// Event args for player hurt events.
    /// </summary>
    public class PlayerHurtEventArgs : CancellableEventArgs
    {
        /// <summary>The player index.</summary>
        public int PlayerIndex { get; set; }

        /// <summary>The player object.</summary>
        public object Player { get; set; }

        /// <summary>Damage amount.</summary>
        public int Damage { get; set; }

        /// <summary>Hit direction.</summary>
        public int HitDirection { get; set; }

        /// <summary>Whether this was PvP damage.</summary>
        public bool PvP { get; set; }
    }

    /// <summary>
    /// Event args for buff applied events.
    /// </summary>
    public class BuffEventArgs : PlayerEventArgs
    {
        /// <summary>Buff type ID.</summary>
        public int BuffType { get; set; }

        /// <summary>Buff duration in frames (60 frames = 1 second).</summary>
        public int Duration { get; set; }
    }
}
