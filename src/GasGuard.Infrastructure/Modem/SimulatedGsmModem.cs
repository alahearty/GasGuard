using System.Globalization;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Services;
using GasGuard.Contracts.Modem;
using Microsoft.Extensions.Logging;

namespace GasGuard.Infrastructure.Modem;

public sealed class SimulatedGsmModem : IGsmModemAdapter
{
    private readonly IClock _clock;
    private readonly ILogger<SimulatedGsmModem> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<int, IncomingSms> _storedMessages = [];
    private readonly List<ModemDiagnosticEntry> _diagnostics = [];
    private ModemStatus _status = new()
    {
        ConnectionState = ModemConnectionState.Disconnected,
        IsSimulated = true,
        PortName = "SIMULATOR",
        SimStatusText = "Simulator disconnected",
        RegistrationText = "Unknown",
        SignalDescription = "Simulated signal",
        UpdatedAtUtc = DateTime.UtcNow
    };
    private int _nextStorageIndex = 1;
    private long _sentCount;
    private SimulatedSendMode _sendMode = SimulatedSendMode.Success;
    private bool _disposed;

    public SimulatedGsmModem(IClock clock, ILogger<SimulatedGsmModem> logger)
    {
        _clock = clock;
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

    public int PendingStoredMessageCount
    {
        get
        {
            lock (_sync)
            {
                return _storedMessages.Count;
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
        await Task.Delay(250, cancellationToken);
        var now = _clock.UtcNow;
        ModemStatus status;
        lock (_sync)
        {
            status = new ModemStatus
            {
                ConnectionState = ModemConnectionState.Connected,
                IsSimulated = true,
                PortName = "SIMULATOR",
                Manufacturer = "GasGuard",
                Model = "Simulated GSM Modem",
                Firmware = "SIM-1.0",
                SerialNumber = "DEMO-0001",
                SimReadiness = SimReadiness.Ready,
                SimStatusText = "SIM ready (simulated)",
                SignalQuality = 25,
                SignalDescription = "Excellent simulated signal",
                RegistrationState = NetworkRegistrationState.RegisteredHome,
                RegistrationText = "Registered on simulated home network",
                NetworkOperator = "GasGuard Demonstration Network",
                SmsTextModeEnabled = true,
                NewMessageIndicationEnabled = true,
                ConnectedAtUtc = now,
                UpdatedAtUtc = now
            };
            _status = status;
            AddDiagnosticCore("SYSTEM", "Simulated modem connected. No physical GSM hardware is in use.", false);
        }

        RaiseStatus(status);
        if (PendingStoredMessageCount > 0)
        {
            try
            {
                ReconciliationRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A simulated reconciliation-request handler failed.");
            }
        }

        return ModemOperationResult.Ok(
            "Simulated modem connected. All readings and messages in this mode are DEMONSTRATION DATA.");
    }

    public async Task<ModemOperationResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(80, cancellationToken);
        var status = new ModemStatus
        {
            ConnectionState = ModemConnectionState.Disconnected,
            IsSimulated = true,
            PortName = "SIMULATOR",
            SimStatusText = "Simulator disconnected",
            RegistrationText = "Unknown",
            SignalDescription = "Simulated signal",
            UpdatedAtUtc = _clock.UtcNow
        };
        lock (_sync)
        {
            _status = status;
            AddDiagnosticCore("SYSTEM", "Simulated modem disconnected.", false);
        }

        RaiseStatus(status);
        return ModemOperationResult.Ok("Simulated modem disconnected.");
    }

    public async Task<ModemOperationResult> SimulateDisconnectionAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(80, cancellationToken);
        var status = new ModemStatus
        {
            ConnectionState = ModemConnectionState.Faulted,
            IsSimulated = true,
            PortName = "SIMULATOR",
            SimStatusText = "Simulated connection lost",
            RegistrationText = "Unknown",
            SignalDescription = "Simulated signal",
            UpdatedAtUtc = _clock.UtcNow,
            LastError = string.IsNullOrWhiteSpace(reason) ? "Simulated serial disconnection." : reason
        };
        lock (_sync)
        {
            _status = status;
            AddDiagnosticCore("SYSTEM", status.LastError!, true);
        }

        RaiseStatus(status);
        RaiseFaulted(new ModemFaultedEventArgs(status.LastError!));
        return ModemOperationResult.Ok("Simulated modem disconnection triggered.");
    }

    public Task<ModemStatus> RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        ModemStatus status;
        lock (_sync)
        {
            _status = _status with { UpdatedAtUtc = _clock.UtcNow };
            status = _status;
        }

        RaiseStatus(status);
        return Task.FromResult(status);
    }

    public async Task<ModemOperationResult> InjectMessageAsync(
        SimulatedSmsRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!string.IsNullOrWhiteSpace(request.Sender) && request.DeliveryDelay is { } delay && delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }

        var now = _clock.UtcNow;
        var connected = IsConnected;
        if (!connected && !request.RetainInStorage)
        {
            return ModemOperationResult.Fail(
                ModemOperationOutcome.NotConnected,
                "The simulated modem is disconnected and the message was not configured to be retained.");
        }

        IncomingSms message;
        int? storageIndex = null;
        lock (_sync)
        {
            if (request.RetainInStorage)
            {
                storageIndex = _nextStorageIndex++;
            }

            var fingerprint = SmsFingerprint.Create(request.Sender, request.Body);
            message = new IncomingSms(
                request.Sender,
                request.Body,
                now,
                storageIndex,
                storageIndex.HasValue ? $"SIM:{storageIndex}:{fingerprint}" : $"SIM-PUSH:{Guid.NewGuid():N}",
                true);

            if (request.RetainInStorage && storageIndex.HasValue)
            {
                _storedMessages[storageIndex.Value] = message;
            }

            AddDiagnosticCore(
                "SIM-IN",
                $"Simulated SMS injected from {request.Sender}; storage index {(storageIndex?.ToString(CultureInfo.InvariantCulture) ?? "none")}.",
                false);
        }

        if (connected && request.DeliverNow)
        {
            RaiseSms(message);
            return ModemOperationResult.Ok("Simulated SMS delivered to the application.");
        }

        return ModemOperationResult.Ok(
            connected
                ? "Simulated SMS stored in the simulated modem for reconciliation."
                : "Simulated modem is disconnected. The message is retained in simulated storage for later reconciliation.");
    }

    public Task<ModemOperationResult> SetSendModeAsync(
        SimulatedSendMode mode,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _sendMode = mode;
            AddDiagnosticCore("SIM", $"Outgoing SMS simulation mode set to {mode}.", false);
        }

        return Task.FromResult(ModemOperationResult.Ok($"Simulated outgoing SMS mode set to {mode}."));
    }

    public Task<IReadOnlyList<IncomingSms>> ReadStoredMessagesAsync(
        bool unreadOnly,
        CancellationToken cancellationToken = default)
    {
        ModemStatus status;
        IncomingSms[] messages;
        lock (_sync)
        {
            if (_status.ConnectionState != ModemConnectionState.Connected)
            {
                return Task.FromResult<IReadOnlyList<IncomingSms>>([]);
            }

            _status = _status with { LastPollAtUtc = _clock.UtcNow, UpdatedAtUtc = _clock.UtcNow };
            status = _status;
            messages = _storedMessages.Values.OrderBy(x => x.StorageIndex).ToArray();
            AddDiagnosticCore("SIM", $"Polled simulated storage; {messages.Length} message(s) available.", false);
        }

        RaiseStatus(status);
        return Task.FromResult<IReadOnlyList<IncomingSms>>(messages);
    }

    public Task<ModemOperationResult> DeleteStoredMessageAsync(
        int storageIndex,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var removed = _storedMessages.Remove(storageIndex);
            AddDiagnosticCore(
                "SIM",
                removed
                    ? $"Simulated stored message {storageIndex} removed after persistence."
                    : $"Simulated stored message {storageIndex} was already absent.",
                false);
        }

        return Task.FromResult(ModemOperationResult.Ok($"Simulated message {storageIndex} cleared from storage."));
    }

    public async Task<SmsSendResult> SendSmsAsync(
        string recipientE164,
        string body,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(250, cancellationToken);
        if (!IsConnected)
        {
            return new SmsSendResult(
                false,
                SmsSendOutcome.NotConnected,
                "The simulated modem is not connected.",
                recipientE164);
        }

        if (string.IsNullOrWhiteSpace(recipientE164) || string.IsNullOrWhiteSpace(body) || body.Length > 160)
        {
            return new SmsSendResult(
                false,
                SmsSendOutcome.RejectedByModem,
                "The simulated modem requires a recipient and a message of 1-160 characters.",
                recipientE164);
        }

        if (body.Any(character => character > 127 || character == '\u001A'))
        {
            return new SmsSendResult(
                false,
                SmsSendOutcome.RejectedByModem,
                "The simulated modem accepts printable ASCII text without the Ctrl-Z terminator.",
                recipientE164);
        }

        SimulatedSendMode mode;
        lock (_sync)
        {
            mode = _sendMode;
        }

        if (mode == SimulatedSendMode.ModemRejected)
        {
            lock (_sync)
            {
                AddDiagnosticCore("SIM-OUT", $"Simulated modem rejected a message to {recipientE164}.", true);
            }

            return new SmsSendResult(
                false,
                SmsSendOutcome.RejectedByModem,
                "SIMULATED REJECTION: the modem refused the message.",
                recipientE164);
        }

        if (mode == SimulatedSendMode.Timeout)
        {
            lock (_sync)
            {
                AddDiagnosticCore("SIM-OUT", $"Simulated timeout while sending to {recipientE164}.", true);
            }

            return new SmsSendResult(
                false,
                SmsSendOutcome.Timeout,
                "SIMULATED TIMEOUT: no modem acknowledgement was produced.",
                recipientE164);
        }

        string reference;
        lock (_sync)
        {
            _sentCount++;
            reference = $"SIM-{_sentCount:0000}";
            AddDiagnosticCore(
                "SIM-OUT",
                $"Simulated SMS accepted for {recipientE164}; modem reference {reference}. Delivery to the recipient is not confirmed.",
                false);
        }

        _logger.LogInformation(
            "Simulated outgoing SMS accepted for {Recipient} with reference {Reference}.",
            SmsPrivacy.MaskPhoneNumber(recipientE164),
            reference);

        return new SmsSendResult(
            true,
            SmsSendOutcome.SubmittedToModem,
            "SIMULATED: message submitted to the simulated modem. Recipient delivery is not confirmed.",
            recipientE164,
            reference,
            false);
    }

    public IReadOnlyList<ModemDiagnosticEntry> GetDiagnostics(int maximumEntries)
    {
        lock (_sync)
        {
            return _diagnostics
                .TakeLast(Math.Clamp(maximumEntries, 1, 1000))
                .ToArray();
        }
    }

    public void ClearDiagnostics()
    {
        lock (_sync)
        {
            _diagnostics.Clear();
        }
    }

    public IReadOnlyList<IncomingSms> GetStoredMessages()
    {
        lock (_sync)
        {
            return _storedMessages.Values.OrderBy(x => x.StorageIndex).ToArray();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Disposing the simulated modem reported an error.");
        }
    }

    private void RaiseStatus(ModemStatus status)
    {
        try
        {
            StatusChanged?.Invoke(this, new ModemStatusChangedEventArgs(status));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A simulated modem status handler failed.");
        }
    }

    private void RaiseSms(IncomingSms message)
    {
        try
        {
            SmsReceived?.Invoke(this, new IncomingSmsEventArgs(message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A simulated modem SMS handler failed.");
        }
    }

    private void RaiseFaulted(ModemFaultedEventArgs args)
    {
        try
        {
            Faulted?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A simulated modem fault handler failed.");
        }
    }

    private void AddDiagnosticCore(string direction, string text, bool isError)
    {
        _diagnostics.Add(new ModemDiagnosticEntry(_clock.UtcNow, direction, text, isError));
        if (_diagnostics.Count > 500)
        {
            _diagnostics.RemoveRange(0, _diagnostics.Count - 500);
        }
    }
}
