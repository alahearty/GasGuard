namespace GasGuard.Domain.Entities;

public sealed class ModemConfiguration
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public bool UseSimulatedModem { get; set; } = true;
    public string PortName { get; set; } = "SIMULATOR";
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public SerialParity Parity { get; set; } = SerialParity.None;
    public SerialStopBits StopBits { get; set; } = SerialStopBits.One;
    public SerialHandshake Handshake { get; set; } = SerialHandshake.None;
    public int CommandTimeoutMs { get; set; } = 8000;
    public int PollIntervalSeconds { get; set; } = 30;
    public bool AutoConnect { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;
    public bool DeleteSmsAfterPersistence { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; }
}
