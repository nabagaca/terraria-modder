using System;

namespace TerrariaModder.Core.Events
{
    /// <summary>
    /// Events related to the game loop (Update/Draw cycle).
    /// Use these for per-frame operations.
    /// </summary>
    public static class FrameEvents
    {
        /// <summary>
        /// Fired before the game updates each frame.
        /// Use for input processing, state changes.
        /// </summary>
        public static event Action OnPreUpdate;

        /// <summary>
        /// Fired after the game updates each frame.
        /// Use for reactions to game state changes.
        /// </summary>
        public static event Action OnPostUpdate;

        /// <summary>
        /// Fired before the game draws each frame.
        /// Use for preparing draw data.
        /// </summary>
        public static event Action OnPreDraw;

        /// <summary>
        /// Fired after the game draws each frame.
        /// Use for custom overlay rendering.
        /// </summary>
        public static event Action OnPostDraw;

        /// <summary>
        /// Fired just before the cursor is drawn.
        /// Use for UI overlays that should appear above game content but below the cursor.
        /// This is the proper hook for custom UI panels.
        /// </summary>
        public static event Action OnUIOverlay;

        // Internal firing methods
        internal static void FirePreUpdate()
            => EventDispatcher.Fire(OnPreUpdate, "FrameEvents.OnPreUpdate");

        internal static void FirePostUpdate()
            => EventDispatcher.Fire(OnPostUpdate, "FrameEvents.OnPostUpdate");

        internal static void FirePreDraw()
            => EventDispatcher.Fire(OnPreDraw, "FrameEvents.OnPreDraw");

        internal static void FirePostDraw()
            => EventDispatcher.Fire(OnPostDraw, "FrameEvents.OnPostDraw");

        internal static void FireUIOverlay()
            => EventDispatcher.Fire(OnUIOverlay, "FrameEvents.OnUIOverlay");

        /// <summary>Clear all event subscriptions.</summary>
        internal static void ClearAll()
        {
            OnPreUpdate = null;
            OnPostUpdate = null;
            OnPreDraw = null;
            OnPostDraw = null;
            OnUIOverlay = null;
        }
    }
}
