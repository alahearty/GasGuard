using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GasGuard.Infrastructure.Persistence.Repositories;

internal sealed class EfIncidentRepository(GasGuardDbContext context) : IIncidentRepository
{
    public async Task AddAsync(Incident incident, CancellationToken cancellationToken = default) =>
        await context.Incidents.AddAsync(incident, cancellationToken);

    public Task<Incident?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Incidents
            .Include(x => x.Device)
            .Include(x => x.TriggeringSmsMessage)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Incident?> GetByDedupKeyAsync(string dedupKey, CancellationToken cancellationToken = default) =>
        context.Incidents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.DedupKey == dedupKey, cancellationToken);

    public Task<Incident?> GetRecentForDedupAsync(
        Guid deviceId,
        Severity severity,
        long? sequenceNumber,
        string fingerprint,
        bool isSimulated,
        DateTime sinceUtc,
        CancellationToken cancellationToken = default)
    {
        var query = context.Incidents
            .AsNoTracking()
            .Where(x => x.DeviceId == deviceId &&
                        x.Severity == severity &&
                        x.IsSimulated == isSimulated &&
                        x.ReceivedAtUtc >= sinceUtc);

        if (sequenceNumber.HasValue)
        {
            query = query.Where(x => x.SequenceNumber == sequenceNumber.Value);
        }
        else
        {
            query = query.Where(x => x.TriggeringSmsMessage.Fingerprint == fingerprint);
        }

        return query
            .OrderByDescending(x => x.ReceivedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PagedResult<Incident>> QueryAsync(IncidentQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        IQueryable<Incident> incidents = context.Incidents
            .AsNoTracking()
            .Include(x => x.Device);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            incidents = incidents.Where(x =>
                x.DeviceCodeSnapshot.Contains(search) ||
                x.DeviceNameSnapshot.Contains(search) ||
                x.LocationSnapshot.Contains(search));
        }

        if (query.FromUtc.HasValue)
        {
            incidents = incidents.Where(x => x.ReceivedAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            incidents = incidents.Where(x => x.ReceivedAtUtc < query.ToUtc.Value);
        }

        if (query.Severity.HasValue)
        {
            incidents = incidents.Where(x => x.Severity == query.Severity.Value);
        }

        if (query.DeviceId.HasValue)
        {
            incidents = incidents.Where(x => x.DeviceId == query.DeviceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Location))
        {
            var location = query.Location.Trim();
            incidents = incidents.Where(x => x.LocationSnapshot.Contains(location));
        }

        if (query.Status.HasValue)
        {
            incidents = incidents.Where(x => x.Status == query.Status.Value);
        }

        var total = await incidents.CountAsync(cancellationToken);
        var items = await incidents
            .OrderByDescending(x => x.ReceivedAtUtc)
            .ThenByDescending(x => x.Severity)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Incident>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<Incident>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        await context.Incidents
            .AsNoTracking()
            .Include(x => x.Device)
            .Where(x => x.Status != IncidentStatus.Resolved)
            .OrderByDescending(x => x.Severity)
            .ThenByDescending(x => x.ReceivedAtUtc)
            .ToListAsync(cancellationToken);
}
