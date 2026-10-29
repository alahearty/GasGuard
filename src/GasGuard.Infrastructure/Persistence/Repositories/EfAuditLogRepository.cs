using GasGuard.Application.Abstractions;
using GasGuard.Domain.Entities;

namespace GasGuard.Infrastructure.Persistence.Repositories;

internal sealed class EfAuditLogRepository(GasGuardDbContext context) : IAuditLogRepository
{
    public async Task AddAsync(AuditLog log, CancellationToken cancellationToken = default) =>
        await context.AuditLogs.AddAsync(log, cancellationToken);
}
