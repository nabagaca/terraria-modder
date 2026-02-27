using Avalonia.Media;

namespace TerrariaModManager.Models;

public class InstalledMod
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string EntryDll { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public bool IsCore { get; set; }
    public ModManifest? Manifest { get; set; }

    // Settings detection
    public bool HasConfigFiles { get; set; }

    // Update tracking
    public int NexusModId { get; set; }
    public bool HasUpdate { get; set; }
    public string LatestVersion { get; set; } = "";
    public int LatestFileId { get; set; }

    // Section grouping
    public bool IsFirstDisabled { get; set; }

    // Computed for Avalonia UI (replaces WPF DataTrigger)
    public string StatusText => HasUpdate ? "Update Available"
        : IsEnabled ? "Enabled" : "Disabled";
    public IBrush StatusColor => HasUpdate
        ? new SolidColorBrush(Color.Parse("#FFE8B93C"))
        : IsEnabled
            ? new SolidColorBrush(Color.Parse("#FF58EB1C"))
            : new SolidColorBrush(Color.Parse("#FF7C828D"));
    public IBrush VersionColor => HasUpdate
        ? new SolidColorBrush(Color.Parse("#FFE8B93C"))
        : new SolidColorBrush(Color.Parse("#FF58EB1C"));
    public IBrush NameColor => HasUpdate
        ? new SolidColorBrush(Color.Parse("#FFE8B93C"))
        : new SolidColorBrush(Color.Parse("#FFE2E4E8"));
}
