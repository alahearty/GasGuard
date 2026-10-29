using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace GasGuard.Desktop.ViewModels;

public sealed partial class DetectorsViewModel : PageViewModelBase
{
    private readonly IDeviceService _devices;
    private readonly IDialogService _dialogs;
    private readonly ILogger<DetectorsViewModel> _logger;
    private IReadOnlyList<DeviceListItem> _allDevices = [];

    public DetectorsViewModel(
        IDeviceService devices,
        IDialogService dialogs,
        ILogger<DetectorsViewModel> logger) : base(logger)
    {
        _devices = devices;
        _dialogs = dialogs;
        _logger = logger;
    }

    public override string Title => "Gas Detectors";

    public ObservableCollection<DeviceListItem> Devices { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _includeInactive;
    [ObservableProperty] private DeviceListItem? _selectedDevice;
    [ObservableProperty] private bool _hasDevices;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _filteredCountText = "0 detectors";
    [ObservableProperty] private string _toggleActiveText = "Deactivate detector";

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnIncludeInactiveChanged(bool value) => _ = ActivateAsync();

    partial void OnSelectedDeviceChanged(DeviceListItem? value)
    {
        HasSelection = value is not null;
        ToggleActiveText = value?.IsActive == false ? "Reactivate detector" : "Deactivate detector";
    }

    protected override async Task LoadAsync()
    {
        _allDevices = await _devices.GetDevicesAsync(IncludeInactive);
        if (SelectedDevice is not null)
        {
            var selectionId = SelectedDevice.Id;
            SelectedDevice = _allDevices.FirstOrDefault(x => x.Id == selectionId);
        }

        ApplyFilter();
        SetStatus(null);
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var input = _dialogs.EditDevice(null, "Add detector");
        if (input is null)
        {
            return;
        }

        var result = await _devices.CreateAsync(input);
        if (!result.Success)
        {
            SetError(FormatFailure(result));
            return;
        }

        SetError(null);
        SetStatus(result.Message);
        await ActivateAsync();
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var selected = SelectedDevice;
        var input = _dialogs.EditDevice(ToInput(selected), "Edit detector");
        if (input is null)
        {
            return;
        }

        var identityChanged =
            !string.Equals(selected.DeviceCode, input.DeviceCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selected.ExpectedSenderNumber, input.ExpectedSenderNumber, StringComparison.Ordinal);

        if (identityChanged &&
            !_dialogs.Confirm(
                "Change detector identity",
                "The device code or expected SMS sender is changing. Messages using the old identity will no longer be accepted for this detector. Confirm that the detector firmware and SIM are being changed together.",
                "Save identity change",
                destructive: true))
        {
            return;
        }

        var result = await _devices.UpdateAsync(selected.Id, input);
        if (!result.Success)
        {
            SetError(FormatFailure(result));
            return;
        }

        SetError(null);
        SetStatus(result.Message);
        await ActivateAsync();
    }

    [RelayCommand]
    private async Task ToggleActiveAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var selected = SelectedDevice;
        if (selected.IsActive)
        {
            if (!_dialogs.Confirm(
                    "Deactivate detector",
                    $"Deactivate {selected.DeviceCode} ({selected.DisplayName})? The detector record and every historical incident will be retained. A deactivated detector is shown as OFFLINE.",
                    "Deactivate",
                    destructive: true))
            {
                return;
            }
        }

        var result = await _devices.SetActiveAsync(selected.Id, !selected.IsActive);
        if (!result.Success)
        {
            SetError(result.Message);
            return;
        }

        SetError(null);
        SetStatus(result.Message);
        await ActivateAsync();
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        var filtered = string.IsNullOrWhiteSpace(search)
            ? _allDevices
            : _allDevices.Where(x =>
                x.DeviceCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.InstallationLocation.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.ExpectedSenderNumber.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();

        Devices.Clear();
        foreach (var device in filtered)
        {
            Devices.Add(device);
        }

        HasDevices = Devices.Count > 0;
        FilteredCountText = $"{Devices.Count} detector{(Devices.Count == 1 ? string.Empty : "s")} shown";
    }

    private static DeviceInput ToInput(DeviceListItem device) => new(
        device.DeviceCode,
        device.DisplayName,
        device.InstallationLocation,
        device.ExpectedSenderNumber,
        device.DetectorType,
        device.Description,
        device.InstallationDate,
        device.IsActive,
        device.MaintenanceNotes);

    private static string FormatFailure(OperationResult result)
    {
        if (result.ValidationErrors is null || result.ValidationErrors.Count == 0)
        {
            return result.Message;
        }

        return $"{result.Message} {string.Join(" ", result.ValidationErrors)}";
    }
}
