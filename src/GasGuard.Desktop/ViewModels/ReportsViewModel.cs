using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Desktop.Services;
using GasGuard.Domain;
using Microsoft.Extensions.Logging;

namespace GasGuard.Desktop.ViewModels;

public sealed partial class ReportsViewModel : PageViewModelBase
{
    private readonly IReportService _reports;
    private readonly IDeviceService _devices;
    private readonly IDialogService _dialogs;
    private readonly IClock _clock;
    private readonly ILogger<ReportsViewModel> _logger;

    public ReportsViewModel(
        IReportService reports,
        IDeviceService devices,
        IDialogService dialogs,
        IClock clock,
        ILogger<ReportsViewModel> logger) : base(logger)
    {
        _reports = reports;
        _devices = devices;
        _dialogs = dialogs;
        _clock = clock;
        _logger = logger;

        var today = clock.ToLagosDate(clock.UtcNow);
        _fromDateText = today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _toDateText = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        SeverityOptions =
        [
            new("All severities", null),
            new("Critical", Severity.Critical),
            new("Warning", Severity.Warning)
        ];
        _selectedSeverity = SeverityOptions[0];
        DeviceOptions.Add(new DeviceFilterOption("All detectors", null, null));
        _selectedDevice = DeviceOptions[0];
    }

    public override string Title => "Reports & Analytics";

    public ObservableCollection<SeverityCount> SeverityCounts { get; } = [];
    public ObservableCollection<DeviceIncidentCount> DeviceCounts { get; } = [];
    public ObservableCollection<CommunicationFailureSummaryItem> CommunicationFailures { get; } = [];
    public ObservableCollection<DetectorActivityItem> DetectorActivity { get; } = [];
    public ObservableCollection<TrendBarRow> Trend { get; } = [];
    public ObservableCollection<DeviceFilterOption> DeviceOptions { get; } = [];
    public IReadOnlyList<ChoiceOption<Severity?>> SeverityOptions { get; }

    [ObservableProperty] private string _fromDateText;
    [ObservableProperty] private string _toDateText;
    [ObservableProperty] private ChoiceOption<Severity?> _selectedSeverity;
    [ObservableProperty] private DeviceFilterOption _selectedDevice;
    [ObservableProperty] private bool _includeSimulated = true;
    [ObservableProperty] private bool _hasSummary;
    [ObservableProperty] private int _totalIncidents;
    [ObservableProperty] private int _criticalIncidentCount;
    [ObservableProperty] private int _warningIncidentCount;
    [ObservableProperty] private int _simulatedIncidents;
    [ObservableProperty] private int _communicationFailureCount;
    [ObservableProperty] private string _generatedText = "No report generated.";
    [ObservableProperty] private bool _hasDeviceCounts;
    [ObservableProperty] private bool _hasCommunicationFailures;
    [ObservableProperty] private bool _hasTrend;
    [ObservableProperty] private bool _hasActivity;

    protected override async Task LoadAsync()
    {
        await LoadDeviceOptionsAsync();
        if (!TryGetCriteria(out var criteria, out var error))
        {
            SetError(error);
            HasSummary = false;
            return;
        }

        var summary = await _reports.GetSummaryAsync(criteria);
        TotalIncidents = summary.TotalIncidents;
        CriticalIncidentCount = summary.IncidentsBySeverity
            .FirstOrDefault(x => x.Severity == Severity.Critical)?.Count ?? 0;
        WarningIncidentCount = summary.IncidentsBySeverity
            .FirstOrDefault(x => x.Severity == Severity.Warning)?.Count ?? 0;
        SimulatedIncidents = summary.SimulatedIncidentCount;
        CommunicationFailureCount = summary.CommunicationFailureCount;
        GeneratedText = $"Generated {UiFormat.Local(summary.GeneratedAtUtc)} (Lagos). Raw readings are device values, not calibrated ppm.";
        HasSummary = true;

        Replace(SeverityCounts, summary.IncidentsBySeverity);
        Replace(DeviceCounts, summary.IncidentsByDevice);
        Replace(CommunicationFailures, summary.CommunicationFailures);
        Replace(DetectorActivity, summary.DetectorActivity);
        Replace(Trend, BuildTrendRows(summary.Trend));
        HasDeviceCounts = DeviceCounts.Count > 0;
        HasCommunicationFailures = CommunicationFailures.Count > 0;
        HasTrend = Trend.Count > 0;
        HasActivity = DetectorActivity.Count > 0;
        SetError(null);
        SetStatus(null);
    }

    [RelayCommand]
    private async Task GenerateAsync() => await ActivateAsync();

    [RelayCommand]
    private async Task ExportIncidentsAsync() => await ExportAsync(
        "Export incident report",
        "incidents",
        (criteria, path) => _reports.ExportIncidentsCsvAsync(criteria, path));

    [RelayCommand]
    private async Task ExportCommunicationAsync() => await ExportAsync(
        "Export communication failure report",
        "communication-failures",
        (criteria, path) => _reports.ExportCommunicationFailuresCsvAsync(criteria, path));

    [RelayCommand]
    private async Task ExportActivityAsync() => await ExportAsync(
        "Export detector activity report",
        "detector-activity",
        (criteria, path) => _reports.ExportDetectorActivityCsvAsync(criteria, path));

    private async Task ExportAsync(
        string title,
        string fileStem,
        Func<ReportCriteria, string, Task<OperationResult>> exporter)
    {
        if (!TryGetCriteria(out var criteria, out var error))
        {
            SetError(error);
            return;
        }

        var path = _dialogs.ChooseSaveFile(
            title,
            "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            $"gasguard-{fileStem}-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        if (path is null)
        {
            return;
        }

        try
        {
            var result = await exporter(criteria, path);
            if (result.Success)
            {
                SetError(null);
                SetStatus(result.Message);
            }
            else
            {
                SetError(result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Report export failed.");
            SetError($"The report could not be exported: {ex.Message}");
        }
    }

    private async Task LoadDeviceOptionsAsync()
    {
        var devices = await _devices.GetDevicesAsync(true);
        var selectedId = SelectedDevice?.DeviceId;
        DeviceOptions.Clear();
        DeviceOptions.Add(new DeviceFilterOption("All detectors", null, null));
        foreach (var device in devices)
        {
            DeviceOptions.Add(new DeviceFilterOption($"{device.DeviceCode} — {device.DisplayName}", device.Id, device.IsActive));
        }

        SelectedDevice = DeviceOptions.FirstOrDefault(x => x.DeviceId == selectedId) ?? DeviceOptions[0];
    }

    private bool TryGetCriteria(out ReportCriteria criteria, out string? error)
    {
        criteria = null!;
        error = null;

        if (!DateOnly.TryParseExact(FromDateText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDate) ||
            !DateOnly.TryParseExact(ToDateText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDate))
        {
            error = "Select dates in yyyy-MM-dd format.";
            return false;
        }

        if (toDate < fromDate)
        {
            error = "The end date must be on or after the start date.";
            return false;
        }

        var fromUtc = _clock.LagosToUtc(fromDate.ToDateTime(TimeOnly.MinValue));
        var toUtc = _clock.LagosToUtc(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue));
        criteria = new ReportCriteria(fromUtc, toUtc, SelectedDevice.DeviceId, SelectedSeverity.Value, IncludeSimulated);
        return true;
    }

    private static IReadOnlyList<TrendBarRow> BuildTrendRows(IReadOnlyList<IncidentTrendPoint> points)
    {
        var maximum = Math.Max(1, points.Count == 0 ? 1 : points.Max(x => x.Total));
        const double maximumWidth = 240;
        return points
            .Select(x => new TrendBarRow(
                x.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                x.Critical,
                x.Warning,
                x.Total,
                x.Critical / (double)maximum * maximumWidth,
                x.Warning / (double)maximum * maximumWidth))
            .ToArray();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}

public sealed record TrendBarRow(
    string DateLabel,
    int Critical,
    int Warning,
    int Total,
    double CriticalWidth,
    double WarningWidth);
