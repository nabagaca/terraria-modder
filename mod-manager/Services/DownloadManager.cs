using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using Avalonia.Threading;
using MsBox.Avalonia.Enums;
using TerrariaModManager.ViewModels;

namespace TerrariaModManager.Services;

public class DownloadManager : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly NexusApiService _nexusApi;
    private readonly ModInstallService _installer;
    private readonly string _downloadDir;

    public ObservableCollection<DownloadItem> Downloads { get; } = new();

    public event Action<DownloadItem>? DownloadCompleted;
    public event Action<DownloadItem, string>? DownloadFailed;

    public DownloadManager(NexusApiService nexusApi, ModInstallService installer)
    {
        _nexusApi = nexusApi;
        _installer = installer;
        _downloadDir = SettingsService.DownloadsDir;
        Directory.CreateDirectory(_downloadDir);

        try
        {
            foreach (var file in Directory.GetFiles(_downloadDir))
                try { File.Delete(file); } catch { }
        }
        catch { }
    }

    public async Task EnqueueAsync(int modId, int fileId, string? key = null, long? expires = null, bool forceKeepSettings = false)
    {
        var item = new DownloadItem
        {
            ModId = modId,
            FileId = fileId,
            Name = $"Mod {modId} (file {fileId})",
            Status = "Fetching info..."
        };

        await SafeDispatch(() => Downloads.Insert(0, item));

        try
        {
            var modInfo = await _nexusApi.GetModInfoAsync(modId);
            if (modInfo != null)
                await SafeDispatch(() => item.Name = modInfo.Name);

            await SafeDispatch(() => item.Status = "Getting download link...");
            var links = await _nexusApi.GetDownloadLinksAsync(modId, fileId, key, expires);
            if (links.Count == 0)
            {
                await SafeDispatch(() => item.Status = "Failed: No download links");
                DownloadFailed?.Invoke(item, "No download links returned from Nexus API");
                return;
            }

            var downloadUrl = links[0].Uri;

            var files = await _nexusApi.GetModFilesAsync(modId);
            var fileInfo = files.FirstOrDefault(f => f.FileId == fileId);
            var fileName = fileInfo?.FileName ?? $"mod_{modId}_{fileId}.zip";

            await SafeDispatch(() => item.Status = "Downloading...");
            var filePath = Path.Combine(_downloadDir, $"{Guid.NewGuid():N}_{fileName}");

            using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
            Logger.Info($"Download {modId}/{fileId}: content-type={contentType}, url={downloadUrl}");

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await SafeDispatch(() => item.TotalBytes = totalBytes);

            {
                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long downloaded = 0;
                int bytesRead;
                var lastProgressUpdate = DateTime.UtcNow;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    downloaded += bytesRead;

                    // Throttle UI updates to every 100ms to avoid flooding the dispatcher
                    if ((DateTime.UtcNow - lastProgressUpdate).TotalMilliseconds >= 100)
                    {
                        var dl = downloaded;
                        await SafeDispatch(() =>
                        {
                            item.DownloadedBytes = dl;
                            if (totalBytes > 0)
                                item.Progress = (double)dl / totalBytes * 100;
                        });
                        lastProgressUpdate = DateTime.UtcNow;
                    }
                }

                // Final progress update
                {
                    var dl = downloaded;
                    await SafeDispatch(() =>
                    {
                        item.DownloadedBytes = dl;
                        if (totalBytes > 0)
                            item.Progress = (double)dl / totalBytes * 100;
                    });
                }
            }

            var fileLen = new FileInfo(filePath).Length;
            if (fileLen < 10)
            {
                try { File.Delete(filePath); } catch { }
                await SafeDispatch(() =>
                    item.Status = "Failed: Downloaded file is empty or corrupt");
                DownloadFailed?.Invoke(item, "Downloaded file is empty or corrupt");
                return;
            }

            var peekSize = (int)Math.Min(512, fileLen);
            var peek = new byte[peekSize];
            using (var check = File.OpenRead(filePath))
                check.Read(peek, 0, peekSize);
            var peekStr = System.Text.Encoding.UTF8.GetString(peek);

            if (peekStr.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
                peekStr.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(filePath); } catch { }
                await SafeDispatch(() =>
                    item.Status = "Failed: Nexus returned an error page instead of the file");
                DownloadFailed?.Invoke(item, "Server returned an HTML error page instead of the archive");
                return;
            }

            await SafeDispatch(() => item.Status = "Installing...");
            Logger.Info($"Download {modId}/{fileId}: file downloaded ({new FileInfo(filePath).Length} bytes), starting install");
            var result = await _installer.InstallModAsync(filePath, forceKeepSettings);

            if (result.Success)
            {
                Logger.Info($"Download {modId}/{fileId}: install succeeded, mod-id='{result.InstalledModId}'");
                if (result.InstalledModId != null)
                {
                    App.UpdateTracker.RecordInstall(result.InstalledModId, modId);

                    // Stamp Nexus file version into manifest so update checker
                    // sees the actual Nexus version (e.g. "1.1.1-hotfix") instead of
                    // whatever the manifest inside the zip said (e.g. "1.1.1")
                    var nexusVersion = fileInfo?.Version;
                    if (!string.IsNullOrWhiteSpace(nexusVersion))
                        _installer.StampManifestVersion(result.InstalledModId, nexusVersion);
                }

                await SafeDispatch(() =>
                {
                    item.Status = "Installed";
                    item.Progress = 100;
                });
                DownloadCompleted?.Invoke(item);

                try { File.Delete(filePath); } catch { }
            }
            else
            {
                string? savedPath = null;
                if (result.DownloadedFilePath != null)
                {
                    try
                    {
                        var userDownloads = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                        var destName = fileName;
                        savedPath = Path.Combine(userDownloads, destName);
                        if (File.Exists(savedPath))
                            savedPath = Path.Combine(userDownloads, $"{Path.GetFileNameWithoutExtension(destName)}_{DateTime.Now:HHmmss}{Path.GetExtension(destName)}");
                        File.Move(filePath, savedPath);
                    }
                    catch
                    {
                        savedPath = filePath;
                    }
                }

                var statusMsg = result.Error ?? "Unknown error";
                Logger.Error($"Download {modId}/{fileId}: install failed — {statusMsg}");
                if (savedPath != null)
                    statusMsg += $" — saved to {Path.GetFileName(savedPath)}";

                await SafeDispatch(() =>
                    item.Status = $"Install failed: {statusMsg}");

                if (savedPath != null)
                {
                    await SafeDispatch(async () =>
                    {
                        await Helpers.DialogHelper.ShowDialog(
                            "Install Failed",
                            $"{result.Error}\n\nThe file was downloaded to:\n{savedPath}\n\n" +
                            "You can try installing it manually or contact the mod author.",
                            ButtonEnum.Ok, Icon.Warning);
                    });
                }

                DownloadFailed?.Invoke(item, result.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            await SafeDispatch(() =>
                item.Status = $"Error: {ex.Message}");
            DownloadFailed?.Invoke(item, ex.Message);
        }
    }

    private static async Task SafeDispatch(Action action)
    {
        try { await Dispatcher.UIThread.InvokeAsync(action); }
        catch (InvalidOperationException) { /* dispatcher shut down */ }
    }

    private static async Task SafeDispatch(Func<Task> action)
    {
        try { await Dispatcher.UIThread.InvokeAsync(action); }
        catch (InvalidOperationException) { /* dispatcher shut down */ }
    }

    public void Dispose() => _http.Dispose();
}
