namespace GasGuard.Contracts.Protocol;

public sealed record ProtocolIssue(string Code, string Message, bool IsError)
{
    public static ProtocolIssue Error(string code, string message) => new(code, message, true);

    public static ProtocolIssue Warning(string code, string message) => new(code, message, false);
}
