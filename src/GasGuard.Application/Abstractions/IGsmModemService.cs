using GasGuard.Contracts.Modem;

namespace GasGuard.Application.Abstractions;

public interface IGsmModemService : IAsyncDisposable
{
    bool IsConnected { get; }
    bool IsSimulated { get; }
    ModemStatus Status { get; }

    event EventHandler<ModemStatusChangedEventArgs>? StatusChanged;
    event EventHandler<IncomingSmsEventArgs>? SmsReceived;
    event EventHandler<ModemFaultedEventArgs>? Faulted;
    event EventHandler? ReconciliationRequested;

    Task<ModemOperationResult> ConnectAsync(ModemConnectionOptions options, CancellationToken cancellationToken = default);
    Task<ModemOperationResult> DisconnectAsync(CancellationToken cancellationToken = default);
    Task<ModemStatus> RefreshStatusAsync(CancellationToken cancellationToken = default);
    Task<SmsSendResult> SendSmsAsync(string recipientE164, string body, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IncomingSms>> ReadStoredMessagesAsync(bool unreadOnly, CancellationToken cancellationToken = default);
    Task<ModemOperationResult> DeleteStoredMessageAsync(int storageIndex, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAvailablePortsAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<ModemDiagnosticEntry> GetDiagnostics(int maximumEntries = 250);
    void ClearDiagnostics();
}
