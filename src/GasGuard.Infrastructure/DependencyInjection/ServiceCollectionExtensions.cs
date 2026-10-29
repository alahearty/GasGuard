using GasGuard.Application.Abstractions;
using GasGuard.Application.Services;
using GasGuard.Infrastructure.Auditing;
using GasGuard.Infrastructure.Backup;
using GasGuard.Infrastructure.Configuration;
using GasGuard.Infrastructure.Maintenance;
using GasGuard.Infrastructure.Modem;
using GasGuard.Infrastructure.Persistence;
using GasGuard.Infrastructure.Querying;
using GasGuard.Infrastructure.Reporting;
using GasGuard.Infrastructure.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GasGuard.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGasGuardInfrastructure(
        this IServiceCollection services,
        GasGuardPaths? paths = null)
    {
        var resolvedPaths = paths ?? new GasGuardPaths();
        services.AddSingleton(resolvedPaths);
        services.AddSingleton<IAppPathProvider, AppPathProvider>();

        services.AddDbContextFactory<GasGuardDbContext>(options =>
            options.UseSqlite(resolvedPaths.ConnectionString, sqlite =>
                sqlite.MigrationsAssembly(typeof(GasGuardDbContext).Assembly.GetName().Name)));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IUserContext, WindowsUserContext>();
        services.AddSingleton<IPhoneNumberNormalizer, NigerianPhoneNumberNormalizer>();
        services.AddSingleton<ISmsProtocolParser, SmsProtocolParser>();
        services.AddSingleton<IAppEventBus, AppEventBus>();
        services.AddSingleton<IGasGuardUnitOfWorkFactory, EfGasGuardUnitOfWorkFactory>();
        services.AddSingleton<IIncomingSmsJournal, IncomingSmsJournal>();

        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IAuditLogService, AuditLogService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<ICommunicationLogService, CommunicationLogService>();
        services.AddSingleton<ISmsEvidenceService, SmsEvidenceService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<IRetentionService, RetentionService>();

        services.AddSingleton<IAtTransportFactory, SerialAtTransportFactory>();
        services.AddSingleton<GsmModemService>();
        services.AddSingleton<IGsmModemService>(provider => provider.GetRequiredService<GsmModemService>());
        services.AddSingleton<ISimulatedModemControl>(provider => provider.GetRequiredService<GsmModemService>());

        services.AddTransient<IDeviceService, DeviceService>();
        services.AddTransient<IContactService, ContactService>();
        services.AddTransient<ISettingsService, SettingsService>();
        services.AddTransient<ISmsIngestionService, SmsIngestionService>();
        services.AddTransient<IIncidentService, IncidentService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IDemoScenarioService, DemoScenarioService>();
        services.AddSingleton<ISmsProcessingCoordinator, SmsProcessingCoordinator>();

        return services;
    }
}
