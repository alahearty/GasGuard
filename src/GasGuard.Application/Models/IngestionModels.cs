using GasGuard.Domain;

namespace GasGuard.Application.Models;

public sealed record SmsEvidenceItem(
    Guid Id,
    DateTime ReceivedAtUtc,
    string SenderNumberRaw,
    string? SenderNumberNormalized,
    string? DeviceCode,
    SmsProcessingStatus ProcessingStatus,
    string? ProcessingDetail,
    GasEventCode EventCode,
    Severity Severity,
    int? Reading,
    string? Unit,
    long? SequenceNumber,
    bool IsSimulated,
    string BodyRaw);

public sealed record IngestionResult(
    bool Persisted,
    SmsProcessingStatus Status,
    Guid? MessageId,
    Guid? IncidentId,
    bool Duplicate,
    string Message,
    string? ErrorCode = null)
{
    public bool IncidentCreated => IncidentId.HasValue;
}
