using System.IO;

namespace TerrariaModManager.Services;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(SettingsService.AppDataDir, "app.log");
    private static readonly object Lock = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Error(string message, Exception ex) => Write("ERROR", $"{message}: {ex.Message}");

    public static string ReadTail(int lines = 100)
    {
        try
        {
            lock (Lock)
            {
                if (!File.Exists(LogPath)) return "(no log file)";
                var allLines = File.ReadAllLines(LogPath);
                var start = Math.Max(0, allLines.Length - lines);
                return string.Join(Environment.NewLine, allLines[start..]);
            }
        }
        catch (Exception ex)
        {
            return $"(could not read log: {ex.Message})";
        }
    }

    public static void Clear()
    {
        try
        {
            lock (Lock)
            {
                if (File.Exists(LogPath))
                    File.WriteAllText(LogPath, "");
            }
        }
        catch { }
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch { }
    }
}
