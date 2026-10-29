using System.IO.Ports;
using GasGuard.Application.Abstractions;
using GasGuard.Contracts.Modem;
using Microsoft.Extensions.Logging;

namespace GasGuard.Infrastructure.Modem;

public sealed class GsmModemService : IGsmModemService, ISimulatedModemControl
{
    private readonly IAtTransportFactory _transportFactory;
    private readonly IClock _clock;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<GsmModemService> _logger;
    private readonly SimulatedGsmModem _simulatedModem;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _sync = new();
    private readonly List<ModemDiagnosticEntry> _managerDiagnostics = [];
    private IGsmModemAdapter? _active;
    private PhysicalGsmModemAdapter? _physicalModem;
    private ModemConnectionOptions? _lastOptions;
    private ModemStatus _status;
    private bool _isSimulated = true;
    private bool _disposed;

    public GsmModemService(
        IAtTransportFactory transportFactory,
        IClock clock,
        ILoggerFactory loggerFactory,
        ILogger<GsmModemService> logger)
    {
        _transportFactory = transportFactory;
        _clock = clock;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _simulatedModem = new SimulatedGsmModem(clock, loggerFactory.CreateLogger<SimulatedGsmModem>());
        _simulatedModem.StatusChanged += OnAdapterStatusChanged;
        _simulatedModem.SmsReceived += OnAdapterSmsReceived;
        _simulatedModem.Faulted += OnAdapterFaulted;
        _simulatedModem.ReconciliationRequested += OnAdapterReconciliationRequested;
        _active = null;
        _isSimulated = false;
        _status = ModemStatus.Disconnected("No GSM modem is connected.");
    }

    public bool IsConnected => _active?.IsConnected == true;
    public bool IsSimulated => _isSimulated;

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

    public bool SimulatorActive => _active is SimulatedGsmModem;
    public int PendingStoredMessageCount => _simulatedModem.PendingStoredMessageCount;

    public event EventHandler<ModemStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<IncomingSmsEventArgs>? SmsReceived;
    public event EventHandler<ModemFaultedEventArgs>? Faulted;
    public event EventHandler? ReconciliationRequested;

    public async Task<ModemOperationResult> ConnectAsync(
        ModemConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await DisposePhysicalModemAsync();

            _lastOptions = options;
            _isSimulated = options.UseSimulator;

            if (options.UseSimulator)
            {
                SetActive(_simulatedModem);
                AddManagerDiagnostic("SYSTEM", "Connecting simulated modem. Demonstration data only.", false);
                var result = await _simulatedModem.ConnectAsync(options, cancellationToken);
                SetStatus(_simulatedModem.Status);
                AddManagerDiagnostic("SYSTEM", result.Message, !result.Success);
                return result;
            }

            await _simulatedModem.DisconnectAsync(cancellationToken);
            var physical = new PhysicalGsmModemAdapter(
                options,
                _transportFactory,
                _clock,
                _loggerFactory,
                _loggerFactory.CreateLogger<PhysicalGsmModemAdapter>());
            physical.StatusChanged += OnAdapterStatusChanged;
            physical.SmsReceived += OnAdapterSmsReceived;
            physical.Faulted += OnAdapterFaulted;
            physical.ReconciliationRequested += OnAdapterReconciliationRequested;

            _physicalModem = physical;
            SetActive(physical);
            AddManagerDiagnostic("SYSTEM", $"Connecting physical modem on {options.PortName}.", false);
            var connectResult = await physical.ConnectAsync(options, cancellationToken);
            SetStatus(physical.Status);
            AddManagerDiagnostic(
                "SYSTEM",
                connectResult.Success
                    ? connectResult.Message
                    : $"Physical modem connection failed: {connectResult.Message}",
                !connectResult.Success);
            return connectResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Modem connection failed.");
            var status = ModemStatus.Disconnected($"Modem connection failed: {ex.Message}") with
            {
                IsSimulated = options.UseSimulator,
                PortName = options.PortName
            };
            SetStatus(status);
            AddManagerDiagnostic("ERROR", status.LastError!, true);
            return ModemOperationResult.Fail(
                ModemOperationOutcome.Failure,
                status.LastError!);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<ModemOperationResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_active is null)
            {
                return ModemOperationResult.Ok("The modem is already disconnected.");
            }

            AddManagerDiagnostic("SYSTEM", "Disconnecting modem.", false);
            var result = await _active.DisconnectAsync(cancellationToken);
            SetStatus(_active.Status);

            if (_active is PhysicalGsmModemAdapter physical)
            {
                CopyDiagnostics(physical.GetDiagnostics(500));
                physical.StatusChanged -= OnAdapterStatusChanged;
                physical.SmsReceived -= OnAdapterSmsReceived;
                physical.Faulted -= OnAdapterFaulted;
                physical.ReconciliationRequested -= OnAdapterReconciliationRequested;
                await physical.DisposeAsync();
                if (ReferenceEquals(_physicalModem, physical))
                {
                    _physicalModem = null;
                }

                _active = null;
            }

            return result;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<ModemStatus> RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        var active = _active;
        if (active is null)
        {
            return Status;
        }

        var status = await active.RefreshStatusAsync(cancellationToken);
        SetStatus(status);
        return status;
    }

    public async Task<SmsSendResult> SendSmsAsync(
        string recipientE164,
        string body,
        CancellationToken cancellationToken = default)
    {
        var active = _active;
        if (active is null || !active.IsConnected)
        {
            return new SmsSendResult(
                false,
                SmsSendOutcome.NotConnected,
                "The modem is not connected.",
                recipientE164);
        }

        return await active.SendSmsAsync(recipientE164, body, cancellationToken);
    }

    public async Task<IReadOnlyList<IncomingSms>> ReadStoredMessagesAsync(
        bool unreadOnly,
        CancellationToken cancellationToken = default)
    {
        var active = _active;
        if (active is null || !active.IsConnected)
        {
            return [];
        }

        return await active.ReadStoredMessagesAsync(unreadOnly, cancellationToken);
    }

    public async Task<ModemOperationResult> DeleteStoredMessageAsync(
        int storageIndex,
        CancellationToken cancellationToken = default)
    {
        var active = _active;
        if (active is null || !active.IsConnected)
        {
            return ModemOperationResult.Fail(
                ModemOperationOutcome.NotConnected,
                "The modem is not connected.");
        }

        return await active.DeleteStoredMessageAsync(storageIndex, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetAvailablePortsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<string> ports = SerialPort.GetPortNames()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Task.FromResult(ports);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Serial port enumeration failed.");
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }

    public IReadOnlyList<ModemDiagnosticEntry> GetDiagnostics(int maximumEntries = 250)
    {
        var limit = Math.Clamp(maximumEntries, 1, 1000);
        var combined = new List<ModemDiagnosticEntry>();
        lock (_sync)
        {
            combined.AddRange(_managerDiagnostics);
        }

        var active = _active;
        if (active is not null)
        {
            combined.AddRange(active.GetDiagnostics(limit));
        }

        return combined
            .OrderBy(x => x.TimestampUtc)
            .TakeLast(limit)
            .ToArray();
    }

    public void ClearDiagnostics()
    {
        lock (_sync)
        {
            _managerDiagnostics.Clear();
        }

        _active?.ClearDiagnostics();
    }

    public async Task<ModemOperationResult> InjectMessageAsync(
        SimulatedSmsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_active is not SimulatedGsmModem simulated)
        {
            return ModemOperationResult.Fail(
                ModemOperationOutcome.Rejected,
                "Close the physical modem connection and connect the simulated modem before injecting demonstration messages.");
        }

        return await simulated.InjectMessageAsync(request, cancellationToken);
    }

    public async Task<ModemOperationResult> SimulateDisconnectionAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (_active is not SimulatedGsmModem simulated)
        {
            return ModemOperationResult.Fail(
                ModemOperationOutcome.Rejected,
                "Simulated disconnection is available only in simulated modem mode.");
        }

        var result = await simulated.SimulateDisconnectionAsync(reason, cancellationToken);
        SetStatus(simulated.Status);
        return result;
    }

    public async Task<ModemOperationResult> SetSendModeAsync(
        SimulatedSendMode mode,
        CancellationToken cancellationToken = default)
    {
        if (_active is not SimulatedGsmModem simulated)
        {
            return ModemOperationResult.Fail(
                ModemOperationOutcome.Rejected,
                "Outgoing SMS simulation is available only in simulated modem mode.");
        }

        return await simulated.SetSendModeAsync(mode, cancellationToken);
    }

    public IReadOnlyList<IncomingSms> GetStoredMessages() =>
        _simulatedModem.GetStoredMessages();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _lifecycleGate.WaitAsync();
        try
        {
            if (_physicalModem is not null)
            {
                _physicalModem.StatusChanged -= OnAdapterStatusChanged;
                _physicalModem.SmsReceived -= OnAdapterSmsReceived;
                _physicalModem.Faulted -= OnAdapterFaulted;
                _physicalModem.ReconciliationRequested -= OnAdapterReconciliationRequested;
                await _physicalModem.DisposeAsync();
                _physicalModem = null;
            }

            _simulatedModem.StatusChanged -= OnAdapterStatusChanged;
            _simulatedModem.SmsReceived -= OnAdapterSmsReceived;
            _simulatedModem.Faulted -= OnAdapterFaulted;
            _simulatedModem.ReconciliationRequested -= OnAdapterReconciliationRequested;
            await _simulatedModem.DisposeAsync();
            _active = null;
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }

    private void SetActive(IGsmModemAdapter adapter)
    {
        _active = adapter;
        SetStatus(adapter.Status);
    }

    private void OnAdapterStatusChanged(object? sender, ModemStatusChangedEventArgs args)
    {
        if (!ReferenceEquals(sender, _active))
        {
            return;
        }

        SetStatus(args.Status);
    }

    private void OnAdapterSmsReceived(object? sender, IncomingSmsEventArgs args)
    {
        if (!ReferenceEquals(sender, _active))
        {
            return;
        }

        AddManagerDiagnostic(
            "RX",
            $"SMS received from {args.Message.From}{(args.Message.IsSimulated ? " (simulated)" : string.Empty)}.",
            false);
        try
        {
            SmsReceived?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An incoming SMS handler failed.");
        }
    }

    private void OnAdapterFaulted(object? sender, ModemFaultedEventArgs args)
    {
        if (!ReferenceEquals(sender, _active))
        {
            return;
        }

        AddManagerDiagnostic("ERROR", args.Message, true);
        try
        {
            Faulted?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A modem fault handler failed.");
        }
    }

    private void OnAdapterReconciliationRequested(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _active))
        {
            return;
        }

        try
        {
            ReconciliationRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A reconciliation-request handler failed.");
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

    private void AddManagerDiagnostic(string direction, string text, bool isError)
    {
        lock (_sync)
        {
            _managerDiagnostics.Add(new ModemDiagnosticEntry(_clock.UtcNow, direction, text, isError));
            if (_managerDiagnostics.Count > 500)
            {
                _managerDiagnostics.RemoveRange(0, _managerDiagnostics.Count - 500);
            }
        }
    }

    private void CopyDiagnostics(IReadOnlyList<ModemDiagnosticEntry> entries)
    {
        lock (_sync)
        {
            _managerDiagnostics.AddRange(entries);
            if (_managerDiagnostics.Count > 500)
            {
                _managerDiagnostics.RemoveRange(0, _managerDiagnostics.Count - 500);
            }
        }
    }

    private async Task DisposePhysicalModemAsync()
    {
        if (_physicalModem is null)
        {
            return;
        }

        _physicalModem.StatusChanged -= OnAdapterStatusChanged;
        _physicalModem.SmsReceived -= OnAdapterSmsReceived;
        _physicalModem.Faulted -= OnAdapterFaulted;
        _physicalModem.ReconciliationRequested -= OnAdapterReconciliationRequested;
        CopyDiagnostics(_physicalModem.GetDiagnostics(500));
        await _physicalModem.DisposeAsync();
        _physicalModem = null;
        if (_active is PhysicalGsmModemAdapter)
        {
            _active = null;
        }
    }
}
