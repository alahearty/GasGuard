using GasGuard.Domain;

namespace GasGuard.Application.Models;

public sealed record ModemSettingsInput(
    bool UseSimulatedModem,
    string PortName,
    int BaudRate,
    int DataBits,
    SerialParity Parity,
    SerialStopBits StopBits,
    SerialHandshake Handshake,
    int CommandTimeoutMs,
    int PollIntervalSeconds,
    bool AutoConnect,
    bool AutoReconnect,
    bool DeleteSmsAfterPersistence);

public sealed record PreferencesInput(
    int StaleAfterMinutes,
    int DataRetentionDays,
    bool AutomaticContactNotifications,
    int NotificationMaxAttempts,
    string MinimumLogLevel);

public sealed record SettingsSnapshot(
    ModemSettingsInput Modem,
    PreferencesInput Preferences,
    DateTime? LastBackupAtUtc,
    string? LastBackupPath,
    DateTime ModemUpdatedAtUtc,
    DateTime PreferencesUpdatedAtUtc);

public sealed record SettingsOverview(
    SettingsSnapshot Settings,
    string DatabasePath,
    string ProtocolVersion,
    IReadOnlyList<string> SupportedEvents,
    IReadOnlyList<string> SupportedLevels,
    string AppVersion,
    string LagosTimeZoneName);
