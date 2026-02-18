namespace FpsUnlocked
{
    /// <summary>
    /// Static timing state shared between patches and interpolator.
    /// Updated each frame by DoUpdate patches.
    /// </summary>
    public static class FrameState
    {
        /// <summary>Whether the interpolation system is active (enabled + in world + not paused).</summary>
        public static bool Active;

        /// <summary>True when the current frame had no game logic (draw-only partial tick).</summary>
        public static bool IsPartialTick;

        /// <summary>Blend factor 0..1 between previous and current game tick positions.</summary>
        public static float PartialTick;

        /// <summary>True on the frame where game logic just ran (capture EndKeyframe).</summary>
        public static bool WasFullTick;

        /// <summary>Monotonic tick counter incremented each full game tick.</summary>
        public static long TickCount;

        public static void Reset()
        {
            Active = false;
            IsPartialTick = false;
            PartialTick = 0f;
            WasFullTick = false;
            TickCount = 0;
        }
    }
}
