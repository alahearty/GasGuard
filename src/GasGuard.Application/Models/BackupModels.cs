namespace GasGuard.Application.Models;

public sealed record DatabaseHealthInfo(
    bool IsHealthy,
    string Message,
    string DatabasePath,
    long SizeBytes,
    string? IntegrityCheck,
    bool ForeignKeysEnabled,
    DateTime CheckedAtUtc);

public sealed record BackupFileInfo(
    string Path,
    string FileName,
    DateTime CreatedAtUtc,
    long SizeBytes);

public sealed record BackupValidationResult(
    bool IsValid,
    string Message,
    string? Path = null,
    long SizeBytes = 0);

public sealed record BackupCreationResult(
    bool Success,
    string Message,
    string? BackupPath = null,
    bool Verified = false,
    DateTime? CreatedAtUtc = null);

public sealed record RestoreResult(
    bool Success,
    string Message,
    string? PreRestoreBackupPath = null,
    bool RestartRequired = false,
    bool Verified = false);

public sealed record RetentionResult(
    bool Success,
    string Message,
    int DeletedCommunicationLogs,
    int DeletedAuditLogs,
    int DeletedSmsMessages);
