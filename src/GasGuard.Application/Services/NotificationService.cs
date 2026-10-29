using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Contracts.Modem;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GasGuard.Application.Services;

public sealed class NotificationService(
    IGsmModemService modemService,
    IIncidentService incidentService,
    IPhoneNumberNormalizer phoneNumbers,
    ISettingsService settingsService,
    IGasGuardUnitOfWorkFactory unitOfWorkFactory,
    IClock clock,
    IUserContext userContext,
    IAuditLogService auditLog,
    ILogger<NotificationService> logger) : INotificationService
{
    private const int MaximumSmsLength = 160;
    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5)
    ];

    public async Task<SmsSendResult> SendTestMessageAsync(
        string recipientNumber,
        string message,
        bool operatorConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!operatorConfirmed)
        {
            await auditLog.RecordAsync(new AuditEntryDraft(
                userContext.OperatorName,
                "TestSmsDenied",
                "SmsSend",
                null,
                AuditOutcome.Denied,
                "Test SMS was not sent because operator confirmation was absent."),
                cancellationToken);

            return new SmsSendResult(
                false,
                SmsSendOutcome.Cancelled,
                "Operator confirmation is required before sending a test SMS.",
                recipientNumber);
        }

        if (!phoneNumbers.TryNormalize(recipientNumber, out var normalizedRecipient, out var phoneError))
        {
            return new SmsSendResult(
                false,
                SmsSendOutcome.RejectedByModem,
                phoneError ?? "The recipient number is invalid.",
                recipientNumber);
        }

        if (string.IsNullOrWhiteSpace(message) || message.Length > MaximumSmsLength)
        {
            return new SmsSendResult(
                false,
                SmsSendOutcome.RejectedByModem,
                $"The test message must contain 1-{MaximumSmsLength} characters.",
                normalizedRecipient);
        }

        if (message.Any(character => character > 127 || character == '\u001A'))
        {
            return new SmsSendResult(
                false,
                SmsSendOutcome.RejectedByModem,
                "The test message must use printable ASCII characters only.",
                normalizedRecipient);
        }

        var result = await modemService.SendSmsAsync(normalizedRecipient, message, cancellationToken);
        await RecordOutgoingAsync(normalizedRecipient, result, "Operator-confirmed test SMS", cancellationToken);
        await auditLog.RecordAsync(new AuditEntryDraft(
            userContext.OperatorName,
            "TestSmsSent",
            "SmsSend",
            null,
            result.SubmittedToModem ? AuditOutcome.Success : AuditOutcome.Failure,
            result.SubmittedToModem
                ? $"Test SMS submitted to the modem for {SmsPrivacy.MaskPhoneNumber(normalizedRecipient)}. Recipient delivery is not confirmed."
                : $"Test SMS to {SmsPrivacy.MaskPhoneNumber(normalizedRecipient)} failed: {result.Message}"),
            cancellationToken);

        return result;
    }

    public async Task<NotificationDispatchResult> DispatchIncidentNotificationsAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default)
    {
        var details = await incidentService.GetDetailsAsync(incidentId, cancellationToken);
        if (details is null)
        {
            return new NotificationDispatchResult(0, 0, 0, []);
        }

        if (details.Summary.IsSimulated)
        {
            return new NotificationDispatchResult(0, 0, 0, []);
        }

        var settings = await settingsService.GetSnapshotAsync(cancellationToken);
        if (!settings.Preferences.AutomaticContactNotifications)
        {
            return new NotificationDispatchResult(0, 0, 0, []);
        }

        await using var unitOfWork = unitOfWorkFactory.Create();
        var incidentDevice = await unitOfWork.Devices.GetByIdAsync(details.Summary.DeviceId, cancellationToken);
        if (incidentDevice?.IsDemo == true)
        {
            return new NotificationDispatchResult(0, 0, 0, []);
        }

        var contacts = await unitOfWork.Contacts.ListAsync(false, cancellationToken);
        var recipients = contacts
            .Where(contact => details.Summary.Severity == Severity.Critical
                ? contact.NotifyOnCritical
                : contact.NotifyOnWarning)
            .ToArray();

        if (recipients.Length == 0)
        {
            return new NotificationDispatchResult(0, 0, 0, []);
        }

        var message = BuildIncidentMessage(details);
        var items = new List<NotificationDispatchItem>();
        var submitted = 0;
        var failed = 0;

        foreach (var contact in recipients)
        {
            if (!phoneNumbers.TryNormalize(contact.PhoneNumber, out var normalized, out _))
            {
                items.Add(new NotificationDispatchItem(
                    contact.Id,
                    contact.FullName,
                    contact.PhoneNumber,
                    "Rejected: invalid stored number",
                    false,
                    "The contact phone number is not valid."));
                failed++;
                continue;
            }

            var (result, attempts) = await SendWithBoundedRetriesAsync(
                normalized,
                message,
                settings.Preferences.NotificationMaxAttempts,
                "Automatic incident notification",
                cancellationToken);

            var status = result.SubmittedToModem
                ? $"Submitted to modem after {attempts} attempt(s); delivery not confirmed"
                : $"Failed after {attempts} attempt(s): {result.Message}";
            items.Add(new NotificationDispatchItem(
                contact.Id,
                contact.FullName,
                normalized,
                status,
                result.SubmittedToModem,
                result.SubmittedToModem ? null : result.Message));

            if (result.SubmittedToModem)
            {
                submitted++;
            }
            else
            {
                failed++;
            }
        }

        await auditLog.RecordAsync(new AuditEntryDraft(
            userContext.OperatorName,
            "IncidentNotificationsDispatched",
            "Incident",
            details.Summary.Id.ToString(),
            submitted > 0 || failed == 0 ? AuditOutcome.Success : AuditOutcome.Failure,
            $"Automatic incident notification dispatch for {details.Summary.DeviceCode}: {submitted} submitted, {failed} failed, {items.Count} contact(s). Delivery is not claimed."),
            cancellationToken);

        return new NotificationDispatchResult(items.Count, submitted, failed, items);
    }

    public async Task<NotificationDispatchResult> DispatchCommunicationFailureNotificationAsync(
        string failureSummary,
        CancellationToken cancellationToken = default)
    {
        if (modemService.Status.IsSimulated)
        {
            return new NotificationDispatchResult(0, 0, 0, []);
        }

        var settings = await settingsService.GetSnapshotAsync(cancellationToken);
        if (!settings.Preferences.AutomaticContactNotifications)
        {
            return new NotificationDispatchResult(0, 0, 0, []);
        }

        await using var unitOfWork = unitOfWorkFactory.Create();
        var contacts = await unitOfWork.Contacts.ListAsync(false, cancellationToken);
        var recipients = contacts.Where(x => x.NotifyOnCommunicationFailure).ToArray();
        if (recipients.Length == 0)
        {
            return new NotificationDispatchResult(0, 0, 0, []);
        }

        var message = "GasGuard: GSM modem communication failure. Gas detection local alarms operate independently. Check GasGuard Desktop."
            .Truncate(MaximumSmsLength);
        var items = new List<NotificationDispatchItem>();
        var submitted = 0;
        var failed = 0;

        foreach (var contact in recipients)
        {
            if (!phoneNumbers.TryNormalize(contact.PhoneNumber, out var normalized, out _))
            {
                failed++;
                items.Add(new NotificationDispatchItem(
                    contact.Id,
                    contact.FullName,
                    contact.PhoneNumber,
                    "Rejected: invalid stored number",
                    false,
                    "The contact phone number is not valid."));
                continue;
            }

            var (result, attempts) = await SendWithBoundedRetriesAsync(
                normalized,
                message,
                settings.Preferences.NotificationMaxAttempts,
                "Communication failure notification",
                cancellationToken);
            submitted += result.SubmittedToModem ? 1 : 0;
            failed += result.SubmittedToModem ? 0 : 1;
            items.Add(new NotificationDispatchItem(
                contact.Id,
                contact.FullName,
                normalized,
                result.SubmittedToModem
                    ? $"Submitted to modem after {attempts} attempt(s); delivery not confirmed"
                    : $"Failed after {attempts} attempt(s): {result.Message}",
                result.SubmittedToModem,
                result.SubmittedToModem ? null : result.Message));
        }

        await auditLog.RecordAsync(new AuditEntryDraft(
            userContext.OperatorName,
            "CommunicationFailureNotificationsDispatched",
            "Modem",
            null,
            submitted > 0 ? AuditOutcome.Success : AuditOutcome.Failure,
            $"Communication failure notification for {failureSummary}: {submitted} submitted, {failed} failed. Delivery is not claimed."),
            cancellationToken);

        return new NotificationDispatchResult(items.Count, submitted, failed, items);
    }

    private async Task<(SmsSendResult Result, int Attempts)> SendWithBoundedRetriesAsync(
        string recipient,
        string message,
        int maximumAttempts,
        string purpose,
        CancellationToken cancellationToken)
    {
        var attemptsAllowed = Math.Clamp(maximumAttempts, 1, 3);
        SmsSendResult? last = null;

        for (var attempt = 1; attempt <= attemptsAllowed; attempt++)
        {
            last = await modemService.SendSmsAsync(recipient, message, cancellationToken);
            await RecordOutgoingAsync(recipient, last, $"{purpose} (attempt {attempt}/{attemptsAllowed})", cancellationToken);

            if (last.SubmittedToModem)
            {
                return (last, attempt);
            }

            if (last.Outcome is SmsSendOutcome.NotConnected or SmsSendOutcome.Cancelled or SmsSendOutcome.RejectedByModem)
            {
                return (last, attempt);
            }

            if (attempt < attemptsAllowed)
            {
                var delay = RetryBackoff[Math.Min(attempt - 1, RetryBackoff.Length - 1)];
                await Task.Delay(delay, cancellationToken);
            }
        }

        return (last ?? new SmsSendResult(
            false,
            SmsSendOutcome.NotConnected,
            "No send attempt was made.",
            recipient), attemptsAllowed);
    }

    private async Task RecordOutgoingAsync(
        string recipient,
        SmsSendResult result,
        string summary,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var unitOfWork = unitOfWorkFactory.Create();
            var outcome = result.Outcome switch
            {
                SmsSendOutcome.SubmittedToModem => CommunicationOutcome.Success,
                SmsSendOutcome.Timeout => CommunicationOutcome.Timeout,
                SmsSendOutcome.NotConnected => CommunicationOutcome.Failure,
                SmsSendOutcome.Cancelled => CommunicationOutcome.Skipped,
                _ => CommunicationOutcome.Rejected
            };

            await unitOfWork.CommunicationLogs.AddAsync(new CommunicationLog
            {
                Id = Guid.NewGuid(),
                TimestampUtc = clock.UtcNow,
                Direction = CommunicationDirection.Outbound,
                Kind = CommunicationKind.SmsSend,
                Outcome = outcome,
                Summary = summary,
                Detail = result.Message,
                ErrorCode = result.SubmittedToModem ? null : result.Outcome.ToString().ToUpperInvariant(),
                RecipientNumber = recipient,
                ProviderReference = result.ProviderReference,
                IsSimulated = modemService.Status.IsSimulated
            }, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Outgoing SMS communication log write failed.");
        }
    }

    private static string BuildIncidentMessage(IncidentDetails details)
    {
        var summary = details.Summary;
        var severity = summary.Severity == Severity.Critical ? "CRITICAL" : "WARNING";
        var head = $"GasGuard {severity}: {summary.DeviceCode} at ";
        var reading = summary.Reading.HasValue
            ? $" Raw {summary.Reading.Value} {summary.Unit ?? "raw"}."
            : string.Empty;
        var tail = $".{reading} Open incident. Local alarm independent.";
        var availableForLocation = MaximumSmsLength - head.Length - tail.Length;
        if (availableForLocation <= 0)
        {
            return $"GasGuard {severity}: {summary.DeviceCode}. Local alarm independent.".Truncate(MaximumSmsLength);
        }

        var location = summary.Location;
        if (location.Length > availableForLocation)
        {
            location = availableForLocation <= 3
                ? string.Empty
                : location[..(availableForLocation - 3)] + "...";
        }

        return new string((head + location + tail)
            .Select(character => character <= 127 ? character : '?')
            .ToArray());
    }
}

internal static class NotificationTextExtensions
{
    public static string Truncate(this string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
