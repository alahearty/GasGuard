using GasGuard.Contracts.Modem;

namespace GasGuard.Application.Abstractions;

public interface ISimulatedModemControl
{
    bool SimulatorActive { get; }
    int PendingStoredMessageCount { get; }

    Task<ModemOperationResult> InjectMessageAsync(SimulatedSmsRequest request, CancellationToken cancellationToken = default);
    Task<ModemOperationResult> SimulateDisconnectionAsync(string reason, CancellationToken cancellationToken = default);
    Task<ModemOperationResult> SetSendModeAsync(SimulatedSendMode mode, CancellationToken cancellationToken = default);
    IReadOnlyList<IncomingSms> GetStoredMessages();
}
