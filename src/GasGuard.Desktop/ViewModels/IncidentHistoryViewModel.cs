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

public sealed partial class IncidentHistoryViewModel : PageViewModelBase
{
    private const int PageSize = 25;
    private readonly IIncidentService _incidents;
    private readonly IDeviceService _devices;
    private readonly IReportService _reports;
    private readonly IDialogService _dialogs;
    private readonly IClock _clock;
    private readonly ILogger<IncidentHistoryViewModel> _logger;

    public IncidentHistoryViewModel(
        IIncidentService incidents,
        IDeviceService devices,
        IReportService reports,
        IDialogService dialogs,
        IClock clock,
        ILogger<IncidentHistoryViewModel> logger) : base(logger)
    {
        _incidents = incidents;
        _devices = devices;
        _reports = reports;
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
        StatusOptions =
        [
            new("All statuses", null),
            new("Open", IncidentStatus.Open),
            new("Acknowledged", IncidentStatus.Acknowledged),
            new("Resolved", IncidentStatus.Resolved)
        ];
        _selectedSeverity = SeverityOptions[0];
        _selectedStatus = StatusOptions[0];
        DeviceOptions.Add(new DeviceFilterOption("All detectors", null, null));
        _selectedDevice = DeviceOptions[0];
    }

    public override string Title => "Incident History";

    public ObservableCollection<IncidentListItem> Items { get; } = [];
    public ObservableCollection<AuditEntryItem> History { get; } = [];
    public ObservableCollection<DeviceFilterOption> DeviceOptions { get; } = [];
    public IReadOnlyList<ChoiceOption<Severity?>> SeverityOptions { get; }
    public IReadOnlyList<ChoiceOption<IncidentStatus?>> StatusOptions { get; }

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _fromDateText;
    [ObservableProperty] private string _toDateText;
    [ObservableProperty] private string _locationText = string.Empty;
    [ObservableProperty] private ChoiceOption<Severity?> _selectedSeverity;
    [ObservableProperty] private ChoiceOption<IncidentStatus?> _selectedStatus;
    [ObservableProperty] private DeviceFilterOption _selectedDevice;
    [ObservableProperty] private IncidentListItem? _selectedIncident;
    [ObservableProperty] private IncidentDetails? _selectedDetails;
    [ObservableProperty] private bool _hasItems;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string _pageText = "Page 1 of 1";
    [ObservableProperty] private bool _canGoPrevious;
    [ObservableProperty] private bool _canGoNext;

    partial void OnSelectedIncidentChanged(IncidentListItem? value)
    {
        HasSelection = value is not null;
        if (value is null)
        {
            SelectedDetails = null;
            History.Clear();
            return;
        }

        _ = LoadDetailsSafeAsync(value.Id);
    }

    protected override async Task LoadAsync()
    {
        await LoadDeviceOptionsAsync();

        if (!TryGetCriteria(out var fromUtc, out var toUtc, out var dateError))
        {
            SetError(dateError);
            return;
        }

        SetError(null);
        var query = new IncidentQuery(
            SearchText,
            fromUtc,
            toUtc,
            SelectedSeverity.Value,
            SelectedDevice.DeviceId,
            LocationText,
            SelectedStatus.Value,
            Page,
            PageSize);

        var result = await _incidents.QueryAsync(query);
        Items.Clear();
        foreach (var item in result.Items)
        {
            Items.Add(item);
        }

        TotalCount = result.TotalCount;
        TotalPages = result.TotalPages;
        CanGoPrevious = result.HasPrevious;
        CanGoNext = result.HasNext;
        PageText = $"Page {result.Page} of {result.TotalPages} · {result.TotalCount} incident{(result.TotalCount == 1 ? string.Empty : "s")}";
        HasItems = Items.Count > 0;

        var selectedId = SelectedIncident?.Id;
        SelectedIncident = Items.FirstOrDefault(x => x.Id == selectedId) ?? Items.FirstOrDefault();
        if (SelectedIncident is null)
        {
            SelectedDetails = null;
            History.Clear();
        }

        SetStatus(null);
    }

    [RelayCommand]
    private async Task ApplyFiltersAsync()
    {
        Page = 1;
        await ActivateAsync();
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (!CanGoPrevious)
        {
            return;
        }

        Page = Math.Max(1, Page - 1);
        await ActivateAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!CanGoNext)
        {
            return;
        }

        Page = Math.Min(TotalPages, Page + 1);
        await ActivateAsync();
    }

    [RelayCommand]
    private async Task ResetFiltersAsync()
    {
        SearchText = string.Empty;
        LocationText = string.Empty;
        var today = _clock.ToLagosDate(_clock.UtcNow);
        FromDateText = today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ToDateText = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        SelectedSeverity = SeverityOptions[0];
        SelectedStatus = StatusOptions[0];
        SelectedDevice = DeviceOptions.FirstOrDefault() ?? DeviceOptions[0];
        Page = 1;
        await ActivateAsync();
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (!TryGetCriteria(out var fromUtc, out var toUtc, out var error))
        {
            SetError(error);
            return;
        }

        var path = _dialogs.ChooseSaveFile(
            "Export filtered incidents",
            "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            $"gasguard-incidents-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        if (path is null)
        {
            return;
        }

        try
        {
            var criteria = new ReportCriteria(fromUtc, toUtc, SelectedDevice.DeviceId, SelectedSeverity.Value);
            var result = await _reports.ExportIncidentsCsvAsync(criteria, path);
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
            _logger.LogError(ex, "Incident CSV export failed.");
            SetError($"The export failed: {ex.Message}");
        }
    }

    private async Task LoadDetailsSafeAsync(Guid incidentId)
    {
        try
        {
            var details = await _incidents.GetDetailsAsync(incidentId);
            if (SelectedIncident?.Id != incidentId)
            {
                return;
            }

            SelectedDetails = details;
            History.Clear();
            if (details is not null)
            {
                foreach (var entry in details.History)
                {
                    History.Add(entry);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Incident details could not be loaded.");
            SetError($"Incident details could not be loaded: {ex.Message}");
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

    private bool TryGetCriteria(out DateTime fromUtc, out DateTime toUtc, out string? error)
    {
        fromUtc = default;
        toUtc = default;
        error = null;

        DateOnly? fromDate = null;
        DateOnly? toDate = null;
        if (!string.IsNullOrWhiteSpace(FromDateText))
        {
            if (!DateOnly.TryParseExact(FromDateText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFrom))
            {
                error = "From date must use yyyy-MM-dd.";
                return false;
            }

            fromDate = parsedFrom;
        }

        if (!string.IsNullOrWhiteSpace(ToDateText))
        {
            if (!DateOnly.TryParseExact(ToDateText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTo))
            {
                error = "To date must use yyyy-MM-dd.";
                return false;
            }

            toDate = parsedTo;
        }

        if (fromDate.HasValue && toDate.HasValue && toDate < fromDate)
        {
            error = "The end date must be on or after the start date.";
            return false;
        }

        fromUtc = fromDate.HasValue
            ? _clock.LagosToUtc(fromDate.Value.ToDateTime(TimeOnly.MinValue))
            : DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        toUtc = toDate.HasValue
            ? _clock.LagosToUtc(toDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue))
            : DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        return true;
    }
}

public sealed record DeviceFilterOption(string Label, Guid? DeviceId, bool? IsActive)
{
    public override string ToString() => Label;
}
