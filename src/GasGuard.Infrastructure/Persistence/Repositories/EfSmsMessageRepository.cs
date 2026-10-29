using GasGuard.Application.Abstractions;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GasGuard.Infrastructure.Persistence.Repositories;

internal sealed class EfSmsMessageRepository(GasGuardDbContext context) : ISmsMessageRepository
{
    public async Task AddAsync(SmsMessage message, CancellationToken cancellationToken = default) =>
        await context.SmsMessages.AddAsync(message, cancellationToken);

    public Task<SmsMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.SmsMessages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> FingerprintExistsAsync(string fingerprint, CancellationToken cancellationToken = default) =>
        context.SmsMessages.AnyAsync(x => x.Fingerprint == fingerprint, cancellationToken);

    public Task<SmsMessage?> GetLatestProcessedByFingerprintSinceAsync(
        string fingerprint,
        bool isSimulated,
        DateTime sinceUtc,
        CancellationToken cancellationToken = default) =>
        context.SmsMessages
            .AsNoTracking()
            .Where(x => x.Fingerprint == fingerprint &&
                        x.IsSimulated == isSimulated &&
                        x.ProcessingStatus == SmsProcessingStatus.Processed &&
                        x.ReceivedAtUtc >= sinceUtc)
            .OrderByDescending(x => x.ReceivedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<SmsMessage?> GetLatestBySourceReferenceAsync(string sourceReference, CancellationToken cancellationToken = default) =>
        context.SmsMessages
            .AsNoTracking()
            .Where(x => x.SourceReference == sourceReference)
            .OrderByDescending(x => x.StoredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SmsMessage>> GetRecentAsync(int take, CancellationToken cancellationToken = default) =>
        await context.SmsMessages
            .AsNoTracking()
            .OrderByDescending(x => x.ReceivedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);
}
