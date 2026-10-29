using GasGuard.Application.Abstractions;
using GasGuard.Application.Services;
using GasGuard.Contracts.Modem;
using Microsoft.Extensions.Logging;

namespace GasGuard.Infrastructure.Modem;

public sealed class AtCommandQueue : IModemCommandQueue
{
    private readonly IAtTransport _transport;
    private readonly int _commandTimeoutMs;
    private readonly IClock _clock;
    private readonly ILogger<AtCommandQueue>? _logger;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly object _sync = new();
    private PendingCommand? _pending;
    private bool _awaitingCmtBody;
    private string? _cmtSender;
    private bool _desynchronized;
    private bool _subscribed;
    private bool _disposed;

    public AtCommandQueue(
        IAtTransport transport,
        int commandTimeoutMs,
        IClock clock,
        ILogger<AtCommandQueue>? logger = null)
    {
        _transport = transport;
        _commandTimeoutMs = Math.Clamp(commandTimeoutMs, 500, 120_000);
        _clock = clock;
        _logger = logger;
    }

    public bool IsOpen => !_disposed && _transport.IsOpen && !_desynchronized;

    public event EventHandler<IncomingSms>? UnsolicitedSmsReceived;
    public event EventHandler<string>? UnsolicitedNotificationReceived;
    public event EventHandler<Exception>? TransportFaulted;

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Subscribe();
        await _transport.OpenAsync(cancellationToken);
        lock (_sync)
        {
            _desynchronized = false;
            _pending = null;
            _awaitingCmtBody = false;
            _cmtSender = null;
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        PendingCommand? pending;
        lock (_sync)
        {
            pending = _pending;
            _pending = null;
            _desynchronized = true;
            _awaitingCmtBody = false;
            _cmtSender = null;
        }

        pending?.Completion.TrySetResult(
            AtCommandResponse.Failed("AT_TRANSPORT_CLOSED", "The serial transport was closed before a response arrived."));

        await _transport.CloseAsync(cancellationToken);
    }

    public async Task<AtCommandResponse> ExecuteAsync(
        AtCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsOpen)
        {
            return AtCommandResponse.Failed(
                "AT_TRANSPORT_CLOSED",
                "The serial transport is not open. Reconnect the modem before sending AT commands.");
        }

        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsOpen)
            {
                return AtCommandResponse.Failed("AT_TRANSPORT_CLOSED", "The serial transport is not open.");
            }

            var pending = new PendingCommand(
                request,
                new TaskCompletionSource<AtCommandResponse>(TaskCreationOptions.RunContinuationsAsynchronously));
            lock (_sync)
            {
                _pending = pending;
            }

            try
            {
                await _transport.WriteAsync(request.Command + "\r", cancellationToken);
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_pending, pending))
                    {
                        _pending = null;
                    }

                    _desynchronized = true;
                }

                _logger?.LogWarning(ex, "Writing AT command failed.");
                return AtCommandResponse.Failed(
                    "AT_WRITE_FAILED",
                    $"The AT command could not be written: {ex.Message}");
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_commandTimeoutMs);

            try
            {
                return await pending.Completion.Task.WaitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_pending, pending))
                    {
                        _pending = null;
                    }

                    _desynchronized = true;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return AtCommandResponse.FromCancellation();
                }

                _logger?.LogWarning(
                    "AT command {Command} timed out after {Timeout} ms; the queue requires reconnection.",
                    request.Command,
                    _commandTimeoutMs);
                return AtCommandResponse.Timeout(
                    $"No complete modem response arrived within {_commandTimeoutMs} ms.");
            }
        }
        finally
        {
            _commandGate.Release();
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
            await CloseAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Closing the AT command queue reported an error.");
        }

        Unsubscribe();
        await _transport.DisposeAsync();
        _commandGate.Dispose();
    }

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _transport.LineReceived += OnLineReceived;
        _transport.Faulted += OnTransportFaulted;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _transport.LineReceived -= OnLineReceived;
        _transport.Faulted -= OnTransportFaulted;
        _subscribed = false;
    }

    private void OnLineReceived(object? sender, string line)
    {
        IncomingSms? incomingSms = null;
        string? notification = null;
        PendingCommand? completedPending = null;
        AtCommandResponse? completedResponse = null;
        PendingCommand? payloadPending = null;
        string? payloadToWrite = null;

        try
        {
            lock (_sync)
            {
                var trimmed = line.TrimEnd('\r', '\n');
                if (trimmed.Length == 0)
                {
                    return;
                }

                if (_awaitingCmtBody)
                {
                    var senderNumber = _cmtSender ?? "UNKNOWN";
                    var bodyFingerprint = SmsFingerprint.Create(senderNumber, trimmed);
                    incomingSms = new IncomingSms(
                        senderNumber,
                        trimmed,
                        _clock.UtcNow,
                        null,
                        $"CMT:{_clock.UtcNow:yyyyMMddHHmmssfff}:{senderNumber}:{bodyFingerprint[..16]}",
                        false);
                    _awaitingCmtBody = false;
                    _cmtSender = null;
                }
                else if (trimmed.StartsWith("+CMT:", StringComparison.OrdinalIgnoreCase))
                {
                    _awaitingCmtBody = true;
                    _cmtSender = AtResponseParsing.ParseCmtSender(trimmed);
                }
                else if (trimmed.StartsWith("+CMTI:", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.Equals("RING", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.StartsWith("+CIEV:", StringComparison.OrdinalIgnoreCase))
                {
                    notification = trimmed;
                }
                else
                {
                    var pending = _pending;
                    if (pending is not null)
                    {
                        if (trimmed.Equals(">", StringComparison.Ordinal))
                        {
                            if (pending.Request.Payload is not null && !pending.PayloadSent)
                            {
                                pending.PayloadSent = true;
                                payloadPending = pending;
                                payloadToWrite = pending.Request.Payload;
                            }
                            else
                            {
                                pending.Lines.Add(trimmed);
                                completedPending = pending;
                                completedResponse = AtCommandResponse.Failed(
                                    "AT_UNEXPECTED_PROMPT",
                                    "The modem requested input for a command that has no payload.");
                                _pending = null;
                                _desynchronized = true;
                            }
                        }
                        else if (pending.Lines.Count == 0 &&
                                 trimmed.Equals(pending.Request.Command, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                        else
                        {
                            pending.Lines.Add(trimmed);

                            if (trimmed.Equals("OK", StringComparison.OrdinalIgnoreCase))
                            {
                                completedPending = pending;
                                completedResponse = new AtCommandResponse(
                                    true,
                                    pending.Lines
                                        .Where(x => !x.Equals("OK", StringComparison.OrdinalIgnoreCase))
                                        .ToArray(),
                                    string.Join("\r\n", pending.Lines));
                                _pending = null;
                            }
                            else if (trimmed.Equals("ERROR", StringComparison.OrdinalIgnoreCase) ||
                                     trimmed.StartsWith("+CME ERROR", StringComparison.OrdinalIgnoreCase) ||
                                     trimmed.StartsWith("+CMS ERROR", StringComparison.OrdinalIgnoreCase))
                            {
                                var errorCode = trimmed.StartsWith("+CMS", StringComparison.OrdinalIgnoreCase)
                                    ? "CMS_ERROR"
                                    : trimmed.StartsWith("+CME", StringComparison.OrdinalIgnoreCase)
                                        ? "CME_ERROR"
                                        : "AT_ERROR";
                                completedPending = pending;
                                completedResponse = new AtCommandResponse(
                                    false,
                                    pending.Lines.ToArray(),
                                    string.Join("\r\n", pending.Lines),
                                    errorCode);
                                _pending = null;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "AT response handling failed.");
        }

        if (incomingSms is not null)
        {
            RaiseSms(incomingSms);
        }

        if (notification is not null)
        {
            RaiseNotification(notification);
        }

        if (payloadPending is not null && payloadToWrite is not null)
        {
            _ = WritePayloadAsync(payloadPending, payloadToWrite);
        }

        if (completedPending is not null && completedResponse is not null)
        {
            completedPending.Completion.TrySetResult(completedResponse);
        }
    }

    private async Task WritePayloadAsync(PendingCommand pending, string payload)
    {
        try
        {
            await _transport.WriteAsync(payload + "\u001A", CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Writing the SMS payload failed.");
            lock (_sync)
            {
                if (ReferenceEquals(_pending, pending))
                {
                    _pending = null;
                    _desynchronized = true;
                }
            }

            pending.Completion.TrySetResult(
                AtCommandResponse.Failed("AT_WRITE_FAILED", $"The SMS payload could not be written: {ex.Message}"));
        }
    }

    private void OnTransportFaulted(object? sender, Exception exception)
    {
        PendingCommand? pending;
        lock (_sync)
        {
            pending = _pending;
            _pending = null;
            _desynchronized = true;
            _awaitingCmtBody = false;
            _cmtSender = null;
        }

        pending?.Completion.TrySetResult(
            AtCommandResponse.Failed("TRANSPORT_FAULTED", $"The serial transport failed: {exception.Message}"));

        try
        {
            TransportFaulted?.Invoke(this, exception);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "A transport-fault handler failed.");
        }
    }

    private void RaiseSms(IncomingSms sms)
    {
        try
        {
            UnsolicitedSmsReceived?.Invoke(this, sms);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "An unsolicited-SMS handler failed.");
        }
    }

    private void RaiseNotification(string notification)
    {
        try
        {
            UnsolicitedNotificationReceived?.Invoke(this, notification);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "An unsolicited AT notification handler failed.");
        }
    }

    private sealed class PendingCommand(AtCommandRequest request, TaskCompletionSource<AtCommandResponse> completion)
    {
        public AtCommandRequest Request { get; } = request;
        public TaskCompletionSource<AtCommandResponse> Completion { get; } = completion;
        public List<string> Lines { get; } = [];
        public bool PayloadSent { get; set; }
    }
}
