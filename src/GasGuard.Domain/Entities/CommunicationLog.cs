namespace GasGuard.Domain.Entities;

public sealed class CommunicationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime TimestampUtc { get; set; }
    public CommunicationDirection Direction { get; set; }
    public CommunicationKind Kind { get; set; }
    public CommunicationOutcome Outcome { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? ErrorCode { get; set; }
    public string? RecipientNumber { get; set; }
    public string? ProviderReference { get; set; }
    public bool IsSimulated { get; set; }
    public Guid? DeviceId { get; set; }
    public int? ModemStorageIndex { get; set; }

    public GasDevice? Device { get; set; }
}
