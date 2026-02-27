namespace TerrariaModManager.Models;

public class CoreInstallInfo
{
    public bool IsInstalled { get; set; }
    public string? CoreVersion { get; set; }
    public bool InjectorPresent { get; set; }
    public bool ModsFolderExists { get; set; }
}
