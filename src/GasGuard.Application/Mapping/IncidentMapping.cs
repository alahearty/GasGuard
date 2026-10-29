using GasGuard.Application.Models;
using GasGuard.Domain.Entities;

namespace GasGuard.Application.Mapping;

internal static class IncidentMapping
{
    public static IncidentListItem ToListItem(Incident incident) => new(
        incident.Id,
        incident.DeviceId,
        incident.DeviceCodeSnapshot,
        incident.DeviceNameSnapshot,
        incident.LocationSnapshot,
        incident.Severity,
        incident.Status,
        incident.OccurredAtUtc,
        incident.OccurredTimeSource,
        incident.ReceivedAtUtc,
        incident.OpenedAtUtc,
        incident.AcknowledgedAtUtc,
        incident.ResolvedAtUtc,
        incident.Reading,
        incident.Unit,
        incident.SequenceNumber,
        incident.IsSimulated);
}
