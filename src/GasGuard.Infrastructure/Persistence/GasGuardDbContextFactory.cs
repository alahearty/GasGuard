using GasGuard.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GasGuard.Infrastructure.Persistence;

public sealed class GasGuardDbContextFactory : IDesignTimeDbContextFactory<GasGuardDbContext>
{
    public GasGuardDbContext CreateDbContext(string[] args)
    {
        var designRoot = Path.Combine(Path.GetTempPath(), "GasGuardDesign");
        var paths = new GasGuardPaths(designRoot);
        Directory.CreateDirectory(paths.RootDirectory);

        var options = new DbContextOptionsBuilder<GasGuardDbContext>()
            .UseSqlite(paths.ConnectionString, sqlite =>
                sqlite.MigrationsAssembly(typeof(GasGuardDbContext).Assembly.GetName().Name))
            .Options;

        return new GasGuardDbContext(options);
    }
}
