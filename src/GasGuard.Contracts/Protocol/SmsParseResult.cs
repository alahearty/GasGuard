namespace GasGuard.Contracts.Protocol;

public enum SmsParseStatus
{
    Valid = 1,
    Malformed = 2,
    UnsupportedVersion = 3,
    ValidationFailed = 4
}

public sealed record SmsParseResult(
    SmsParseStatus Status,
    ParsedGasMessage? Message,
    IReadOnlyList<ProtocolIssue> Issues)
{
    public bool IsValid => Status == SmsParseStatus.Valid && Message is not null;

    public string? PrimaryErrorCode => Issues.FirstOrDefault(i => i.IsError)?.Code;

    public string? PrimaryErrorMessage => Issues.FirstOrDefault(i => i.IsError)?.Message;

    public static SmsParseResult Valid(ParsedGasMessage message) =>
        new(SmsParseStatus.Valid, message, message.Warnings);

    public static SmsParseResult Failure(SmsParseStatus status, string code, string message) =>
        new(status, null, [ProtocolIssue.Error(code, message)]);
}
