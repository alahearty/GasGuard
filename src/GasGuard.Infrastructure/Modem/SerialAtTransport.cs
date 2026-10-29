using System.IO.Ports;
using System.Text;
using GasGuard.Contracts.Modem;
using GasGuard.Domain;
using Microsoft.Extensions.Logging;

namespace GasGuard.Infrastructure.Modem;

public sealed class SerialAtTransport : IAtTransport
{
    private readonly ModemConnectionOptions _options;
    private readonly ILogger<SerialAtTransport>? _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly StringBuilder _buffer = new();
    private readonly object _stateLock = new();
    private CancellationTokenSource? _readCancellation;
    private Task? _readTask;
    private SerialPort? _port;
    private bool _closing;
    private bool _nonAsciiWarningLogged;
    private bool _disposed;

    public SerialAtTransport(ModemConnectionOptions options, ILogger<SerialAtTransport>? logger = null)
    {
        _options = options;
        _logger = logger;
    }

    public event EventHandler<string>? LineReceived;
    public event EventHandler<Exception>? Faulted;

    public bool IsOpen
    {
        get
        {
            lock (_stateLock)
            {
                return _port?.IsOpen == true;
            }
        }
    }

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            if (_port?.IsOpen == true)
            {
                return Task.CompletedTask;
            }

            var port = new SerialPort(
                _options.PortName,
                _options.BaudRate,
                MapParity(_options.Parity),
                _options.DataBits,
                MapStopBits(_options.StopBits))
            {
                Handshake = MapHandshake(_options.Handshake),
                ReadBufferSize = 4096,
                WriteBufferSize = 4096,
                DtrEnable = true,
                RtsEnable = true,
                Encoding = Encoding.ASCII,
                NewLine = "\r\n"
            };

            port.Open();
            _port = port;
            _closing = false;
            _nonAsciiWarningLogged = false;
            _buffer.Clear();
            _readCancellation = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(port, _readCancellation.Token));
        }

        return Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        SerialPort? port;
        Task? readTask;
        CancellationTokenSource? cancellation;

        lock (_stateLock)
        {
            _closing = true;
            port = _port;
            readTask = _readTask;
            cancellation = _readCancellation;
            _port = null;
            _readTask = null;
            _readCancellation = null;
        }

        cancellation?.Cancel();
        if (port is not null)
        {
            try
            {
                if (port.IsOpen)
                {
                    port.Close();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Closing the serial port reported an error.");
            }

            port.Dispose();
        }

        if (readTask is not null)
        {
            try
            {
                await readTask.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
                _logger?.LogWarning("The serial reader did not stop within the shutdown window.");
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "The serial reader ended with an error during close.");
            }
        }

        cancellation?.Dispose();
    }

    public async Task WriteAsync(string text, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var port = _port;
            if (port?.IsOpen != true)
            {
                throw new InvalidOperationException("The serial port is not open.");
            }

            var bytes = Encoding.ASCII.GetBytes(text);
            await port.BaseStream.WriteAsync(bytes, cancellationToken);
            await port.BaseStream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await CloseAsync();
        _writeLock.Dispose();
    }

    private async Task ReadLoopAsync(SerialPort port, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await port.BaseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0)
                {
                    continue;
                }

                if (!_nonAsciiWarningLogged && buffer.Take(read).Any(value => value > 0x7F))
                {
                    _nonAsciiWarningLogged = true;
                    _logger?.LogWarning(
                        "Non-ASCII bytes were received on {Port}. The v1 text-mode pipeline is ASCII-only; SMS encoding may be unsupported.",
                        _options.PortName);
                }

                var text = Encoding.ASCII.GetString(buffer, 0, read);
                foreach (var line in ExtractLines(text))
                {
                    LineReceived?.Invoke(this, line);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException) when (_closing)
        {
        }
        catch (Exception ex)
        {
            if (!_closing)
            {
                _logger?.LogWarning(ex, "Serial read loop failed on {Port}.", _options.PortName);
                Faulted?.Invoke(this, ex);
            }
        }
    }

    private IEnumerable<string> ExtractLines(string text)
    {
        var lines = new List<string>();
        lock (_stateLock)
        {
            _buffer.Append(text);
            while (true)
            {
                var content = _buffer.ToString();
                var newlineIndex = content.IndexOf('\n');
                if (newlineIndex < 0)
                {
                    var trimmed = content.TrimEnd('\r');
                    if (trimmed.Length > 0 && trimmed.Trim() == ">")
                    {
                        _buffer.Clear();
                        lines.Add(">");
                    }

                    break;
                }

                var line = content[..newlineIndex].TrimEnd('\r');
                _buffer.Remove(0, newlineIndex + 1);
                if (line.Length > 0)
                {
                    lines.Add(line);
                }

                if (_buffer.Length == 0)
                {
                    break;
                }
            }
        }

        return lines;
    }

    private static Parity MapParity(SerialParity parity) => parity switch
    {
        SerialParity.Odd => Parity.Odd,
        SerialParity.Even => Parity.Even,
        SerialParity.Mark => Parity.Mark,
        SerialParity.Space => Parity.Space,
        _ => Parity.None
    };

    private static StopBits MapStopBits(SerialStopBits stopBits) => stopBits switch
    {
        SerialStopBits.OnePointFive => StopBits.OnePointFive,
        SerialStopBits.Two => StopBits.Two,
        _ => StopBits.One
    };

    private static Handshake MapHandshake(SerialHandshake handshake) => handshake switch
    {
        SerialHandshake.XOnXOff => Handshake.XOnXOff,
        SerialHandshake.RequestToSend => Handshake.RequestToSend,
        SerialHandshake.RequestToSendXOnXOff => Handshake.RequestToSendXOnXOff,
        _ => Handshake.None
    };
}
