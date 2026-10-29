using GasGuard.Domain;

namespace GasGuard.Application.Models;

public sealed record DeviceInput(
    string DeviceCode,
    string DisplayName,
    string InstallationLocation,
    string ExpectedSenderNumber,
    DetectorType DetectorType,
    string? Description,
    DateOnly? InstallationDate,
    bool IsActive,
    string? MaintenanceNotes);

public sealed record DeviceListItem(
    Guid Id,
    string DeviceCode,
    string DisplayName,
    string InstallationLocation,
    string ExpectedSenderNumber,
    DetectorType DetectorType,
    bool IsActive,
    DateOnly? InstallationDate,
    string? Description,
    string? MaintenanceNotes,
    DateTime? LastSuccessfulCommunicationUtc,
    DeviceReportedState LastReportedState,
    DeviceOperationalState OperationalState,
    int? LastReading,
    string? LastReadingUnit,
    long? LastSequenceNumber,
    DateTime? LastCommunicationErrorUtc,
    string? LastCommunicationError,
    bool IsDemo,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
