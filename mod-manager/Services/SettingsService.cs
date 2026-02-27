using System.IO;
using System.Text.Json;
using TerrariaModManager.Models;

namespace TerrariaModManager.Services;

public class SettingsService
{
    public static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TerrariaModManager");

    private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");

    public static string DownloadsDir => Path.Combine(AppDataDir, "downloads");
    public static string CacheDir => Path.Combine(AppDataDir, "cache");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            var json = JsonSerializer.Serialize(settings, JsonOpts);
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(DownloadsDir);
        Directory.CreateDirectory(CacheDir);
    }
}
