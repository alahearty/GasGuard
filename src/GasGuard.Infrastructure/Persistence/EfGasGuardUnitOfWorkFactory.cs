using GasGuard.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GasGuard.Infrastructure.Persistence;

public sealed class EfGasGuardUnitOfWorkFactory(IDbContextFactory<GasGuardDbContext> contextFactory)
    : IGasGuardUnitOfWorkFactory
{
    public IGasGuardUnitOfWork Create() => new EfGasGuardUnitOfWork(contextFactory);
}
