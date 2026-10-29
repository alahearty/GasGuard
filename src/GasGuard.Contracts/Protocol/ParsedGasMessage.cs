using GasGuard.Domain;

namespace GasGuard.Contracts.Protocol;

public sealed record ParsedGasMessage(
    string ProtocolVersion,
    string DeviceCode,
    GasEventCode EventCode,
    string EventText,
    Severity Severity,
    string LevelText,
    int Reading,
    string Unit,
    long? SequenceNumber,
    DateTimeOffset? DeviceTimestamp,
    bool HasExplicitTimestampOffset,
    IReadOnlyList<ProtocolIssue> Warnings);
