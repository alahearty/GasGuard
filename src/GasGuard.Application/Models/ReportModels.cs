using GasGuard.Domain;

namespace GasGuard.Application.Models;

public sealed record ReportCriteria(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? DeviceId = null,
    Severity? Severity = null,
    bool IncludeSimulated = true);

public sealed record SeverityCount(Severity Severity, int Count);

public sealed record DeviceIncidentCount(
    Guid DeviceId,
    string DeviceCode,
    string DeviceName,
    string Location,
    int IncidentCount,
    int CriticalCount,
    int WarningCount);

public sealed record IncidentTrendPoint(DateOnly LocalDate, int Critical, int Warning, int Total);

public sealed record DetectorActivityItem(
    Guid DeviceId,
    string DeviceCode,
    string DeviceName,
    string Location,
    bool IsActive,
    int ProcessedMessageCount,
    int FailedMessageCount,
    int IncidentCount,
    DateTime? LastSuccessfulCommunicationUtc,
    bool IsDemo);

public sealed record CommunicationFailureSummaryItem(
    CommunicationKind Kind,
    CommunicationOutcome Outcome,
    int Count,
    DateTime? LastOccurrenceUtc);

public sealed record ReportSummary(
    ReportCriteria Criteria,
    int TotalIncidents,
    int SimulatedIncidentCount,
    IReadOnlyList<SeverityCount> IncidentsBySeverity,
    IReadOnlyList<DeviceIncidentCount> IncidentsByDevice,
    IReadOnlyList<IncidentTrendPoint> Trend,
    int CommunicationFailureCount,
    IReadOnlyList<CommunicationFailureSummaryItem> CommunicationFailures,
    IReadOnlyList<DetectorActivityItem> DetectorActivity,
    DateTime GeneratedAtUtc);
