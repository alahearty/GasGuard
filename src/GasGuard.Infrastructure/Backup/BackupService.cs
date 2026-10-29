using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using GasGuard.Infrastructure.Configuration;
using GasGuard.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GasGuard.Infrastructure.Backup;

public sealed class BackupService(
    GasGuardPaths paths,
    IDbContextFactory<GasGuardDbContext> contextFactory,
    IClock clock,
    IUserContext userContext,
    IAuditLogService auditLog,
    ILogger<BackupService> logger) : IBackupService
{
    public async Task<DatabaseHealthInfo> GetDatabaseHealthAsync(CancellationToken cancellationToken = default)
    {
        var checkedAt = clock.UtcNow;
        var fileSize = File.Exists(paths.DatabasePath) ? new FileInfo(paths.DatabasePath).Length : 0;

        try
        {
            await using var connection = new SqliteConnection(paths.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var integrityCommand = connection.CreateCommand();
            integrityCommand.CommandText = "PRAGMA integrity_check;";
            var integrity = (await integrityCommand.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "no result";

            await using var foreignKeyCommand = connection.CreateCommand();
            foreignKeyCommand.CommandText = "PRAGMA foreign_keys;";
            var foreignKeys = Convert.ToInt64(await foreignKeyCommand.ExecuteScalarAsync(cancellationToken)) == 1;

            var healthy = string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase);
            return new DatabaseHealthInfo(
                healthy,
                healthy
                    ? foreignKeys
                        ? "Database integrity check passed; foreign keys are enforced."
                        : "Database integrity check passed, but foreign-key enforcement is not active."
                    : $"Database integrity check failed: {integrity}",
                paths.DatabasePath,
                fileSize,
                integrity,
                foreignKeys,
                checkedAt);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database health check failed.");
            return new DatabaseHealthInfo(
                false,
                $"Database health check failed: {ex.Message}",
                paths.DatabasePath,
                fileSize,
                null,
                false,
                checkedAt);
        }
    }

    public Task<IReadOnlyList<BackupFileInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        var backups = Directory
            .EnumerateFiles(paths.BackupsDirectory, "*.db", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new BackupFileInfo(path, info.Name, info.CreationTimeUtc, info.Length);
            })
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToArray();

        return Task.FromResult<IReadOnlyList<BackupFileInfo>>(backups);
    }

    public Task<BackupValidationResult> ValidateBackupAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(SqliteBackupUtility.ValidateDatabase(filePath));

    public async Task<BackupCreationResult> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        var backupPath = Path.Combine(paths.BackupsDirectory, $"backup-{clock.UtcNow:yyyyMMdd-HHmmss-fff}.db");
        var result = SqliteBackupUtility.CreateVerifiedBackup(paths.DatabasePath, backupPath, logger);

        if (result.Success)
        {
            try
            {
                await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
                var preferences = await context.Preferences.FirstAsync(
                    x => x.Id == AppPreferences.SingletonId,
                    cancellationToken);
                preferences.LastBackupAtUtc = result.CreatedAtUtc;
                preferences.LastBackupPath = result.BackupPath;
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Backup succeeded but last-backup information could not be saved.");
            }

            await RecordAuditSafeAsync(
                "BackupCreated",
                AuditOutcome.Success,
                $"Database backup created and verified at {Path.GetFileName(result.BackupPath)}.",
                cancellationToken);
        }
        else
        {
            await RecordAuditSafeAsync("BackupCreated", AuditOutcome.Failure, result.Message, cancellationToken);
        }

        return result;
    }

    public async Task<RestoreResult> RestoreBackupAsync(string filePath, CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        if (string.Equals(
                Path.GetFullPath(filePath),
                Path.GetFullPath(paths.DatabasePath),
                StringComparison.OrdinalIgnoreCase))
        {
            return new RestoreResult(
                false,
                "The live database cannot be selected as its own restore source.");
        }

        var validation = SqliteBackupUtility.ValidateDatabase(filePath);
        if (!validation.IsValid)
        {
            return new RestoreResult(false, $"Restore was not attempted: {validation.Message}");
        }

        var safetyPath = Path.Combine(paths.BackupsDirectory, $"pre-restore-{clock.UtcNow:yyyyMMdd-HHmmss-fff}.db");
        var result = SqliteBackupUtility.RestoreDatabase(filePath, paths.DatabasePath, safetyPath, logger);

        await RecordAuditSafeAsync(
            "BackupRestored",
            result.Success ? AuditOutcome.Success : AuditOutcome.Failure,
            result.Success
                ? $"Database restored from {Path.GetFileName(filePath)}. Pre-restore safety backup: {Path.GetFileName(result.PreRestoreBackupPath)}. Restart required."
                : $"Database restore from {Path.GetFileName(filePath)} failed: {result.Message}",
            cancellationToken);

        return result;
    }

    private async Task RecordAuditSafeAsync(string action, AuditOutcome outcome, string details, CancellationToken cancellationToken)
    {
        try
        {
            await auditLog.RecordAsync(new AuditEntryDraft(
                userContext.OperatorName,
                action,
                "Database",
                null,
                outcome,
                details),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audit logging failed for backup operation {Action}.", action);
        }
    }
}
