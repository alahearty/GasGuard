using GasGuard.Contracts.Protocol;

namespace GasGuard.Application.Abstractions;

public interface ISmsProtocolParser
{
    SmsParseResult Parse(string? rawBody);
}
