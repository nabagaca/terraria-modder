namespace TerrariaModder.Core.Logging
{
    /// <summary>
    /// Log severity levels.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>Verbose debugging information.</summary>
        Debug = 0,

        /// <summary>General information.</summary>
        Info = 1,

        /// <summary>Warnings (non-fatal issues).</summary>
        Warn = 2,

        /// <summary>Errors (mod may not function correctly).</summary>
        Error = 3
    }
}
