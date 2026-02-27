using System.Windows.Input;
using Avalonia.Threading;
using TerrariaModManager.Helpers;
using TerrariaModManager.Services;

namespace TerrariaModManager.ViewModels;

public class MainViewModel : ViewModelBase
{
    private ViewModelBase _currentView;
    private string _statusText = "Ready";
    private bool _needsSetup;

    public InstalledModsViewModel InstalledModsVm { get; }
    public BrowseViewModel BrowseVm { get; }
    public DownloadsViewModel DownloadsVm { get; }
    public SettingsViewModel SettingsVm { get; }

    public ViewModelBase CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool NeedsSetup
    {
        get => _needsSetup;
        set => SetProperty(ref _needsSetup, value);
    }

    public ICommand ShowInstalledCommand { get; }
    public ICommand ShowBrowseCommand { get; }
    public ICommand ShowDownloadsCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand LaunchModdedCommand { get; }
    public ICommand LaunchVanillaCommand { get; }

    public MainViewModel()
    {
        InstalledModsVm = new InstalledModsViewModel();
        BrowseVm = new BrowseViewModel();
        DownloadsVm = new DownloadsViewModel();
        SettingsVm = new SettingsViewModel();

        var settings = App.AppSettings;
        NeedsSetup = string.IsNullOrWhiteSpace(settings.TerrariaPath);

        _currentView = NeedsSetup ? SettingsVm : InstalledModsVm;

        ShowInstalledCommand = new RelayCommand(() =>
        {
            CurrentView = InstalledModsVm;
            InstalledModsVm.Refresh();
        });
        ShowBrowseCommand = new RelayCommand(() =>
        {
            CurrentView = BrowseVm;
            if (BrowseVm.Mods.Count == 0)
                _ = BrowseVm.LoadFeedAsync();
            else
                BrowseVm.RefreshInstallStates();
        });
        ShowDownloadsCommand = new RelayCommand(() => CurrentView = DownloadsVm);
        ShowSettingsCommand = new RelayCommand(() => CurrentView = SettingsVm);
        LaunchModdedCommand = new RelayCommand(() => LaunchGame(modded: true));
        LaunchVanillaCommand = new RelayCommand(() => LaunchGame(modded: false));

        if (!NeedsSetup)
        {
            InstalledModsVm.Refresh();
            SettingsVm.LoadFromSettings();
            UpdateStatus();
        }
        else
        {
            StatusText = "Welcome! Set your Terraria path to get started.";
            SettingsVm.LoadFromSettings();
        }

        if (App.Downloads != null)
        {
            App.Downloads.DownloadCompleted += _ =>
            {
                try
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        InstalledModsVm.Refresh();
                        BrowseVm.RefreshInstallStates();
                        if (BrowseVm.HideInstalled)
                            BrowseVm.ApplyCurrentSort();
                        UpdateStatus();
                    });
                }
                catch (InvalidOperationException) { /* dispatcher shut down */ }
            };
        }
    }

    public void HandleNxmLink(NxmLink link)
    {
        CurrentView = DownloadsVm;
        _ = App.Downloads.EnqueueAsync(link.ModId, link.FileId, link.Key, link.Expires);
        StatusText = $"Downloading mod {link.ModId}...";
    }

    private void LaunchGame(bool modded)
    {
        var path = App.AppSettings.TerrariaPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "Set your Terraria path in Settings first";
            return;
        }

        var injector = System.IO.Path.Combine(path, "TerrariaInjector.exe");
        var terraria = System.IO.Path.Combine(path, "Terraria.exe");

        string exe;
        string label;
        if (modded)
        {
            exe = System.IO.File.Exists(injector) ? injector : terraria;
            label = System.IO.File.Exists(injector) ? "modded" : "vanilla (injector not found)";
        }
        else
        {
            exe = terraria;
            label = "vanilla";
        }

        if (!System.IO.File.Exists(exe))
        {
            StatusText = "Could not find Terraria executable";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = path,
                UseShellExecute = true
            });
            StatusText = $"Launching Terraria ({label})...";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to launch: {ex.Message}";
        }
    }

    private void UpdateStatus()
    {
        var path = App.AppSettings.TerrariaPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "No Terraria path configured";
            return;
        }

        var coreInfo = App.ModState.GetCoreInfo(path);
        var modCount = InstalledModsVm.Mods.Count;

        if (coreInfo.IsInstalled)
            StatusText = $"Core v{coreInfo.CoreVersion} | {modCount} mod(s) installed";
        else
            StatusText = $"TerrariaModder Core not installed | {modCount} mod(s) found";
    }
}
