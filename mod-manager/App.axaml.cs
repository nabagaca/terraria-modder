using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using TerrariaModManager.Services;
using TerrariaModManager.Views;
using TerrariaModManager.ViewModels;

namespace TerrariaModManager;

public partial class App : Application
{
    private SingleInstance? _singleInstance;

    // Shared services — accessed by ViewModels
    public static SettingsService Settings { get; } = new();
    public static TerrariaDetector Detector { get; } = new();
    public static ModStateService ModState { get; } = new();
    public static NexusApiService NexusApi { get; } = new();
    public static NxmLinkHandler NxmHandler { get; } = new();
    public static NxmProtocolRegistrar NxmRegistrar { get; } = new();
    public static ModInstallService Installer { get; } = new();
    public static UpdateTracker UpdateTracker { get; } = new();
    public static DownloadManager Downloads { get; private set; } = null!;

    public static Models.AppSettings AppSettings { get; set; } = null!;
    public static Avalonia.Controls.Window? MainWindow { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Logger.Error("Fatal unhandled exception", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        Logger.Info("TerrariaModder Manager starting");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Single instance check
            _singleInstance = new SingleInstance();
            if (!_singleInstance.TryAcquire())
            {
                var nxmArg = FindNxmArg(desktop.Args ?? []);
                SingleInstance.SendToExisting(nxmArg ?? "ACTIVATE");
                desktop.Shutdown();
                return;
            }

            SettingsService.EnsureDirectories();

            // Load settings
            AppSettings = Settings.Load();

            // Load .env file (dev API key)
            LoadEnvFile();

            // Init services
            if (!string.IsNullOrWhiteSpace(AppSettings.NexusApiKey))
                NexusApi.SetApiKey(AppSettings.NexusApiKey);
            else if (!string.IsNullOrWhiteSpace(EnvApiKey))
                NexusApi.SetApiKey(EnvApiKey);

            if (!string.IsNullOrWhiteSpace(AppSettings.TerrariaPath))
                Installer.SetTerrariaPath(AppSettings.TerrariaPath);

            // Config found callback — invoked on UI thread via Dispatcher.UIThread.InvokeAsync
            Installer.OnExistingConfigFound = async (modId, configFiles) =>
            {
                var fileList = string.Join("\n", configFiles.Select(f => $"  • {f}"));
                var box = MessageBoxManager.GetMessageBoxCustom(
                    new MsBox.Avalonia.Dto.MessageBoxCustomParams
                    {
                        ContentTitle = "Existing Settings Found",
                        ContentMessage = $"The mod '{modId}' has existing configuration files:\n\n{fileList}\n\n" +
                            "Keep your current settings, or delete them for a clean install?",
                        Icon = Icon.Question,
                        WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                        ButtonDefinitions = new[]
                        {
                            new MsBox.Avalonia.Models.ButtonDefinition { Name = "Keep Settings", IsDefault = true },
                            new MsBox.Avalonia.Models.ButtonDefinition { Name = "Clean Install", IsCancel = true }
                        }
                    });
                var result = MainWindow != null
                    ? await box.ShowWindowDialogAsync(MainWindow)
                    : await box.ShowAsync();
                return result == "Keep Settings" ? ConfigAction.Keep : ConfigAction.Delete;
            };

            Downloads = new DownloadManager(NexusApi, Installer);

            // Create and show main window
            var mainWindow = new MainWindow();
            var vm = (MainViewModel)mainWindow.DataContext!;

            // Listen for nxm:// links from other instances
            _singleInstance.MessageReceived += msg =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (msg == "ACTIVATE")
                    {
                        mainWindow.Activate();
                        return;
                    }

                    var link = NxmHandler.Parse(msg);
                    if (link != null && NxmHandler.IsTerrariaLink(link))
                        vm.HandleNxmLink(link);
                });
            };
            _singleInstance.StartListening();

            // Check for nxm:// link in startup args
            var startupNxm = FindNxmArg(desktop.Args ?? []);
            if (startupNxm != null)
            {
                var link = NxmHandler.Parse(startupNxm);
                if (link != null && NxmHandler.IsTerrariaLink(link))
                {
                    mainWindow.Opened += (_, _) => vm.HandleNxmLink(link);
                }
            }

            MainWindow = mainWindow;
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += (_, _) =>
            {
                Settings.Save(AppSettings);
                NexusApi.Dispose();
                Downloads?.Dispose();
                _singleInstance?.Dispose();
            };

            Logger.Info("Main window shown");
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static string? EnvApiKey { get; private set; }

    private static void LoadEnvFile()
    {
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var envPath = Path.Combine(exeDir, ".env");

        if (!File.Exists(envPath))
        {
            var dir = new DirectoryInfo(exeDir);
            while (dir?.Parent != null)
            {
                var candidate = Path.Combine(dir.Parent.FullName, ".env");
                if (File.Exists(candidate))
                {
                    envPath = candidate;
                    break;
                }
                dir = dir.Parent;
            }
        }

        if (!File.Exists(envPath)) return;

        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') || !trimmed.Contains('=')) continue;

            var eq = trimmed.IndexOf('=');
            var key = trimmed[..eq].Trim();
            var val = trimmed[(eq + 1)..].Trim();

            if (key == "NEXUS_API_KEY" && !string.IsNullOrWhiteSpace(val))
                EnvApiKey = val;
        }
    }

    private static string? FindNxmArg(string[] args)
    {
        return args.FirstOrDefault(a => a.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase));
    }
}
