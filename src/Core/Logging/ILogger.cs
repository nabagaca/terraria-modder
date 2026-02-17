using System;

namespace TerrariaModder.Core.Logging
{
    /// <summary>
    /// Logger interface for per-mod logging.
    /// </summary>
    public interface ILogger
    {
        /// <summary>Log a debug message.</summary>
        void Debug(string message);

        /// <summary>Log an info message.</summary>
        void Info(string message);

        /// <summary>Log a warning message.</summary>
        void Warn(string message);

        /// <summary>Log an error message.</summary>
        void Error(string message);

        /// <summary>Log an error message with exception details.</summary>
        void Error(string message, Exception ex);

        /// <summary>Minimum log level to output. Messages below this level are ignored.</summary>
        LogLevel MinLevel { get; set; }

        /// <summary>The mod ID this logger is for.</summary>
        string ModId { get; }
    }
}
