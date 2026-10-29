namespace GasGuard.Domain.Entities;

public sealed class Incident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public Guid TriggeringSmsMessageId { get; set; }
    public string DeviceCodeSnapshot { get; set; } = string.Empty;
    public string DeviceNameSnapshot { get; set; } = string.Empty;
    public string LocationSnapshot { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.Open;
    public DateTime OccurredAtUtc { get; set; }
    public IncidentTimeSource OccurredTimeSource { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public int? Reading { get; set; }
    public string? Unit { get; set; }
    public long? SequenceNumber { get; set; }
    public string DedupKey { get; set; } = string.Empty;
    public bool IsSimulated { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public string? AcknowledgedBy { get; set; }
    public string? AcknowledgementNotes { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionNotes { get; set; }

    public GasDevice Device { get; set; } = null!;
    public SmsMessage TriggeringSmsMessage { get; set; } = null!;

    public void Acknowledge(string actor, string? notes, DateTime utcNow)
    {
        if (Status == IncidentStatus.Resolved)
        {
            throw new DomainException("A resolved incident cannot be acknowledged.");
        }

        if (Status == IncidentStatus.Acknowledged)
        {
            throw new DomainException("The incident has already been acknowledged.");
        }

        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new DomainException("An operator name is required to acknowledge an incident.");
        }

        Status = IncidentStatus.Acknowledged;
        AcknowledgedAtUtc = utcNow;
        AcknowledgedBy = actor.Trim();
        AcknowledgementNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void Resolve(string actor, string notes, DateTime utcNow)
    {
        if (Status != IncidentStatus.Acknowledged)
        {
            throw new DomainException("An incident must be acknowledged before it can be resolved.");
        }

        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new DomainException("An operator name is required to resolve an incident.");
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new DomainException("Resolution notes are required.");
        }

        Status = IncidentStatus.Resolved;
        ResolvedAtUtc = utcNow;
        ResolvedBy = actor.Trim();
        ResolutionNotes = notes.Trim();
    }
}
