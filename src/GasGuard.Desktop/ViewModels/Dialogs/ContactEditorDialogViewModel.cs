using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Domain;

namespace GasGuard.Desktop.ViewModels.Dialogs;

public sealed partial class ContactEditorDialogViewModel : ObservableObject
{
    private readonly IPhoneNumberNormalizer _phoneNumbers;

    public ContactEditorDialogViewModel(IPhoneNumberNormalizer phoneNumbers, ContactInput? existing)
    {
        _phoneNumbers = phoneNumbers;
        FullName = existing?.FullName ?? string.Empty;
        PhoneNumber = existing?.PhoneNumber ?? string.Empty;
        Role = existing?.Role ?? ContactRole.SafetyOfficer;
        IsActive = existing?.IsActive ?? true;
        NotifyOnCritical = existing?.NotifyOnCritical ?? true;
        NotifyOnWarning = existing?.NotifyOnWarning ?? true;
        NotifyOnCommunicationFailure = existing?.NotifyOnCommunicationFailure ?? false;
        Notes = existing?.Notes ?? string.Empty;
        IsEditing = existing is not null;
    }

    public bool IsEditing { get; }
    public string DialogHeading => IsEditing ? "Edit contact" : "Add contact";
    public IReadOnlyList<ContactRole> Roles { get; } = Enum.GetValues<ContactRole>();

    [ObservableProperty] private string _fullName;
    [ObservableProperty] private string _phoneNumber;
    [ObservableProperty] private ContactRole _role;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _notifyOnCritical;
    [ObservableProperty] private bool _notifyOnWarning;
    [ObservableProperty] private bool _notifyOnCommunicationFailure;
    [ObservableProperty] private string _notes;
    [ObservableProperty] private string? _errorText;

    public ContactInput? Result { get; private set; }
    public event Action<bool>? CloseRequested;

    [RelayCommand]
    private void Save()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(FullName))
        {
            errors.Add("A contact name is required.");
        }

        if (!_phoneNumbers.TryNormalize(PhoneNumber, out var normalizedPhone, out var phoneError))
        {
            errors.Add(phoneError ?? "The phone number is invalid.");
        }

        if (errors.Count > 0)
        {
            ErrorText = string.Join(Environment.NewLine, errors);
            return;
        }

        Result = new ContactInput(
            FullName.Trim(),
            normalizedPhone,
            Role,
            IsActive,
            NotifyOnCritical,
            NotifyOnWarning,
            NotifyOnCommunicationFailure,
            string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim());
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
