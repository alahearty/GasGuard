using GasGuard.Domain;

namespace GasGuard.Application.Models;

public sealed record ContactInput(
    string FullName,
    string PhoneNumber,
    ContactRole Role,
    bool IsActive,
    bool NotifyOnCritical,
    bool NotifyOnWarning,
    bool NotifyOnCommunicationFailure,
    string? Notes);

public sealed record ContactListItem(
    Guid Id,
    string FullName,
    string PhoneNumber,
    string PhoneNumberDisplay,
    ContactRole Role,
    bool IsActive,
    bool NotifyOnCritical,
    bool NotifyOnWarning,
    bool NotifyOnCommunicationFailure,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
