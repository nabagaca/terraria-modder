using System.IO;
using System.Text.Json;
using SharpCompress.Archives;
using TerrariaModManager.Models;

namespace TerrariaModManager.Services;

public class InstallResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? InstalledModId { get; set; }
    public string? DownloadedFilePath { get; set; }
}

public enum ConfigAction { Keep, Delete }

public class ModInstallService
{
    private string? _terrariaPath;

    /// <summary>
    /// Called when existing config files are found during install.
    /// Return Keep to preserve old settings, Delete for a clean install.
    /// If null, defaults to Keep.
    /// </summary>
    public Func<string, List<string>, Task<ConfigAction>>? OnExistingConfigFound { get; set; }

    public void SetTerrariaPath(string path)
    {
        _terrariaPath = path;
    }

    public async Task<InstallResult> InstallModAsync(string archivePath, bool forceKeepSettings = false)
    {
        Logger.Info($"Install: opening archive {Path.GetFileName(archivePath)}");

        if (_terrariaPath == null)
        {
            Logger.Error("Install: Terraria path not configured");
            return new InstallResult { Error = "Terraria path not configured", DownloadedFilePath = archivePath };
        }

        var modsDir = Path.Combine(_terrariaPath, "TerrariaModder", "mods");

        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
            Logger.Info($"Install: archive has {entries.Count} file entries");

            // Strategy 1: Look for manifest.json to determine mod structure
            var manifest = FindManifestInArchive(entries);
            if (manifest != null)
            {
                var modId = manifest.Manifest.Id;
                Logger.Info($"Install: found manifest, id='{modId}', version='{manifest.Manifest.Version}', prefix='{manifest.PathPrefix}'");

                if (string.IsNullOrWhiteSpace(modId))
                {
                    Logger.Error("Install: manifest has no id field");
                    return new InstallResult { Error = "Manifest has no id field", DownloadedFilePath = archivePath };
                }

                var targetDir = Path.Combine(modsDir, modId);
                var disabledDir = Path.Combine(modsDir, "." + modId);

                // Check for existing config files to preserve
                var existingDir = Directory.Exists(targetDir) ? targetDir
                    : Directory.Exists(disabledDir) ? disabledDir : null;
                Logger.Info($"Install: existing dir = {(existingDir != null ? Path.GetFileName(existingDir) : "none")}");

                var preservedConfigs = await PreserveConfigsAsync(existingDir, modId, forceKeepSettings);
                Logger.Info($"Install: preserved {preservedConfigs.Count} config file(s)");

                // Clean up both path variants
                if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                if (Directory.Exists(disabledDir)) Directory.Delete(disabledDir, true);

                Directory.CreateDirectory(targetDir);

                // Extract relative to manifest location
                ExtractEntries(entries, targetDir, manifest.PathPrefix);

                RestoreConfigs(preservedConfigs, targetDir);

                // Verify files were extracted
                var extractedFiles = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
                Logger.Info($"Install: extracted {extractedFiles.Length} files to {modId}/");

                return new InstallResult { Success = true, InstalledModId = modId };
            }

            // Strategy 2: Look for TerrariaModder/ folder structure
            var tmPrefix = FindTerrariaModderPrefix(entries);
            if (tmPrefix != null)
            {
                Logger.Info($"Install: TerrariaModder folder structure detected, prefix='{tmPrefix}'");
                ExtractEntries(entries, _terrariaPath, tmPrefix);
                return new InstallResult { Success = true, InstalledModId = "core" };
            }

            // Strategy 3: Flat archive with DLL + other files — use DLL name as mod-id
            var dll = entries.FirstOrDefault(e =>
            {
                var name = Path.GetFileName(e.Key ?? "");
                return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase);
            });

            if (dll != null)
            {
                var dllName = Path.GetFileNameWithoutExtension(Path.GetFileName(dll.Key!));
                var modId = ToKebabCase(dllName);
                Logger.Info($"Install: flat archive, DLL='{dllName}.dll', mod-id='{modId}'");
                var targetDir = Path.Combine(modsDir, modId);

                if (Directory.Exists(targetDir))
                    Directory.Delete(targetDir, true);

                Directory.CreateDirectory(targetDir);

                // Strip common top-level folder if all entries share one
                var prefix = FindCommonPrefix(entries);
                ExtractEntries(entries, targetDir, prefix);

                var extractedFiles = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
                Logger.Info($"Install: extracted {extractedFiles.Length} files to {modId}/");

                return new InstallResult { Success = true, InstalledModId = modId };
            }

            Logger.Warn($"Install: no manifest, no TerrariaModder folder, no DLL found. Archive entries: {string.Join(", ", entries.Take(10).Select(e => e.Key))}");
            return new InstallResult
            {
                Error = "Mod is not in a recognized format. Please contact the mod author.",
                DownloadedFilePath = archivePath
            };
        }
        catch (Exception ex)
        {
            Logger.Error("Install failed", ex);
            return new InstallResult { Error = ex.Message, DownloadedFilePath = archivePath };
        }
    }

    /// <summary>
    /// Update the version field in an installed mod's manifest.json to match
    /// the Nexus file version (e.g. "1.1.1-hotfix"), so the update checker
    /// sees the correct version after install.
    /// </summary>
    public void StampManifestVersion(string modId, string version)
    {
        if (_terrariaPath == null) return;

        var manifestPath = Path.Combine(_terrariaPath, "TerrariaModder", "mods", modId, "manifest.json");
        if (!File.Exists(manifestPath)) return;

        try
        {
            var json = File.ReadAllText(manifestPath);
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (doc == null) return;

            // Replace version value
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var prop in doc)
                {
                    if (prop.Key == "version")
                        writer.WriteString("version", version);
                    else
                    {
                        writer.WritePropertyName(prop.Key);
                        prop.Value.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }

            File.WriteAllText(manifestPath, System.Text.Encoding.UTF8.GetString(ms.ToArray()));
            Logger.Info($"Install: stamped manifest version '{version}' for '{modId}'");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Install: failed to stamp manifest version for '{modId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Find the prefix to strip so that "TerrariaModder/" ends up at the Terraria root.
    /// Handles: "TerrariaModder/...", "Terraria/TerrariaModder/..."
    /// </summary>
    private static string? FindTerrariaModderPrefix(List<IArchiveEntry> entries)
    {
        foreach (var entry in entries)
        {
            var key = NormalizePath(entry.Key ?? "");

            // Case: Terraria/TerrariaModder/...
            if (key.StartsWith("Terraria/TerrariaModder/", StringComparison.OrdinalIgnoreCase))
                return "Terraria/";

            // Case: TerrariaModder/...
            if (key.StartsWith("TerrariaModder/", StringComparison.OrdinalIgnoreCase))
                return "";
        }
        return null;
    }

    /// <summary>
    /// If all entries share a common top-level folder (e.g. "ModName/file.dll"),
    /// returns that folder as a prefix to strip. Otherwise returns empty string.
    /// </summary>
    private static string FindCommonPrefix(List<IArchiveEntry> entries)
    {
        if (entries.Count == 0) return "";

        var firstKey = NormalizePath(entries[0].Key ?? "");
        var slashIndex = firstKey.IndexOf('/');
        if (slashIndex < 0) return ""; // flat file, no folder

        var candidate = firstKey[..(slashIndex + 1)];

        // Check if ALL entries start with this folder
        foreach (var entry in entries)
        {
            var key = NormalizePath(entry.Key ?? "");
            if (!key.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                return ""; // not all entries share this prefix
        }

        return candidate;
    }

    private static void ExtractEntries(List<IArchiveEntry> entries, string targetDir, string? prefix)
    {
        foreach (var entry in entries)
        {
            var key = NormalizePath(entry.Key ?? "");

            string relativePath;
            if (!string.IsNullOrEmpty(prefix) && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                relativePath = key[prefix.Length..];
            else
                relativePath = key;

            if (string.IsNullOrEmpty(relativePath)) continue;

            var destPath = Path.Combine(targetDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            using var entryStream = entry.OpenEntryStream();
            using var fileStream = File.Create(destPath);
            entryStream.CopyTo(fileStream);
        }
    }

    private async Task<Dictionary<string, byte[]>> PreserveConfigsAsync(
        string? existingDir, string modId, bool forceKeep = false)
    {
        var preserved = new Dictionary<string, byte[]>();
        if (existingDir == null) return preserved;

        var configFiles = new List<string>();

        // Scan top-level config files
        foreach (var file in Directory.GetFiles(existingDir))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".json" or ".cfg" or ".ini" or ".xml" or ".config"
                && !Path.GetFileName(file).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                configFiles.Add(Path.GetFileName(file));
            }
        }

        // Also scan config/ subdirectory (preserved by DeleteNonConfigFiles)
        var configDir = Path.Combine(existingDir, "config");
        if (Directory.Exists(configDir))
        {
            foreach (var file in Directory.GetFiles(configDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.Combine("config", Path.GetRelativePath(configDir, file));
                configFiles.Add(relative);
            }
        }

        var action = ConfigAction.Keep;
        if (!forceKeep && configFiles.Count > 0 && OnExistingConfigFound != null)
        {
            // Show only display names (filenames, not paths) in the dialog
            var displayNames = configFiles.Select(Path.GetFileName).Distinct().ToList();

            // Use Func<Task> overload and capture result via closure to avoid
            // double-Task wrapping with InvokeAsync<TResult>(Func<TResult>)
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                action = await OnExistingConfigFound(modId, displayNames!);
            }).ConfigureAwait(false);
        }

        if (action == ConfigAction.Keep)
        {
            foreach (var name in configFiles)
            {
                var filePath = Path.Combine(existingDir, name);
                if (File.Exists(filePath))
                    preserved[name] = File.ReadAllBytes(filePath);
            }
        }

        return preserved;
    }

    private static void RestoreConfigs(Dictionary<string, byte[]> preserved, string targetDir)
    {
        foreach (var (name, data) in preserved)
        {
            var configPath = Path.Combine(targetDir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            if (!File.Exists(configPath))
                File.WriteAllBytes(configPath, data);
        }
    }

    private static ManifestInfo? FindManifestInArchive(List<IArchiveEntry> entries)
    {
        foreach (var entry in entries)
        {
            var fileName = Path.GetFileName(entry.Key ?? "");
            if (!fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                using var stream = entry.OpenEntryStream();
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var manifest = JsonSerializer.Deserialize<ModManifest>(json);

                if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Id))
                {
                    var key = NormalizePath(entry.Key ?? "");
                    var prefix = key[..^"manifest.json".Length];
                    return new ManifestInfo { Manifest = manifest, PathPrefix = prefix };
                }
            }
            catch { }
        }
        return null;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string ToKebabCase(string input)
    {
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c) && i > 0 && !char.IsUpper(input[i - 1]))
                result.Append('-');
            result.Append(char.ToLower(c));
        }
        return result.ToString();
    }

    public async Task<InstallResult> InstallFromFolderAsync(string sourceFolder)
    {
        if (_terrariaPath == null)
            return new InstallResult { Error = "Terraria path not configured" };

        var modsDir = Path.Combine(_terrariaPath, "TerrariaModder", "mods");

        try
        {
            // Validate: look for manifest.json
            string? modId = null;
            var manifestPath = Path.Combine(sourceFolder, "manifest.json");

            if (File.Exists(manifestPath))
            {
                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var manifest = JsonSerializer.Deserialize<ModManifest>(json);
                    if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Id))
                        modId = manifest.Id;
                }
                catch { /* invalid JSON, fall through to DLL check */ }
            }

            // Fallback: DLL name → kebab-case
            if (modId == null)
            {
                var dll = Directory.GetFiles(sourceFolder, "*.dll")
                    .Select(Path.GetFileName)
                    .FirstOrDefault(n => !n!.StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase));

                if (dll != null)
                    modId = ToKebabCase(Path.GetFileNameWithoutExtension(dll)!);
            }

            if (modId == null)
                return new InstallResult { Error = "NO_MOD_FOUND" };

            var targetDir = Path.Combine(modsDir, modId);
            var disabledDir = Path.Combine(modsDir, "." + modId);

            // Check if source IS the target (already in the right place)
            var normalizedSource = Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar);
            var normalizedTarget = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar);
            var normalizedDisabled = Path.GetFullPath(disabledDir).TrimEnd(Path.DirectorySeparatorChar);

            if (string.Equals(normalizedSource, normalizedTarget, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedSource, normalizedDisabled, StringComparison.OrdinalIgnoreCase))
            {
                return new InstallResult { Error = "ALREADY_INSTALLED", InstalledModId = modId };
            }

            // Preserve configs from existing install
            var existingDir = Directory.Exists(targetDir) ? targetDir
                : Directory.Exists(disabledDir) ? disabledDir : null;
            var preservedConfigs = await PreserveConfigsAsync(existingDir, modId);

            // Clean up existing
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
            if (Directory.Exists(disabledDir)) Directory.Delete(disabledDir, true);

            // Copy folder contents
            CopyDirectory(sourceFolder, targetDir);

            // Restore configs
            RestoreConfigs(preservedConfigs, targetDir);

            return new InstallResult { Success = true, InstalledModId = modId };
        }
        catch (Exception ex)
        {
            return new InstallResult { Error = ex.Message };
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destDir = Path.Combine(targetDir, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }

    private class ManifestInfo
    {
        public ModManifest Manifest { get; set; } = null!;
        public string PathPrefix { get; set; } = "";
    }
}
