using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using GasGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GasGuard.Infrastructure.Querying;

public sealed class DashboardService(
    IDbContextFactory<GasGuardDbContext> contextFactory,
    IGsmModemService modemService,
    IBackupService backupService,
    IClock clock) : IDashboardService
{
    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var preferences = await context.Preferences
            .AsNoTracking()
            .FirstAsync(x => x.Id == AppPreferences.SingletonId, cancellationToken);
        var staleAfter = TimeSpan.FromMinutes(preferences.StaleAfterMinutes);

        var activeDetectorCount = await context.GasDevices.CountAsync(x => x.IsActive, cancellationToken);
        var unresolvedCritical = await context.Incidents.CountAsync(
            x => x.Severity == Severity.Critical && x.Status != IncidentStatus.Resolved,
            cancellationToken);
        var unresolvedWarning = await context.Incidents.CountAsync(
            x => x.Severity == Severity.Warning && x.Status != IncidentStatus.Resolved,
            cancellationToken);

        var lastSuccessfulDevice = await context.GasDevices
            .AsNoTracking()
            .Where(x => x.LastSuccessfulCommunicationUtc != null)
            .OrderByDescending(x => x.LastSuccessfulCommunicationUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var devices = await context.GasDevices
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DeviceCode)
            .ToListAsync(cancellationToken);

        var detectorItems = devices.Select(x => new DashboardDeviceItem(
            x.Id,
            x.DeviceCode,
            x.DisplayName,
            x.InstallationLocation,
            x.GetOperationalState(now, staleAfter),
            x.LastReportedState,
            x.LastSuccessfulCommunicationUtc,
            x.LastReading,
            x.LastReadingUnit,
            x.LastCommunicationErrorUtc,
            x.LastCommunicationError,
            x.IsDemo)).ToArray();

        var alerts = await context.Incidents
            .AsNoTracking()
            .OrderByDescending(x => x.ReceivedAtUtc)
            .Take(8)
            .Select(x => new RecentAlertItem(
                x.Id,
                x.DeviceCodeSnapshot,
                x.LocationSnapshot,
                x.Severity,
                x.Status,
                x.OccurredAtUtc,
                x.ReceivedAtUtc,
                x.IsSimulated))
            .ToListAsync(cancellationToken);

        var failures = await context.CommunicationLogs
            .AsNoTracking()
            .Where(x => x.Outcome == CommunicationOutcome.Failure ||
                        x.Outcome == CommunicationOutcome.Rejected ||
                        x.Outcome == CommunicationOutcome.Timeout)
            .OrderByDescending(x => x.TimestampUtc)
            .Take(8)
            .Select(x => new CommunicationFailureItem(
                x.Id,
                x.TimestampUtc,
                x.Summary,
                x.Detail,
                x.ErrorCode,
                x.IsSimulated))
            .ToListAsync(cancellationToken);

        var containsSimulated = modemService.Status.IsSimulated ||
                                await context.Incidents.AnyAsync(x => x.IsSimulated, cancellationToken) ||
                                await context.SmsMessages.AnyAsync(x => x.IsSimulated, cancellationToken) ||
                                await context.GasDevices.AnyAsync(x => x.IsDemo, cancellationToken);
        var health = await backupService.GetDatabaseHealthAsync(cancellationToken);

        return new DashboardSnapshot(
            activeDetectorCount,
            unresolvedCritical,
            unresolvedWarning,
            lastSuccessfulDevice?.LastSuccessfulCommunicationUtc,
            lastSuccessfulDevice?.DeviceCode,
            lastSuccessfulDevice?.LastReportedState,
            lastSuccessfulDevice?.LastReading,
            lastSuccessfulDevice?.LastReadingUnit,
            modemService.Status,
            health,
            preferences.LastBackupAtUtc,
            preferences.LastBackupPath,
            detectorItems,
            alerts,
            failures,
            containsSimulated,
            staleAfter,
            now);
    }
}
