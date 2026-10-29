using GasGuard.Application.Abstractions;
using GasGuard.Application.Auditing;
using GasGuard.Application.Models;
using GasGuard.Contracts.Protocol;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GasGuard.Application.Services;

public sealed class SettingsService(
    IGasGuardUnitOfWorkFactory unitOfWorkFactory,
    IAppPathProvider pathProvider,
    IClock clock,
    IUserContext userContext,
    ILogger<SettingsService> logger) : ISettingsService
{
    private static readonly HashSet<string> LogLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Trace", "Debug", "Information", "Warning", "Error", "Critical"
    };

    public async Task<SettingsOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var appVersion = typeof(SettingsService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        return new SettingsOverview(
            snapshot,
            pathProvider.DatabasePath,
            SmsProtocolConstants.CurrentVersion.ToString(),
            SmsProtocolConstants.SupportedEvents,
            SmsProtocolConstants.SupportedLevels,
            appVersion,
            clock.LagosTimeZoneName);
    }

    public async Task<SettingsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var modem = await unitOfWork.Settings.GetModemConfigurationAsync(cancellationToken);
        var preferences = await unitOfWork.Settings.GetPreferencesAsync(cancellationToken);

        return new SettingsSnapshot(
            new ModemSettingsInput(
                modem.UseSimulatedModem,
                modem.PortName,
                modem.BaudRate,
                modem.DataBits,
                modem.Parity,
                modem.StopBits,
                modem.Handshake,
                modem.CommandTimeoutMs,
                modem.PollIntervalSeconds,
                modem.AutoConnect,
                modem.AutoReconnect,
                modem.DeleteSmsAfterPersistence),
            new PreferencesInput(
                preferences.StaleAfterMinutes,
                preferences.DataRetentionDays,
                preferences.AutomaticContactNotifications,
                preferences.NotificationMaxAttempts,
                preferences.MinimumLogLevel),
            preferences.LastBackupAtUtc,
            preferences.LastBackupPath,
            modem.UpdatedAtUtc,
            preferences.UpdatedAtUtc);
    }

    public async Task<TimeSpan> GetStaleAfterAsync(CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var preferences = await unitOfWork.Settings.GetPreferencesAsync(cancellationToken);
        return TimeSpan.FromMinutes(preferences.StaleAfterMinutes);
    }

    public async Task<OperationResult> SaveModemSettingsAsync(
        ModemSettingsInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateModem(input, out var normalizedPort);
        if (errors.Count > 0)
        {
            return OperationResult.Fail("The modem settings were not saved.", errors);
        }

        try
        {
            await using var unitOfWork = unitOfWorkFactory.Create();
            var config = await unitOfWork.Settings.GetModemConfigurationAsync(cancellationToken);
            var changedConnectionSettings =
                config.UseSimulatedModem != input.UseSimulatedModem ||
                !string.Equals(config.PortName, normalizedPort, StringComparison.OrdinalIgnoreCase) ||
                config.BaudRate != input.BaudRate ||
                config.DataBits != input.DataBits ||
                config.Parity != input.Parity ||
                config.StopBits != input.StopBits ||
                config.Handshake != input.Handshake;

            config.UseSimulatedModem = input.UseSimulatedModem;
            config.PortName = normalizedPort;
            config.BaudRate = input.BaudRate;
            config.DataBits = input.DataBits;
            config.Parity = input.Parity;
            config.StopBits = input.StopBits;
            config.Handshake = input.Handshake;
            config.CommandTimeoutMs = input.CommandTimeoutMs;
            config.PollIntervalSeconds = input.PollIntervalSeconds;
            config.AutoConnect = input.AutoConnect;
            config.AutoReconnect = input.AutoReconnect;
            config.DeleteSmsAfterPersistence = input.DeleteSmsAfterPersistence;
            var now = clock.UtcNow;
            config.UpdatedAtUtc = now;
            await unitOfWork.AddAuditLogAsync(new AuditEntryDraft(
                userContext.OperatorName,
                "ModemConfigurationChanged",
                "ModemConfiguration",
                ModemConfiguration.SingletonId.ToString(),
                AuditOutcome.Success,
                $"Modem mode: {(input.UseSimulatedModem ? "simulated" : "physical")}; port: {normalizedPort}; baud: {input.BaudRate}; poll: {input.PollIntervalSeconds}s; delete-after-persist: {input.DeleteSmsAfterPersistence}."),
                now,
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return OperationResult.Ok(changedConnectionSettings
                ? "Modem settings saved. Reconnect the modem to apply connection changes."
                : "Modem settings saved.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save modem settings.");
            return OperationResult.Fail("The modem settings could not be saved.");
        }
    }

    public async Task<OperationResult> SavePreferencesAsync(
        PreferencesInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidatePreferences(input);
        if (errors.Count > 0)
        {
            return OperationResult.Fail("The preferences were not saved.", errors);
        }

        try
        {
            await using var unitOfWork = unitOfWorkFactory.Create();
            var preferences = await unitOfWork.Settings.GetPreferencesAsync(cancellationToken);
            preferences.StaleAfterMinutes = input.StaleAfterMinutes;
            preferences.DataRetentionDays = input.DataRetentionDays;
            preferences.AutomaticContactNotifications = input.AutomaticContactNotifications;
            preferences.NotificationMaxAttempts = input.NotificationMaxAttempts;
            preferences.MinimumLogLevel = NormalizeLogLevel(input.MinimumLogLevel);
            var now = clock.UtcNow;
            preferences.UpdatedAtUtc = now;
            await unitOfWork.AddAuditLogAsync(new AuditEntryDraft(
                userContext.OperatorName,
                "PreferencesChanged",
                "AppPreferences",
                AppPreferences.SingletonId.ToString(),
                AuditOutcome.Success,
                $"Stale threshold: {input.StaleAfterMinutes} min; retention: {input.DataRetentionDays} days; automatic notifications: {input.AutomaticContactNotifications}; minimum log level: {preferences.MinimumLogLevel}."),
                now,
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return OperationResult.Ok("Application preferences saved.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save application preferences.");
            return OperationResult.Fail("The preferences could not be saved.");
        }
    }

    private static List<string> ValidateModem(ModemSettingsInput input, out string normalizedPort)
    {
        var errors = new List<string>();
        normalizedPort = input.UseSimulatedModem ? "SIMULATOR" : (input.PortName ?? string.Empty).Trim();

        if (!input.UseSimulatedModem)
        {
            if (string.IsNullOrWhiteSpace(normalizedPort))
            {
                errors.Add("A serial port name is required for a physical modem.");
            }
            else if (normalizedPort.Length > 32)
            {
                errors.Add("The serial port name cannot exceed 32 characters.");
            }
            else if (normalizedPort.Equals("SIMULATOR", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("SIMULATOR is reserved for demonstration mode. Select a physical COM port or enable simulated mode.");
            }
        }

        if (input.BaudRate is < 300 or > 4_000_000)
        {
            errors.Add("The baud rate must be between 300 and 4,000,000.");
        }

        if (input.DataBits is < 5 or > 8)
        {
            errors.Add("Data bits must be between 5 and 8.");
        }

        if (!Enum.IsDefined(input.Parity))
        {
            errors.Add("Select a valid parity setting.");
        }

        if (!Enum.IsDefined(input.StopBits))
        {
            errors.Add("Select a valid stop-bit setting.");
        }

        if (!Enum.IsDefined(input.Handshake))
        {
            errors.Add("Select a valid handshake setting.");
        }

        if (input.CommandTimeoutMs is < 1000 or > 60_000)
        {
            errors.Add("The command timeout must be between 1,000 and 60,000 milliseconds.");
        }

        if (input.PollIntervalSeconds is < 5 or > 3600)
        {
            errors.Add("The SMS polling interval must be between 5 and 3,600 seconds.");
        }

        return errors;
    }

    private static List<string> ValidatePreferences(PreferencesInput input)
    {
        var errors = new List<string>();
        if (input.StaleAfterMinutes is < 1 or > 1440)
        {
            errors.Add("The stale-data threshold must be between 1 and 1,440 minutes.");
        }

        if (input.DataRetentionDays != 0 && (input.DataRetentionDays < 7 || input.DataRetentionDays > 3650))
        {
            errors.Add("Data retention must be 0 (disabled) or between 7 and 3,650 days.");
        }

        if (input.NotificationMaxAttempts is < 1 or > 3)
        {
            errors.Add("Automatic notification attempts must be between 1 and 3.");
        }

        if (!LogLevels.Contains(input.MinimumLogLevel ?? string.Empty))
        {
            errors.Add("Select a valid diagnostic log level.");
        }

        return errors;
    }

    private static string NormalizeLogLevel(string value)
    {
        var match = LogLevels.FirstOrDefault(x => x.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? "Information";
    }
}
