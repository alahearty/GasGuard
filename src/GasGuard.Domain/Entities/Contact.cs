namespace GasGuard.Domain.Entities;

public sealed class Contact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public ContactRole Role { get; set; } = ContactRole.Other;
    public bool IsActive { get; set; } = true;
    public bool NotifyOnCritical { get; set; } = true;
    public bool NotifyOnWarning { get; set; }
    public bool NotifyOnCommunicationFailure { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
