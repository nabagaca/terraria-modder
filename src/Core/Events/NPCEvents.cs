using System;

namespace TerrariaModder.Core.Events
{
    /// <summary>
    /// Events related to NPCs.
    /// </summary>
    public static class NPCEvents
    {
        /// <summary>Fired when an NPC spawns.</summary>
        public static event Action<NPCSpawnEventArgs> OnNPCSpawn;

        /// <summary>Fired when an NPC dies.</summary>
        public static event Action<NPCDeathEventArgs> OnNPCDeath;

        /// <summary>Fired when a boss spawns.</summary>
        public static event Action<BossSpawnEventArgs> OnBossSpawn;

        /// <summary>Fired when an NPC is hit. Cancellable.</summary>
        public static event Action<NPCHitEventArgs> OnNPCHit;

        // Internal firing methods
        internal static void FireNPCSpawn(NPCSpawnEventArgs args)
            => EventDispatcher.Fire(OnNPCSpawn, args, "NPCEvents.OnNPCSpawn");

        internal static void FireNPCDeath(NPCDeathEventArgs args)
            => EventDispatcher.Fire(OnNPCDeath, args, "NPCEvents.OnNPCDeath");

        internal static void FireBossSpawn(BossSpawnEventArgs args)
            => EventDispatcher.Fire(OnBossSpawn, args, "NPCEvents.OnBossSpawn");

        internal static bool FireNPCHit(NPCHitEventArgs args)
            => EventDispatcher.FireCancellable(OnNPCHit, args, "NPCEvents.OnNPCHit");

        /// <summary>Clear all event subscriptions.</summary>
        internal static void ClearAll()
        {
            OnNPCSpawn = null;
            OnNPCDeath = null;
            OnBossSpawn = null;
            OnNPCHit = null;
        }
    }
}
