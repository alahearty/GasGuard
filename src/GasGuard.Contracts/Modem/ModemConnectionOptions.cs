using GasGuard.Domain;

namespace GasGuard.Contracts.Modem;

public sealed record ModemConnectionOptions(
    bool UseSimulator,
    string PortName,
    int BaudRate,
    int DataBits,
    SerialParity Parity,
    SerialStopBits StopBits,
    SerialHandshake Handshake,
    int CommandTimeoutMs,
    int PollIntervalSeconds,
    bool DeleteSmsAfterPersistence)
{
    public static ModemConnectionOptions Simulator { get; } = new(
        true,
        "SIMULATOR",
        0,
        0,
        SerialParity.None,
        SerialStopBits.One,
        SerialHandshake.None,
        5000,
        30,
        true);
}
