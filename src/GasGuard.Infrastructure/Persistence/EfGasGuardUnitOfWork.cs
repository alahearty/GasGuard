using GasGuard.Application.Abstractions;
using GasGuard.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GasGuard.Infrastructure.Persistence;

public sealed class EfGasGuardUnitOfWork : IGasGuardUnitOfWork
{
    private readonly GasGuardDbContext _context;
    private IDbContextTransaction? _transaction;
    private bool _disposed;

    public EfGasGuardUnitOfWork(IDbContextFactory<GasGuardDbContext> contextFactory)
    {
        _context = contextFactory.CreateDbContext();
        Devices = new EfDeviceRepository(_context);
        Incidents = new EfIncidentRepository(_context);
        Messages = new EfSmsMessageRepository(_context);
        Contacts = new EfContactRepository(_context);
        Settings = new EfSettingsRepository(_context);
        CommunicationLogs = new EfCommunicationLogRepository(_context);
        AuditLogs = new EfAuditLogRepository(_context);
    }

    public IDeviceRepository Devices { get; }
    public IIncidentRepository Incidents { get; }
    public ISmsMessageRepository Messages { get; }
    public IContactRepository Contacts { get; }
    public ISettingsRepository Settings { get; }
    public ICommunicationLogRepository CommunicationLogs { get; }
    public IAuditLogRepository AuditLogs { get; }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already active in this unit of work.");
        }

        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        _context.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        await _context.DisposeAsync();
    }
}
