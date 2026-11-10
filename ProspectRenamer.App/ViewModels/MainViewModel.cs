using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using ProspectRenamer.Core.Models;
using ProspectRenamer.Core.Services;

namespace ProspectRenamer.App.ViewModels;

public sealed record StatusItem(DateTime Time, string Message, string Detail);

public sealed class MainViewModel : ObservableObject
{
    private readonly ProspectDiscoveryService _discoveryService;
    private readonly SettingsService _settingsService;
    private AppSettings _settings;
    private ProspectService _prospectService;
    private BackupService _backupService;
    private IReadOnlyList<ProspectLocation> _locations = [];
    private ProspectEntry? _selectedProspect;
    private string _locationDisplay = "Scanning local save locations…";
    private string _locationMode = "AUTO DETECT";
    private bool _isBusy;
    private bool _isIcarusRunning;
    private string _busyTitle = "C:\\> SCANNING PROSPECT DATA";
    private string _busyDetail = "Locating local prospect files...";

    private static readonly string[] RenameProgressMessages =
    [
        "C:\\> Verifying prospect structure and destination...",
        "C:\\> Creating checksum-verified safety backups...",
        "C:\\> Migrating local loadout coordinates...",
        "C:\\> Archiving Icarus autosave sidecars...",
        "C:\\> Convincing Steam Cloud that time is linear...",
        "C:\\> Reticulating orbital drop splines...",
        "C:\\> The polar bears have been notified. Almost there..."
    ];

    private static readonly string[] DuplicateProgressMessages =
    [
        "C:\\> Verifying prospect structure and destination...",
        "C:\\> Creating checksum-verified safety backups...",
        "C:\\> Cloning prospect data without disturbing the original...",
        "C:\\> Assigning a fresh orbital identity...",
        "C:\\> Reticulating orbital drop splines...",
        "C:\\> Packing the duplicated drop pod. Almost there..."
    ];

    public MainViewModel()
    {
        _discoveryService = new ProspectDiscoveryService();
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        _backupService = new BackupService(GetBackupRoot(_settings));
        _prospectService = CreateProspectService(_backupService);

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        AutoDetectCommand = new AsyncRelayCommand(UseAutoDetectAsync, () => !IsBusy);
    }

    public ObservableCollection<ProspectEntry> Prospects { get; } = [];
    public ObservableCollection<StatusItem> StatusHistory { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AutoDetectCommand { get; }

    public ProspectEntry? SelectedProspect
    {
        get => _selectedProspect;
        set
        {
            if (SetProperty(ref _selectedProspect, value))
            {
                OnPropertyChanged(nameof(CanRenameSelected));
            }
        }
    }

    public bool CanRenameSelected => SelectedProspect is { IsValid: true } && !IsBusy;
    public bool HasProspects => Prospects.Count > 0;
    public int ValidProspectCount => Prospects.Count(prospect => prospect.IsValid);
    public bool ShowRenameWarning => _settings.ShowRenameWarning;
    public string BackupRoot => _backupService.BackupRoot;
    public string AppVersion { get; } = GetAppVersion();

    public string BusyTitle
    {
        get => _busyTitle;
        private set => SetProperty(ref _busyTitle, value);
    }

    public string BusyDetail
    {
        get => _busyDetail;
        private set => SetProperty(ref _busyDetail, value);
    }

    public string LocationDisplay
    {
        get => _locationDisplay;
        private set => SetProperty(ref _locationDisplay, value);
    }

    public string LocationMode
    {
        get => _locationMode;
        private set => SetProperty(ref _locationMode, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRenameSelected));
                RefreshCommand.RaiseCanExecuteChanged();
                AutoDetectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsIcarusRunning
    {
        get => _isIcarusRunning;
        private set => SetProperty(ref _isIcarusRunning, value);
    }

    public async Task InitializeAsync()
    {
        if (!_settings.UseAutoDetect && !string.IsNullOrWhiteSpace(_settings.CustomFolder))
        {
            var customLocations = _discoveryService.ResolveManualSelection(_settings.CustomFolder);
            if (customLocations.Count > 0)
            {
                _locations = customLocations;
                LocationMode = "CUSTOM LOCATION";
            }
            else
            {
                _settings.UseAutoDetect = true;
                _settingsService.Save(_settings);
                _locations = _discoveryService.FindAutomaticLocations();
                AddStatus("CUSTOM PATH UNAVAILABLE", "Returned to automatic save detection.");
            }
        }
        else
        {
            _locations = _discoveryService.FindAutomaticLocations();
        }

        await RefreshAsync();
    }

    public async Task<bool> UseCustomFolderAsync(string selectedPath)
    {
        var locations = _discoveryService.ResolveManualSelection(selectedPath);
        if (locations.Count == 0)
        {
            return false;
        }

        _settings.UseAutoDetect = false;
        _settings.CustomFolder = selectedPath;
        _settingsService.Save(_settings);
        _locations = locations;
        LocationMode = "CUSTOM LOCATION";
        AddStatus("CUSTOM LOCATION SELECTED", $"{locations.Count} prospect folder(s) resolved.");
        await RefreshAsync();
        return true;
    }

    public async Task UseAutoDetectAsync()
    {
        _settings.UseAutoDetect = true;
        _settingsService.Save(_settings);
        _locations = _discoveryService.FindAutomaticLocations();
        LocationMode = "AUTO DETECT";
        AddStatus("AUTO DETECT ENABLED", "Searching standard Icarus PlayerData accounts.");
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        BusyTitle = "C:\\> SCANNING PROSPECT DATA";
        BusyDetail = "C:\\> Reading save headers and filtering active prospects...";
        IsBusy = true;
        try
        {
            if (_settings.UseAutoDetect)
            {
                _locations = _discoveryService.FindAutomaticLocations();
                LocationMode = "AUTO DETECT";
            }
            else if (!string.IsNullOrWhiteSpace(_settings.CustomFolder))
            {
                _locations = _discoveryService.ResolveManualSelection(_settings.CustomFolder);
            }

            UpdateLocationDisplay();
            var prospects = await Task.Run(() => _prospectService.LoadProspectsAsync(_locations));
            Prospects.Clear();
            foreach (var prospect in prospects)
            {
                Prospects.Add(prospect);
            }

            SelectedProspect = Prospects.FirstOrDefault(prospect => prospect.IsValid);
            UpdateIcarusStatus();
            OnPropertyChanged(nameof(HasProspects));
            OnPropertyChanged(nameof(ValidProspectCount));

            if (_locations.Count == 0)
            {
                AddStatus("NO SAVE PATH FOUND", "Use Browse Folder to select a Prospects or parent directory.");
            }
            else
            {
                AddStatus(
                    $"{ValidProspectCount} PROSPECT{(ValidProspectCount == 1 ? string.Empty : "S")} DETECTED",
                    Prospects.Any(prospect => !prospect.IsValid)
                        ? "Some files could not be parsed; valid saves remain available."
                        : "Save path verified.");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public RenameViewModel CreateRenameViewModel() =>
        new(SelectedProspect ?? throw new InvalidOperationException("No prospect is selected."), _prospectService);

    public async Task<RenameResult> SaveSelectedAsAsync(string newName, ProspectOperationMode mode)
    {
        var selected = SelectedProspect ?? throw new InvalidOperationException("No prospect is selected.");
        BusyTitle = mode == ProspectOperationMode.Rename
            ? "C:\\> SAFE RENAME TRANSACTION IN PROGRESS"
            : "C:\\> SAFE DUPLICATE TRANSACTION IN PROGRESS";
        BusyDetail = mode == ProspectOperationMode.Rename
            ? RenameProgressMessages[0]
            : DuplicateProgressMessages[0];
        IsBusy = true;
        using var animationCancellation = new CancellationTokenSource();
        var animation = AnimateBusyDetailAsync(mode, animationCancellation.Token);
        try
        {
            var result = await Task.Run(() => _prospectService.SaveAsAsync(selected, newName, mode));
            if (result.IsSuccess)
            {
                AddStatus(
                    mode == ProspectOperationMode.Rename ? "PROSPECT RENAMED" : "PROSPECT DUPLICATED",
                    $"{result.OldName} → {result.NewName}");
                AddStatus("BACKUP CREATED", result.BackupPath ?? BackupRoot);
                if (result.MigratedLoadoutReferences > 0)
                {
                    AddStatus(
                        "LOCAL LOADOUT MIGRATED",
                        $"{result.MigratedLoadoutReferences} reference(s) updated; backup: {result.LoadoutBackupPath}");
                }
                if (result.ArchivedGameBackups > 0)
                {
                    AddStatus(
                        "GAME BACKUPS ARCHIVED",
                        $"{result.ArchivedGameBackups} old autosave(s) moved out of the live folder.");
                }
            }
            else
            {
                AddStatus(
                    mode == ProspectOperationMode.Rename ? "RENAME FAILED" : "DUPLICATE FAILED",
                    result.Error ?? "The original save was preserved.");
                if (!string.IsNullOrWhiteSpace(result.LoadoutBackupPath))
                {
                    AddStatus("LOADOUT BACKUP AVAILABLE", result.LoadoutBackupPath);
                }
                if (!string.IsNullOrWhiteSpace(result.GameBackupArchivePath))
                {
                    AddStatus("GAME BACKUP ARCHIVE AVAILABLE", result.GameBackupArchivePath);
                }
            }

            await RefreshAfterRenameAsync();
            return result;
        }
        finally
        {
            animationCancellation.Cancel();
            try
            {
                await animation;
            }
            catch (OperationCanceledException)
            {
                // Expected when the file transaction completes.
            }

            IsBusy = false;
        }
    }

    public void UpdateIcarusStatus() => IsIcarusRunning = DetectIcarusProcess();

    public AppSettings GetSettingsCopy() => new()
    {
        UseAutoDetect = _settings.UseAutoDetect,
        CustomFolder = _settings.CustomFolder,
        ShowRenameWarning = _settings.ShowRenameWarning,
        BackupFolder = _settings.BackupFolder
    };

    public void SaveSettings(AppSettings updatedSettings)
    {
        _settings.ShowRenameWarning = updatedSettings.ShowRenameWarning;
        _settings.BackupFolder = string.IsNullOrWhiteSpace(updatedSettings.BackupFolder)
            ? null
            : Path.GetFullPath(updatedSettings.BackupFolder.Trim());
        _settingsService.Save(_settings);
        _backupService = new BackupService(GetBackupRoot(_settings));
        _prospectService = CreateProspectService(_backupService);
        OnPropertyChanged(nameof(ShowRenameWarning));
        OnPropertyChanged(nameof(BackupRoot));
        AddStatus("SETTINGS SAVED", "Local preferences updated.");
    }

    public void SuppressRenameWarning()
    {
        _settings.ShowRenameWarning = false;
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(ShowRenameWarning));
    }

    private async Task RefreshAfterRenameAsync()
    {
        var prospects = await _prospectService.LoadProspectsAsync(_locations);
        Prospects.Clear();
        foreach (var prospect in prospects)
        {
            Prospects.Add(prospect);
        }

        SelectedProspect = Prospects.FirstOrDefault();
        OnPropertyChanged(nameof(HasProspects));
        OnPropertyChanged(nameof(ValidProspectCount));
    }

    private void UpdateLocationDisplay()
    {
        LocationDisplay = _locations.Count switch
        {
            0 => "No prospect directory detected",
            1 => _locations[0].DirectoryPath,
            _ => string.Join(Environment.NewLine, _locations.Select(location => $"{location.AccountId}  ·  {location.DirectoryPath}"))
        };
    }

    private void AddStatus(string message, string detail)
    {
        StatusHistory.Insert(0, new StatusItem(DateTime.Now, message, detail));
        while (StatusHistory.Count > 4)
        {
            StatusHistory.RemoveAt(StatusHistory.Count - 1);
        }
    }

    private async Task AnimateBusyDetailAsync(ProspectOperationMode mode, CancellationToken cancellationToken)
    {
        var messages = mode == ProspectOperationMode.Rename
            ? RenameProgressMessages
            : DuplicateProgressMessages;

        var index = 0;
        while (true)
        {
            BusyDetail = messages[index % messages.Length];
            index++;
            await Task.Delay(TimeSpan.FromSeconds(2.4), cancellationToken);
        }
    }

    private static bool DetectIcarusProcess()
    {
        try
        {
            var processes = Process.GetProcesses();
            try
            {
                return processes.Any(process =>
                    string.Equals(process.ProcessName, "Icarus", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(process.ProcessName, "Icarus-Win64-Shipping", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            return false;
        }
    }

    private static string GetAppVersion()
    {
        var version = typeof(MainViewModel).Assembly.GetName().Version;
        return version is null
            ? "VERSION UNKNOWN"
            : $"VERSION {version.Major}.{version.Minor}.{version.Build}";
    }

    private static string GetBackupRoot(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.BackupFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProspectRenamer", "Backups")
            : settings.BackupFolder;

    private static ProspectService CreateProspectService(BackupService backupService) =>
        new(new IcarusProspectCodec(), backupService, new NameValidationService());
}
