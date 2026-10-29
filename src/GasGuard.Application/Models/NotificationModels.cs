using GasGuard.Domain;

namespace GasGuard.Application.Models;

public sealed record NotificationDispatchItem(
    Guid ContactId,
    string ContactName,
    string PhoneNumber,
    string Status,
    bool SubmittedToModem,
    string? Error);

public sealed record NotificationDispatchResult(
    int Attempted,
    int Submitted,
    int Failed,
    IReadOnlyList<NotificationDispatchItem> Items);

public sealed record MonitorNotification(
    string Title,
    string Message,
    Severity Severity,
    bool IsSimulated,
    DateTime TimestampUtc);
