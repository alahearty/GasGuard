using GasGuard.Domain.Entities;
using GasGuard.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GasGuard.Infrastructure.Persistence;

public sealed class GasGuardDbContext : DbContext
{
    public GasGuardDbContext(DbContextOptions<GasGuardDbContext> options) : base(options)
    {
    }

    public DbSet<GasDevice> GasDevices => Set<GasDevice>();
    public DbSet<SmsMessage> SmsMessages => Set<SmsMessage>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ModemConfiguration> ModemConfigurations => Set<ModemConfiguration>();
    public DbSet<CommunicationLog> CommunicationLogs => Set<CommunicationLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppPreferences> Preferences => Set<AppPreferences>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GasGuardDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }
}
