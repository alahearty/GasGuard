using GasGuard.Application.Abstractions;
using GasGuard.Domain.Entities;
using GasGuard.Infrastructure.Backup;
using GasGuard.Infrastructure.Configuration;
using GasGuard.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GasGuard.Infrastructure.Startup;

public sealed class StartupService(
    GasGuardPaths paths,
    IDbContextFactory<GasGuardDbContext> contextFactory,
    IClock clock,
    ILogger<StartupService> logger) : IStartupService
{
    public Task<StartupResult> InitializeAsync(CancellationToken cancellationToken = default) =>
        ApplyMigrationsAsync(cancellationToken);

    public async Task<StartupResult> ApplyMigrationsAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        var databaseExisted = File.Exists(paths.DatabasePath);
        var pendingCount = 0;
        string? preMigrationBackup = null;

        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

            if (databaseExisted)
            {
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
                pendingCount = pending.Length;
                if (pendingCount > 0)
                {
                    var backupName = $"pre-migration-{clock.UtcNow:yyyyMMdd-HHmmss}.db";
                    var backupPath = Path.Combine(paths.BackupsDirectory, backupName);
                    var backup = SqliteBackupUtility.CreateVerifiedBackup(paths.DatabasePath, backupPath, logger);
                    if (!backup.Success)
                    {
                        return new StartupResult(
                            false,
                            $"Schema migration was stopped because the pre-migration backup failed: {backup.Message}",
                            paths.DatabasePath,
                            false);
                    }

                    preMigrationBackup = backup.BackupPath;
                    logger.LogInformation("Pre-migration database backup created at {BackupPath}.", backup.BackupPath);
                }
            }

            await context.Database.MigrateAsync(cancellationToken);
            await ConfigureConnectionAsync(context, cancellationToken);
            await EnsureSingletonRowsAsync(context, cancellationToken);

            return new StartupResult(
                true,
                pendingCount > 0
                    ? $"Database ready. {pendingCount} migration(s) applied."
                    : "Database ready.",
                paths.DatabasePath,
                pendingCount > 0,
                preMigrationBackup);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database startup or migration failed.");
            SqliteConnection.ClearAllPools();
            return new StartupResult(
                false,
                $"The database could not be opened or migrated: {ex.Message}",
                paths.DatabasePath,
                false,
                preMigrationBackup,
                ex);
        }
    }

    private async Task ConfigureConnectionAsync(GasGuardDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SQLite WAL mode could not be enabled. The application will continue with the existing journal mode.");
        }

        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys;";
            var value = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            if (value != 1)
            {
                throw new InvalidOperationException("SQLite foreign-key enforcement is not enabled for this connection.");
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task EnsureSingletonRowsAsync(GasGuardDbContext context, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (!await context.ModemConfigurations.AnyAsync(
                x => x.Id == ModemConfiguration.SingletonId,
                cancellationToken))
        {
            context.ModemConfigurations.Add(new ModemConfiguration
            {
                Id = ModemConfiguration.SingletonId,
                UseSimulatedModem = true,
                PortName = "SIMULATOR",
                UpdatedAtUtc = now
            });
        }

        if (!await context.Preferences.AnyAsync(x => x.Id == AppPreferences.SingletonId, cancellationToken))
        {
            context.Preferences.Add(new AppPreferences
            {
                Id = AppPreferences.SingletonId,
                UpdatedAtUtc = now
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
