using GasGuard.Application.Abstractions;
using GasGuard.Domain.Entities;

namespace GasGuard.Infrastructure.Persistence.Repositories;

internal sealed class EfCommunicationLogRepository(GasGuardDbContext context) : ICommunicationLogRepository
{
    public async Task AddAsync(CommunicationLog log, CancellationToken cancellationToken = default) =>
        await context.CommunicationLogs.AddAsync(log, cancellationToken);
}
