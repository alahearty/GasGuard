namespace GasGuard.Contracts.Modem;

public sealed record SimulatedSmsRequest(
    string Sender,
    string Body,
    TimeSpan? DeliveryDelay = null,
    bool RetainInStorage = true,
    bool DeliverNow = true)
{
    public bool IsMalformedMessage => string.IsNullOrWhiteSpace(Body) ||
                                      !Body.TrimStart().StartsWith("GASALERT", StringComparison.OrdinalIgnoreCase);
}
