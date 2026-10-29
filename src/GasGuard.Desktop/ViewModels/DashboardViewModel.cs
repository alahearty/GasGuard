using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Contracts.Modem;
using GasGuard.Domain;
using Microsoft.Extensions.Logging;

namespace GasGuard.Desktop.ViewModels;

public sealed partial class DashboardViewModel : PageViewModelBase
{
    private readonly IDashboardService _dashboard;
    private readonly IDemoScenarioService _demoScenario;

    public DashboardViewModel(
        IDashboardService dashboard,
        IDemoScenarioService demoScenario,
        ILogger<DashboardViewModel> logger) : base(logger)
    {
        _dashboard = dashboard;
        _demoScenario = demoScenario;
    }

    public override string Title => "Dashboard";

    public ObservableCollection<DashboardDeviceItem> Detectors { get; } = [];
    public ObservableCollection<RecentAlertItem> RecentAlerts { get; } = [];
    public ObservableCollection<CommunicationFailureItem> RecentCommunicationFailures { get; } = [];

    [ObservableProperty] private int _activeDetectorCount;
    [ObservableProperty] private int _unresolvedCriticalCount;
    [ObservableProperty] private int _unresolvedWarningCount;
    [ObservableProperty] private string _lastReportText = "No detector status has been received.";
    [ObservableProperty] private string _lastReportFreshness = "Unknown";
    [ObservableProperty] private bool _lastReportIsStale;
    [ObservableProperty] private string _modemHeadline = "Modem disconnected";
    [ObservableProperty] private string _modemDetail = "No GSM modem is connected.";
    [ObservableProperty] private ModemConnectionState _modemState = ModemConnectionState.Disconnected;
    [ObservableProperty] private bool _modemReady;
    [ObservableProperty] private bool _databaseHealthy;
    [ObservableProperty] private string _databaseHealthText = "Not checked";
    [ObservableProperty] private string _databaseSizeText = "—";
    [ObservableProperty] private string _lastBackupText = "No verified backup recorded";
    [ObservableProperty] private bool _hasDetectors;
    [ObservableProperty] private bool _hasAlerts;
    [ObservableProperty] private bool _hasCommunicationFailures;
    [ObservableProperty] private bool _containsSimulatedData;
    [ObservableProperty] private string _staleWindowText = "15 minutes";
    [ObservableProperty] private string _generatedAtText = "—";

    protected override async Task LoadAsync()
    {
        var snapshot = await _dashboard.GetSnapshotAsync();
        ActiveDetectorCount = snapshot.ActiveDetectorCount;
        UnresolvedCriticalCount = snapshot.UnresolvedCriticalCount;
        UnresolvedWarningCount = snapshot.UnresolvedWarningCount;
        ModemState = snapshot.ModemStatus.ConnectionState;
        ModemReady = snapshot.ModemStatus.IsReadyForSms;
        ModemHeadline = GetModemHeadline(snapshot.ModemStatus);
        ModemDetail = GetModemDetail(snapshot.ModemStatus);
        DatabaseHealthy = snapshot.DatabaseHealth.IsHealthy;
        DatabaseHealthText = snapshot.DatabaseHealth.Message;
        DatabaseSizeText = FormatSize(snapshot.DatabaseHealth.SizeBytes);
        LastBackupText = snapshot.LastBackupAtUtc.HasValue
            ? $"Last verified backup: {UiFormat.Local(snapshot.LastBackupAtUtc)} ({UiFormat.Age(snapshot.LastBackupAtUtc, snapshot.GeneratedAtUtc)})"
            : "No verified backup has been recorded.";
        ContainsSimulatedData = snapshot.ContainsSimulatedData || snapshot.ModemStatus.IsSimulated;
        StaleWindowText = $"{snapshot.StaleAfter.TotalMinutes:0} minutes";
        GeneratedAtText = UiFormat.Local(snapshot.GeneratedAtUtc);

        if (snapshot.LastSuccessfulCommunicationUtc.HasValue)
        {
            var age = snapshot.GeneratedAtUtc - snapshot.LastSuccessfulCommunicationUtc.Value;
            LastReportIsStale = age > snapshot.StaleAfter;
            LastReportText =
                $"{snapshot.LastSuccessfulDeviceCode}: {snapshot.LastSuccessfulState?.ToString().ToUpperInvariant() ?? "UNKNOWN"} | raw {snapshot.LastSuccessfulReading?.ToString() ?? "—"} {snapshot.LastSuccessfulUnit ?? string.Empty} | {UiFormat.Local(snapshot.LastSuccessfulCommunicationUtc)} (Lagos)";
            LastReportFreshness = LastReportIsStale
                ? $"{UiFormat.Age(snapshot.LastSuccessfulCommunicationUtc, snapshot.GeneratedAtUtc)} — STALE. This is not evidence of current safety."
                : $"{UiFormat.Age(snapshot.LastSuccessfulCommunicationUtc, snapshot.GeneratedAtUtc)}. Within the {snapshot.StaleAfter.TotalMinutes:0}-minute freshness window.";
        }
        else
        {
            LastReportIsStale = true;
            LastReportText = "No detector status has ever been received.";
            LastReportFreshness = "Unknown. Do not treat this screen as evidence that any area is safe.";
        }

        Replace(Detectors, snapshot.Detectors);
        Replace(RecentAlerts, snapshot.RecentAlerts);
        Replace(RecentCommunicationFailures, snapshot.RecentCommunicationFailures);
        HasDetectors = Detectors.Count > 0;
        HasAlerts = RecentAlerts.Count > 0;
        HasCommunicationFailures = RecentCommunicationFailures.Count > 0;
        SetStatus(null);
    }

    [RelayCommand]
    private async Task LoadDemoDataAsync()
    {
        try
        {
            var result = await _demoScenario.LoadDemoDataAsync();
            if (!result.Success)
            {
                SetError(result.Message);
                return;
            }

            await LoadAsync();
            SetStatus(result.Message);
        }
        catch (Exception ex)
        {
            SetError($"Demo data could not be loaded: {ex.Message}");
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static string GetModemHeadline(ModemStatus status) => status.ConnectionState switch
    {
        ModemConnectionState.Connected when status.IsReadyForSms => "GSM modem ready",
        ModemConnectionState.Connected => "GSM modem connected, not ready",
        ModemConnectionState.Connecting => "GSM modem connecting",
        ModemConnectionState.Reconnecting => "GSM modem reconnecting",
        ModemConnectionState.Faulted => "GSM modem fault",
        _ => "GSM modem disconnected"
    };

    private static string GetModemDetail(ModemStatus status)
    {
        if (status.ConnectionState != ModemConnectionState.Connected)
        {
            return status.LastError ?? "No modem connection. Detector last-report times remain the only evidence.";
        }

        var signal = status.SignalQuality.HasValue
            ? $"{status.SignalQuality}/31 {status.SignalDescription}"
            : status.SignalDescription ?? "signal unknown";
        return $"{status.SimStatusText} | {status.RegistrationText} | {signal}{(status.IsSimulated ? " | SIMULATED DEMO" : string.Empty)}";
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
