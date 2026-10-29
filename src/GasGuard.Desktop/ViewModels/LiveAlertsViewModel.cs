using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Desktop.Services;
using GasGuard.Domain;
using Microsoft.Extensions.Logging;

namespace GasGuard.Desktop.ViewModels;

public sealed partial class LiveAlertsViewModel : PageViewModelBase
{
    private readonly IIncidentService _incidents;
    private readonly IDialogService _dialogs;
    private readonly ILogger<LiveAlertsViewModel> _logger;

    public LiveAlertsViewModel(
        IIncidentService incidents,
        IDialogService dialogs,
        ILogger<LiveAlertsViewModel> logger) : base(logger)
    {
        _incidents = incidents;
        _dialogs = dialogs;
        _logger = logger;
    }

    public override string Title => "Live Alerts";

    public ObservableCollection<IncidentListItem> Alerts { get; } = [];
    public ObservableCollection<AuditEntryItem> History { get; } = [];

    [ObservableProperty] private IncidentListItem? _selectedAlert;
    [ObservableProperty] private IncidentDetails? _selectedDetails;
    [ObservableProperty] private string _acknowledgementNotes = string.Empty;
    [ObservableProperty] private string _resolutionNotes = string.Empty;
    [ObservableProperty] private bool _hasAlerts;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private bool _canAcknowledge;
    [ObservableProperty] private bool _canResolve;
    [ObservableProperty] private string _acknowledgementSummary = "Not acknowledged.";
    [ObservableProperty] private string _resolutionSummary = "Not resolved.";
    [ObservableProperty] private string _detailsHeading = "Select an active incident.";

    partial void OnSelectedAlertChanged(IncidentListItem? value)
    {
        UpdateAvailability(value);
        if (value is null)
        {
            SelectedDetails = null;
            History.Clear();
            DetailsHeading = "Select an active incident.";
            return;
        }

        DetailsHeading = $"{value.DeviceCode} — {value.DeviceName}";
        _ = LoadDetailsSafeAsync(value.Id);
    }

    protected override async Task LoadAsync()
    {
        var selectedId = SelectedAlert?.Id;
        var active = await _incidents.GetActiveAsync();

        Alerts.Clear();
        foreach (var item in active)
        {
            Alerts.Add(item);
        }

        HasAlerts = Alerts.Count > 0;
        var selection = selectedId.HasValue
            ? Alerts.FirstOrDefault(x => x.Id == selectedId.Value)
            : Alerts.FirstOrDefault();
        SelectedAlert = selection;

        if (selection is null)
        {
            HasSelection = false;
            CanAcknowledge = false;
            CanResolve = false;
            SelectedDetails = null;
            History.Clear();
        }
    }

    [RelayCommand]
    private async Task AcknowledgeAsync()
    {
        if (SelectedAlert is null)
        {
            return;
        }

        if (!_dialogs.Confirm(
                "Acknowledge incident",
                "Acknowledging records that an operator has seen the incident. It does NOT mean the hazard has cleared and does not resolve the incident.",
                "Acknowledge"))
        {
            return;
        }

        var incidentId = SelectedAlert.Id;
        var result = await _incidents.AcknowledgeAsync(incidentId, AcknowledgementNotes);
        if (!result.Success)
        {
            SetError(result.Message);
            return;
        }

        SetError(null);
        SetStatus(result.Message);
        AcknowledgementNotes = string.Empty;
        await ReloadAndSelectAsync(incidentId);
    }

    [RelayCommand]
    private async Task ResolveAsync()
    {
        if (SelectedAlert is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ResolutionNotes))
        {
            SetError("Enter resolution notes before resolving the incident.");
            return;
        }

        if (!_dialogs.Confirm(
                "Resolve incident",
                $"Resolve the {SelectedAlert.Severity.ToString().ToUpperInvariant()} incident for {SelectedAlert.DeviceCode} at {SelectedAlert.Location}? The original alert and all history will be retained.",
                "Resolve incident",
                destructive: true))
        {
            return;
        }

        var incidentId = SelectedAlert.Id;
        var result = await _incidents.ResolveAsync(incidentId, ResolutionNotes);
        if (!result.Success)
        {
            SetError(result.Message);
            return;
        }

        SetError(null);
        SetStatus(result.Message);
        ResolutionNotes = string.Empty;
        await ReloadAndSelectAsync(incidentId);
    }

    [RelayCommand]
    private void ClearSelection()
    {
        SelectedAlert = null;
    }

    private async Task ReloadAndSelectAsync(Guid incidentId)
    {
        await LoadAsync();
        if (Alerts.FirstOrDefault(x => x.Id == incidentId) is { } updated)
        {
            SelectedAlert = updated;
        }
    }

    private async Task LoadDetailsSafeAsync(Guid incidentId)
    {
        try
        {
            var details = await _incidents.GetDetailsAsync(incidentId);
            if (SelectedAlert?.Id != incidentId)
            {
                return;
            }

            SelectedDetails = details;
            HasSelection = details is not null;
            History.Clear();
            if (details is not null)
            {
                foreach (var entry in details.History)
                {
                    History.Add(entry);
                }

                AcknowledgementSummary = details.Summary.AcknowledgedAtUtc.HasValue
                    ? $"Acknowledged {UiFormat.Local(details.Summary.AcknowledgedAtUtc)} by {details.AcknowledgedBy ?? "unknown"}. {details.AcknowledgementNotes ?? "No note."}"
                    : "Not acknowledged.";
                ResolutionSummary = details.Summary.ResolvedAtUtc.HasValue
                    ? $"Resolved {UiFormat.Local(details.Summary.ResolvedAtUtc)} by {details.ResolvedBy ?? "unknown"}. {details.ResolutionNotes ?? "No note."}"
                    : "Not resolved.";
            }

            UpdateAvailability(SelectedAlert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Incident details could not be loaded.");
            SetError($"Incident details could not be loaded: {ex.Message}");
        }
    }

    private void UpdateAvailability(IncidentListItem? item)
    {
        HasSelection = item is not null;
        CanAcknowledge = item?.Status == IncidentStatus.Open;
        CanResolve = item?.Status == IncidentStatus.Acknowledged;
    }
}
