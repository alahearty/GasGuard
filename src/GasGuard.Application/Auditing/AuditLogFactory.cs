using GasGuard.Application.Models;
using GasGuard.Domain.Entities;

namespace GasGuard.Application.Auditing;

public static class AuditLogFactory
{
    public static AuditLog Create(AuditEntryDraft entry, DateTime timestampUtc) => new()
    {
        Id = Guid.NewGuid(),
        TimestampUtc = timestampUtc,
        Actor = Truncate(string.IsNullOrWhiteSpace(entry.Actor) ? "System" : entry.Actor.Trim(), 100),
        Action = Truncate(entry.Action.Trim(), 100),
        EntityType = Truncate(entry.EntityType.Trim(), 100),
        EntityId = string.IsNullOrWhiteSpace(entry.EntityId) ? null : Truncate(entry.EntityId.Trim(), 100),
        Outcome = entry.Outcome,
        Details = Truncate(string.IsNullOrWhiteSpace(entry.Details) ? "No details." : entry.Details.Trim(), 2000)
    };

    public static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
