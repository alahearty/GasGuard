using GasGuard.Application.Abstractions;
using GasGuard.Application.Auditing;
using GasGuard.Application.Models;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using GasGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GasGuard.Infrastructure.Maintenance;

public sealed class RetentionService(
    IDbContextFactory<GasGuardDbContext> contextFactory,
    IClock clock,
    IUserContext userContext,
    ILogger<RetentionService> logger) : IRetentionService
{
    public async Task<RetentionResult> ApplyRetentionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var preferences = await context.Preferences.FirstAsync(
                x => x.Id == AppPreferences.SingletonId,
                cancellationToken);

            if (preferences.DataRetentionDays == 0)
            {
                return new RetentionResult(true, "Retention cleanup is disabled (retention = 0).", 0, 0, 0);
            }

            var cutoff = clock.UtcNow.AddDays(-preferences.DataRetentionDays);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var deletedLogs = await context.CommunicationLogs
                .Where(x => x.TimestampUtc < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            var deletedAudit = await context.AuditLogs
                .Where(x => x.TimestampUtc < cutoff && x.EntityType != "Incident")
                .ExecuteDeleteAsync(cancellationToken);

            var deletedMessages = await context.SmsMessages
                .Where(x => x.ReceivedAtUtc < cutoff &&
                            !context.Incidents.Any(i => i.TriggeringSmsMessageId == x.Id))
                .ExecuteDeleteAsync(cancellationToken);

            var message =
                $"Retention cleanup removed {deletedMessages} SMS record(s), {deletedLogs} communication log(s) and {deletedAudit} non-incident audit row(s) older than {cutoff:yyyy-MM-dd}. Incidents, linked SMS evidence and incident audit history were preserved.";

            context.AuditLogs.Add(AuditLogFactory.Create(new AuditEntryDraft(
                userContext.OperatorName,
                "RetentionCleanup",
                "Database",
                null,
                AuditOutcome.Success,
                message), clock.UtcNow));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new RetentionResult(true, message, deletedLogs, deletedAudit, deletedMessages);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Retention cleanup failed.");
            return new RetentionResult(false, $"Retention cleanup failed: {ex.Message}", 0, 0, 0);
        }
    }
}
