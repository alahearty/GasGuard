using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GasGuard.Infrastructure.Querying;

public sealed class SmsEvidenceService(IDbContextFactory<GasGuardDbContext> contextFactory) : ISmsEvidenceService
{
    public async Task<IReadOnlyList<SmsEvidenceItem>> GetRecentAsync(
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SmsMessages
            .AsNoTracking()
            .OrderByDescending(x => x.ReceivedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(x => new SmsEvidenceItem(
                x.Id,
                x.ReceivedAtUtc,
                x.SenderNumberRaw,
                x.SenderNumberNormalized,
                x.DeviceCode,
                x.ProcessingStatus,
                x.ProcessingDetail,
                x.EventCode,
                x.Severity,
                x.Reading,
                x.Unit,
                x.SequenceNumber,
                x.IsSimulated,
                x.BodyRaw))
            .ToListAsync(cancellationToken);
    }
}
