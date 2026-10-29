using GasGuard.Contracts.Modem;

namespace GasGuard.Application.Abstractions;

public interface IModemCommandQueue : IAsyncDisposable
{
    bool IsOpen { get; }

    event EventHandler<IncomingSms>? UnsolicitedSmsReceived;
    event EventHandler<string>? UnsolicitedNotificationReceived;
    event EventHandler<Exception>? TransportFaulted;

    Task OpenAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
    Task<AtCommandResponse> ExecuteAsync(AtCommandRequest request, CancellationToken cancellationToken = default);
}
