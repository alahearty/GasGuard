using System.Text.RegularExpressions;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Auditing;
using GasGuard.Application.Models;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GasGuard.Application.Services;

public sealed partial class DeviceService(
    IGasGuardUnitOfWorkFactory unitOfWorkFactory,
    IPhoneNumberNormalizer phoneNumbers,
    IClock clock,
    IUserContext userContext,
    ILogger<DeviceService> logger) : IDeviceService
{
    public async Task<IReadOnlyList<DeviceListItem>> GetDevicesAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var devices = await unitOfWork.Devices.ListAsync(includeInactive, cancellationToken);
        var preferences = await unitOfWork.Settings.GetPreferencesAsync(cancellationToken);
        var now = clock.UtcNow;
        var staleAfter = TimeSpan.FromMinutes(preferences.StaleAfterMinutes);
        return devices.Select(x => Map(x, now, staleAfter)).ToArray();
    }

    public async Task<DeviceListItem?> GetDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var device = await unitOfWork.Devices.GetByIdAsync(deviceId, cancellationToken);
        if (device is null)
        {
            return null;
        }

        var preferences = await unitOfWork.Settings.GetPreferencesAsync(cancellationToken);
        return Map(device, clock.UtcNow, TimeSpan.FromMinutes(preferences.StaleAfterMinutes));
    }

    public async Task<OperationResult> CreateAsync(DeviceInput input, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var unitOfWork = unitOfWorkFactory.Create();
            var validation = await ValidateAsync(unitOfWork, input, null, cancellationToken);
            if (validation.Errors.Count > 0)
            {
                return OperationResult.Fail("The detector was not created.", validation.Errors);
            }

            var now = clock.UtcNow;
            var device = new GasDevice
            {
                Id = Guid.NewGuid(),
                DeviceCode = validation.DeviceCode,
                DisplayName = input.DisplayName.Trim(),
                InstallationLocation = input.InstallationLocation.Trim(),
                ExpectedSenderNumber = validation.SenderNumber,
                DetectorType = input.DetectorType,
                Description = CleanOptional(input.Description),
                InstallationDate = input.InstallationDate,
                IsActive = input.IsActive,
                MaintenanceNotes = CleanOptional(input.MaintenanceNotes),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            await unitOfWork.Devices.AddAsync(device, cancellationToken);
            await unitOfWork.AddAuditLogAsync(new AuditEntryDraft(
                userContext.OperatorName,
                "DeviceCreated",
                "GasDevice",
                device.Id.ToString(),
                AuditOutcome.Success,
                $"Detector {device.DeviceCode} ({device.DisplayName}) created at {device.InstallationLocation}."),
                now,
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return OperationResult.Ok($"Detector {device.DeviceCode} was created.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create a detector.");
            return OperationResult.Fail("The detector could not be saved. Check the database and try again.");
        }
    }

    public async Task<OperationResult> UpdateAsync(
        Guid deviceId,
        DeviceInput input,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var unitOfWork = unitOfWorkFactory.Create();
            var device = await unitOfWork.Devices.GetByIdAsync(deviceId, cancellationToken);
            if (device is null)
            {
                return OperationResult.Fail("The detector no longer exists.");
            }

            var validation = await ValidateAsync(unitOfWork, input, deviceId, cancellationToken);
            if (validation.Errors.Count > 0)
            {
                return OperationResult.Fail("The detector was not updated.", validation.Errors);
            }

            device.DeviceCode = validation.DeviceCode;
            device.DisplayName = input.DisplayName.Trim();
            device.InstallationLocation = input.InstallationLocation.Trim();
            device.ExpectedSenderNumber = validation.SenderNumber;
            device.DetectorType = input.DetectorType;
            device.Description = CleanOptional(input.Description);
            device.InstallationDate = input.InstallationDate;
            device.IsActive = input.IsActive;
            device.MaintenanceNotes = CleanOptional(input.MaintenanceNotes);
            var now = clock.UtcNow;
            device.UpdatedAtUtc = now;
            unitOfWork.Devices.Update(device);
            await unitOfWork.AddAuditLogAsync(new AuditEntryDraft(
                userContext.OperatorName,
                "DeviceUpdated",
                "GasDevice",
                device.Id.ToString(),
                AuditOutcome.Success,
                $"Detector {device.DeviceCode} configuration updated. Active state: {(device.IsActive ? "active" : "inactive")}."),
                now,
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return OperationResult.Ok($"Detector {device.DeviceCode} was updated.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update detector {DeviceId}.", deviceId);
            return OperationResult.Fail("The detector could not be updated. Check the database and try again.");
        }
    }

    public async Task<OperationResult> SetActiveAsync(
        Guid deviceId,
        bool active,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var unitOfWork = unitOfWorkFactory.Create();
            var device = await unitOfWork.Devices.GetByIdAsync(deviceId, cancellationToken);
            if (device is null)
            {
                return OperationResult.Fail("The detector no longer exists.");
            }

            if (device.IsActive == active)
            {
                return OperationResult.Ok(active ? "The detector is already active." : "The detector is already inactive.");
            }

            var now = clock.UtcNow;
            device.IsActive = active;
            device.UpdatedAtUtc = now;
            await unitOfWork.AddAuditLogAsync(new AuditEntryDraft(
                userContext.OperatorName,
                active ? "DeviceActivated" : "DeviceDeactivated",
                "GasDevice",
                device.Id.ToString(),
                AuditOutcome.Success,
                active
                    ? $"Detector {device.DeviceCode} reactivated. Historical incidents were preserved."
                    : $"Detector {device.DeviceCode} deactivated. The record and all historical incidents were preserved."),
                now,
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return OperationResult.Ok(active
                ? "Detector reactivated. Historical records were retained."
                : "Detector deactivated. Historical records and incidents were retained.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to change active state for detector {DeviceId}.", deviceId);
            return OperationResult.Fail("The detector state could not be changed.");
        }
    }

    private static DeviceListItem Map(GasDevice device, DateTime utcNow, TimeSpan staleAfter) => new(
        device.Id,
        device.DeviceCode,
        device.DisplayName,
        device.InstallationLocation,
        device.ExpectedSenderNumber,
        device.DetectorType,
        device.IsActive,
        device.InstallationDate,
        device.Description,
        device.MaintenanceNotes,
        device.LastSuccessfulCommunicationUtc,
        device.LastReportedState,
        device.GetOperationalState(utcNow, staleAfter),
        device.LastReading,
        device.LastReadingUnit,
        device.LastSequenceNumber,
        device.LastCommunicationErrorUtc,
        device.LastCommunicationError,
        device.IsDemo,
        device.CreatedAtUtc,
        device.UpdatedAtUtc);

    private async Task<(List<string> Errors, string DeviceCode, string SenderNumber)> ValidateAsync(
        IGasGuardUnitOfWork unitOfWork,
        DeviceInput input,
        Guid? excludingDeviceId,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var deviceCode = (input.DeviceCode ?? string.Empty).Trim().ToUpperInvariant();
        var senderNumber = string.Empty;

        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            errors.Add("A device code is required.");
        }
        else if (!DeviceCodePattern().IsMatch(deviceCode))
        {
            errors.Add("The device code must be 3-32 characters using letters, digits, hyphens or underscores and must start with a letter or digit.");
        }
        else if (await unitOfWork.Devices.CodeExistsAsync(deviceCode, excludingDeviceId, cancellationToken))
        {
            errors.Add($"Device code {deviceCode} is already registered.");
        }

        if (string.IsNullOrWhiteSpace(input.DisplayName))
        {
            errors.Add("A display name is required.");
        }
        else if (input.DisplayName.Trim().Length > 120)
        {
            errors.Add("The display name cannot exceed 120 characters.");
        }

        if (string.IsNullOrWhiteSpace(input.InstallationLocation))
        {
            errors.Add("An installation location is required.");
        }
        else if (input.InstallationLocation.Trim().Length > 200)
        {
            errors.Add("The installation location cannot exceed 200 characters.");
        }

        if (!phoneNumbers.TryNormalize(input.ExpectedSenderNumber, out senderNumber, out var phoneError))
        {
            errors.Add($"Expected sender number: {phoneError}");
        }
        else if (await unitOfWork.Devices.SenderExistsAsync(senderNumber, excludingDeviceId, cancellationToken))
        {
            errors.Add($"{phoneNumbers.FormatForDisplay(senderNumber)} is already assigned to another detector.");
        }

        if (input.Description?.Length > 1000)
        {
            errors.Add("The description cannot exceed 1000 characters.");
        }

        if (input.MaintenanceNotes?.Length > 2000)
        {
            errors.Add("Maintenance notes cannot exceed 2000 characters.");
        }

        if (input.InstallationDate.HasValue && input.InstallationDate.Value > clock.ToLagosDate(clock.UtcNow))
        {
            errors.Add("The installation date cannot be in the future.");
        }

        return (errors, deviceCode, senderNumber);
    }

    private static string? CleanOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9_-]{2,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex DeviceCodePattern();
}
