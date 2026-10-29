namespace GasGuard.Domain.Entities;

public sealed class SmsMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SenderNumberRaw { get; set; } = string.Empty;
    public string? SenderNumberNormalized { get; set; }
    public string BodyRaw { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime StoredAtUtc { get; set; }
    public int? ModemStorageIndex { get; set; }
    public string? SourceReference { get; set; }
    public bool IsSimulated { get; set; }
    public SmsProcessingStatus ProcessingStatus { get; set; } = SmsProcessingStatus.Pending;
    public string? ProcessingDetail { get; set; }
    public Guid? DeviceId { get; set; }
    public string? DeviceCode { get; set; }
    public string? ProtocolVersion { get; set; }
    public GasEventCode EventCode { get; set; } = GasEventCode.Unknown;
    public Severity Severity { get; set; } = Severity.Unknown;
    public int? Reading { get; set; }
    public string? Unit { get; set; }
    public long? SequenceNumber { get; set; }
    public DateTime? DeviceTimestampUtc { get; set; }
    public bool DeviceTimestampTrusted { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }

    public GasDevice? Device { get; set; }
    public Incident? Incident { get; set; }
}
