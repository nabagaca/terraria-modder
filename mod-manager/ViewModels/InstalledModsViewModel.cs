using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using TerrariaModManager.Helpers;
using TerrariaModManager.Models;

namespace TerrariaModManager.ViewModels;

public class InstalledModsViewModel : ViewModelBase
{
    private bool _isCheckingUpdates;
    private int _updatesAvailable;
    private int _enabledCount;
    private int _disabledCount;
    private List<InstalledMod> _allMods = new();

    public ObservableCollection<InstalledMod> Mods { get; } = new();
    public ObservableCollection<InstalledMod> UpdateMods { get; } = new();
    public List<InstalledMod> SelectedMods { get; } = new();

    public bool HasSelection => SelectedMods.Count > 0;
    public bool HasSingleSelection => SelectedMods.Count == 1;
    public bool AnySelectedHasSettings => SelectedMods.Any(m => m.HasConfigFiles);
    public bool AnySelectedHasUpdate => SelectedMods.Any(m => m.HasUpdate);

    public void UpdateSelection(IList<object> selectedItems)
    {
        SelectedMods.Clear();
        foreach (var item in selectedItems)
        {
            if (item is InstalledMod mod)
                SelectedMods.Add(mod);
        }
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(AnySelectedHasSettings));
        OnPropertyChanged(nameof(AnySelectedHasUpdate));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    public string SelectionSummary => SelectedMods.Count switch
    {
        0 => "",
        1 => SelectedMods[0].Name,
        _ => $"{SelectedMods.Count} mods selected"
    };

    public bool IsCheckingUpdates
    {
        get => _isCheckingUpdates;
        set => SetProperty(ref _isCheckingUpdates, value);
    }

    public int UpdatesAvailable
    {
        get => _updatesAvailable;
        set
        {
            SetProperty(ref _updatesAvailable, value);
            OnPropertyChanged(nameof(HasUpdates));
        }
    }

    public bool HasUpdates => _updatesAvailable > 0;

    public int EnabledCount
    {
        get => _enabledCount;
        set
        {
            SetProperty(ref _enabledCount, value);
            OnPropertyChanged(nameof(EnabledHeader));
        }
    }

    public int DisabledCount
    {
        get => _disabledCount;
        set
        {
            SetProperty(ref _disabledCount, value);
            OnPropertyChanged(nameof(DisabledHeader));
            OnPropertyChanged(nameof(HasDisabled));
        }
    }

    public string EnabledHeader => $"Enabled ({_enabledCount})";
    public string DisabledHeader => $"Disabled ({_disabledCount})";
    public bool HasDisabled => _disabledCount > 0;


    public ICommand ToggleEnabledCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand UpdateModCommand { get; }
    public ICommand UpdateAllCommand { get; }
    public ICommand OpenModFolderCommand { get; }
    public ICommand OpenOnNexusCommand { get; }
    public ICommand UninstallKeepSettingsCommand { get; }
    public ICommand UninstallCleanCommand { get; }
    public ICommand InstallLocalCommand { get; }
    public ICommand UpdateSingleCommand { get; }

    public InstalledModsViewModel()
    {
        ToggleEnabledCommand = new AsyncRelayCommand(ToggleEnabled);
        UninstallCommand = new AsyncRelayCommand(() => Uninstall(deleteSettings: false));
        UninstallKeepSettingsCommand = new AsyncRelayCommand(() => Uninstall(deleteSettings: false));
        UninstallCleanCommand = new AsyncRelayCommand(() => Uninstall(deleteSettings: true));
        RefreshCommand = new RelayCommand(Refresh);
        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdates);
        UpdateModCommand = new AsyncRelayCommand(UpdateSelectedMods);
        UpdateAllCommand = new AsyncRelayCommand(UpdateAll);
        OpenModFolderCommand = new RelayCommand(OpenModFolder);
        OpenOnNexusCommand = new RelayCommand(OpenOnNexus);
        InstallLocalCommand = new AsyncRelayCommand(InstallLocal);
        UpdateSingleCommand = new RelayCommand<InstalledMod>(mod => { if (mod != null) _ = DownloadUpdate(mod); });
    }

    public void Refresh()
    {
        var path = App.AppSettings.TerrariaPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        var mods = App.ModState.ScanInstalledMods(path);
        foreach (var mod in mods)
            mod.NexusModId = App.UpdateTracker.GetNexusModId(mod);

        _allMods = mods
            .OrderByDescending(m => m.IsEnabled)
            .ThenBy(m => m.Name)
            .ToList();

        ApplyFilter();

        SelectedMods.Clear();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(AnySelectedHasSettings));
        OnPropertyChanged(nameof(AnySelectedHasUpdate));

        // Auto-check for updates in the background
        if (App.NexusApi.HasApiKey && !_isCheckingUpdates)
            _ = CheckUpdates();
    }

    private void ApplyFilter()
    {
        // Populate updates section
        UpdateMods.Clear();
        foreach (var mod in _allMods.Where(m => m.HasUpdate))
            UpdateMods.Add(mod);

        // Main list excludes mods that are in the updates section
        IEnumerable<InstalledMod> source = _allMods.Where(m => !m.HasUpdate);

        var filtered = source.ToList();

        // Mark the first disabled mod for the section divider
        bool seenDisabled = false;
        foreach (var mod in filtered)
        {
            mod.IsFirstDisabled = false;
            if (!mod.IsEnabled && !seenDisabled)
            {
                mod.IsFirstDisabled = true;
                seenDisabled = true;
            }
        }

        Mods.Clear();
        foreach (var mod in filtered)
            Mods.Add(mod);

        EnabledCount = _allMods.Count(m => m.IsEnabled);
        DisabledCount = _allMods.Count(m => !m.IsEnabled);
    }

    private async Task CheckUpdates()
    {
        if (!App.NexusApi.HasApiKey) return;

        IsCheckingUpdates = true;
        try
        {
            var count = await App.UpdateTracker.CheckForUpdatesAsync(_allMods, App.NexusApi);
            UpdatesAvailable = count;
            ApplyFilter();
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    private async Task UpdateSelectedMods()
    {
        var toUpdate = SelectedMods.Where(m => m.HasUpdate).ToList();
        foreach (var mod in toUpdate)
            await DownloadUpdate(mod);
    }

    private async Task UpdateAll()
    {
        var modsToUpdate = Mods.Where(m => m.HasUpdate).ToList();
        foreach (var mod in modsToUpdate)
            await DownloadUpdate(mod);
    }

    private async Task DownloadUpdate(InstalledMod mod)
    {
        if (mod.NexusModId <= 0 || mod.LatestFileId <= 0) return;

        if (App.NexusApi.IsPremium)
        {
            await App.Downloads.EnqueueAsync(mod.NexusModId, mod.LatestFileId);
        }
        else
        {
            var url = $"https://www.nexusmods.com/terraria/mods/{mod.NexusModId}?tab=files";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private async Task ToggleEnabled()
    {
        if (SelectedMods.Count == 0) return;
        var path = App.AppSettings.TerrariaPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        var mods = SelectedMods.ToList();

        var coreEnabled = mods.FirstOrDefault(m => m.IsCore && m.IsEnabled);
        if (coreEnabled != null)
        {
            var result = await DialogHelper.ShowDialog(
                "Disable Core Framework?",
                "WARNING: Disabling TerrariaModder Core will break ALL mods!\n\n" +
                "No mods will load until Core is re-enabled. Are you absolutely sure?",
                ButtonEnum.YesNo, Icon.Error);
            if (result != ButtonResult.Yes) return;
        }

        foreach (var mod in mods)
        {
            if (mod.IsEnabled)
                App.ModState.DisableMod(mod.Id, path);
            else
                App.ModState.EnableMod(mod.Id, path);
        }

        Refresh();
    }

    private void OpenModFolder()
    {
        if (SelectedMods.Count != 1) return;
        var mod = SelectedMods[0];
        if (mod.FolderPath == null) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = mod.FolderPath,
            UseShellExecute = true
        });
    }

    private void OpenOnNexus()
    {
        if (SelectedMods.Count != 1) return;
        var mod = SelectedMods[0];
        if (mod.NexusModId <= 0) return;
        var url = $"https://www.nexusmods.com/terraria/mods/{mod.NexusModId}";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async Task Uninstall(bool deleteSettings)
    {
        if (SelectedMods.Count == 0) return;
        var path = App.AppSettings.TerrariaPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        var mods = SelectedMods.ToList();

        if (mods.Any(m => m.IsCore))
        {
            var result = await DialogHelper.ShowDialog(
                "Uninstall Core Framework?",
                "DANGER: Uninstalling TerrariaModder Core will completely remove the modding framework!\n\n" +
                "ALL mods will stop working. You will need to reinstall Core to use any mods again.\n\n" +
                "Are you absolutely sure you want to do this?",
                ButtonEnum.YesNo, Icon.Error);
            if (result != ButtonResult.Yes) return;
        }

        var anyHasConfig = mods.Any(m => m.HasConfigFiles && !m.IsCore);

        if (!anyHasConfig)
        {
            var names = mods.Count == 1 ? mods[0].Name : $"{mods.Count} mods";
            var result = await DialogHelper.ShowDialog(
                "Uninstall", $"Uninstall {names}?",
                ButtonEnum.YesNo, Icon.Question);
            if (result != ButtonResult.Yes) return;
            deleteSettings = true;
        }
        else if (deleteSettings)
        {
            var names = mods.Count == 1 ? mods[0].Name : $"{mods.Count} mods";
            var result = await DialogHelper.ShowDialog(
                "Clean Uninstall",
                $"Uninstall {names} and delete all settings?\n\n" +
                "This removes the mod(s) AND their configuration (preferences, keybinds, etc.).\n" +
                "A fresh install will start with default settings.",
                ButtonEnum.YesNo, Icon.Warning);
            if (result != ButtonResult.Yes) return;
        }
        else
        {
            var names = mods.Count == 1 ? mods[0].Name : $"{mods.Count} mods";
            var result = await DialogHelper.ShowDialog(
                "Uninstall (Keep Settings)",
                $"Uninstall {names}?\n\n" +
                "Settings and preferences will be kept.\n" +
                "If you reinstall later, old settings will be restored.",
                ButtonEnum.YesNo, Icon.Question);
            if (result != ButtonResult.Yes) return;
        }

        foreach (var mod in mods)
            App.ModState.UninstallMod(mod.Id, path, deleteSettings);

        Refresh();
    }

    private async Task InstallLocal()
    {
        var path = App.AppSettings.TerrariaPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            await DialogHelper.ShowDialog("Error",
                "Set your Terraria path in Settings first.",
                ButtonEnum.Ok, Icon.Warning);
            return;
        }

        // Ask user: zip file or mod folder?
        var choiceBox = MessageBoxManager.GetMessageBoxCustom(
            new MsBox.Avalonia.Dto.MessageBoxCustomParams
            {
                ContentTitle = "Install Local Mod",
                ContentMessage = "Select the mod source:",
                Icon = Icon.Question,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ButtonDefinitions = new[]
                {
                    new MsBox.Avalonia.Models.ButtonDefinition { Name = "Zip File", IsDefault = true },
                    new MsBox.Avalonia.Models.ButtonDefinition { Name = "Mod Folder" },
                    new MsBox.Avalonia.Models.ButtonDefinition { Name = "Cancel", IsCancel = true }
                }
            });

        var choice = App.MainWindow != null
            ? await choiceBox.ShowWindowDialogAsync(App.MainWindow)
            : await choiceBox.ShowAsync();

        if (choice == "Cancel" || string.IsNullOrEmpty(choice)) return;

        var topLevel = GetTopLevel();
        if (topLevel == null) return;

        if (choice == "Zip File")
            await InstallFromZip(topLevel);
        else
            await InstallFromFolder(topLevel);
    }

    private async Task InstallFromZip(TopLevel topLevel)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Mod Archive",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Mod Archives") { Patterns = new[] { "*.zip" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        });

        if (files.Count == 0) return;

        var filePath = files[0].Path.LocalPath;
        var result = await App.Installer.InstallModAsync(filePath);

        if (result.Success)
        {
            Refresh();
            await DialogHelper.ShowDialog("Success",
                $"Mod '{result.InstalledModId}' installed successfully.",
                ButtonEnum.Ok, Icon.Success);
        }
        else
        {
            await DialogHelper.ShowDialog("Install Failed",
                result.Error ?? "The archive doesn't contain a valid TerrariaModder mod.",
                ButtonEnum.Ok, Icon.Error);
        }
    }

    private async Task InstallFromFolder(TopLevel topLevel)
    {
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Mod Folder",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        var folderPath = folders[0].Path.LocalPath;
        var result = await App.Installer.InstallFromFolderAsync(folderPath);

        if (result.Success)
        {
            Refresh();
            await DialogHelper.ShowDialog("Success",
                $"Mod '{result.InstalledModId}' installed successfully.",
                ButtonEnum.Ok, Icon.Success);
        }
        else if (result.Error == "ALREADY_INSTALLED")
        {
            await DialogHelper.ShowDialog("Already Installed",
                $"This folder is already the install location for '{result.InstalledModId}'.",
                ButtonEnum.Ok, Icon.Info);
        }
        else if (result.Error == "NO_MOD_FOUND")
        {
            await DialogHelper.ShowDialog("No Mod Found",
                "The selected folder doesn't contain a valid TerrariaModder mod.\n\n" +
                "A mod folder needs a manifest.json or a .dll file.",
                ButtonEnum.Ok, Icon.Error);
        }
        else
        {
            await DialogHelper.ShowDialog("Install Failed",
                result.Error ?? "Unknown error",
                ButtonEnum.Ok, Icon.Error);
        }
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
