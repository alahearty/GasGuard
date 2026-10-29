using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GasGuard.Application.Abstractions;
using GasGuard.Desktop.Logging;
using GasGuard.Desktop.Services;
using GasGuard.Desktop.ViewModels;
using GasGuard.Infrastructure.Configuration;
using GasGuard.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GasGuard.Desktop;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\GasGuardDesktop.SingleInstance";
    private ServiceProvider? _provider;
    private ILogger<App>? _logger;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstance;
    private bool _exitStarted;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
            _ownsSingleInstance = createdNew;
            if (!createdNew)
            {
                MessageBox.Show(
                    "GasGuard Desktop is already running. Only one monitoring instance may use the local modem and database at a time.",
                    "GasGuard already running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown();
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"GasGuard Desktop could not create its single-instance lock.\n\n{ex.Message}",
                "GasGuard startup failure",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            var paths = new GasGuardPaths();
            paths.EnsureDirectories();
            var logLevelController = new DiagnosticLogLevelController();

            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddProvider(new FileLoggerProvider(paths.LogsDirectory, logLevelController));
            });
            services.AddSingleton(logLevelController);
            services.AddGasGuardInfrastructure(paths);
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IAppShutdownService, AppShutdownService>();
            services.AddSingleton<IShellService, ShellService>();
            services.AddSingleton<IApplicationOperationGuard, ApplicationOperationGuard>();
            services.AddSingleton<DashboardViewModel>();
            services.AddSingleton<LiveAlertsViewModel>();
            services.AddSingleton<ModemViewModel>();
            services.AddSingleton<DetectorsViewModel>();
            services.AddSingleton<IncidentHistoryViewModel>();
            services.AddSingleton<ContactsViewModel>();
            services.AddSingleton<ReportsViewModel>();
            services.AddSingleton<AuditLogViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();

            _provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
            _logger = _provider.GetRequiredService<ILogger<App>>();
            _logger.LogInformation("GasGuard Desktop starting. Database root: {Root}", paths.RootDirectory);

            var startup = _provider.GetRequiredService<IStartupService>();
            var startupResult = await startup.InitializeAsync();
            if (!startupResult.Success)
            {
                _logger.LogCritical("Database startup failed: {Message}", startupResult.Message);
                MessageBox.Show(
                    $"GasGuard Desktop could not start.\n\n{startupResult.Message}\n\nDiagnostic logs: {paths.LogsDirectory}",
                    "GasGuard startup failure",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            _logger.LogInformation(
                "Database ready at {DatabasePath}. Migrated: {Migrated}. Pre-migration backup: {Backup}",
                startupResult.DatabasePath,
                startupResult.Migrated,
                startupResult.PreMigrationBackupPath ?? "none");

            try
            {
                var settings = _provider.GetRequiredService<ISettingsService>();
                var overview = await settings.GetOverviewAsync();
                logLevelController.Apply(overview.Settings.Preferences.MinimumLogLevel);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not apply the configured diagnostic log level.");
            }

            var window = _provider.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "Fatal startup error.");
            MessageBox.Show(
                $"GasGuard Desktop could not start.\n\n{ex.Message}",
                "GasGuard startup failure",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_exitStarted)
        {
            base.OnExit(e);
            return;
        }

        _exitStarted = true;
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        if (_provider is not null)
        {
            try
            {
                var viewModel = _provider.GetService<MainViewModel>();
                if (viewModel is not null)
                {
                    var shutdown = Task.Run(() => viewModel.ShutdownAsync());
                    if (!shutdown.Wait(TimeSpan.FromSeconds(8)))
                    {
                        _logger?.LogWarning("Application shutdown timed out while stopping monitoring services.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Monitoring services reported an error during shutdown.");
            }

            try
            {
                var disposal = Task.Run(async () => await _provider.DisposeAsync());
                if (!disposal.Wait(TimeSpan.FromSeconds(6)))
                {
                    Debug.WriteLine("GasGuard service provider disposal timed out.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GasGuard service provider disposal failed: {ex}");
            }
        }

        if (_ownsSingleInstance && _singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            _ownsSingleInstance = false;
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled UI exception.");
        MessageBox.Show(
            $"An unexpected interface error occurred.\n\n{e.Exception.Message}\n\nDiagnostic logs are available in the GasGuard data folder.",
            "GasGuard error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled application-domain exception. Terminating: {Terminating}", e.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unobserved background task exception.");
        e.SetObserved();
    }
}
