using GasGuard.Application.Abstractions;
using GasGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GasGuard.Infrastructure.Persistence.Repositories;

internal sealed class EfSettingsRepository(GasGuardDbContext context) : ISettingsRepository
{
    public async Task<ModemConfiguration> GetModemConfigurationAsync(CancellationToken cancellationToken = default) =>
        await context.ModemConfigurations.FirstOrDefaultAsync(x => x.Id == ModemConfiguration.SingletonId, cancellationToken)
        ?? throw new InvalidOperationException("The modem configuration row is missing from the database.");

    public async Task<AppPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default) =>
        await context.Preferences.FirstOrDefaultAsync(x => x.Id == AppPreferences.SingletonId, cancellationToken)
        ?? throw new InvalidOperationException("The application preferences row is missing from the database.");
}
