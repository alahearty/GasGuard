using GasGuard.Application.Abstractions;
using GasGuard.Application.Auditing;
using GasGuard.Application.Models;
using GasGuard.Contracts.Modem;
using GasGuard.Contracts.Protocol;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GasGuard.Application.Services;

public sealed class SmsIngestionService(
    IGasGuardUnitOfWorkFactory unitOfWorkFactory,
    ISmsProtocolParser parser,
    IPhoneNumberNormalizer phoneNumbers,
    IClock clock,
    IAppEventBus eventBus,
    ILogger<SmsIngestionService> logger) : ISmsIngestionService
{
    private static readonly TimeSpan MaximumDeviceClockAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan MaximumDeviceClockSkew = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(15);
    private const string SystemActor = "System (SMS)";

    public async Task<IngestionResult> ProcessAsync(IncomingSms incomingSms, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incomingSms);

        var now = clock.UtcNow;
        var receivedAt = EnsureUtc(incomingSms.ReceivedAtUtc == default ? now : incomingSms.ReceivedAtUtc);
        phoneNumbers.TryNormalize(incomingSms.From, out var normalizedSender, out _);
        var senderForFingerprint = string.IsNullOrWhiteSpace(normalizedSender) ? incomingSms.From : normalizedSender;
        var fingerprint = SmsFingerprint.Create(senderForFingerprint, incomingSms.Body);
        var parseResult = parser.Parse(incomingSms.Body);
        var sourceReference = string.IsNullOrWhiteSpace(incomingSms.SourceReference) ? null : incomingSms.SourceReference.Trim();

        IGasGuardUnitOfWork? unitOfWork = null;
        try
        {
            unitOfWork = unitOfWorkFactory.Create();

            if (sourceReference is not null)
            {
                var alreadyStored = await unitOfWork.Messages.GetLatestBySourceReferenceAsync(sourceReference, cancellationToken);
                if (alreadyStored is not null && alreadyStored.ProcessingStatus != SmsProcessingStatus.Failed)
                {
                    return new IngestionResult(
                        true,
                        SmsProcessingStatus.Duplicate,
                        alreadyStored.Id,
                        null,
                        true,
                        "This stored SMS was already processed.",
                        "ALREADY_PROCESSED");
                }
            }

            await unitOfWork.BeginTransactionAsync(cancellationToken);

            var message = BuildMessage(incomingSms, normalizedSender, fingerprint, receivedAt, now, sourceReference, parseResult);

            if (!parseResult.IsValid)
            {
                message.ProcessingStatus = SmsProcessingStatus.Invalid;
                message.ProcessingDetail = DescribeParseFailure(parseResult);
                message.ProcessedAtUtc = now;
                await unitOfWork.Messages.AddAsync(message, cancellationToken);
                await AddCommunicationLogAsync(
                    unitOfWork,
                    CommunicationOutcome.Failure,
                    "Invalid gas SMS rejected",
                    message.ProcessingDetail,
                    parseResult.PrimaryErrorCode,
                    null,
                    incomingSms,
                    now,
                    cancellationToken);
                await AddAuditAsync(
                    unitOfWork,
                    "SmsRejected",
                    "SmsMessage",
                    message.Id,
                    AuditOutcome.Failure,
                    $"Invalid SMS from {SmsPrivacy.MaskPhoneNumber(incomingSms.From)}: {parseResult.PrimaryErrorCode}.",
                    now,
                    cancellationToken);

                await unitOfWork.CommitAsync(cancellationToken);
                PublishSafe(new MonitorNotification(
                    "Invalid gas SMS rejected",
                    $"A message from {incomingSms.From} was rejected: {parseResult.PrimaryErrorMessage}",
                    Severity.Warning,
                    incomingSms.IsSimulated,
                    now));

                return new IngestionResult(
                    true,
                    SmsProcessingStatus.Invalid,
                    message.Id,
                    null,
                    false,
                    parseResult.PrimaryErrorMessage ?? "The SMS is invalid.",
                    parseResult.PrimaryErrorCode);
            }

            var parsed = parseResult.Message!;
            var device = await unitOfWork.Devices.GetByCodeAsync(parsed.DeviceCode, cancellationToken);
            message.DeviceCode = parsed.DeviceCode;

            if (device is null)
            {
                message.ProcessingStatus = SmsProcessingStatus.UnknownDevice;
                message.ProcessingDetail = $"No detector is registered with code {parsed.DeviceCode}.";
                message.ProcessedAtUtc = now;
                await unitOfWork.Messages.AddAsync(message, cancellationToken);
                await AddCommunicationLogAsync(
                    unitOfWork,
                    CommunicationOutcome.Rejected,
                    "SMS from unknown detector rejected",
                    message.ProcessingDetail,
                    "UNKNOWN_DEVICE",
                    null,
                    incomingSms,
                    now,
                    cancellationToken);
                await AddAuditAsync(
                    unitOfWork,
                    "SmsUnknownDeviceRejected",
                    "SmsMessage",
                    message.Id,
                    AuditOutcome.Denied,
                    $"Unknown detector {parsed.DeviceCode} attempted to report from {SmsPrivacy.MaskPhoneNumber(incomingSms.From)}.",
                    now,
                    cancellationToken);

                await unitOfWork.CommitAsync(cancellationToken);
                PublishSafe(new MonitorNotification(
                    "Unknown detector message rejected",
                    $"Detector code {parsed.DeviceCode} is not registered. The raw SMS was retained for review.",
                    Severity.Warning,
                    incomingSms.IsSimulated,
                    now));

                return new IngestionResult(
                    true,
                    SmsProcessingStatus.UnknownDevice,
                    message.Id,
                    null,
                    false,
                    message.ProcessingDetail,
                    "UNKNOWN_DEVICE");
            }

            message.DeviceId = device.Id;

            if (incomingSms.IsSimulated && !device.IsDemo)
            {
                message.ProcessingStatus = SmsProcessingStatus.SimulationRejected;
                message.ProcessingDetail =
                    "A SIMULATED message was rejected because the detector is not marked as demonstration data. Use a DEMO detector or connect physical hardware.";
                message.ProcessedAtUtc = now;
                await unitOfWork.Messages.AddAsync(message, cancellationToken);
                await AddCommunicationLogAsync(
                    unitOfWork,
                    CommunicationOutcome.Rejected,
                    "Simulated SMS rejected for non-demo detector",
                    message.ProcessingDetail,
                    "SIMULATION_NOT_ALLOWED",
                    device,
                    incomingSms,
                    now,
                    cancellationToken);
                await AddAuditAsync(
                    unitOfWork,
                    "SmsSimulationRejected",
                    "GasDevice",
                    device.Id,
                    AuditOutcome.Denied,
                    message.ProcessingDetail,
                    now,
                    cancellationToken);
                await unitOfWork.CommitAsync(cancellationToken);
                PublishSafe(new MonitorNotification(
                    "Simulated data rejected",
                    $"Simulated data for {device.DeviceCode} was not applied because that detector is not a DEMO detector.",
                    Severity.Warning,
                    true,
                    now));

                return new IngestionResult(
                    true,
                    SmsProcessingStatus.SimulationRejected,
                    message.Id,
                    null,
                    false,
                    message.ProcessingDetail,
                    "SIMULATION_NOT_ALLOWED");
            }

            if (!phoneNumbers.NumbersMatch(device.ExpectedSenderNumber, incomingSms.From))
            {
                message.ProcessingStatus = SmsProcessingStatus.UnauthorizedSender;
                message.ProcessingDetail =
                    $"The sender does not match the registered number for detector {device.DeviceCode}.";
                message.ProcessedAtUtc = now;
                device.LastCommunicationErrorUtc = now;
                device.LastCommunicationError = "Unauthorized sender attempted to report.";
                device.UpdatedAtUtc = now;

                await unitOfWork.Messages.AddAsync(message, cancellationToken);
                await AddCommunicationLogAsync(
                    unitOfWork,
                    CommunicationOutcome.Rejected,
                    "SMS sender not authorised for detector",
                    message.ProcessingDetail,
                    "UNAUTHORIZED_SENDER",
                    device,
                    incomingSms,
                    now,
                    cancellationToken);
                await AddAuditAsync(
                    unitOfWork,
                    "SmsUnauthorizedSenderRejected",
                    "GasDevice",
                    device.Id,
                    AuditOutcome.Denied,
                    $"SMS claiming to be {device.DeviceCode} came from an unauthorised sender.",
                    now,
                    cancellationToken);

                await unitOfWork.CommitAsync(cancellationToken);
                PublishSafe(new MonitorNotification(
                    "Unauthorised detector message",
                    $"A message claiming to be {device.DeviceCode} came from an unregistered sender and was rejected.",
                    Severity.Warning,
                    incomingSms.IsSimulated,
                    now));

                return new IngestionResult(
                    true,
                    SmsProcessingStatus.UnauthorizedSender,
                    message.Id,
                    null,
                    false,
                    message.ProcessingDetail,
                    "UNAUTHORIZED_SENDER");
            }

            var deviceTimestamp = parsed.DeviceTimestamp?.UtcDateTime;
            var timestampTrusted = deviceTimestamp.HasValue &&
                                   deviceTimestamp.Value <= now.Add(MaximumDeviceClockSkew) &&
                                   deviceTimestamp.Value >= now.Subtract(MaximumDeviceClockAge) &&
                                   (!device.LastSuccessfulCommunicationUtc.HasValue ||
                                    deviceTimestamp.Value >= device.LastSuccessfulCommunicationUtc.Value);
            var occurredAt = timestampTrusted ? deviceTimestamp!.Value : receivedAt;
            var occurredTimeSource = deviceTimestamp.HasValue
                ? timestampTrusted ? IncidentTimeSource.DeviceReported : IncidentTimeSource.DesktopReceiptFallback
                : IncidentTimeSource.DesktopReceipt;
            var isAlert = parsed.EventCode is GasEventCode.GasWarning or GasEventCode.GasCritical;
            var resolvedIncidentInDedupWindow = false;
            string? sequenceResetDetail = null;

            if (!isAlert &&
                !parsed.SequenceNumber.HasValue &&
                await unitOfWork.Messages.GetLatestProcessedByFingerprintSinceAsync(
                    fingerprint,
                    incomingSms.IsSimulated,
                    now - DuplicateWindow,
                    cancellationToken) is not null)
            {
                message.ProcessingStatus = SmsProcessingStatus.Duplicate;
                message.ProcessingDetail = "An identical SMS from this sender has already been recorded.";
                message.ProcessedAtUtc = now;
                await unitOfWork.Messages.AddAsync(message, cancellationToken);
                await AddCommunicationLogAsync(
                    unitOfWork,
                    CommunicationOutcome.Information,
                    "Duplicate SMS retained without new state change",
                    message.ProcessingDetail,
                    "DUPLICATE_SMS",
                    device,
                    incomingSms,
                    now,
                    cancellationToken);

                await unitOfWork.CommitAsync(cancellationToken);
                return new IngestionResult(
                    true,
                    SmsProcessingStatus.Duplicate,
                    message.Id,
                    null,
                    true,
                    message.ProcessingDetail,
                    "DUPLICATE_SMS");
            }

            if (isAlert)
            {
                var recentIncident = await unitOfWork.Incidents.GetRecentForDedupAsync(
                    device.Id,
                    parsed.Severity,
                    parsed.SequenceNumber,
                    fingerprint,
                    incomingSms.IsSimulated,
                    now - DuplicateWindow,
                    cancellationToken);

                if (recentIncident is not null && recentIncident.Status != IncidentStatus.Resolved)
                {
                    var duplicateDetail = parsed.SequenceNumber.HasValue
                        ? $"An unresolved {parsed.Severity} incident for sequence {parsed.SequenceNumber.Value} already exists in the duplicate window."
                        : "An unresolved incident for this sender and identical SMS body already exists in the duplicate window.";
                    return await StoreDuplicateAsync(
                        unitOfWork,
                        message,
                        device,
                        incomingSms,
                        now,
                        duplicateDetail,
                        "DUPLICATE_INCIDENT",
                        cancellationToken);
                }

                resolvedIncidentInDedupWindow = recentIncident is { Status: IncidentStatus.Resolved };

                if (parsed.SequenceNumber.HasValue &&
                    device.LastSequenceNumber.HasValue &&
                    parsed.SequenceNumber.Value <= device.LastSequenceNumber.Value)
                {
                    sequenceResetDetail =
                        $"Sequence {parsed.SequenceNumber.Value} is not newer than {device.LastSequenceNumber.Value}; treated as a possible device reset. The alert was retained and was not suppressed.";
                }
            }

            if (!isAlert && parsed.SequenceNumber.HasValue && device.LastSequenceNumber.HasValue)
            {
                var sequence = parsed.SequenceNumber.Value;
                if (sequence <= device.LastSequenceNumber.Value)
                {
                    var probableReset = IsProbableStatusReset(device, deviceTimestamp, timestampTrusted);
                    if (!probableReset)
                    {
                        if (sequence == device.LastSequenceNumber.Value)
                        {
                            return await StoreDuplicateAsync(
                                unitOfWork,
                                message,
                                device,
                                incomingSms,
                                now,
                                $"Sequence {sequence} has already been processed for this detector.",
                                "DUPLICATE_SEQUENCE",
                                cancellationToken);
                        }

                        message.ProcessingStatus = SmsProcessingStatus.OutOfOrder;
                        message.ProcessingDetail =
                            $"Sequence {sequence} is older than the last processed sequence {device.LastSequenceNumber.Value}; detector state was not changed.";
                        message.ProcessedAtUtc = now;
                        await unitOfWork.Messages.AddAsync(message, cancellationToken);
                        await AddCommunicationLogAsync(
                            unitOfWork,
                            CommunicationOutcome.Skipped,
                            "Out-of-order SMS retained without regression",
                            message.ProcessingDetail,
                            "OUT_OF_ORDER",
                            device,
                            incomingSms,
                            now,
                            cancellationToken);
                        await AddAuditAsync(
                            unitOfWork,
                            "SmsOutOfOrder",
                            "GasDevice",
                            device.Id,
                            AuditOutcome.Failure,
                            message.ProcessingDetail,
                            now,
                            cancellationToken);

                        await unitOfWork.CommitAsync(cancellationToken);
                        PublishSafe(new MonitorNotification(
                            "Out-of-order detector message",
                            $"{device.DeviceCode} sent sequence {sequence} after sequence {device.LastSequenceNumber.Value}. The older reading was retained but not applied.",
                            Severity.Warning,
                            incomingSms.IsSimulated,
                            now));

                        return new IngestionResult(
                            true,
                            SmsProcessingStatus.OutOfOrder,
                            message.Id,
                            null,
                            false,
                            message.ProcessingDetail,
                            "OUT_OF_ORDER");
                    }

                    sequenceResetDetail =
                        $"Sequence {sequence} is not newer than {device.LastSequenceNumber.Value}; accepted as a probable reset because the device timestamp advanced beyond the last successful report.";
                }
            }

            message.DeviceTimestampUtc = deviceTimestamp;
            message.DeviceTimestampTrusted = timestampTrusted;
            message.ProcessingStatus = SmsProcessingStatus.Processed;
            message.ProcessedAtUtc = now;
            var warningDetail = BuildWarningDetail(parseResult.Issues);
            message.ProcessingDetail = CombineDetails(warningDetail, sequenceResetDetail);

            if (sequenceResetDetail is not null)
            {
                await AddCommunicationLogAsync(
                    unitOfWork,
                    CommunicationOutcome.Information,
                    "Possible sequence reset accepted",
                    sequenceResetDetail,
                    "SEQUENCE_RESET_ACCEPTED",
                    device,
                    incomingSms,
                    now,
                    cancellationToken);
                await AddAuditAsync(
                    unitOfWork,
                    "SmsSequenceResetAccepted",
                    "GasDevice",
                    device.Id,
                    AuditOutcome.Success,
                    sequenceResetDetail,
                    now,
                    cancellationToken);
            }

            device.LastSuccessfulCommunicationUtc = receivedAt;
            device.LastReportedState = GasProtocolMap.ToReportedState(parsed.EventCode);
            device.LastReading = parsed.Reading;
            device.LastReadingUnit = parsed.Unit;
            device.LastEventCode = parsed.EventCode;
            device.LastCommunicationErrorUtc = null;
            device.LastCommunicationError = null;
            device.UpdatedAtUtc = now;
            if (parsed.SequenceNumber.HasValue)
            {
                device.LastSequenceNumber = parsed.SequenceNumber.Value;
            }

            await unitOfWork.Messages.AddAsync(message, cancellationToken);

            if (parsed.EventCode == GasEventCode.Status)
            {
                await unitOfWork.CommitAsync(cancellationToken);

                return new IngestionResult(
                    true,
                    SmsProcessingStatus.Processed,
                    message.Id,
                    null,
                    false,
                    $"Status reading accepted for {device.DeviceCode}.",
                    null);
            }

            var dedupKey = BuildIncidentDedupKey(
                device.DeviceCode,
                parsed,
                fingerprint,
                receivedAt,
                resolvedIncidentInDedupWindow,
                incomingSms.IsSimulated);

            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                DeviceId = device.Id,
                TriggeringSmsMessageId = message.Id,
                DeviceCodeSnapshot = device.DeviceCode,
                DeviceNameSnapshot = device.DisplayName,
                LocationSnapshot = device.InstallationLocation,
                Severity = parsed.Severity,
                Status = IncidentStatus.Open,
                OccurredAtUtc = occurredAt,
                OccurredTimeSource = occurredTimeSource,
                ReceivedAtUtc = receivedAt,
                OpenedAtUtc = now,
                Reading = parsed.Reading,
                Unit = parsed.Unit,
                SequenceNumber = parsed.SequenceNumber,
                DedupKey = dedupKey,
                IsSimulated = incomingSms.IsSimulated
            };

            await unitOfWork.Incidents.AddAsync(incident, cancellationToken);
            await AddAuditAsync(
                unitOfWork,
                "IncidentOpened",
                "Incident",
                incident.Id,
                AuditOutcome.Success,
                CombineDetails(
                    $"{parsed.Severity} alert opened for {device.DeviceCode} at {device.InstallationLocation}. Raw reading {parsed.Reading} {parsed.Unit}. Sequence {parsed.SequenceNumber?.ToString() ?? "not provided"}.",
                    sequenceResetDetail) ?? $"{parsed.Severity} alert opened.",
                now,
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            var simulatedPrefix = incomingSms.IsSimulated ? "SIMULATED - " : string.Empty;
            PublishSafe(new MonitorNotification(
                $"{simulatedPrefix}{parsed.Severity.ToString().ToUpperInvariant()} gas alert",
                $"{device.DeviceCode} at {device.InstallationLocation} reported {parsed.Severity.ToString().ToUpperInvariant()} (raw reading {parsed.Reading} {parsed.Unit}). Acknowledge the incident in Live Alerts.",
                parsed.Severity,
                incomingSms.IsSimulated,
                now));
            PublishIncidentCreatedSafe(incident.Id);

            return new IngestionResult(
                true,
                SmsProcessingStatus.Processed,
                message.Id,
                incident.Id,
                false,
                $"Incident opened for {device.DeviceCode}.",
                null);
        }
        catch (OperationCanceledException)
        {
            if (unitOfWork is not null)
            {
                await RollbackSafeAsync(unitOfWork);
            }

            throw;
        }
        catch (Exception ex)
        {
            if (unitOfWork is not null)
            {
                await RollbackSafeAsync(unitOfWork);
            }

            logger.LogError(
                ex,
                "Failed to persist incoming SMS from {Sender} (fingerprint {FingerprintPrefix}).",
                SmsPrivacy.MaskPhoneNumber(incomingSms.From),
                fingerprint[..12]);
            PublishSafe(new MonitorNotification(
                "SMS could not be saved",
                "An incoming SMS could not be persisted. If it came from a physical modem it remains in modem storage for reconciliation; the raw content is not claimed as processed.",
                Severity.Warning,
                incomingSms.IsSimulated,
                now));

            return new IngestionResult(
                false,
                SmsProcessingStatus.Failed,
                null,
                null,
                false,
                "The SMS could not be saved to the database. It has not been acknowledged to the modem.",
                "DATABASE_WRITE_FAILED");
        }
        finally
        {
            if (unitOfWork is not null)
            {
                await unitOfWork.DisposeAsync();
            }
        }
    }

    private static SmsMessage BuildMessage(
        IncomingSms incomingSms,
        string? normalizedSender,
        string fingerprint,
        DateTime receivedAtUtc,
        DateTime storedAtUtc,
        string? sourceReference,
        SmsParseResult parseResult)
    {
        var parsed = parseResult.Message;
        return new SmsMessage
        {
            Id = Guid.NewGuid(),
            SenderNumberRaw = incomingSms.From,
            SenderNumberNormalized = string.IsNullOrWhiteSpace(normalizedSender) ? null : normalizedSender,
            BodyRaw = incomingSms.Body,
            Fingerprint = fingerprint,
            ReceivedAtUtc = receivedAtUtc,
            StoredAtUtc = storedAtUtc,
            ModemStorageIndex = incomingSms.StorageIndex,
            SourceReference = sourceReference,
            IsSimulated = incomingSms.IsSimulated,
            DeviceCode = parsed?.DeviceCode ?? TryExtractDeviceCode(incomingSms.Body),
            ProtocolVersion = parsed?.ProtocolVersion,
            EventCode = parsed?.EventCode ?? GasEventCode.Unknown,
            Severity = parsed?.Severity ?? Severity.Unknown,
            Reading = parsed?.Reading,
            Unit = parsed?.Unit,
            SequenceNumber = parsed?.SequenceNumber,
            DeviceTimestampUtc = parsed?.DeviceTimestamp?.UtcDateTime
        };
    }

    private static string? TryExtractDeviceCode(string body)
    {
        foreach (var segment in body.Split('|').Skip(1))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = segment[..separator].Trim();
            if (!key.Equals("device", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = segment[(separator + 1)..].Trim().ToUpperInvariant();
            return value.Length is >= 3 and <= 32 &&
                   value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
                ? value
                : null;
        }

        return null;
    }

    private static string DescribeParseFailure(SmsParseResult result) =>
        $"{result.PrimaryErrorCode ?? "INVALID_MESSAGE"}: {result.PrimaryErrorMessage ?? "The SMS could not be parsed."}";

    private static string? BuildWarningDetail(IReadOnlyList<ProtocolIssue> issues)
    {
        var warnings = issues.Where(x => !x.IsError).Select(x => x.Message).ToArray();
        if (warnings.Length == 0)
        {
            return null;
        }

        var text = string.Join(" ", warnings);
        return text.Length <= 1000 ? text : text[..1000];
    }

    private static string? CombineDetails(string? first, string? second)
    {
        var text = string.Join(
            " ",
            new[] { first, second }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length <= 1000 ? text : text[..1000];
    }

    private static bool IsProbableStatusReset(
        GasDevice device,
        DateTime? deviceTimestamp,
        bool timestampTrusted)
    {
        if (!device.LastSuccessfulCommunicationUtc.HasValue)
        {
            return true;
        }

        return timestampTrusted &&
               deviceTimestamp.HasValue &&
               deviceTimestamp.Value > device.LastSuccessfulCommunicationUtc.Value;
    }

    private static string BuildIncidentDedupKey(
        string deviceCode,
        ParsedGasMessage parsed,
        string fingerprint,
        DateTime receivedAtUtc,
        bool resolvedIncidentInDedupWindow,
        bool isSimulated)
    {
        var eventText = GasProtocolMap.EventText(parsed.EventCode);
        var bucket = receivedAtUtc.Ticks / DuplicateWindow.Ticks;
        var mode = isSimulated ? "SIM" : "REAL";
        var baseKey = parsed.SequenceNumber.HasValue
            ? $"{deviceCode}|{mode}|{eventText}|seq={parsed.SequenceNumber.Value}|b={bucket}"
            : $"{deviceCode}|{mode}|{eventText}|fp={fingerprint}|b={bucket}";

        return resolvedIncidentInDedupWindow
            ? $"{baseKey}|r={receivedAtUtc.Ticks}"
            : baseKey;
    }

    private async Task<IngestionResult> StoreDuplicateAsync(
        IGasGuardUnitOfWork unitOfWork,
        SmsMessage message,
        GasDevice device,
        IncomingSms incomingSms,
        DateTime now,
        string detail,
        string errorCode,
        CancellationToken cancellationToken)
    {
        message.ProcessingStatus = SmsProcessingStatus.Duplicate;
        message.ProcessingDetail = detail;
        message.ProcessedAtUtc = now;
        await unitOfWork.Messages.AddAsync(message, cancellationToken);
        await AddCommunicationLogAsync(
            unitOfWork,
            CommunicationOutcome.Information,
            "Duplicate SMS retained without new state change",
            detail,
            errorCode,
            device,
            incomingSms,
            now,
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return new IngestionResult(
            true,
            SmsProcessingStatus.Duplicate,
            message.Id,
            null,
            true,
            detail,
            errorCode);
    }

    private async Task AddCommunicationLogAsync(
        IGasGuardUnitOfWork unitOfWork,
        CommunicationOutcome outcome,
        string summary,
        string? detail,
        string? errorCode,
        GasDevice? device,
        IncomingSms incomingSms,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await unitOfWork.CommunicationLogs.AddAsync(new CommunicationLog
        {
            Id = Guid.NewGuid(),
            TimestampUtc = now,
            Direction = CommunicationDirection.Inbound,
            Kind = CommunicationKind.Processing,
            Outcome = outcome,
            Summary = summary,
            Detail = detail,
            ErrorCode = errorCode,
            DeviceId = device?.Id,
            ModemStorageIndex = incomingSms.StorageIndex,
            IsSimulated = incomingSms.IsSimulated
        }, cancellationToken);
    }

    private static Task AddAuditAsync(
        IGasGuardUnitOfWork unitOfWork,
        string action,
        string entityType,
        Guid entityId,
        AuditOutcome outcome,
        string details,
        DateTime now,
        CancellationToken cancellationToken) =>
        unitOfWork.AddAuditLogAsync(
            new AuditEntryDraft(SystemActor, action, entityType, entityId.ToString(), outcome, details),
            now,
            cancellationToken);

    private void PublishSafe(MonitorNotification notification)
    {
        try
        {
            eventBus.PublishNotification(notification);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A monitor notification handler failed.");
        }
    }

    private void PublishIncidentCreatedSafe(Guid incidentId)
    {
        try
        {
            eventBus.PublishIncidentCreated(incidentId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An incident-created handler failed.");
        }
    }

    private async Task RollbackSafeAsync(IGasGuardUnitOfWork unitOfWork)
    {
        try
        {
            await unitOfWork.RollbackAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rollback failed after an SMS persistence error.");
        }
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
