using GasGuard.Domain;

namespace GasGuard.Application.Models;

public sealed record IncidentQuery(
    string? Search = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    Severity? Severity = null,
    Guid? DeviceId = null,
    string? Location = null,
    IncidentStatus? Status = null,
    int Page = 1,
    int PageSize = 25);

public sealed record IncidentListItem(
    Guid Id,
    Guid DeviceId,
    string DeviceCode,
    string DeviceName,
    string Location,
    Severity Severity,
    IncidentStatus Status,
    DateTime OccurredAtUtc,
    IncidentTimeSource OccurredTimeSource,
    DateTime ReceivedAtUtc,
    DateTime OpenedAtUtc,
    DateTime? AcknowledgedAtUtc,
    DateTime? ResolvedAtUtc,
    int? Reading,
    string? Unit,
    long? SequenceNumber,
    bool IsSimulated);

public sealed record IncidentDetails(
    IncidentListItem Summary,
    Guid TriggeringSmsMessageId,
    string RawSmsBody,
    string SenderNumberRaw,
    string? SenderNumberNormalized,
    string? ProtocolVersion,
    SmsProcessingStatus SmsProcessingStatus,
    string? AcknowledgedBy,
    string? AcknowledgementNotes,
    string? ResolvedBy,
    string? ResolutionNotes,
    IReadOnlyList<AuditEntryItem> History);

public sealed record IncidentAcknowledgementRequest(string? Notes);

public sealed record IncidentResolutionRequest(string Notes);
