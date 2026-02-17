using System;
using TerrariaModder.Core.Logging;

namespace TerrariaModder.Core.Events
{
    /// <summary>
    /// Safely dispatches events to multiple handlers with error isolation.
    /// </summary>
    public static class EventDispatcher
    {
        private static ILogger _log;

        internal static void Initialize(ILogger logger)
        {
            _log = logger;
        }

        /// <summary>
        /// Fire an event to all subscribers safely.
        /// If a handler throws, the exception is logged and other handlers still run.
        /// </summary>
        public static void Fire(Action eventHandler, string eventName)
        {
            if (eventHandler == null) return;

            foreach (var handler in eventHandler.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception ex)
                {
                    _log?.Error($"[Events] Handler for {eventName} threw: {ex.Message}");
                    _log?.Debug($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        /// <summary>
        /// Fire an event with arguments to all subscribers safely.
        /// </summary>
        public static void Fire<T>(Action<T> eventHandler, T args, string eventName)
        {
            if (eventHandler == null) return;

            foreach (var handler in eventHandler.GetInvocationList())
            {
                try
                {
                    ((Action<T>)handler)(args);
                }
                catch (Exception ex)
                {
                    _log?.Error($"[Events] Handler for {eventName} threw: {ex.Message}");
                    _log?.Debug($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        /// <summary>
        /// Fire a cancellable event. Returns true if any handler cancelled it.
        /// </summary>
        public static bool FireCancellable<T>(Action<T> eventHandler, T args, string eventName)
            where T : CancellableEventArgs
        {
            if (eventHandler == null) return false;

            foreach (var handler in eventHandler.GetInvocationList())
            {
                try
                {
                    ((Action<T>)handler)(args);
                    if (args.Cancelled)
                    {
                        _log?.Debug($"[Events] {eventName} was cancelled");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _log?.Error($"[Events] Handler for {eventName} threw: {ex.Message}");
                    _log?.Debug($"Stack trace: {ex.StackTrace}");
                }
            }

            return false;
        }
    }
}
