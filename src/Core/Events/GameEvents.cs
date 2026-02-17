using System;

namespace TerrariaModder.Core.Events
{
    /// <summary>
    /// Events related to world and game state.
    /// </summary>
    public static class GameEvents
    {
        /// <summary>Fired when a world finishes loading.</summary>
        public static event Action OnWorldLoad;

        /// <summary>Fired when leaving a world.</summary>
        public static event Action OnWorldUnload;

        /// <summary>Fired when day starts (6:00 AM).</summary>
        public static event Action OnDayStart;

        /// <summary>Fired when night starts (7:30 PM).</summary>
        public static event Action OnNightStart;

        /// <summary>Fired when the game is about to save.</summary>
        public static event Action OnWorldSave;

        /// <summary>Fired when game exits to menu.</summary>
        public static event Action OnReturnToMenu;

        // Internal firing methods
        internal static void FireWorldLoad()
            => EventDispatcher.Fire(OnWorldLoad, "GameEvents.OnWorldLoad");

        internal static void FireWorldUnload()
            => EventDispatcher.Fire(OnWorldUnload, "GameEvents.OnWorldUnload");

        internal static void FireDayStart()
            => EventDispatcher.Fire(OnDayStart, "GameEvents.OnDayStart");

        internal static void FireNightStart()
            => EventDispatcher.Fire(OnNightStart, "GameEvents.OnNightStart");

        internal static void FireWorldSave()
            => EventDispatcher.Fire(OnWorldSave, "GameEvents.OnWorldSave");

        internal static void FireReturnToMenu()
            => EventDispatcher.Fire(OnReturnToMenu, "GameEvents.OnReturnToMenu");

        /// <summary>
        /// Clear all event subscriptions (for testing/cleanup).
        /// </summary>
        internal static void ClearAll()
        {
            OnWorldLoad = null;
            OnWorldUnload = null;
            OnDayStart = null;
            OnNightStart = null;
            OnWorldSave = null;
            OnReturnToMenu = null;
        }
    }
}
