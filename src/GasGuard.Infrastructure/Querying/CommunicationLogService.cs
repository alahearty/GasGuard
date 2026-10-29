using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using GasGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GasGuard.Infrastructure.Querying;

public sealed class CommunicationLogService(IDbContextFactory<GasGuardDbContext> contextFactory) : ICommunicationLogService
{
    public async Task<PagedResult<CommunicationLogItem>> QueryAsync(
        CommunicationLogQuery query,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        IQueryable<CommunicationLog> logs = context.CommunicationLogs.AsNoTracking();

        if (query.FromUtc.HasValue)
        {
            logs = logs.Where(x => x.TimestampUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            logs = logs.Where(x => x.TimestampUtc < query.ToUtc.Value);
        }

        if (query.Direction.HasValue)
        {
            logs = logs.Where(x => x.Direction == query.Direction.Value);
        }

        if (query.Outcome.HasValue)
        {
            logs = logs.Where(x => x.Outcome == query.Outcome.Value);
        }

        if (query.Kind.HasValue)
        {
            logs = logs.Where(x => x.Kind == query.Kind.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            logs = logs.Where(x =>
                x.Summary.Contains(search) ||
                (x.Detail != null && x.Detail.Contains(search)) ||
                (x.ErrorCode != null && x.ErrorCode.Contains(search)) ||
                (x.RecipientNumber != null && x.RecipientNumber.Contains(search)));
        }

        var total = await logs.CountAsync(cancellationToken);
        var items = await logs
            .OrderByDescending(x => x.TimestampUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new CommunicationLogItem(
                x.Id,
                x.TimestampUtc,
                x.Direction,
                x.Kind,
                x.Outcome,
                x.Summary,
                x.Detail,
                x.ErrorCode,
                x.RecipientNumber,
                x.ProviderReference,
                x.IsSimulated))
            .ToListAsync(cancellationToken);

        return new PagedResult<CommunicationLogItem>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<CommunicationLogItem>> GetOutgoingAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.CommunicationLogs
            .AsNoTracking()
            .Where(x => x.Direction == CommunicationDirection.Outbound && x.Kind == CommunicationKind.SmsSend)
            .OrderByDescending(x => x.TimestampUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(x => new CommunicationLogItem(
                x.Id,
                x.TimestampUtc,
                x.Direction,
                x.Kind,
                x.Outcome,
                x.Summary,
                x.Detail,
                x.ErrorCode,
                x.RecipientNumber,
                x.ProviderReference,
                x.IsSimulated))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CommunicationLogItem>> GetRecentFailuresAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.CommunicationLogs
            .AsNoTracking()
            .Where(x => x.Outcome == CommunicationOutcome.Failure ||
                        x.Outcome == CommunicationOutcome.Rejected ||
                        x.Outcome == CommunicationOutcome.Timeout)
            .OrderByDescending(x => x.TimestampUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(x => new CommunicationLogItem(
                x.Id,
                x.TimestampUtc,
                x.Direction,
                x.Kind,
                x.Outcome,
                x.Summary,
                x.Detail,
                x.ErrorCode,
                x.RecipientNumber,
                x.ProviderReference,
                x.IsSimulated))
            .ToListAsync(cancellationToken);
    }
}
