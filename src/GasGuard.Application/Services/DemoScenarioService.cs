using System.Globalization;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Auditing;
using GasGuard.Application.Models;
using GasGuard.Contracts.Modem;
using GasGuard.Contracts.Protocol;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GasGuard.Application.Services;

public sealed class DemoScenarioService(
    IGasGuardUnitOfWorkFactory unitOfWorkFactory,
    ISimulatedModemControl simulatedModem,
    IClock clock,
    IUserContext userContext,
    IAuditLogService auditLog,
    ILogger<DemoScenarioService> logger) : IDemoScenarioService
{
    private readonly object _sequenceSync = new();
    private readonly Dictionary<string, long> _demoSequences = new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> IsDemoDataLoadedAsync(CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var devices = await unitOfWork.Devices.ListAsync(true, cancellationToken);
        return devices.Any(x => x.IsDemo);
    }

    public async Task<OperationResult> LoadDemoDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var unitOfWork = unitOfWorkFactory.Create();
            var devices = await unitOfWork.Devices.ListAsync(true, cancellationToken);
            var contacts = await unitOfWork.Contacts.ListAsync(true, cancellationToken);
            var now = clock.UtcNow;
            var createdDevices = 0;
            var createdContacts = 0;

            if (!devices.Any(x => x.DeviceCode == "GAS-001"))
            {
                var device = CreateDemoDevice(
                    "GAS-001",
                    "Training Room Detector",
                    "DEMO SITE - Training Room",
                    "+2348030000001",
                    DetectorType.CombustibleGas,
                    "Demonstration detector. Values are simulated and are not real gas measurements.",
                    now);
                await unitOfWork.Devices.AddAsync(device, cancellationToken);
                createdDevices++;
            }

            if (!devices.Any(x => x.DeviceCode == "GAS-002"))
            {
                var device = CreateDemoDevice(
                    "GAS-002",
                    "Generator Room Detector",
                    "DEMO SITE - Generator Room",
                    "+2348030000002",
                    DetectorType.LiquefiedPetroleumGas,
                    "Demonstration detector. Values are simulated and are not real gas measurements.",
                    now);
                await unitOfWork.Devices.AddAsync(device, cancellationToken);
                createdDevices++;
            }

            if (!contacts.Any(x => x.PhoneNumber == "+2348030000010"))
            {
                await unitOfWork.Contacts.AddAsync(new Contact
                {
                    Id = Guid.NewGuid(),
                    FullName = "Demo Safety Officer",
                    PhoneNumber = "+2348030000010",
                    Role = ContactRole.SafetyOfficer,
                    IsActive = true,
                    NotifyOnCritical = true,
                    NotifyOnWarning = true,
                    NotifyOnCommunicationFailure = true,
                    Notes = "DEMO CONTACT created by the demonstration scenario. Not a real person.",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                }, cancellationToken);
                createdContacts++;
            }

            if (!contacts.Any(x => x.PhoneNumber == "+2348030000011"))
            {
                await unitOfWork.Contacts.AddAsync(new Contact
                {
                    Id = Guid.NewGuid(),
                    FullName = "Demo Supervisor",
                    PhoneNumber = "+2348030000011",
                    Role = ContactRole.Supervisor,
                    IsActive = true,
                    NotifyOnCritical = true,
                    NotifyOnWarning = false,
                    NotifyOnCommunicationFailure = false,
                    Notes = "DEMO CONTACT created by the demonstration scenario. Not a real person.",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                }, cancellationToken);
                createdContacts++;
            }

            await unitOfWork.AddAuditLogAsync(new AuditEntryDraft(
                userContext.OperatorName,
                "DemoDataLoaded",
                "DemoScenario",
                null,
                AuditOutcome.Success,
                $"Demo data loaded: {createdDevices} detector(s) and {createdContacts} contact(s) created. Existing records were not overwritten."),
                now,
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return OperationResult.Ok(
                $"Demo data ready: {createdDevices} new detector(s), {createdContacts} new contact(s). Existing records were left unchanged.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Loading demo data failed.");
            return OperationResult.Fail("Demo data could not be loaded.");
        }
    }

    public async Task<OperationResult> InjectDemoMessageAsync(
        string deviceCode,
        GasEventCode eventCode,
        int reading,
        CancellationToken cancellationToken = default)
    {
        if (eventCode is not (GasEventCode.Status or GasEventCode.GasWarning or GasEventCode.GasCritical))
        {
            return OperationResult.Fail("Select STATUS, GAS_WARNING or GAS_CRITICAL for the demo message.");
        }

        if (reading is < 0 or > SmsProtocolConstants.MaximumReading)
        {
            return OperationResult.Fail(
                $"The demonstration raw reading must be between 0 and {SmsProtocolConstants.MaximumReading}.");
        }

        await using var unitOfWork = unitOfWorkFactory.Create();
        var normalizedCode = (deviceCode ?? string.Empty).Trim().ToUpperInvariant();
        var device = await unitOfWork.Devices.GetByCodeAsync(normalizedCode, cancellationToken);
        if (device is null)
        {
            return OperationResult.Fail($"Detector {normalizedCode} is not registered. Load demo data or create the detector first.");
        }

        if (!device.IsDemo)
        {
            return OperationResult.Fail(
                $"Detector {normalizedCode} is a production detector. Simulated messages are restricted to DEMO detectors.");
        }

        var sequence = NextSequence(normalizedCode, device.LastSequenceNumber);
        var level = eventCode switch
        {
            GasEventCode.Status => SmsProtocolConstants.LevelNormal,
            GasEventCode.GasWarning => SmsProtocolConstants.LevelWarning,
            _ => SmsProtocolConstants.LevelCritical
        };
        var eventText = GasProtocolMap.EventText(eventCode);
        var deviceTime = clock.UtcNow.AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:ss'+01:00'", CultureInfo.InvariantCulture);
        var body =
            $"{SmsProtocolConstants.Marker}|v=1|device={device.DeviceCode}|event={eventText}|level={level}|reading={reading}|unit=raw|seq={sequence}|ts={deviceTime}";

        var result = await simulatedModem.InjectMessageAsync(
            new SimulatedSmsRequest(device.ExpectedSenderNumber, body),
            cancellationToken);

        await auditLog.RecordAsync(new AuditEntryDraft(
            userContext.OperatorName,
            "DemoMessageInjected",
            "GasDevice",
            device.Id.ToString(),
            result.Success ? AuditOutcome.Success : AuditOutcome.Failure,
            $"Simulated {eventText} for {device.DeviceCode}, raw reading {reading}, sequence {sequence}. {result.Message}"),
            cancellationToken);

        return result.Success
            ? OperationResult.Ok(
                $"Simulated {eventText} delivered for {device.DeviceCode} (raw reading {reading}, sequence {sequence}). The message is labelled SIMULATED.")
            : OperationResult.Fail(result.Message);
    }

    private long NextSequence(string deviceCode, long? lastPersistedSequence)
    {
        lock (_sequenceSync)
        {
            _demoSequences.TryGetValue(deviceCode, out var current);
            var next = Math.Max(current, lastPersistedSequence ?? 1000) + 1;
            _demoSequences[deviceCode] = next;
            return next;
        }
    }

    private static GasDevice CreateDemoDevice(
        string code,
        string name,
        string location,
        string sender,
        DetectorType detectorType,
        string description,
        DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        DeviceCode = code,
        DisplayName = name,
        InstallationLocation = location,
        ExpectedSenderNumber = sender,
        DetectorType = detectorType,
        Description = description,
        InstallationDate = DateOnly.FromDateTime(now.AddDays(-30)),
        IsActive = true,
        IsDemo = true,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };
}
