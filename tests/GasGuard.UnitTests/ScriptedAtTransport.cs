using GasGuard.Infrastructure.Modem;

namespace GasGuard.UnitTests;

internal sealed class ScriptedAtTransport : IAtTransport
{
    private readonly string _cpinResponse;
    private readonly string _storedSmsBody;
    private readonly string _cregResponse;

    public ScriptedAtTransport(
        string cpinResponse = "+CPIN: READY",
        string storedSmsBody = "GASALERT|v=1|device=GAS-001|event=STATUS|level=NORMAL|reading=120|seq=42",
        string cregResponse = "+CREG: 0,1")
    {
        _cpinResponse = cpinResponse;
        _storedSmsBody = storedSmsBody;
        _cregResponse = cregResponse;
    }

    public bool FailCsq { get; set; }
    public bool FailDelete { get; set; }
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

        if (text.EndsWith('\u001A'))
        {
            Emit("+CMGS: 42");
            Emit("OK");
            return Task.CompletedTask;
        }

        var command = text.TrimEnd('\r', '\n');
        if (command.Equals("ATI", StringComparison.OrdinalIgnoreCase))
        {
            Emit("SIM800C R14.18");
        }
        else if (command.Equals("AT+CPIN?", StringComparison.OrdinalIgnoreCase))
        {
            Emit(_cpinResponse);
        }
        else if (command.Equals("AT+CSQ", StringComparison.OrdinalIgnoreCase))
        {
            if (FailCsq)
            {
                Emit("ERROR");
                return Task.CompletedTask;
            }

            Emit("+CSQ: 21,99");
        }
        else if (command.Equals("AT+CREG?", StringComparison.OrdinalIgnoreCase))
        {
            Emit(_cregResponse);
        }
        else if (command.Equals("AT+COPS?", StringComparison.OrdinalIgnoreCase))
        {
            Emit("+COPS: 0,0,\"MTN\",7");
        }
        else if (command.StartsWith("AT+CMGS=", StringComparison.OrdinalIgnoreCase))
        {
            Emit(">");
            return Task.CompletedTask;
        }
        else if (command.StartsWith("AT+CMGD=", StringComparison.OrdinalIgnoreCase) && FailDelete)
        {
            Emit("+CMS ERROR: 500");
            return Task.CompletedTask;
        }
        else if (command.StartsWith("AT+CMGL=", StringComparison.OrdinalIgnoreCase))
        {
            Emit("+CMGL: 7,\"REC UNREAD\",\"+2348031234567\",,\"26/10/28,15:30:00+04\"");
            Emit(_storedSmsBody);
        }

        Emit("OK");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsOpen = false;
        return ValueTask.CompletedTask;
    }

    public void SimulateFault(Exception exception) => Faulted?.Invoke(this, exception);

    private void Emit(string line) => LineReceived?.Invoke(this, line);
}

internal sealed class ScriptedTransportFactory(IAtTransport transport) : IAtTransportFactory
{
    public IAtTransport Create(GasGuard.Contracts.Modem.ModemConnectionOptions options) => transport;
}
