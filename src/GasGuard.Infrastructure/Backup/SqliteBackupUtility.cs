using GasGuard.Application.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GasGuard.Infrastructure.Backup;

public static class SqliteBackupUtility
{
    private static readonly string[] RequiredTables = ["GasDevices", "SmsMessages", "Incidents"];

    public static BackupCreationResult CreateVerifiedBackup(
        string sourceDatabasePath,
        string targetBackupPath,
        ILogger? logger = null)
    {
        if (!File.Exists(sourceDatabasePath))
        {
            return new BackupCreationResult(false, "The database file does not exist.");
        }

        var fullTarget = Path.GetFullPath(targetBackupPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullTarget)!);

        if (string.Equals(Path.GetFullPath(sourceDatabasePath), fullTarget, StringComparison.OrdinalIgnoreCase))
        {
            return new BackupCreationResult(false, "The backup path cannot be the live database path.");
        }

        var temporaryPath = fullTarget + ".tmp";
        try
        {
            DeleteDatabaseSidecars(temporaryPath);
            DeleteDatabaseSidecars(fullTarget);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            CopyDatabase(sourceDatabasePath, temporaryPath);
            FinalizeBackupFile(temporaryPath);
            var validation = ValidateDatabase(temporaryPath);
            if (!validation.IsValid)
            {
                DeleteDatabaseSidecars(temporaryPath);
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                return new BackupCreationResult(false, $"The backup failed validation: {validation.Message}");
            }

            File.Move(temporaryPath, fullTarget, true);
            DeleteDatabaseSidecars(temporaryPath);
            DeleteDatabaseSidecars(fullTarget);
            var createdAt = DateTime.UtcNow;
            return new BackupCreationResult(
                true,
                "Backup created and integrity-checked.",
                fullTarget,
                true,
                createdAt);
        }
        catch (Exception ex)
        {
            DeleteDatabaseSidecars(temporaryPath);
            logger?.LogError(ex, "Database backup to {BackupPath} failed.", fullTarget);
            return new BackupCreationResult(false, $"The backup could not be created: {ex.Message}");
        }
    }

    public static BackupValidationResult ValidateDatabase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
        {
            return new BackupValidationResult(false, "The selected backup file does not exist.");
        }

        var fullPath = Path.GetFullPath(databasePath);
        var size = new FileInfo(fullPath).Length;
        if (size == 0)
        {
            return new BackupValidationResult(false, "The selected backup file is empty.", fullPath, size);
        }

        try
        {
            using var connection = new SqliteConnection(BuildConnectionString(fullPath, SqliteOpenMode.ReadOnly));
            connection.Open();

            using (var integrityCommand = connection.CreateCommand())
            {
                integrityCommand.CommandText = "PRAGMA integrity_check;";
                var integrity = integrityCommand.ExecuteScalar()?.ToString();
                if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return new BackupValidationResult(
                        false,
                        $"SQLite integrity check failed: {integrity ?? "no result"}.",
                        fullPath,
                        size);
                }
            }

            foreach (var table in RequiredTables)
            {
                if (!TableExists(connection, table))
                {
                    return new BackupValidationResult(
                        false,
                        $"The backup does not contain the required table '{table}'.",
                        fullPath,
                        size);
                }
            }

            using (var foreignKeyCommand = connection.CreateCommand())
            {
                foreignKeyCommand.CommandText = "PRAGMA foreign_key_check;";
                using var reader = foreignKeyCommand.ExecuteReader();
                if (reader.Read())
                {
                    return new BackupValidationResult(
                        false,
                        "The backup contains foreign-key violations.",
                        fullPath,
                        size);
                }
            }

            return new BackupValidationResult(
                true,
                "Backup integrity check passed and required tables are present.",
                fullPath,
                size);
        }
        catch (Exception ex)
        {
            return new BackupValidationResult(false, $"The backup could not be opened: {ex.Message}", fullPath, size);
        }
    }

    public static RestoreResult RestoreDatabase(
        string backupPath,
        string destinationDatabasePath,
        string safetyBackupPath,
        ILogger? logger = null)
    {
        var validation = ValidateDatabase(backupPath);
        if (!validation.IsValid)
        {
            return new RestoreResult(false, $"Restore was not attempted: {validation.Message}");
        }

        var safety = CreateVerifiedBackup(destinationDatabasePath, safetyBackupPath, logger);
        if (!safety.Success)
        {
            return new RestoreResult(false, $"Restore was not attempted because the safety backup failed: {safety.Message}");
        }

        try
        {
            SqliteConnection.ClearAllPools();
            CopyDatabase(backupPath, destinationDatabasePath, destinationDatabasePath);
            var restoredValidation = ValidateDatabase(destinationDatabasePath);
            if (!restoredValidation.IsValid)
            {
                var rollback = CreateVerifiedBackup(safetyBackupPath, destinationDatabasePath + ".rollback", logger);
                if (rollback.Success)
                {
                    CopyDatabase(safetyBackupPath, destinationDatabasePath, destinationDatabasePath);
                }

                return new RestoreResult(
                    false,
                    $"The restored database failed validation and was rolled back from the safety backup: {restoredValidation.Message}",
                    safety.BackupPath,
                    true);
            }

            return new RestoreResult(
                true,
                "The database was restored and integrity-checked. Restart GasGuard Desktop before continuing.",
                safety.BackupPath,
                true,
                true);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Restore from {BackupPath} failed.", backupPath);
            try
            {
                CopyDatabase(safetyBackupPath, destinationDatabasePath, destinationDatabasePath);
            }
            catch (Exception rollbackException)
            {
                logger?.LogError(rollbackException, "Automatic rollback from the safety backup also failed.");
            }

            return new RestoreResult(
                false,
                $"The restore failed and an automatic rollback was attempted: {ex.Message}",
                safety.BackupPath,
                true);
        }
    }

    private static void CopyDatabase(string sourcePath, string targetPath, string? checkpointPath = null)
    {
        using var source = new SqliteConnection(BuildConnectionString(sourcePath, SqliteOpenMode.ReadWrite));
        source.Open();
        using var destination = new SqliteConnection(BuildConnectionString(targetPath, SqliteOpenMode.ReadWriteCreate));
        destination.Open();

        if (checkpointPath is not null)
        {
            using var checkpoint = destination.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            checkpoint.ExecuteNonQuery();
        }

        source.BackupDatabase(destination);

        using var verify = destination.CreateCommand();
        verify.CommandText = "PRAGMA quick_check;";
        var result = verify.ExecuteScalar()?.ToString();
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The destination database quick check returned '{result}'.");
        }
    }

    private static void FinalizeBackupFile(string path)
    {
        using (var connection = new SqliteConnection(BuildConnectionString(path, SqliteOpenMode.ReadWrite)))
        {
            connection.Open();
            using var checkpoint = connection.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            checkpoint.ExecuteNonQuery();

            using var journalMode = connection.CreateCommand();
            journalMode.CommandText = "PRAGMA journal_mode=DELETE;";
            journalMode.ExecuteScalar();
        }

        DeleteDatabaseSidecars(path);
    }

    private static void DeleteDatabaseSidecars(string databasePath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = databasePath + suffix;
            try
            {
                if (File.Exists(sidecar))
                {
                    File.Delete(sidecar);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static string BuildConnectionString(string path, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = mode,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 10
        }.ToString();
}
