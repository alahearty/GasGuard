using System.Globalization;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Services;
using GasGuard.Contracts.Modem;
using Microsoft.Extensions.Logging;

namespace GasGuard.Infrastructure.Modem;

public sealed class PhysicalGsmModemAdapter : IGsmModemAdapter
{
    private readonly ModemConnectionOptions _options;
    private readonly IAtTransportFactory _transportFactory;
    private readonly IClock _clock;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PhysicalGsmModemAdapter> _logger;
    private readonly object _sync = new();
    private readonly List<ModemDiagnosticEntry> _diagnostics = [];
    private AtCommandQueue? _queue;
    private ModemStatus _status = ModemStatus.Disconnected();
    private bool _disposed;

    public PhysicalGsmModemAdapter(
        ModemConnectionOptions options,
        IAtTransportFactory transportFactory,
        IClock clock,
        ILoggerFactory loggerFactory,
        ILogger<PhysicalGsmModemAdapter> logger)
    {
        _options = options;
        _transportFactory = transportFactory;
        _clock = clock;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public bool IsConnected => Status.ConnectionState == ModemConnectionState.Connected;
    public ModemStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public event EventHandler<ModemStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<IncomingSmsEventArgs>? SmsReceived;
    public event EventHandler<ModemFaultedEventArgs>? Faulted;
    public event EventHandler? ReconciliationRequested;

    public async Task<ModemOperationResult> ConnectAsync(
        ModemConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_queue is not null)
        {
            await DisconnectAsync(cancellationToken);
        }

        SetStatus(_status with
        {
            ConnectionState = ModemConnectionState.Connecting,
            IsSimulated = false,
            PortName = options.PortName,
            UpdatedAtUtc = _clock.UtcNow,
            LastError = null
        });

        var queue = new AtCommandQueue(
            _transportFactory.Create(options),
            options.CommandTimeoutMs,
            _clock,
            _loggerFactory.CreateLogger<AtCommandQueue>());
        queue.UnsolicitedSmsReceived += OnUnsolicitedSms;
        queue.UnsolicitedNotificationReceived += OnUnsolicitedNotification;
        queue.TransportFaulted += OnTransportFaulted;
        _queue = queue;

        try
        {
            await queue.OpenAsync(cancellationToken);
            AddDiagnostic("TX", "Serial port opened.", false);

            await ExecuteBestEffortAsync("ATE0", cancellationToken);
            var at = await ExecuteLoggedAsync("AT", cancellationToken);
            if (!at.Success)
            {
                await CloseQueueAsync(queue);
                var error = at.TimedOut
                    ? "The modem did not respond to AT. Check the selected port and baud rate."
                    : $"The modem rejected AT ({at.ErrorCode ?? "ERROR"}). Check the port and modem mode.";
                SetStatus(ModemStatus.Disconnected(error) with
                {
                    IsSimulated = false,
                    PortName = options.PortName
                });
                return ModemOperationResult.Fail(
                    at.TimedOut ? ModemOperationOutcome.Timeout : ModemOperationOutcome.Failure,
                    error);
            }

            var identityResponse = await ExecuteLoggedAsync("ATI", cancellationToken);
            var identity = ModemStatusParser.ParseModemIdentity(identityResponse.RawResponse);

            var cpin = await ExecuteLoggedAsync("AT+CPIN?", cancellationToken);
            var simReadiness = ModemStatusParser.ParseSimReadiness(cpin.RawResponse);

            var csq = await ExecuteLoggedAsync("AT+CSQ", cancellationToken);
            var signalQuality = ModemStatusParser.ParseSignalQuality(csq.RawResponse);

            var creg = await ExecuteLoggedAsync("AT+CREG?", cancellationToken);
            var registration = ModemStatusParser.ParseRegistration(creg.RawResponse);

            var cops = await ExecuteBestEffortAsync("AT+COPS?", cancellationToken);
            var networkOperator = ModemStatusParser.ParseNetworkOperator(cops.RawResponse);

            var cmgf = await ExecuteLoggedAsync("AT+CMGF=1", cancellationToken);
            var smsTextMode = cmgf.Success;

            if (smsTextMode)
            {
                var characterSet = await ExecuteBestEffortAsync("AT+CSCS=\"GSM\"", cancellationToken);
                if (!characterSet.Success)
                {
                    await ExecuteBestEffortAsync("AT+CSCS=\"IRA\"", cancellationToken);
                }
            }

            var cnmi = await ExecuteBestEffortAsync("AT+CNMI=2,1,0,0,0", cancellationToken);
            var newMessageIndication = cnmi.Success;

            if (!queue.IsOpen)
            {
                var error = "The modem stopped responding during initialization. Check the port, baud rate and modem, then reconnect.";
                await CloseQueueAsync(queue);
                SetStatus(ModemStatus.Disconnected(error) with
                {
                    IsSimulated = false,
                    PortName = options.PortName
                });
                AddDiagnostic("ERROR", error, true);
                return ModemOperationResult.Fail(ModemOperationOutcome.Timeout, error);
            }

            var now = _clock.UtcNow;
            var status = new ModemStatus
            {
                ConnectionState = ModemConnectionState.Connected,
                IsSimulated = false,
                PortName = options.PortName,
                Model = identity,
                SimReadiness = simReadiness,
                SimStatusText = ModemStatusParser.SimReadinessText(simReadiness),
                SignalQuality = signalQuality,
                SignalDescription = ModemStatusParser.SignalDescription(signalQuality),
                RegistrationState = registration,
                RegistrationText = ModemStatusParser.RegistrationText(registration),
                NetworkOperator = networkOperator,
                SmsTextModeEnabled = smsTextMode,
                NewMessageIndicationEnabled = newMessageIndication,
                ConnectedAtUtc = now,
                UpdatedAtUtc = now
            };
            SetStatus(status);

            var warnings = new List<string>();
            if (!status.IsReadyForSms)
            {
                warnings.Add("The modem serial link is open, but SIM/network readiness is incomplete.");
            }

            if (!smsTextMode)
            {
                warnings.Add("SMS text mode could not be enabled; incoming and outgoing SMS processing is unavailable.");
            }

            if (!newMessageIndication)
            {
                warnings.Add("New-message indication was rejected; unread messages will still be polled.");
            }

            return ModemOperationResult.Ok(
                warnings.Count == 0
                    ? $"Modem connected on {options.PortName}. {status.SimStatusText}; {status.RegistrationText}."
                    : $"Modem serial link opened on {options.PortName}. {string.Join(" ", warnings)}");
        }
        catch (OperationCanceledException)
        {
            await CloseQueueAsync(queue);
            SetStatus(ModemStatus.Disconnected("Connection attempt cancelled.") with
            {
                IsSimulated = false,
                PortName = options.PortName
            });
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physical modem connection failed on {Port}.", options.PortName);
            await CloseQueueAsync(queue);
            SetStatus(ModemStatus.Disconnected($"Serial connection failed: {ex.Message}") with
            {
                IsSimulated = false,
                PortName = options.PortName
            });
            AddDiagnostic("ERROR", ex.Message, true);
            return ModemOperationResult.Fail(
                ModemOperationOutcome.Failure,
                $"The modem could not be opened on {options.PortName}: {ex.Message}");
        }
    }

    public async Task<ModemOperationResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var queue = _queue;
        _queue = null;
        if (queue is not null)
        {
            queue.UnsolicitedSmsReceived -= OnUnsolicitedSms;
            queue.UnsolicitedNotificationReceived -= OnUnsolicitedNotification;
            queue.TransportFaulted -= OnTransportFaulted;
            try
            {
                await queue.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Disposing the physical AT queue reported an error.");
            }
        }

        SetStatus(ModemStatus.Disconnected("Modem disconnected by the operator.") with
        {
            IsSimulated = false,
            PortName = _options.PortName
        });
        AddDiagnostic("SYSTEM", "Physical modem disconnected.", false);
        return ModemOperationResult.Ok("Modem disconnected.");
    }

    public async Task<ModemStatus> RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        var queue = _queue;
        if (queue is null)
        {
            return Status;
        }

        if (!queue.IsOpen)
        {
            if (Status.ConnectionState == ModemConnectionState.Connected)
            {
                MarkFaulted("The modem command pipeline is desynchronized. Reconnect the modem.");
            }

            return Status;
        }

        try
        {
            var cpin = await ExecuteLoggedAsync("AT+CPIN?", cancellationToken);
            var csq = await ExecuteLoggedAsync("AT+CSQ", cancellationToken);
            var creg = await ExecuteLoggedAsync("AT+CREG?", cancellationToken);
            var cops = await ExecuteBestEffortAsync("AT+COPS?", cancellationToken);

            if (!cpin.Success || !csq.Success || !creg.Success || !queue.IsOpen)
            {
                MarkFaulted("One or more required modem status commands failed. Reconnect the modem and check SIM/network readiness.");
                return Status;
            }

            var current = Status;
            var status = current with
            {
                SimReadiness = ModemStatusParser.ParseSimReadiness(cpin.RawResponse),
                SignalQuality = ModemStatusParser.ParseSignalQuality(csq.RawResponse),
                RegistrationState = ModemStatusParser.ParseRegistration(creg.RawResponse),
                NetworkOperator = ModemStatusParser.ParseNetworkOperator(cops.RawResponse),
                UpdatedAtUtc = _clock.UtcNow
            };
            status = status with
            {
                SimStatusText = ModemStatusParser.SimReadinessText(status.SimReadiness),
                SignalDescription = ModemStatusParser.SignalDescription(status.SignalQuality),
                RegistrationText = ModemStatusParser.RegistrationText(status.RegistrationState)
            };
            SetStatus(status);

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Modem status refresh failed.");
            MarkFaulted($"Modem status refresh failed: {ex.Message}");
            return Status;
        }
    }

    public async Task<SmsSendResult> SendSmsAsync(
        string recipientE164,
        string body,
        CancellationToken cancellationToken = default)
    {
        var queue = _queue;
        var current = Status;
        if (queue is null || current.ConnectionState != ModemConnectionState.Connected)
        {
            return new SmsSendResult(
                false,
                SmsSendOutcome.NotConnected,
                "The physical modem is not connected.",
                recipientE164);
        }

        if (!queue.IsOpen)
        {
            MarkFaulted("The modem command pipeline is desynchronized. Reconnect before sending SMS.");
            return new SmsSendResult(
                false,
                SmsSendOutcome.NotConnected,
                "The modem command pipeline is desynchronized. Reconnect the modem.",
                recipientE164);
        }

        if (!current.IsReadyForSms)
        {
            return new SmsSendResult(
                false,
                SmsSendOutcome.RejectedByModem,
                $"The modem is not ready to send SMS: {current.SimStatusText}. {current.RegistrationText}.",
                recipientE164);
        }

        if (!current.SmsTextModeEnabled)
        {
            return new SmsSendResult(
                false,
                SmsSendOutcome.RejectedByModem,
                "SMS text mode is not enabled on this modem.",
                recipientE164);
        }

        if (string.IsNullOrWhiteSpace(body) || body.Length > 160)
        {
            return new SmsSendResult(
                false,
                SmsSendOutcome.RejectedByModem,
                "The message must contain 1-160 characters. Multipart SMS is not implemented.",
                recipientE164);
        }

        if (body.Any(character => character > 127 || character == '\u001A'))
        {
            return new SmsSendResult(
                false,
                SmsSendOutcome.RejectedByModem,
                "Only printable ASCII SMS text is supported. Non-ASCII or Ctrl-Z characters are rejected.",
                recipientE164);
        }

        AddDiagnostic("TX", $"SMS send request for {recipientE164}; payload suppressed from diagnostics.", false);
        var response = await queue.ExecuteAsync(
            new AtCommandRequest($"AT+CMGS=\"{recipientE164}\"", body),
            cancellationToken);

        if (response.Success)
        {
            var reference = AtResponseParsing.ParseCmgsReference(response.Lines);
            AddDiagnostic(
                "RX",
                $"SMS accepted by the modem for {recipientE164}; reference {reference ?? "not provided"}. Delivery is not confirmed.",
                false);
            return new SmsSendResult(
                true,
                SmsSendOutcome.SubmittedToModem,
                "The modem accepted the message. This confirms modem submission only, not delivery to the recipient.",
                recipientE164,
                reference,
                false);
        }

        if (response.TimedOut)
        {
            MarkFaulted("The modem did not answer the SMS send command. Reconnect before trying again.");
            return new SmsSendResult(
                false,
                SmsSendOutcome.Timeout,
                "The modem did not acknowledge the SMS send command.",
                recipientE164);
        }

        if (response.Cancelled)
        {
            return new SmsSendResult(false, SmsSendOutcome.Cancelled, "SMS sending was cancelled.", recipientE164);
        }

        if (!queue.IsOpen)
        {
            MarkFaulted("The modem command pipeline is desynchronized after the SMS send attempt. Reconnect the modem.");
        }

        AddDiagnostic("ERROR", $"SMS send failed: {response.ErrorCode ?? "ERROR"}.", true);
        return new SmsSendResult(
            false,
            SmsSendOutcome.RejectedByModem,
            $"The modem rejected the message ({response.ErrorCode ?? "ERROR"}).",
            recipientE164);
    }

    public async Task<IReadOnlyList<IncomingSms>> ReadStoredMessagesAsync(
        bool unreadOnly,
        CancellationToken cancellationToken = default)
    {
        var queue = _queue;
        var current = Status;
        if (queue is null)
        {
            return [];
        }

        if (!queue.IsOpen)
        {
            MarkFaulted("The modem command pipeline is desynchronized. Reconnect before reading stored SMS.");
            throw new InvalidOperationException("The modem command pipeline is desynchronized.");
        }

        if (current.ConnectionState != ModemConnectionState.Connected)
        {
            return [];
        }

        if (!current.SmsTextModeEnabled)
        {
            throw new InvalidOperationException("SMS text mode is not enabled on the modem.");
        }

        var response = await queue.ExecuteAsync(
            new AtCommandRequest(unreadOnly ? "AT+CMGL=\"REC UNREAD\"" : "AT+CMGL=\"ALL\""),
            cancellationToken);
        if (!response.Success)
        {
            if (response.TimedOut || !queue.IsOpen)
            {
                MarkFaulted("Reading stored SMS failed or timed out. Reconnect the modem.");
            }

            AddDiagnostic("ERROR", $"Reading stored SMS failed: {response.ErrorCode ?? "ERROR"}.", true);
            throw new InvalidOperationException(
                $"Reading stored SMS failed ({response.ErrorCode ?? "ERROR"}).");
        }

        var messages = ParseStoredMessages(response.Lines);
        SetStatus(Status with
        {
            LastPollAtUtc = _clock.UtcNow,
            UpdatedAtUtc = _clock.UtcNow,
            LastError = null
        });
        AddDiagnostic("RX", $"Stored SMS poll returned {messages.Count} message(s).", false);
        return messages;
    }

    public async Task<ModemOperationResult> DeleteStoredMessageAsync(
        int storageIndex,
        CancellationToken cancellationToken = default)
    {
        var queue = _queue;
        if (queue is null)
        {
            return ModemOperationResult.Fail(
                ModemOperationOutcome.NotConnected,
                "The modem is not connected.");
        }

        if (!queue.IsOpen)
        {
            MarkFaulted("The modem command pipeline is desynchronized. Reconnect before deleting stored SMS.");
            return ModemOperationResult.Fail(
                ModemOperationOutcome.NotConnected,
                "The modem command pipeline is desynchronized.");
        }

        var response = await queue.ExecuteAsync(
            new AtCommandRequest($"AT+CMGD={storageIndex}"),
            cancellationToken);
        if (response.Success)
        {
            AddDiagnostic("SYSTEM", $"Stored SMS {storageIndex} deleted after successful persistence.", false);
            return ModemOperationResult.Ok($"Stored message {storageIndex} was deleted.");
        }

        if (response.TimedOut || !queue.IsOpen)
        {
            MarkFaulted("Deleting a stored SMS failed or timed out. Reconnect the modem.");
        }

        AddDiagnostic(
            "ERROR",
            $"Deleting stored SMS {storageIndex} failed: {response.ErrorCode ?? "ERROR"}.",
            true);
        return ModemOperationResult.Fail(
            response.TimedOut ? ModemOperationOutcome.Timeout : ModemOperationOutcome.Failure,
            $"Stored message {storageIndex} could not be deleted ({response.ErrorCode ?? "ERROR"}).");
    }

    public IReadOnlyList<ModemDiagnosticEntry> GetDiagnostics(int maximumEntries)
    {
        lock (_sync)
        {
            return _diagnostics.TakeLast(Math.Clamp(maximumEntries, 1, 1000)).ToArray();
        }
    }

    public void ClearDiagnostics()
    {
        lock (_sync)
        {
            _diagnostics.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisconnectAsync();
    }

    private List<IncomingSms> ParseStoredMessages(IReadOnlyList<string> lines)
    {
        var messages = new List<IncomingSms>();
        int? currentIndex = null;
        string? currentSender = null;
        string? currentTimestampText = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("+CMGL:", StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = AtResponseParsing.ParseStorageIndex(line);
                currentSender = AtResponseParsing.ParseSender(line);
                currentTimestampText = AtResponseParsing.ParseQuotedValue(line, 4);
                continue;
            }

            if (currentIndex is null)
            {
                continue;
            }

            var sender = string.IsNullOrWhiteSpace(currentSender) ? "UNKNOWN" : currentSender;
            var fingerprint = SmsFingerprint.Create(sender, line);
            messages.Add(new IncomingSms(
                sender,
                line,
                _clock.UtcNow,
                currentIndex,
                $"MODEM:{currentIndex}:{fingerprint}",
                false,
                currentTimestampText));

            currentIndex = null;
            currentSender = null;
            currentTimestampText = null;
        }

        return messages;
    }

    private async Task<AtCommandResponse> ExecuteLoggedAsync(string command, CancellationToken cancellationToken)
    {
        var queue = _queue;
        if (queue is null)
        {
            return AtCommandResponse.Failed("AT_TRANSPORT_CLOSED", "The AT queue is not available.");
        }

        AddDiagnostic("TX", command, false);
        var response = await queue.ExecuteAsync(new AtCommandRequest(command), cancellationToken);
        AddDiagnostic(
            response.Success ? "RX" : "ERROR",
            response.Success
                ? $"{command} succeeded."
                : $"{command} failed: {response.ErrorCode ?? "ERROR"}.",
            !response.Success);
        if (response.TimedOut || (!response.Success && queue is not null && !queue.IsOpen))
        {
            MarkFaulted($"The modem did not answer {command} or the command pipeline was lost. Reconnect before continuing.");
        }

        return response;
    }

    private async Task<AtCommandResponse> ExecuteBestEffortAsync(string command, CancellationToken cancellationToken)
    {
        var response = await ExecuteLoggedAsync(command, cancellationToken);
        if (!response.Success)
        {
            _logger.LogInformation("Optional modem command {Command} failed with {Error}.", command, response.ErrorCode);
        }

        return response;
    }

    private void OnUnsolicitedSms(object? sender, IncomingSms sms)
    {
        AddDiagnostic("RX", $"Unsolicited SMS received from {sms.From}.", false);
        try
        {
            SmsReceived?.Invoke(this, new IncomingSmsEventArgs(sms));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An incoming SMS handler failed.");
        }
    }

    private void OnUnsolicitedNotification(object? sender, string notification)
    {
        AddDiagnostic("RX", notification, false);
        if (notification.StartsWith("+CMTI:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ReconciliationRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A reconciliation-request handler failed.");
            }
        }
    }

    private void OnTransportFaulted(object? sender, Exception exception)
    {
        _logger.LogWarning(exception, "The serial transport faulted.");
        MarkFaulted($"Serial transport failure: {exception.Message}");
    }

    private void MarkFaulted(string message)
    {
        SetStatus(Status with
        {
            ConnectionState = ModemConnectionState.Faulted,
            LastError = message,
            UpdatedAtUtc = _clock.UtcNow
        });
        AddDiagnostic("ERROR", message, true);
        try
        {
            Faulted?.Invoke(this, new ModemFaultedEventArgs(message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A modem-fault handler failed.");
        }
    }

    private async Task CloseQueueAsync(AtCommandQueue queue)
    {
        queue.UnsolicitedSmsReceived -= OnUnsolicitedSms;
        queue.UnsolicitedNotificationReceived -= OnUnsolicitedNotification;
        queue.TransportFaulted -= OnTransportFaulted;
        try
        {
            await queue.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Disposing the AT queue after connection failure reported an error.");
        }

        if (ReferenceEquals(_queue, queue))
        {
            _queue = null;
        }
    }

    private void SetStatus(ModemStatus status)
    {
        lock (_sync)
        {
            _status = status;
        }

        try
        {
            StatusChanged?.Invoke(this, new ModemStatusChangedEventArgs(status));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A modem status handler failed.");
        }
    }

    private void AddDiagnostic(string direction, string text, bool isError)
    {
        lock (_sync)
        {
            _diagnostics.Add(new ModemDiagnosticEntry(_clock.UtcNow, direction, text, isError));
            if (_diagnostics.Count > 500)
            {
                _diagnostics.RemoveRange(0, _diagnostics.Count - 500);
            }
        }
    }
}
