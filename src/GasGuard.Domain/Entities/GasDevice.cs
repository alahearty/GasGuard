namespace GasGuard.Domain.Entities;

public sealed class GasDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DeviceCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string InstallationLocation { get; set; } = string.Empty;
    public string ExpectedSenderNumber { get; set; } = string.Empty;
    public DetectorType DetectorType { get; set; } = DetectorType.CombustibleGas;
    public string? Description { get; set; }
    public DateOnly? InstallationDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastSuccessfulCommunicationUtc { get; set; }
    public DeviceReportedState LastReportedState { get; set; } = DeviceReportedState.Unknown;
    public int? LastReading { get; set; }
    public string? LastReadingUnit { get; set; }
    public long? LastSequenceNumber { get; set; }
    public GasEventCode LastEventCode { get; set; } = GasEventCode.Unknown;
    public DateTime? LastCommunicationErrorUtc { get; set; }
    public string? LastCommunicationError { get; set; }
    public string? MaintenanceNotes { get; set; }
    public bool IsDemo { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
    public ICollection<SmsMessage> Messages { get; set; } = new List<SmsMessage>();

    public DeviceOperationalState GetOperationalState(DateTime utcNow, TimeSpan staleAfter)
    {
        if (!IsActive)
        {
            return DeviceOperationalState.Offline;
        }

        var lastSuccess = LastSuccessfulCommunicationUtc;
        var lastError = LastCommunicationErrorUtc;

        if (lastError.HasValue && (!lastSuccess.HasValue || lastError.Value > lastSuccess.Value))
        {
            return DeviceOperationalState.CommunicationError;
        }

        if (!lastSuccess.HasValue)
        {
            return DeviceOperationalState.Unknown;
        }

        if (utcNow - lastSuccess.Value > staleAfter)
        {
            return DeviceOperationalState.Offline;
        }

        return LastReportedState switch
        {
            DeviceReportedState.Normal => DeviceOperationalState.Normal,
            DeviceReportedState.Warning => DeviceOperationalState.Warning,
            DeviceReportedState.Critical => DeviceOperationalState.Critical,
            _ => DeviceOperationalState.Unknown
        };
    }
}
