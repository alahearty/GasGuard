using System.Threading.Channels;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Contracts.Modem;
using GasGuard.Domain;
using GasGuard.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GasGuard.Application.Services;

public sealed class SmsProcessingCoordinator(
    IGsmModemService modemService,
    ISmsIngestionService ingestionService,
    IIncomingSmsJournal journal,
    INotificationService notificationService,
    ISettingsService settingsService,
    IGasGuardUnitOfWorkFactory unitOfWorkFactory,
    IClock clock,
    IAppEventBus eventBus,
    ILogger<SmsProcessingCoordinator> logger) : ISmsProcessingCoordinator
{
    private readonly Channel<IncomingSms> _ingress = Channel.CreateUnbounded<IncomingSms>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<bool> _reconcileRequests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly SemaphoreSlim _processingGate = new(1, 1);
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);
    private readonly SemaphoreSlim _pauseSync = new(1, 1);
    private readonly object _lifecycleSync = new();
    private CancellationTokenSource? _lifetime;
    private Task? _processTask;
    private Task? _reconcileTask;
    private Task? _pollTask;
    private Task? _recoveryTask;
    private int _reconnectInProgress;
    private int _modemConnectedFlag;
    private int _ingressPending;
    private bool _paused;
    private bool _started;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleSync)
        {
            if (_started)
            {
                return Task.CompletedTask;
            }

            _started = true;
            _lifetime = new CancellationTokenSource();
            modemService.SmsReceived += OnSmsReceived;
            modemService.StatusChanged += OnModemStatusChanged;
            modemService.Faulted += OnModemFaulted;
            modemService.ReconciliationRequested += OnReconciliationRequested;
            _processTask = Task.Run(() => ProcessQueueAsync(_lifetime.Token));
            _reconcileTask = Task.Run(() => ReconcileQueueAsync(_lifetime.Token));
            _pollTask = Task.Run(() => PollLoopAsync(_lifetime.Token));
            _recoveryTask = Task.Run(() => RecoverJournalAsync(_lifetime.Token));
        }

        RequestReconciliation();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? lifetime;
        Task?[] tasks;
        lock (_lifecycleSync)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            lifetime = _lifetime;
            tasks = [_processTask, _reconcileTask, _pollTask, _recoveryTask];
            modemService.SmsReceived -= OnSmsReceived;
            modemService.StatusChanged -= OnModemStatusChanged;
            modemService.Faulted -= OnModemFaulted;
            modemService.ReconciliationRequested -= OnReconciliationRequested;
            _ingress.Writer.TryComplete();
            _reconcileRequests.Writer.TryComplete();
            _lifetime = null;
            _processTask = null;
            _reconcileTask = null;
            _pollTask = null;
            _recoveryTask = null;
        }

        lifetime?.Cancel();
        foreach (var task in tasks.Where(x => x is not null))
        {
            try
            {
                await task!.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
                logger.LogWarning("SMS coordinator shutdown timed out waiting for a worker.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SMS coordinator worker stopped with an error.");
            }
        }

        lifetime?.Dispose();
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await _pauseSync.WaitAsync(cancellationToken);
        try
        {
            if (_paused)
            {
                return;
            }

            await _reconcileGate.WaitAsync(cancellationToken);
            try
            {
                await _processingGate.WaitAsync(cancellationToken);
                _paused = true;
            }
            catch
            {
                _reconcileGate.Release();
                throw;
            }
        }
        finally
        {
            _pauseSync.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _pauseSync.WaitAsync(cancellationToken);
        try
        {
            if (!_paused)
            {
                return;
            }

            _paused = false;
            _processingGate.Release();
            _reconcileGate.Release();
        }
        finally
        {
            _pauseSync.Release();
        }
    }

    public async Task WaitForIngressDrainAsync(CancellationToken cancellationToken = default)
    {
        while (Volatile.Read(ref _ingressPending) > 0)
        {
            await Task.Delay(25, cancellationToken);
        }
    }

    public async Task<int> ReconcileNowAsync(CancellationToken cancellationToken = default)
    {
        await _reconcileGate.WaitAsync(cancellationToken);
        try
        {
            if (!modemService.IsConnected)
            {
                return 0;
            }

            IReadOnlyList<IncomingSms> messages;
            try
            {
                messages = await modemService.ReadStoredMessagesAsync(true, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Stored SMS reconciliation failed.");
                await RecordCommunicationSafeAsync(
                    CommunicationDirection.Inbound,
                    CommunicationKind.Polling,
                    CommunicationOutcome.Failure,
                    "Stored SMS reconciliation failed",
                    ex.Message,
                    "POLL_FAILED",
                    modemService.Status.IsSimulated,
                    null,
                    null,
                    cancellationToken);
                PublishNotificationSafe(new MonitorNotification(
                    "SMS reconciliation failed",
                    "Unread messages could not be read from the modem. They were not deleted and will be retried.",
                    Severity.Warning,
                    modemService.Status.IsSimulated,
                    clock.UtcNow));
                return 0;
            }

            if (messages.Count == 0)
            {
                return 0;
            }

            var processed = 0;
            foreach (var message in messages)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await ProcessOneAsync(message, null, cancellationToken);
                processed++;
            }

            await RecordCommunicationSafeAsync(
                CommunicationDirection.Inbound,
                CommunicationKind.Polling,
                CommunicationOutcome.Information,
                $"Reconciled {processed} stored SMS message(s)",
                "Messages were handed to the ingestion pipeline. Deletion occurs only after persistence.",
                null,
                modemService.Status.IsSimulated,
                null,
                null,
                cancellationToken);

            return processed;
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _ingress.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await ProcessOneAsync(message, null, cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref _ingressPending);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The SMS processing queue stopped unexpectedly.");
        }
    }

    private async Task ReconcileQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var _ in _reconcileRequests.Reader.ReadAllAsync(cancellationToken))
            {
                await ReconcileNowAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The reconciliation queue stopped unexpectedly.");
        }
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var interval = TimeSpan.FromSeconds(30);
                try
                {
                    interval = TimeSpan.FromSeconds(
                        (await settingsService.GetSnapshotAsync(cancellationToken)).Modem.PollIntervalSeconds);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not read the modem polling interval; using 30 seconds.");
                }

                await Task.Delay(interval, cancellationToken);
                if (modemService.IsConnected)
                {
                    await ReconcileNowAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The modem polling loop stopped unexpectedly.");
        }
    }

    private async Task ProcessOneAsync(
        IncomingSms message,
        string? journalId,
        CancellationToken cancellationToken)
    {
        if (journalId is null)
        {
            try
            {
                var entry = await journal.PersistAsync(message, cancellationToken);
                journalId = entry.JournalId;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Could not write the incoming SMS journal entry for sender {Sender}, storage index {StorageIndex}.",
                    SmsPrivacy.MaskPhoneNumber(message.From),
                    message.StorageIndex);
            }
        }

        await _processingGate.WaitAsync(cancellationToken);
        try
        {
            var result = await ingestionService.ProcessAsync(message, cancellationToken);
            if (!result.Persisted)
            {
                await RecordCommunicationSafeAsync(
                    CommunicationDirection.Inbound,
                    CommunicationKind.Processing,
                    CommunicationOutcome.Failure,
                    "Incoming SMS was not persisted",
                    result.Message,
                    result.ErrorCode,
                    message.IsSimulated,
                    result.MessageId,
                    message.StorageIndex,
                    cancellationToken);
                return;
            }

            await RemoveJournalSafeAsync(journalId);

            if (message.StorageIndex.HasValue)
            {
                var snapshot = await settingsService.GetSnapshotAsync(cancellationToken);
                if (snapshot.Modem.DeleteSmsAfterPersistence)
                {
                    var deletion = await modemService.DeleteStoredMessageAsync(
                        message.StorageIndex.Value,
                        cancellationToken);
                    if (!deletion.Success)
                    {
                        logger.LogWarning(
                            "Stored SMS {StorageIndex} was not deleted: {Message}",
                            message.StorageIndex.Value,
                            deletion.Message);
                        await RecordCommunicationSafeAsync(
                            CommunicationDirection.Inbound,
                            CommunicationKind.Processing,
                            CommunicationOutcome.Failure,
                            "Stored SMS could not be deleted after persistence",
                            deletion.Message,
                            "SMS_DELETE_FAILED",
                            message.IsSimulated,
                            result.MessageId,
                            message.StorageIndex,
                            cancellationToken);
                    }
                }
            }

            if (result.IncidentId.HasValue)
            {
                try
                {
                    await notificationService.DispatchIncidentNotificationsAsync(
                        result.IncidentId.Value,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Automatic contact notification dispatch failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "SMS processing failed unexpectedly for sender {Sender}, storage index {StorageIndex}.",
                SmsPrivacy.MaskPhoneNumber(message.From),
                message.StorageIndex);
            PublishNotificationSafe(new MonitorNotification(
                "SMS processing error",
                "An unexpected error occurred while processing an incoming message. Check the diagnostic log.",
                Severity.Warning,
                message.IsSimulated,
                clock.UtcNow));
        }
        finally
        {
            _processingGate.Release();
        }
    }

    private void OnSmsReceived(object? sender, IncomingSmsEventArgs args)
    {
        Interlocked.Increment(ref _ingressPending);
        if (!_ingress.Writer.TryWrite(args.Message))
        {
            Interlocked.Decrement(ref _ingressPending);
            logger.LogWarning("Incoming SMS could not be queued for processing.");
        }
    }

    private void OnReconciliationRequested(object? sender, EventArgs args) => RequestReconciliation();

    private void RequestReconciliation()
    {
        if (_started)
        {
            _reconcileRequests.Writer.TryWrite(true);
        }
    }

    private void OnModemStatusChanged(object? sender, ModemStatusChangedEventArgs args)
    {
        if (args.Status.ConnectionState == ModemConnectionState.Connected)
        {
            if (Interlocked.Exchange(ref _modemConnectedFlag, 1) == 0)
            {
                RequestReconciliation();
            }
        }
        else
        {
            Interlocked.Exchange(ref _modemConnectedFlag, 0);
        }
    }

    private void OnModemFaulted(object? sender, ModemFaultedEventArgs args)
    {
        _ = RecordCommunicationSafeAsync(
            CommunicationDirection.System,
            CommunicationKind.Connection,
            CommunicationOutcome.Failure,
            "GSM modem fault",
            args.Message,
            "MODEM_FAULT",
            modemService.Status.IsSimulated,
            null,
            null,
            CancellationToken.None);

        PublishNotificationSafe(new MonitorNotification(
            "GSM modem fault",
            args.Message,
            Severity.Warning,
            modemService.Status.IsSimulated,
            clock.UtcNow));

        _ = DispatchCommunicationFailureNotificationSafeAsync(args.Message);
        TryAutoReconnect();
    }

    private void TryAutoReconnect()
    {
        if (modemService.Status.IsSimulated)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _reconnectInProgress, 1, 0) != 0)
        {
            return;
        }

        var cancellationToken = _lifetime?.Token ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            try
            {
                var snapshot = await settingsService.GetSnapshotAsync(cancellationToken);
                if (!snapshot.Modem.AutoReconnect)
                {
                    return;
                }

                var delays = new[]
                {
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(15),
                    TimeSpan.FromSeconds(30)
                };

                foreach (var delay in delays)
                {
                    await Task.Delay(delay, cancellationToken);
                    if (modemService.IsConnected)
                    {
                        return;
                    }

                    var current = await settingsService.GetSnapshotAsync(cancellationToken);
                    if (!current.Modem.UseSimulatedModem)
                    {
                        var options = new ModemConnectionOptions(
                            current.Modem.UseSimulatedModem,
                            current.Modem.PortName,
                            current.Modem.BaudRate,
                            current.Modem.DataBits,
                            current.Modem.Parity,
                            current.Modem.StopBits,
                            current.Modem.Handshake,
                            current.Modem.CommandTimeoutMs,
                            current.Modem.PollIntervalSeconds,
                            current.Modem.DeleteSmsAfterPersistence);
                        var result = await modemService.ConnectAsync(options, cancellationToken);
                        await RecordCommunicationSafeAsync(
                            CommunicationDirection.System,
                            CommunicationKind.Reconnect,
                            result.Success ? CommunicationOutcome.Success : CommunicationOutcome.Failure,
                            "Automatic modem reconnection attempt",
                            result.Message,
                            result.Success ? null : "AUTO_RECONNECT_FAILED",
                            false,
                            null,
                            null,
                            cancellationToken);

                        if (result.Success)
                        {
                            PublishNotificationSafe(new MonitorNotification(
                                "Modem reconnected",
                                result.Message,
                                Severity.Normal,
                                false,
                                clock.UtcNow));
                            RequestReconciliation();
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Automatic modem reconnection failed.");
            }
            finally
            {
                Interlocked.Exchange(ref _reconnectInProgress, 0);
            }
        }, cancellationToken);
    }

    private async Task DispatchCommunicationFailureNotificationSafeAsync(string summary)
    {
        try
        {
            await notificationService.DispatchCommunicationFailureNotificationAsync(
                summary,
                _lifetime?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Communication-failure notification dispatch failed.");
        }
    }

    private async Task RecoverJournalAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entries = await journal.LoadPendingAsync(cancellationToken);
            if (entries.Count == 0)
            {
                return;
            }

            logger.LogWarning(
                "Recovering {Count} incoming SMS journal entr(ies) from before the previous application stop.",
                entries.Count);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessOneAsync(entry.Message, entry.JournalId, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Incoming SMS journal recovery failed.");
        }
    }

    private async Task RemoveJournalSafeAsync(string? journalId)
    {
        if (journalId is null)
        {
            return;
        }

        try
        {
            await journal.RemoveAsync(journalId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not remove incoming SMS journal entry {JournalId}.", journalId);
        }
    }

    private async Task RecordCommunicationSafeAsync(
        CommunicationDirection direction,
        CommunicationKind kind,
        CommunicationOutcome outcome,
        string summary,
        string? detail,
        string? errorCode,
        bool isSimulated,
        Guid? deviceId,
        int? storageIndex,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var unitOfWork = unitOfWorkFactory.Create();
            await unitOfWork.CommunicationLogs.AddAsync(new CommunicationLog
            {
                Id = Guid.NewGuid(),
                TimestampUtc = clock.UtcNow,
                Direction = direction,
                Kind = kind,
                Outcome = outcome,
                Summary = summary.Length <= 300 ? summary : summary[..300],
                Detail = detail,
                ErrorCode = errorCode,
                IsSimulated = isSimulated,
                DeviceId = deviceId,
                ModemStorageIndex = storageIndex
            }, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Communication log write failed: {Summary}", summary);
        }
    }

    private void PublishNotificationSafe(MonitorNotification notification)
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
}
