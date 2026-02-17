namespace TerrariaModder.Core.Events
{
    /// <summary>
    /// Base class for NPC-related events.
    /// </summary>
    public class NPCEventArgs : ModEventArgs
    {
        /// <summary>The NPC index in Main.npc[].</summary>
        public int NPCIndex { get; set; }

        /// <summary>The NPC object (Terraria.NPC).</summary>
        public object NPC { get; set; }

        /// <summary>The NPC type ID.</summary>
        public int NPCType { get; set; }
    }

    /// <summary>
    /// Event args for NPC spawn events.
    /// </summary>
    public class NPCSpawnEventArgs : NPCEventArgs
    {
        /// <summary>Spawn X position.</summary>
        public float SpawnX { get; set; }

        /// <summary>Spawn Y position.</summary>
        public float SpawnY { get; set; }
    }

    /// <summary>
    /// Event args for NPC death events.
    /// </summary>
    public class NPCDeathEventArgs : NPCEventArgs
    {
        /// <summary>Last damage that killed the NPC.</summary>
        public int LastDamage { get; set; }

        /// <summary>Player who dealt the killing blow (if any).</summary>
        public int KillerPlayerIndex { get; set; }
    }

    /// <summary>
    /// Event args for boss spawn events.
    /// </summary>
    public class BossSpawnEventArgs : NPCEventArgs
    {
        /// <summary>Boss name.</summary>
        public string BossName { get; set; }
    }

    /// <summary>
    /// Event args for NPC hit events.
    /// </summary>
    public class NPCHitEventArgs : CancellableEventArgs
    {
        /// <summary>The NPC index.</summary>
        public int NPCIndex { get; set; }

        /// <summary>The NPC object.</summary>
        public object NPC { get; set; }

        /// <summary>Damage amount.</summary>
        public int Damage { get; set; }

        /// <summary>Knockback strength.</summary>
        public float Knockback { get; set; }

        /// <summary>Hit direction.</summary>
        public int HitDirection { get; set; }

        /// <summary>Whether this is a critical hit.</summary>
        public bool Crit { get; set; }
    }
}
