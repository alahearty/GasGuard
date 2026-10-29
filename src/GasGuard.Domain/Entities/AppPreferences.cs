namespace GasGuard.Domain.Entities;

public sealed class AppPreferences
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public int StaleAfterMinutes { get; set; } = 15;
    public int DataRetentionDays { get; set; } = 365;
    public bool AutomaticContactNotifications { get; set; }
    public int NotificationMaxAttempts { get; set; } = 2;
    public string MinimumLogLevel { get; set; } = "Information";
    public DateTime? LastBackupAtUtc { get; set; }
    public string? LastBackupPath { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
