using GasGuard.Application.Abstractions;
using GasGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GasGuard.Infrastructure.Persistence.Repositories;

internal sealed class EfDeviceRepository(GasGuardDbContext context) : IDeviceRepository
{
    public Task<GasDevice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.GasDevices.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<GasDevice?> GetByCodeAsync(string normalizedCode, CancellationToken cancellationToken = default) =>
        context.GasDevices.FirstOrDefaultAsync(x => x.DeviceCode == normalizedCode, cancellationToken);

    public Task<GasDevice?> GetBySenderAsync(string normalizedSender, CancellationToken cancellationToken = default) =>
        context.GasDevices.FirstOrDefaultAsync(x => x.ExpectedSenderNumber == normalizedSender, cancellationToken);

    public async Task<IReadOnlyList<GasDevice>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = context.GasDevices.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        return await query
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.DeviceCode)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> CodeExistsAsync(string normalizedCode, Guid? excludingDeviceId, CancellationToken cancellationToken = default) =>
        context.GasDevices.AnyAsync(
            x => x.DeviceCode == normalizedCode &&
                 (!excludingDeviceId.HasValue || x.Id != excludingDeviceId.Value),
            cancellationToken);

    public Task<bool> SenderExistsAsync(string normalizedSender, Guid? excludingDeviceId, CancellationToken cancellationToken = default) =>
        context.GasDevices.AnyAsync(
            x => x.ExpectedSenderNumber == normalizedSender &&
                 (!excludingDeviceId.HasValue || x.Id != excludingDeviceId.Value),
            cancellationToken);

    public Task<int> CountActiveAsync(CancellationToken cancellationToken = default) =>
        context.GasDevices.CountAsync(x => x.IsActive, cancellationToken);

    public async Task AddAsync(GasDevice device, CancellationToken cancellationToken = default) =>
        await context.GasDevices.AddAsync(device, cancellationToken);

    public void Update(GasDevice device)
    {
        if (context.Entry(device).State == EntityState.Detached)
        {
            context.GasDevices.Update(device);
        }
    }
}
