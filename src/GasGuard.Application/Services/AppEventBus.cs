using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;

namespace GasGuard.Application.Services;

public sealed class AppEventBus : IAppEventBus
{
    public event Action<MonitorNotification>? NotificationRaised;
    public event Action<Guid>? IncidentCreated;
    public event Action? IncidentStateChanged;

    public void PublishNotification(MonitorNotification notification) =>
        NotificationRaised?.Invoke(notification);

    public void PublishIncidentCreated(Guid incidentId) =>
        IncidentCreated?.Invoke(incidentId);

    public void PublishIncidentStateChanged() =>
        IncidentStateChanged?.Invoke();
}
