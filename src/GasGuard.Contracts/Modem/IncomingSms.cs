namespace GasGuard.Contracts.Modem;

public sealed record IncomingSms(
    string From,
    string Body,
    DateTime ReceivedAtUtc,
    int? StorageIndex = null,
    string? SourceReference = null,
    bool IsSimulated = false,
    string? ModemTimestampText = null);
