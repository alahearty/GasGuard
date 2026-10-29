namespace GasGuard.Application.Abstractions;

public interface IGasGuardUnitOfWorkFactory
{
    IGasGuardUnitOfWork Create();
}

public interface IGasGuardUnitOfWork : IAsyncDisposable
{
    IDeviceRepository Devices { get; }
    IIncidentRepository Incidents { get; }
    ISmsMessageRepository Messages { get; }
    IContactRepository Contacts { get; }
    ISettingsRepository Settings { get; }
    ICommunicationLogRepository CommunicationLogs { get; }
    IAuditLogRepository AuditLogs { get; }

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
