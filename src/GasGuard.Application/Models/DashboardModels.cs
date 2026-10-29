using GasGuard.Contracts.Modem;
using GasGuard.Domain;

namespace GasGuard.Application.Models;

public sealed record DashboardSnapshot(
    int ActiveDetectorCount,
    int UnresolvedCriticalCount,
    int UnresolvedWarningCount,
    DateTime? LastSuccessfulCommunicationUtc,
    string? LastSuccessfulDeviceCode,
    DeviceReportedState? LastSuccessfulState,
    int? LastSuccessfulReading,
    string? LastSuccessfulUnit,
    ModemStatus ModemStatus,
    DatabaseHealthInfo DatabaseHealth,
    DateTime? LastBackupAtUtc,
    string? LastBackupPath,
    IReadOnlyList<DashboardDeviceItem> Detectors,
    IReadOnlyList<RecentAlertItem> RecentAlerts,
    IReadOnlyList<CommunicationFailureItem> RecentCommunicationFailures,
    bool ContainsSimulatedData,
    TimeSpan StaleAfter,
    DateTime GeneratedAtUtc);

public sealed record DashboardDeviceItem(
    Guid DeviceId,
    string DeviceCode,
    string DisplayName,
    string Location,
    DeviceOperationalState OperationalState,
    DeviceReportedState LastReportedState,
    DateTime? LastSuccessfulCommunicationUtc,
    int? LastReading,
    string? Unit,
    DateTime? LastCommunicationErrorUtc,
    string? LastCommunicationError,
    bool IsDemo);

public sealed record RecentAlertItem(
    Guid IncidentId,
    string DeviceCode,
    string Location,
    Severity Severity,
    IncidentStatus Status,
    DateTime OccurredAtUtc,
    DateTime ReceivedAtUtc,
    bool IsSimulated);

public sealed record CommunicationFailureItem(
    Guid Id,
    DateTime TimestampUtc,
    string Summary,
    string? Detail,
    string? ErrorCode,
    bool IsSimulated);
