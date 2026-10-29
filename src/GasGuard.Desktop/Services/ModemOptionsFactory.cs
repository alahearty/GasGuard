using GasGuard.Application.Models;
using GasGuard.Contracts.Modem;

namespace GasGuard.Desktop.Services;

public static class ModemOptionsFactory
{
    public static ModemConnectionOptions Create(ModemSettingsInput settings) => new(
        settings.UseSimulatedModem,
        settings.UseSimulatedModem ? "SIMULATOR" : settings.PortName,
        settings.BaudRate,
        settings.DataBits,
        settings.Parity,
        settings.StopBits,
        settings.Handshake,
        settings.CommandTimeoutMs,
        settings.PollIntervalSeconds,
        settings.DeleteSmsAfterPersistence);
}
