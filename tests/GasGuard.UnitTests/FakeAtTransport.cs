using System.Threading.Channels;
using GasGuard.Infrastructure.Modem;

namespace GasGuard.UnitTests;

internal sealed class FakeAtTransport : IAtTransport
{
    private readonly Channel<string> _writes = Channel.CreateUnbounded<string>();

    public bool IsOpen { get; private set; }
    public event EventHandler<string>? LineReceived;
    public event EventHandler<Exception>? Faulted;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        IsOpen = true;
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        IsOpen = false;
        return Task.CompletedTask;
    }

    public Task WriteAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("Transport is closed.");
        }

        if (!_writes.Writer.TryWrite(text))
        {
            throw new InvalidOperationException("Could not record the write.");
        }

        return Task.CompletedTask;
    }

    public void Emit(string line) => LineReceived?.Invoke(this, line);

    public void RaiseFault(Exception exception) => Faulted?.Invoke(this, exception);

    public async Task<string> NextWriteAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
        return await _writes.Reader.ReadAsync(cts.Token);
    }

    public ValueTask DisposeAsync()
    {
        IsOpen = false;
        return ValueTask.CompletedTask;
    }
}
