using System.IO;
using System.Text.Json;
using TerrariaModManager.Models;

namespace TerrariaModManager.Services;

/// <summary>
/// Tracks which Nexus mod ID corresponds to each installed mod,
/// and checks for available updates by comparing versions.
/// </summary>
public class UpdateTracker
{
    private const int CoreNexusModId = 135;
    private readonly string _trackingFile;
    private Dictionary<string, int> _entries = new();

    public UpdateTracker()
    {
        _trackingFile = Path.Combine(SettingsService.AppDataDir, "nexus-mod-map.json");
        Load();
    }

    /// <summary>
    /// Record that a local mod ID was installed from a specific Nexus mod.
    /// </summary>
    public void RecordInstall(string modId, int nexusModId)
    {
        _entries[modId] = nexusModId;
        Save();
    }

    /// <summary>
    /// Get the Nexus mod ID for an installed mod.
    /// Checks: manifest nexus_id -> tracking file -> returns 0 if unknown.
    /// </summary>
    public int GetNexusModId(InstalledMod mod)
    {
        if (mod.Manifest?.NexusId > 0)
            return mod.Manifest.NexusId;

        if (_entries.TryGetValue(mod.Id, out var nexusId) && nexusId > 0)
            return nexusId;

        return 0;
    }

    /// <summary>
    /// Reverse lookup: find the local mod ID installed from a given Nexus mod ID.
    /// </summary>
    public string? GetLocalModId(int nexusModId)
    {
        foreach (var (modId, nid) in _entries)
            if (nid == nexusModId) return modId;
        return null;
    }

    /// <summary>
    /// Check for updates on all installed mods that have known Nexus mod IDs.
    /// Returns the number of mods with available updates.
    /// </summary>
    public async Task<int> CheckForUpdatesAsync(List<InstalledMod> mods, NexusApiService nexusApi)
    {
        if (!nexusApi.HasApiKey) return 0;

        int updatesFound = 0;

        foreach (var mod in mods)
        {
            var nexusId = GetNexusModId(mod);
            if (nexusId <= 0) continue;

            mod.NexusModId = nexusId;

            try
            {
                var files = await nexusApi.GetModFilesAsync(nexusId);
                if (files.Count == 0) continue;

                // Find the primary/main file, or fall back to latest uploaded
                var mainFile = files.FirstOrDefault(f => f.IsPrimary)
                    ?? files.OrderByDescending(f => f.UploadedTimestamp).First();

                var hasUpdate = !string.IsNullOrWhiteSpace(mainFile.Version) &&
                    IsNewerVersion(mainFile.Version, mod.Version);
                Logger.Info($"Update check '{mod.Id}': installed v{mod.Version}, latest v{mainFile.Version}, update={hasUpdate}");

                if (hasUpdate)
                {
                    mod.HasUpdate = true;
                    mod.LatestVersion = mainFile.Version;
                    mod.LatestFileId = mainFile.FileId;
                    updatesFound++;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Update check '{mod.Id}' failed: {ex.Message}");
            }
        }

        return updatesFound;
    }

    /// <summary>
    /// Version comparison. Returns true if nexusVersion is newer than localVersion.
    /// Supports -hotfix/-patch/-fix suffixes as incremental versions above the base:
    /// 1.1.1 &lt; 1.1.1-hotfix &lt; 1.1.2
    /// </summary>
    private static bool IsNewerVersion(string nexusVersion, string localVersion)
    {
        var (nBase, nSuffix) = ParseVersion(nexusVersion);
        var (lBase, lSuffix) = ParseVersion(localVersion);

        if (nBase == null || lBase == null)
        {
            // Unparseable — fall back to string comparison on raw input
            var rawN = nexusVersion.TrimStart('v', 'V');
            var rawL = localVersion.TrimStart('v', 'V');
            if (rawN == rawL) return false;
            return string.Compare(rawN, rawL, StringComparison.OrdinalIgnoreCase) > 0;
        }

        if (nBase > lBase) return true;
        if (nBase < lBase) return false;

        // Same base version — suffix breaks the tie
        // no suffix < -hotfix/-patch/-fix
        if (nSuffix && !lSuffix) return true;

        return false;
    }

    /// <summary>
    /// Parses "1.1.1-hotfix" into (Version(1.1.1), true).
    /// Returns (null, false) if the base version can't be parsed.
    /// </summary>
    private static (Version? Base, bool HasIncrementalSuffix) ParseVersion(string version)
    {
        version = version.TrimStart('v', 'V');

        var idx = version.IndexOf('-');
        bool hasSuffix = false;
        string baseStr = version;

        if (idx >= 0)
        {
            var suffix = version[(idx + 1)..].ToLowerInvariant();
            if (suffix.StartsWith("hotfix") || suffix.StartsWith("patch") || suffix.StartsWith("fix"))
                hasSuffix = true;
            baseStr = version[..idx];
        }

        return Version.TryParse(baseStr, out var v) ? (v, hasSuffix) : (null, false);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_trackingFile)) return;
            var json = File.ReadAllText(_trackingFile);
            _entries = JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new();
        }
        catch
        {
            _entries = new();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_trackingFile, json);
        }
        catch { }
    }
}
