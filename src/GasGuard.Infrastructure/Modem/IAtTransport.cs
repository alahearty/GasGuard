namespace GasGuard.Infrastructure.Modem;

public interface IAtTransport : IAsyncDisposable
{
    bool IsOpen { get; }
    event EventHandler<string>? LineReceived;
    event EventHandler<Exception>? Faulted;
    Task OpenAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(string text, CancellationToken cancellationToken = default);
}
