using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace GasGuard.Desktop.ViewModels;

public sealed partial class ContactsViewModel : PageViewModelBase
{
    private const int MaximumTestMessageLength = 160;
    private readonly IContactService _contacts;
    private readonly INotificationService _notifications;
    private readonly ICommunicationLogService _logs;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ContactsViewModel> _logger;
    private IReadOnlyList<ContactListItem> _allContacts = [];

    public ContactsViewModel(
        IContactService contacts,
        INotificationService notifications,
        ICommunicationLogService logs,
        IDialogService dialogs,
        ILogger<ContactsViewModel> logger) : base(logger)
    {
        _contacts = contacts;
        _notifications = notifications;
        _logs = logs;
        _dialogs = dialogs;
        _logger = logger;
        TestMessage = "GasGuard test message. Local gas alarms operate independently. No reply required.";
    }

    public override string Title => "Contacts & Notifications";

    public ObservableCollection<ContactListItem> Contacts { get; } = [];
    public ObservableCollection<CommunicationLogItem> OutgoingLogs { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _includeInactive;
    [ObservableProperty] private ContactListItem? _selectedContact;
    [ObservableProperty] private bool _hasContacts;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private bool _canSendTest;
    [ObservableProperty] private string _toggleActiveText = "Deactivate contact";
    [ObservableProperty] private string _filteredCountText = "0 contacts";
    [ObservableProperty] private string _testMessage;
    [ObservableProperty] private string _testRemainingText = "160 characters remaining";
    [ObservableProperty] private bool _hasOutgoingLogs;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnIncludeInactiveChanged(bool value) => _ = ActivateAsync();

    public bool SelectedContactIsInactive => SelectedContact is { IsActive: false };

    partial void OnSelectedContactChanged(ContactListItem? value)
    {
        HasSelection = value is not null;
        ToggleActiveText = value?.IsActive == false ? "Reactivate contact" : "Deactivate contact";
        OnPropertyChanged(nameof(SelectedContactIsInactive));
        UpdateCanSend();
    }

    partial void OnTestMessageChanged(string value)
    {
        var remaining = MaximumTestMessageLength - (value?.Length ?? 0);
        TestRemainingText = $"{Math.Max(0, remaining)} characters remaining";
        UpdateCanSend();
    }

    protected override async Task LoadAsync()
    {
        _allContacts = await _contacts.GetContactsAsync(IncludeInactive);
        if (SelectedContact is not null)
        {
            var selectedId = SelectedContact.Id;
            SelectedContact = _allContacts.FirstOrDefault(x => x.Id == selectedId);
        }

        ApplyFilter();
        await LoadOutgoingAsync();
        SetStatus(null);
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var input = _dialogs.EditContact(null, "Add contact");
        if (input is null)
        {
            return;
        }

        var result = await _contacts.CreateAsync(input);
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
        if (SelectedContact is null)
        {
            return;
        }

        var input = _dialogs.EditContact(ToInput(SelectedContact), "Edit contact");
        if (input is null)
        {
            return;
        }

        var result = await _contacts.UpdateAsync(SelectedContact.Id, input);
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
        if (SelectedContact is null)
        {
            return;
        }

        var selected = SelectedContact;
        if (selected.IsActive &&
            !_dialogs.Confirm(
                "Deactivate contact",
                $"Deactivate {selected.FullName} ({selected.PhoneNumberDisplay})? The contact remains in history but will not receive automatic SMS notifications.",
                "Deactivate",
                destructive: true))
        {
            return;
        }

        var result = await _contacts.SetActiveAsync(selected.Id, !selected.IsActive);
        if (!result.Success)
        {
            SetError(result.Message);
            return;
        }

        SetError(null);
        SetStatus(result.Message);
        await ActivateAsync();
    }

    [RelayCommand]
    private async Task SendTestMessageAsync()
    {
        if (SelectedContact is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(TestMessage))
        {
            SetError("Enter a test message.");
            return;
        }

        if (TestMessage.Length > MaximumTestMessageLength)
        {
            SetError($"The test message cannot exceed {MaximumTestMessageLength} characters.");
            return;
        }

        if (!_dialogs.Confirm(
                "Send test SMS",
                $"Send a test SMS to {SelectedContact.FullName} at {SelectedContact.PhoneNumberDisplay}? This uses the GSM modem and may incur network charges. A successful result means the modem accepted the message; it does not confirm delivery to the recipient.",
                "Send test SMS"))
        {
            return;
        }

        try
        {
            var result = await _notifications.SendTestMessageAsync(
                SelectedContact.PhoneNumber,
                TestMessage,
                operatorConfirmed: true);

            if (result.SubmittedToModem)
            {
                SetError(null);
                SetStatus($"Submitted to the modem ({result.Outcome}). Delivery to the recipient is NOT confirmed. Reference: {result.ProviderReference ?? "not provided"}.");
            }
            else
            {
                SetError($"The test SMS was not submitted: {result.Message}");
            }

            await LoadOutgoingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test SMS send failed.");
            SetError($"The test SMS failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RefreshLogsAsync() => await LoadOutgoingAsync();

    private async Task LoadOutgoingAsync()
    {
        var logs = await _logs.GetOutgoingAsync(40);
        OutgoingLogs.Clear();
        foreach (var log in logs)
        {
            OutgoingLogs.Add(log);
        }

        HasOutgoingLogs = OutgoingLogs.Count > 0;
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        var filtered = string.IsNullOrWhiteSpace(search)
            ? _allContacts
            : _allContacts.Where(x =>
                x.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.PhoneNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Role.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();

        Contacts.Clear();
        foreach (var contact in filtered)
        {
            Contacts.Add(contact);
        }

        HasContacts = Contacts.Count > 0;
        FilteredCountText = $"{Contacts.Count} contact{(Contacts.Count == 1 ? string.Empty : "s")} shown";
        if (SelectedContact is null || !Contacts.Any(x => x.Id == SelectedContact.Id))
        {
            SelectedContact = Contacts.FirstOrDefault();
        }
    }

    private void UpdateCanSend()
    {
        CanSendTest = SelectedContact is { IsActive: true } &&
                      !string.IsNullOrWhiteSpace(TestMessage) &&
                      TestMessage.Length <= MaximumTestMessageLength;
    }

    private static ContactInput ToInput(ContactListItem contact) => new(
        contact.FullName,
        contact.PhoneNumber,
        contact.Role,
        contact.IsActive,
        contact.NotifyOnCritical,
        contact.NotifyOnWarning,
        contact.NotifyOnCommunicationFailure,
        contact.Notes);

    private static string FormatFailure(OperationResult result)
    {
        if (result.ValidationErrors is null || result.ValidationErrors.Count == 0)
        {
            return result.Message;
        }

        return $"{result.Message} {string.Join(" ", result.ValidationErrors)}";
    }
}
