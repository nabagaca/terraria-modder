using System;

namespace TerrariaModder.Core.Events
{
    /// <summary>
    /// Events related to player actions.
    /// </summary>
    public static class PlayerEvents
    {
        /// <summary>Fired when a player spawns/respawns.</summary>
        public static event Action<PlayerSpawnEventArgs> OnPlayerSpawn;

        /// <summary>Fired when a player dies.</summary>
        public static event Action<PlayerDeathEventArgs> OnPlayerDeath;

        /// <summary>Fired when a player takes damage. Cancellable.</summary>
        public static event Action<PlayerHurtEventArgs> OnPlayerHurt;

        /// <summary>Fired when a buff is applied to a player.</summary>
        public static event Action<BuffEventArgs> OnBuffApplied;

        /// <summary>Fired each frame for the local player.</summary>
        public static event Action<PlayerEventArgs> OnPlayerUpdate;

        // Internal firing methods
        internal static void FirePlayerSpawn(PlayerSpawnEventArgs args)
            => EventDispatcher.Fire(OnPlayerSpawn, args, "PlayerEvents.OnPlayerSpawn");

        internal static void FirePlayerDeath(PlayerDeathEventArgs args)
            => EventDispatcher.Fire(OnPlayerDeath, args, "PlayerEvents.OnPlayerDeath");

        internal static bool FirePlayerHurt(PlayerHurtEventArgs args)
            => EventDispatcher.FireCancellable(OnPlayerHurt, args, "PlayerEvents.OnPlayerHurt");

        internal static void FireBuffApplied(BuffEventArgs args)
            => EventDispatcher.Fire(OnBuffApplied, args, "PlayerEvents.OnBuffApplied");

        internal static void FirePlayerUpdate(PlayerEventArgs args)
            => EventDispatcher.Fire(OnPlayerUpdate, args, "PlayerEvents.OnPlayerUpdate");

        /// <summary>Clear all event subscriptions.</summary>
        internal static void ClearAll()
        {
            OnPlayerSpawn = null;
            OnPlayerDeath = null;
            OnPlayerHurt = null;
            OnBuffApplied = null;
            OnPlayerUpdate = null;
        }
    }
}
