using System.Globalization;
using System.Text;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Exporting;
using GasGuard.Application.Models;
using GasGuard.Application.Time;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using GasGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GasGuard.Infrastructure.Reporting;

public sealed class ReportService(
    IDbContextFactory<GasGuardDbContext> contextFactory,
    IClock clock,
    IUserContext userContext,
    IAuditLogService auditLog,
    ILogger<ReportService> logger) : IReportService
{
    public async Task<ReportSummary> GetSummaryAsync(
        ReportCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ValidateCriteria(criteria);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var incidents = BuildIncidentQuery(context, criteria);

        var total = await incidents.CountAsync(cancellationToken);
        var simulated = await incidents.CountAsync(x => x.IsSimulated, cancellationToken);

        var severityRows = await incidents
            .GroupBy(x => x.Severity)
            .Select(group => new { Severity = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var severityCounts = new List<SeverityCount>
        {
            new(Severity.Critical, severityRows.FirstOrDefault(x => x.Severity == Severity.Critical)?.Count ?? 0),
            new(Severity.Warning, severityRows.FirstOrDefault(x => x.Severity == Severity.Warning)?.Count ?? 0)
        };

        var deviceRows = await incidents
            .GroupBy(x => new
            {
                x.DeviceId,
                x.DeviceCodeSnapshot,
                x.DeviceNameSnapshot,
                x.LocationSnapshot
            })
            .Select(group => new
            {
                group.Key.DeviceId,
                group.Key.DeviceCodeSnapshot,
                group.Key.DeviceNameSnapshot,
                group.Key.LocationSnapshot,
                Count = group.Count(),
                Critical = group.Count(x => x.Severity == Severity.Critical),
                Warning = group.Count(x => x.Severity == Severity.Warning)
            })
            .OrderByDescending(x => x.Count)
            .ToListAsync(cancellationToken);

        var trendRows = await incidents
            .Select(x => new { x.ReceivedAtUtc, x.Severity })
            .ToListAsync(cancellationToken);
        var trend = trendRows
            .GroupBy(x => clock.ToLagosDate(x.ReceivedAtUtc))
            .Select(group => new IncidentTrendPoint(
                group.Key,
                group.Count(x => x.Severity == Severity.Critical),
                group.Count(x => x.Severity == Severity.Warning),
                group.Count()))
            .OrderBy(x => x.LocalDate)
            .ToArray();

        var failureQuery = context.CommunicationLogs
            .AsNoTracking()
            .Where(x => x.TimestampUtc >= criteria.FromUtc &&
                        x.TimestampUtc < criteria.ToUtc &&
                        (x.Outcome == CommunicationOutcome.Failure ||
                         x.Outcome == CommunicationOutcome.Rejected ||
                         x.Outcome == CommunicationOutcome.Timeout));
        if (!criteria.IncludeSimulated)
        {
            failureQuery = failureQuery.Where(x => !x.IsSimulated);
        }

        if (criteria.DeviceId.HasValue)
        {
            failureQuery = failureQuery.Where(x => x.DeviceId == criteria.DeviceId.Value);
        }

        var failureRows = await failureQuery
            .Select(x => new { x.Kind, x.Outcome, x.TimestampUtc })
            .ToListAsync(cancellationToken);
        var failureSummary = failureRows
            .GroupBy(x => new { x.Kind, x.Outcome })
            .Select(group => new CommunicationFailureSummaryItem(
                group.Key.Kind,
                group.Key.Outcome,
                group.Count(),
                group.Max(x => x.TimestampUtc)))
            .OrderByDescending(x => x.Count)
            .ToArray();

        var activity = await BuildDetectorActivityAsync(context, criteria, cancellationToken);

        var deviceIncidents = deviceRows
            .Select(x => new DeviceIncidentCount(
                x.DeviceId,
                x.DeviceCodeSnapshot,
                x.DeviceNameSnapshot,
                x.LocationSnapshot,
                x.Count,
                x.Critical,
                x.Warning))
            .ToArray();

        return new ReportSummary(
            criteria,
            total,
            simulated,
            severityCounts,
            deviceIncidents,
            trend,
            failureRows.Count,
            failureSummary,
            activity,
            clock.UtcNow);
    }

    public async Task<OperationResult> ExportIncidentsCsvAsync(
        ReportCriteria criteria,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ValidateCriteria(criteria);
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var rows = await BuildIncidentQuery(context, criteria)
                .OrderByDescending(x => x.ReceivedAtUtc)
                .ToListAsync(cancellationToken);

            var csv = CsvBuilder.Build(
                [
                    "IncidentId", "Severity", "Status", "DeviceCode", "DeviceName", "Location",
                    "OccurredAtUtc", "OccurredAtLagos", "TimeSource", "ReceivedAtUtc", "ReceivedAtLagos",
                    "AcknowledgedAtUtc", "AcknowledgedAtLagos", "AcknowledgedBy",
                    "ResolvedAtUtc", "ResolvedAtLagos", "ResolvedBy", "RawReading", "Unit",
                    "SequenceNumber", "Simulated", "AcknowledgementNotes", "ResolutionNotes"
                ],
                rows.Select(x => new string?[]
                {
                    x.Id.ToString(),
                    x.Severity.ToString(),
                    x.Status.ToString(),
                    x.DeviceCodeSnapshot,
                    x.DeviceNameSnapshot,
                    x.LocationSnapshot,
                    FormatUtc(x.OccurredAtUtc),
                    AfricaLagosTime.Format(x.OccurredAtUtc),
                    x.OccurredTimeSource.ToString(),
                    FormatUtc(x.ReceivedAtUtc),
                    AfricaLagosTime.Format(x.ReceivedAtUtc),
                    FormatUtc(x.AcknowledgedAtUtc),
                    FormatLagos(x.AcknowledgedAtUtc),
                    x.AcknowledgedBy,
                    FormatUtc(x.ResolvedAtUtc),
                    FormatLagos(x.ResolvedAtUtc),
                    x.ResolvedBy,
                    x.Reading?.ToString(CultureInfo.InvariantCulture),
                    x.Unit,
                    x.SequenceNumber?.ToString(CultureInfo.InvariantCulture),
                    x.IsSimulated ? "Yes" : "No",
                    x.AcknowledgementNotes,
                    x.ResolutionNotes
                }));

            await WriteCsvAsync(filePath, csv, cancellationToken);
            await RecordExportAuditAsync("IncidentCsv", filePath, rows.Count, cancellationToken);
            return OperationResult.Ok($"Exported {rows.Count} incident(s) to {Path.GetFileName(filePath)}.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Incident CSV export failed.");
            return OperationResult.Fail($"The incident CSV could not be exported: {ex.Message}");
        }
    }

    public async Task<OperationResult> ExportCommunicationFailuresCsvAsync(
        ReportCriteria criteria,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ValidateCriteria(criteria);
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var query = context.CommunicationLogs
                .AsNoTracking()
                .Where(x => x.TimestampUtc >= criteria.FromUtc &&
                            x.TimestampUtc < criteria.ToUtc &&
                            (x.Outcome == CommunicationOutcome.Failure ||
                             x.Outcome == CommunicationOutcome.Rejected ||
                             x.Outcome == CommunicationOutcome.Timeout));

            if (!criteria.IncludeSimulated)
            {
                query = query.Where(x => !x.IsSimulated);
            }

            if (criteria.DeviceId.HasValue)
            {
                query = query.Where(x => x.DeviceId == criteria.DeviceId.Value);
            }

            var rows = await query.OrderByDescending(x => x.TimestampUtc).ToListAsync(cancellationToken);
            var csv = CsvBuilder.Build(
                [
                    "TimestampUtc", "TimestampLagos", "Direction", "Kind", "Outcome", "Summary",
                    "Detail", "ErrorCode", "RecipientNumber", "ProviderReference", "Simulated"
                ],
                rows.Select(x => new string?[]
                {
                    FormatUtc(x.TimestampUtc),
                    AfricaLagosTime.Format(x.TimestampUtc),
                    x.Direction.ToString(),
                    x.Kind.ToString(),
                    x.Outcome.ToString(),
                    x.Summary,
                    x.Detail,
                    x.ErrorCode,
                    x.RecipientNumber,
                    x.ProviderReference,
                    x.IsSimulated ? "Yes" : "No"
                }));

            await WriteCsvAsync(filePath, csv, cancellationToken);
            await RecordExportAuditAsync("CommunicationCsv", filePath, rows.Count, cancellationToken);
            return OperationResult.Ok($"Exported {rows.Count} communication failure row(s) to {Path.GetFileName(filePath)}.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Communication failure CSV export failed.");
            return OperationResult.Fail($"The communication failure CSV could not be exported: {ex.Message}");
        }
    }

    public async Task<OperationResult> ExportDetectorActivityCsvAsync(
        ReportCriteria criteria,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ValidateCriteria(criteria);
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var activity = await BuildDetectorActivityAsync(context, criteria, cancellationToken);
            var csv = CsvBuilder.Build(
                [
                    "DeviceCode", "DeviceName", "Location", "Active", "ProcessedMessages",
                    "FailedMessages", "Incidents", "LastSuccessfulCommunicationUtc",
                    "LastSuccessfulCommunicationLagos", "Demo"
                ],
                activity.Select(x => new string?[]
                {
                    x.DeviceCode,
                    x.DeviceName,
                    x.Location,
                    x.IsActive ? "Yes" : "No",
                    x.ProcessedMessageCount.ToString(CultureInfo.InvariantCulture),
                    x.FailedMessageCount.ToString(CultureInfo.InvariantCulture),
                    x.IncidentCount.ToString(CultureInfo.InvariantCulture),
                    FormatUtc(x.LastSuccessfulCommunicationUtc),
                    FormatLagos(x.LastSuccessfulCommunicationUtc),
                    x.IsDemo ? "Yes" : "No"
                }));

            await WriteCsvAsync(filePath, csv, cancellationToken);
            await RecordExportAuditAsync("DetectorActivityCsv", filePath, activity.Count, cancellationToken);
            return OperationResult.Ok($"Exported {activity.Count} detector activity row(s) to {Path.GetFileName(filePath)}.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Detector activity CSV export failed.");
            return OperationResult.Fail($"The detector activity CSV could not be exported: {ex.Message}");
        }
    }

    private static IQueryable<Incident> BuildIncidentQuery(GasGuardDbContext context, ReportCriteria criteria)
    {
        var incidents = context.Incidents
            .AsNoTracking()
            .Where(x => x.ReceivedAtUtc >= criteria.FromUtc && x.ReceivedAtUtc < criteria.ToUtc);

        if (!criteria.IncludeSimulated)
        {
            incidents = incidents.Where(x => !x.IsSimulated);
        }

        if (criteria.DeviceId.HasValue)
        {
            incidents = incidents.Where(x => x.DeviceId == criteria.DeviceId.Value);
        }

        if (criteria.Severity.HasValue)
        {
            incidents = incidents.Where(x => x.Severity == criteria.Severity.Value);
        }

        return incidents;
    }

    private async Task<IReadOnlyList<DetectorActivityItem>> BuildDetectorActivityAsync(
        GasGuardDbContext context,
        ReportCriteria criteria,
        CancellationToken cancellationToken)
    {
        var devicesQuery = context.GasDevices.AsNoTracking();
        if (criteria.DeviceId.HasValue)
        {
            devicesQuery = devicesQuery.Where(x => x.Id == criteria.DeviceId.Value);
        }

        var devices = await devicesQuery.OrderByDescending(x => x.IsActive).ThenBy(x => x.DeviceCode).ToListAsync(cancellationToken);

        var messageQuery = context.SmsMessages
            .AsNoTracking()
            .Where(x => x.ReceivedAtUtc >= criteria.FromUtc &&
                        x.ReceivedAtUtc < criteria.ToUtc &&
                        x.DeviceId != null);
        if (!criteria.IncludeSimulated)
        {
            messageQuery = messageQuery.Where(x => !x.IsSimulated);
        }

        if (criteria.DeviceId.HasValue)
        {
            messageQuery = messageQuery.Where(x => x.DeviceId == criteria.DeviceId.Value);
        }

        var messageRows = await messageQuery
            .Select(x => new { x.DeviceId, x.ProcessingStatus })
            .ToListAsync(cancellationToken);

        var incidentQuery = BuildIncidentQuery(context, criteria);
        var incidentRows = await incidentQuery
            .Select(x => new { x.DeviceId })
            .ToListAsync(cancellationToken);

        var messageGroups = messageRows
            .GroupBy(x => x.DeviceId!.Value)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Processed = group.Count(x => x.ProcessingStatus == SmsProcessingStatus.Processed),
                    Failed = group.Count(x => x.ProcessingStatus is SmsProcessingStatus.Invalid
                        or SmsProcessingStatus.UnknownDevice
                        or SmsProcessingStatus.UnauthorizedSender
                        or SmsProcessingStatus.SimulationRejected
                        or SmsProcessingStatus.Failed)
                });
        var incidentGroups = incidentRows
            .GroupBy(x => x.DeviceId)
            .ToDictionary(group => group.Key, group => group.Count());

        return devices.Select(device =>
        {
            messageGroups.TryGetValue(device.Id, out var messages);
            incidentGroups.TryGetValue(device.Id, out var incidentCount);
            return new DetectorActivityItem(
                device.Id,
                device.DeviceCode,
                device.DisplayName,
                device.InstallationLocation,
                device.IsActive,
                messages?.Processed ?? 0,
                messages?.Failed ?? 0,
                incidentCount,
                device.LastSuccessfulCommunicationUtc,
                device.IsDemo);
        }).ToArray();
    }

    private async Task WriteCsvAsync(string filePath, string csv, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The selected export folder does not exist.");
        }

        await File.WriteAllTextAsync(fullPath, csv, new UTF8Encoding(true), cancellationToken);
    }

    private async Task RecordExportAuditAsync(
        string reportType,
        string filePath,
        int rowCount,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditLog.RecordAsync(new AuditEntryDraft(
                userContext.OperatorName,
                "ReportExported",
                "Report",
                reportType,
                AuditOutcome.Success,
                $"{reportType} exported with {rowCount} row(s) to {Path.GetFileName(filePath)}."),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Report export audit logging failed.");
        }
    }

    private static void ValidateCriteria(ReportCriteria criteria)
    {
        if (criteria.ToUtc <= criteria.FromUtc)
        {
            throw new ArgumentException("The report end date must be later than the start date.");
        }

        if (criteria.ToUtc - criteria.FromUtc > TimeSpan.FromDays(3660))
        {
            throw new ArgumentException("The report period cannot exceed 10 years.");
        }
    }

    private static string FormatUtc(DateTime? value) =>
        value.HasValue ? value.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) : string.Empty;

    private static string FormatLagos(DateTime? value) =>
        value.HasValue ? AfricaLagosTime.Format(value.Value) : string.Empty;
}
