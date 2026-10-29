using GasGuard.Application.Abstractions;
using GasGuard.Application.Auditing;
using GasGuard.Application.Models;
using GasGuard.Domain.Entities;
using GasGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GasGuard.Infrastructure.Auditing;

public sealed class AuditLogService(
    IDbContextFactory<GasGuardDbContext> contextFactory,
    IClock clock,
    ILogger<AuditLogService> logger) : IAuditLogService
{
    public async Task RecordAsync(AuditEntryDraft entry, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            context.AuditLogs.Add(AuditLogFactory.Create(entry, clock.UtcNow));
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audit entry {Action} for {EntityType} could not be persisted.", entry.Action, entry.EntityType);
        }
    }

    public async Task<PagedResult<AuditEntryItem>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        IQueryable<AuditLog> logs = context.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            logs = logs.Where(x =>
                x.Details.Contains(search) ||
                x.Actor.Contains(search) ||
                x.EntityId!.Contains(search));
        }

        if (query.FromUtc.HasValue)
        {
            logs = logs.Where(x => x.TimestampUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            logs = logs.Where(x => x.TimestampUtc < query.ToUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim();
            logs = logs.Where(x => x.Action.Contains(action));
        }

        if (query.Outcome.HasValue)
        {
            logs = logs.Where(x => x.Outcome == query.Outcome.Value);
        }

        var total = await logs.CountAsync(cancellationToken);
        var items = await logs
            .OrderByDescending(x => x.TimestampUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AuditEntryItem(
                x.Id,
                x.TimestampUtc,
                x.Actor,
                x.Action,
                x.EntityType,
                x.EntityId,
                x.Outcome,
                x.Details))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditEntryItem>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<AuditEntryItem>> GetForEntityAsync(
        string entityType,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.AuditLogs
            .AsNoTracking()
            .Where(x => x.EntityType == entityType && x.EntityId == entityId)
            .OrderByDescending(x => x.TimestampUtc)
            .Take(200)
            .Select(x => new AuditEntryItem(
                x.Id,
                x.TimestampUtc,
                x.Actor,
                x.Action,
                x.EntityType,
                x.EntityId,
                x.Outcome,
                x.Details))
            .ToListAsync(cancellationToken);
    }

}
