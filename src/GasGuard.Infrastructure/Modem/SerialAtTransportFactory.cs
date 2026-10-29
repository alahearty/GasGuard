using GasGuard.Contracts.Modem;
using Microsoft.Extensions.Logging;

namespace GasGuard.Infrastructure.Modem;

public interface IAtTransportFactory
{
    IAtTransport Create(ModemConnectionOptions options);
}

public sealed class SerialAtTransportFactory(ILoggerFactory loggerFactory) : IAtTransportFactory
{
    public IAtTransport Create(ModemConnectionOptions options) =>
        new SerialAtTransport(options, loggerFactory.CreateLogger<SerialAtTransport>());
}
