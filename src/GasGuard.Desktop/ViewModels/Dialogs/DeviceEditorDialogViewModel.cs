using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Domain;

namespace GasGuard.Desktop.ViewModels.Dialogs;

public sealed partial class DeviceEditorDialogViewModel : ObservableObject
{
    private readonly IPhoneNumberNormalizer _phoneNumbers;

    public DeviceEditorDialogViewModel(IPhoneNumberNormalizer phoneNumbers, DeviceInput? existing)
    {
        _phoneNumbers = phoneNumbers;
        DeviceCode = existing?.DeviceCode ?? string.Empty;
        DisplayName = existing?.DisplayName ?? string.Empty;
        InstallationLocation = existing?.InstallationLocation ?? string.Empty;
        ExpectedSenderNumber = existing?.ExpectedSenderNumber ?? string.Empty;
        DetectorType = existing?.DetectorType ?? DetectorType.CombustibleGas;
        Description = existing?.Description ?? string.Empty;
        InstallationDateText = existing?.InstallationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        IsActive = existing?.IsActive ?? true;
        MaintenanceNotes = existing?.MaintenanceNotes ?? string.Empty;
        IsEditing = existing is not null;
    }

    public bool IsEditing { get; }
    public string DialogHeading => IsEditing ? "Edit detector" : "Add detector";
    public IReadOnlyList<DetectorType> DetectorTypes { get; } = Enum.GetValues<DetectorType>();

    [ObservableProperty] private string _deviceCode;
    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _installationLocation;
    [ObservableProperty] private string _expectedSenderNumber;
    [ObservableProperty] private DetectorType _detectorType;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _installationDateText;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _maintenanceNotes;
    [ObservableProperty] private string? _errorText;

    public DeviceInput? Result { get; private set; }

    public event Action<bool>? CloseRequested;

    [RelayCommand]
    private void Save()
    {
        var errors = new List<string>();
        var code = DeviceCode.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(code, "^[A-Z0-9][A-Z0-9_-]{2,31}$"))
        {
            errors.Add("Device code must be 3-32 characters using letters, digits, hyphens or underscores and must start with a letter or digit.");
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            errors.Add("A display name is required.");
        }

        if (string.IsNullOrWhiteSpace(InstallationLocation))
        {
            errors.Add("An installation location is required.");
        }

        if (!_phoneNumbers.TryNormalize(ExpectedSenderNumber, out var normalizedSender, out var phoneError))
        {
            errors.Add(phoneError ?? "The expected sender number is invalid.");
        }

        DateOnly? installationDate = null;
        if (!string.IsNullOrWhiteSpace(InstallationDateText))
        {
            if (!DateOnly.TryParseExact(
                    InstallationDateText.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDate))
            {
                errors.Add("Installation date must be in yyyy-MM-dd format, or left blank.");
            }
            else
            {
                installationDate = parsedDate;
            }
        }

        if (errors.Count > 0)
        {
            ErrorText = string.Join(Environment.NewLine, errors);
            return;
        }

        Result = new DeviceInput(
            code,
            DisplayName.Trim(),
            InstallationLocation.Trim(),
            normalizedSender,
            DetectorType,
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            installationDate,
            IsActive,
            string.IsNullOrWhiteSpace(MaintenanceNotes) ? null : MaintenanceNotes.Trim());
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
