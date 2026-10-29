using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Desktop.Logging;
using GasGuard.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace GasGuard.Desktop.ViewModels;

public sealed partial class SettingsViewModel : PageViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IBackupService _backups;
    private readonly IRetentionService _retention;
    private readonly IDialogService _dialogs;
    private readonly IAppShutdownService _shutdown;
    private readonly IShellService _shell;
    private readonly IAppPathProvider _paths;
    private readonly DiagnosticLogLevelController _logLevel;
    private readonly IGsmModemService _modem;
    private readonly ISmsProcessingCoordinator _coordinator;
    private readonly IApplicationOperationGuard _operationGuard;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(
        ISettingsService settings,
        IBackupService backups,
        IRetentionService retention,
        IDialogService dialogs,
        IAppShutdownService shutdown,
        IShellService shell,
        IAppPathProvider paths,
        DiagnosticLogLevelController logLevel,
        IGsmModemService modem,
        ISmsProcessingCoordinator coordinator,
        IApplicationOperationGuard operationGuard,
        ILogger<SettingsViewModel> logger) : base(logger)
    {
        _settings = settings;
        _backups = backups;
        _retention = retention;
        _dialogs = dialogs;
        _shutdown = shutdown;
        _shell = shell;
        _paths = paths;
        _logLevel = logLevel;
        _modem = modem;
        _coordinator = coordinator;
        _operationGuard = operationGuard;
        _logger = logger;

        LogLevels = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];
        _selectedLogLevel = "Information";
    }

    public override string Title => "Settings & Maintenance";

    public IReadOnlyList<string> LogLevels { get; }
    public ObservableCollection<BackupFileInfo> Backups { get; } = [];

    [ObservableProperty] private string _staleAfterText = "15";
    [ObservableProperty] private string _retentionDaysText = "365";
    [ObservableProperty] private bool _automaticNotifications;
    [ObservableProperty] private string _notificationAttemptsText = "2";
    [ObservableProperty] private string _selectedLogLevel;
    [ObservableProperty] private string _modemSummary = "—";
    [ObservableProperty] private string _lastBackupText = "No verified backup recorded.";
    [ObservableProperty] private bool _databaseHealthy;
    [ObservableProperty] private string _databaseHealthText = "Not checked.";
    [ObservableProperty] private string _databaseSizeText = "—";
    [ObservableProperty] private string _databaseIntegrityText = "—";
    [ObservableProperty] private string _foreignKeysText = "Unknown";
    [ObservableProperty] private string _healthCheckedText = "Never";
    [ObservableProperty] private string _databasePath = string.Empty;
    [ObservableProperty] private string _backupsPath = string.Empty;
    [ObservableProperty] private string _logsPath = string.Empty;
    [ObservableProperty] private string _appVersion = "—";
    [ObservableProperty] private string _protocolVersion = "1";
    [ObservableProperty] private string _supportedEvents = "—";
    [ObservableProperty] private string _supportedLevels = "—";
    [ObservableProperty] private string _timeZoneName = "—";
    [ObservableProperty] private BackupFileInfo? _selectedBackup;
    [ObservableProperty] private bool _hasBackups;
    [ObservableProperty] private bool _hasSelectedBackup;
    [ObservableProperty] private string _backupValidationText = "Select a backup to validate it before restoring.";

    partial void OnSelectedBackupChanged(BackupFileInfo? value)
    {
        HasSelectedBackup = value is not null;
        BackupValidationText = value is null
            ? "Select a backup to validate it before restoring."
            : "Not validated in this session.";
    }

    protected override async Task LoadAsync()
    {
        var overview = await _settings.GetOverviewAsync();
        StaleAfterText = overview.Settings.Preferences.StaleAfterMinutes.ToString(CultureInfo.InvariantCulture);
        RetentionDaysText = overview.Settings.Preferences.DataRetentionDays.ToString(CultureInfo.InvariantCulture);
        AutomaticNotifications = overview.Settings.Preferences.AutomaticContactNotifications;
        NotificationAttemptsText = overview.Settings.Preferences.NotificationMaxAttempts.ToString(CultureInfo.InvariantCulture);
        SelectedLogLevel = NormalizeLogLevel(overview.Settings.Preferences.MinimumLogLevel);
        _logLevel.Apply(SelectedLogLevel);

        ModemSummary = overview.Settings.Modem.UseSimulatedModem
            ? $"SIMULATED modem ({overview.Settings.Modem.PollIntervalSeconds}s polling). Change connection settings in the GSM Modem workspace."
            : $"Physical modem on {overview.Settings.Modem.PortName}, {overview.Settings.Modem.BaudRate} baud. Change connection settings in the GSM Modem workspace.";
        LastBackupText = overview.Settings.LastBackupAtUtc.HasValue
            ? $"Last verified backup: {UiFormat.Local(overview.Settings.LastBackupAtUtc)}. {overview.Settings.LastBackupPath}"
            : "No verified backup has been recorded.";
        DatabasePath = overview.DatabasePath;
        BackupsPath = _paths.BackupsDirectory;
        LogsPath = _paths.LogsDirectory;
        AppVersion = overview.AppVersion;
        ProtocolVersion = overview.ProtocolVersion;
        SupportedEvents = string.Join(", ", overview.SupportedEvents);
        SupportedLevels = string.Join(", ", overview.SupportedLevels);
        TimeZoneName = overview.LagosTimeZoneName;

        await RefreshHealthCoreAsync();
        await RefreshBackupsCoreAsync();
        SetError(null);
        SetStatus(null);
    }

    [RelayCommand]
    private async Task SavePreferencesAsync() => await RunOperationAsync("Saving application preferences", async () =>
    {
        if (!TryBuildPreferences(out var input, out var error))
        {
            SetError(error);
            return;
        }

        var result = await _settings.SavePreferencesAsync(input);
        if (!result.Success)
        {
            SetError(FormatFailure(result));
            return;
        }

        _logLevel.Apply(input.MinimumLogLevel);
        await LoadAsync();
        SetError(null);
        SetStatus(result.Message);
    });

    [RelayCommand]
    private async Task RefreshHealthAsync() => await RunOperationAsync("Refreshing database health", async () =>
    {
        await RefreshHealthCoreAsync();
        await RefreshBackupsCoreAsync();
    });

    [RelayCommand]
    private async Task CreateBackupAsync() => await RunOperationAsync("Creating database backup", async () =>
    {
        var result = await _backups.CreateBackupAsync();
        if (!result.Success)
        {
            SetError(result.Message);
            return;
        }

        await RefreshHealthCoreAsync();
        await RefreshBackupsCoreAsync();
        var overview = await _settings.GetOverviewAsync();
        LastBackupText = overview.Settings.LastBackupAtUtc.HasValue
            ? $"Last verified backup: {UiFormat.Local(overview.Settings.LastBackupAtUtc)}. {overview.Settings.LastBackupPath}"
            : "No verified backup has been recorded.";
        SetError(null);
        SetStatus(result.Message);
    });

    [RelayCommand]
    private async Task ValidateSelectedBackupAsync() => await RunOperationAsync("Validating backup", async () =>
    {
        if (SelectedBackup is null)
        {
            SetError("Select a backup file first.");
            return;
        }

        var validation = await _backups.ValidateBackupAsync(SelectedBackup.Path);
        BackupValidationText = validation.Message;
        if (validation.IsValid)
        {
            SetError(null);
            SetStatus("The selected backup passed validation.");
        }
        else
        {
            SetError(validation.Message);
        }
    });

    [RelayCommand]
    private async Task RestoreSelectedAsync()
    {
        if (SelectedBackup is null)
        {
            SetError("Select a backup file first.");
            return;
        }

        await RestoreAsync(SelectedBackup.Path);
    }

    [RelayCommand]
    private async Task RestoreFromFileAsync()
    {
        var path = _dialogs.ChooseOpenFile("Select GasGuard database backup", "SQLite database (*.db)|*.db|All files (*.*)|*.*");
        if (path is not null)
        {
            await RestoreAsync(path);
        }
    }

    [RelayCommand]
    private async Task RunRetentionAsync() => await RunOperationAsync("Running retention cleanup", async () =>
    {
        if (!TryParseRetention(out _))
        {
            SetError("The retention period is invalid. Enter 0 to disable or 7-3650 days.");
            return;
        }

        if (!_dialogs.Confirm(
                "Run retention cleanup",
                "Delete communication logs, non-incident audit rows and unlinked SMS records older than the configured retention period? Incidents, linked SMS evidence, incident audit history and detector records are preserved.",
                "Run cleanup",
                destructive: true))
        {
            return;
        }

        var result = await _retention.ApplyRetentionAsync();
        if (result.Success)
        {
            SetError(null);
            SetStatus(result.Message);
        }
        else
        {
            SetError(result.Message);
        }
    });

    [RelayCommand]
    private void OpenDataFolder() => _shell.OpenFolder(Path.GetDirectoryName(DatabasePath) ?? ".");

    [RelayCommand]
    private void OpenBackupsFolder() => _shell.OpenFolder(BackupsPath);

    [RelayCommand]
    private void OpenLogsFolder() => _shell.OpenFolder(LogsPath);

    private async Task RestoreAsync(string path)
    {
        if (string.Equals(
                Path.GetFullPath(path),
                Path.GetFullPath(DatabasePath),
                StringComparison.OrdinalIgnoreCase))
        {
            SetError("Restore blocked: the live database cannot be selected as its own restore source.");
            return;
        }

        var validation = await _backups.ValidateBackupAsync(path);
        if (!validation.IsValid)
        {
            SetError($"Restore blocked: {validation.Message}");
            return;
        }

        if (!_dialogs.Confirm(
                "Restore database",
                "Restoring replaces the live database with the selected verified backup. A pre-restore safety backup will be created first. GasGuard Desktop must restart immediately afterwards.",
                "Restore and restart",
                destructive: true))
        {
            return;
        }

        await RunOperationAsync("Restoring database", async () =>
        {
            var snapshot = await _settings.GetSnapshotAsync();
            var wasConnected = _modem.IsConnected;
            var paused = false;
            var restored = false;
            RestoreResult? result = null;
            var operation = _operationGuard.Begin("database restore");

            try
            {
                if (wasConnected)
                {
                    var disconnect = await _modem.DisconnectAsync();
                    if (!disconnect.Success)
                    {
                        SetError($"Restore was not attempted: {disconnect.Message}");
                        return;
                    }
                }

                using var quiesceTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await _coordinator.WaitForIngressDrainAsync(quiesceTimeout.Token);
                await _coordinator.PauseAsync(quiesceTimeout.Token);
                paused = true;

                result = await _backups.RestoreBackupAsync(path);
                if (!result.Success)
                {
                    SetError(result.Message);
                    return;
                }

                restored = true;
                BackupValidationText = result.Message;
            }
            finally
            {
                if (!restored)
                {
                    if (paused)
                    {
                        try
                        {
                            await _coordinator.ResumeAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Could not resume SMS processing after a failed restore.");
                        }
                    }

                    if (wasConnected)
                    {
                        try
                        {
                            await _modem.ConnectAsync(ModemOptionsFactory.Create(snapshot.Modem));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Could not reconnect the modem after a failed restore.");
                        }
                    }

                    operation.Dispose();
                }
            }

            operation.Dispose();
            _dialogs.ShowInfo(
                "Restore complete",
                $"The database was restored and integrity-checked. A safety backup was written to:\n{result?.PreRestoreBackupPath}\n\nGasGuard Desktop will now close. Start it again to use the restored database.");
            _shutdown.Shutdown();
        });
    }

    private async Task RefreshHealthCoreAsync()
    {
        var health = await _backups.GetDatabaseHealthAsync();
        DatabaseHealthy = health.IsHealthy;
        DatabaseHealthText = health.Message;
        DatabaseSizeText = FormatSize(health.SizeBytes);
        DatabaseIntegrityText = health.IntegrityCheck ?? "Not available";
        ForeignKeysText = health.ForeignKeysEnabled ? "Enabled" : "NOT ENABLED";
        HealthCheckedText = UiFormat.Local(health.CheckedAtUtc);
        DatabasePath = health.DatabasePath;
    }

    private async Task RefreshBackupsCoreAsync()
    {
        var backups = await _backups.ListBackupsAsync();
        var selectedPath = SelectedBackup?.Path;
        Backups.Clear();
        foreach (var backup in backups)
        {
            Backups.Add(backup);
        }

        HasBackups = Backups.Count > 0;
        SelectedBackup = Backups.FirstOrDefault(x => x.Path == selectedPath) ?? Backups.FirstOrDefault();
    }

    private bool TryBuildPreferences(out PreferencesInput input, out string? error)
    {
        input = null!;
        error = null;
        if (!int.TryParse(StaleAfterText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stale) ||
            !int.TryParse(NotificationAttemptsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var attempts) ||
            !TryParseRetention(out var retention))
        {
            error = "Stale threshold, notification attempts and retention days must be whole numbers.";
            return false;
        }

        input = new PreferencesInput(
            stale,
            retention,
            AutomaticNotifications,
            attempts,
            NormalizeLogLevel(SelectedLogLevel));
        return true;
    }

    private bool TryParseRetention(out int days) =>
        int.TryParse(RetentionDaysText, NumberStyles.Integer, CultureInfo.InvariantCulture, out days);

    private string NormalizeLogLevel(string? level) =>
        LogLevels.FirstOrDefault(x => x.Equals(level, StringComparison.OrdinalIgnoreCase)) ?? "Information";

    private static string FormatFailure(OperationResult result)
    {
        if (result.ValidationErrors is null || result.ValidationErrors.Count == 0)
        {
            return result.Message;
        }

        return $"{result.Message} {string.Join(" ", result.ValidationErrors)}";
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.#} {units[index]}";
    }
}
