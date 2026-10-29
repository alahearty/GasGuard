namespace GasGuard.Contracts.Modem;

public sealed record ModemOperationResult(
    bool Success,
    ModemOperationOutcome Outcome,
    string Message,
    string? Detail = null)
{
    public static ModemOperationResult Ok(string message) =>
        new(true, ModemOperationOutcome.Success, message);

    public static ModemOperationResult Fail(ModemOperationOutcome outcome, string message, string? detail = null) =>
        new(false, outcome, message, detail);
}

public sealed record SmsSendResult(
    bool SubmittedToModem,
    SmsSendOutcome Outcome,
    string Message,
    string Recipient,
    string? ProviderReference = null,
    bool DeliveryConfirmed = false)
{
    public bool DeliveryAcknowledgementAvailable => false;
}

public sealed record AtCommandRequest(string Command, string? Payload = null);

public sealed record AtCommandResponse(
    bool Success,
    IReadOnlyList<string> Lines,
    string RawResponse,
    string? ErrorCode = null,
    bool TimedOut = false,
    bool Cancelled = false)
{
    public string? FirstLine => Lines.Count > 0 ? Lines[0] : null;

    public static AtCommandResponse Timeout(string message) =>
        new(false, [message], message, "AT_TIMEOUT", true);

    public static AtCommandResponse FromCancellation() =>
        new(false, [], "Command cancelled.", "AT_CANCELLED", false, true);

    public static AtCommandResponse Failed(string code, string message) =>
        new(false, [message], message, code);
}

public sealed record ModemDiagnosticEntry(
    DateTime TimestampUtc,
    string Direction,
    string Text,
    bool IsError);

public sealed class ModemStatusChangedEventArgs(ModemStatus status) : EventArgs
{
    public ModemStatus Status { get; } = status;
}

public sealed class IncomingSmsEventArgs(IncomingSms message) : EventArgs
{
    public IncomingSms Message { get; } = message;
}

public sealed class ModemFaultedEventArgs(string message, Exception? exception = null) : EventArgs
{
    public string Message { get; } = message;
    public Exception? Exception { get; } = exception;
}
