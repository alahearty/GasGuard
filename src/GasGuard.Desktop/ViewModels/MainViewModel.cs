using System.Media;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Application.Time;
using GasGuard.Contracts.Modem;
using GasGuard.Desktop.Logging;
using GasGuard.Desktop.Services;
using GasGuard.Domain;
using Microsoft.Extensions.Logging;

namespace GasGuard.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IGsmModemService _modem;
    private readonly ISettingsService _settings;
    private readonly ISmsProcessingCoordinator _coordinator;
    private readonly IAppEventBus _events;
    private readonly IIncidentService _incidents;
    private readonly IClock _clock;
    private readonly IDialogService _dialogs;
    private readonly DiagnosticLogLevelController _logLevel;
    private readonly IApplicationOperationGuard _operationGuard;
    private readonly ILogger<MainViewModel> _logger;
    private readonly DashboardViewModel _dashboard;
    private readonly LiveAlertsViewModel _liveAlerts;
    private Timer? _timer;
    private int _ticks;
    private bool _initialized;
    private int _shuttingDown;
    private bool _refreshingHeader;

    public MainViewModel(
        IGsmModemService modem,
        ISettingsService settings,
        ISmsProcessingCoordinator coordinator,
        IAppEventBus events,
        IIncidentService incidents,
        IClock clock,
        IDialogService dialogs,
        DiagnosticLogLevelController logLevel,
        IApplicationOperationGuard operationGuard,
        ILogger<MainViewModel> logger,
        DashboardViewModel dashboard,
        LiveAlertsViewModel liveAlerts,
        ModemViewModel modemPage,
        DetectorsViewModel detectors,
        IncidentHistoryViewModel incidentHistory,
        ContactsViewModel contacts,
        ReportsViewModel reports,
        AuditLogViewModel auditLog,
        SettingsViewModel settingsPage)
    {
        _modem = modem;
        _settings = settings;
        _coordinator = coordinator;
        _events = events;
        _incidents = incidents;
        _clock = clock;
        _dialogs = dialogs;
        _logLevel = logLevel;
        _operationGuard = operationGuard;
        _logger = logger;
        _dashboard = dashboard;
        _liveAlerts = liveAlerts;

        NavigationItems =
        [
            new("Dashboard", "OV", dashboard),
            new("Live Alerts", "AL", liveAlerts),
            new("GSM Modem", "GSM", modemPage),
            new("Gas Detectors", "DT", detectors),
            new("Incident History", "HI", incidentHistory),
            new("Contacts", "CT", contacts),
            new("Reports", "RP", reports),
            new("Audit Log", "AU", auditLog),
            new("Settings", "ST", settingsPage)
        ];
        _selectedNavigationItem = NavigationItems[0];
        _currentPage = dashboard;
        _currentPageTitle = "Dashboard";
        _appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        _lagosClock = FormatClock();
        _modemStatusText = "MODEM DISCONNECTED";
        _modemStatusDetail = "No GSM modem is connected. Detector last-report times remain the only evidence.";
        _openAlertsSummary = "No unresolved incidents";

        _events.NotificationRaised += OnNotificationRaised;
        _events.IncidentCreated += OnIncidentCreated;
        _events.IncidentStateChanged += OnIncidentStateChanged;
        _modem.StatusChanged += OnModemStatusChanged;
        _operationGuard.StateChanged += OnOperationGuardStateChanged;
    }

    public IReadOnlyList<NavigationItem> NavigationItems { get; }

    [ObservableProperty] private bool _isInitializing = true;
    [ObservableProperty] private string? _initializationError;
    [ObservableProperty] private NavigationItem? _selectedNavigationItem;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _currentPageTitle;
    [ObservableProperty] private string _lagosClock;
    [ObservableProperty] private string _appVersion;
    [ObservableProperty] private string _modemStatusText;
    [ObservableProperty] private string _modemStatusDetail;
    [ObservableProperty] private ModemConnectionState _modemConnectionState;
    [ObservableProperty] private bool _isSimulatedMode;
    [ObservableProperty] private string _modeLabel = "SIMULATED MODE";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpenCritical))]
    private int _openCriticalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpenWarnings))]
    private int _openWarningCount;

    [ObservableProperty] private string _openAlertsSummary;

    public bool HasOpenCritical => OpenCriticalCount > 0;
    public bool HasOpenWarnings => OpenWarningCount > 0;
    [ObservableProperty] private bool _isNotificationVisible;
    [ObservableProperty] private string _notificationTitle = string.Empty;
    [ObservableProperty] private string _notificationMessage = string.Empty;
    [ObservableProperty] private Severity _notificationSeverity;
    [ObservableProperty] private bool _notificationIsSimulated;
    [ObservableProperty] private string _notificationTimeText = string.Empty;
    [ObservableProperty] private bool _modemIsReady;
    [ObservableProperty] private bool _isMaintenanceActive;
    [ObservableProperty] private string _maintenanceDescription = string.Empty;

    public bool HasInitializationError => !string.IsNullOrWhiteSpace(InitializationError);

    partial void OnInitializationErrorChanged(string? value) => OnPropertyChanged(nameof(HasInitializationError));

    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        if (value is null)
        {
            return;
        }

        CurrentPage = value.Page;
        CurrentPageTitle = value.Title;
        _ = ActivatePageSafeAsync(value.Page);
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            var overview = await _settings.GetOverviewAsync();
            _logLevel.Apply(overview.Settings.Preferences.MinimumLogLevel);
            ApplyConfiguredMode(overview.Settings.Modem.UseSimulatedModem);

            await _coordinator.StartAsync();
            await RefreshActiveIncidentCountsAsync();

            if (SelectedNavigationItem?.Page is { } page)
            {
                await ActivatePageSafeAsync(page);
            }

            _timer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            IsInitializing = false;

            if (overview.Settings.Modem.AutoConnect)
            {
                await ConnectFromSavedSettingsAsync(overview.Settings.Modem);
                if (CurrentPage is DashboardViewModel)
                {
                    await ActivatePageSafeAsync(CurrentPage);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Application initialization failed.");
            InitializationError = $"GasGuard could not finish starting: {ex.Message}";
            IsInitializing = false;
            _dialogs.ShowError(
                "Startup incomplete",
                $"{InitializationError}\n\nDiagnostic logs are available in the Settings workspace after the application is usable.");
        }
    }

    public async Task ShutdownAsync()
    {
        if (Interlocked.CompareExchange(ref _shuttingDown, 1, 0) != 0)
        {
            return;
        }

        _timer?.Dispose();
        _timer = null;
        _events.NotificationRaised -= OnNotificationRaised;
        _events.IncidentCreated -= OnIncidentCreated;
        _events.IncidentStateChanged -= OnIncidentStateChanged;
        _modem.StatusChanged -= OnModemStatusChanged;
        _operationGuard.StateChanged -= OnOperationGuardStateChanged;

        try
        {
            await _coordinator.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMS coordinator shutdown failed.");
        }

        try
        {
            await _modem.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Modem shutdown failed.");
        }
    }

    [RelayCommand]
    private void DismissNotification() => IsNotificationVisible = false;

    [RelayCommand]
    private void ReviewNotification()
    {
        var liveAlerts = NavigationItems.FirstOrDefault(x => x.Page == _liveAlerts);
        if (liveAlerts is not null)
        {
            SelectedNavigationItem = liveAlerts;
        }
    }

    private async Task ConnectFromSavedSettingsAsync(ModemSettingsInput settings)
    {
        try
        {
            var result = await _modem.ConnectAsync(ModemOptionsFactory.Create(settings));
            ApplyModemStatus(_modem.Status);
            if (!result.Success)
            {
                RaiseLocalNotification(new MonitorNotification(
                    "Modem connection failed",
                    result.Message,
                    Severity.Warning,
                    settings.UseSimulatedModem,
                    _clock.UtcNow));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic modem connection failed.");
            RaiseLocalNotification(new MonitorNotification(
                "Modem connection failed",
                ex.Message,
                Severity.Warning,
                settings.UseSimulatedModem,
                _clock.UtcNow));
        }
    }

    private async Task ActivatePageSafeAsync(object page)
    {
        if (page is not PageViewModelBase pageViewModel)
        {
            return;
        }

        try
        {
            await pageViewModel.ActivateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Page activation failed for {Page}.", pageViewModel.Title);
        }
    }

    private void OnTimerTick(object? state)
    {
        var tick = Interlocked.Increment(ref _ticks);
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(new Action(() => LagosClock = FormatClock()));

        if (tick % 30 == 0)
        {
            dispatcher.BeginInvoke(new Action(() => _ = RefreshRuntimeStateAsync()));
        }
    }

    private async Task RefreshRuntimeStateAsync()
    {
        await RefreshActiveIncidentCountsAsync();

        if (CurrentPage is DashboardViewModel or LiveAlertsViewModel)
        {
            await ActivatePageSafeAsync(CurrentPage);
        }
    }

    private async Task RefreshActiveIncidentCountsAsync()
    {
        if (_refreshingHeader)
        {
            return;
        }

        _refreshingHeader = true;
        try
        {
            var active = await _incidents.GetActiveAsync();
            OpenCriticalCount = active.Count(x => x.Severity == Severity.Critical);
            OpenWarningCount = active.Count(x => x.Severity == Severity.Warning);
            OpenAlertsSummary = active.Count == 0
                ? "No unresolved incidents"
                : $"{OpenCriticalCount} critical · {OpenWarningCount} warning";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Header incident counts could not be refreshed.");
        }
        finally
        {
            _refreshingHeader = false;
        }
    }

    private void OnNotificationRaised(MonitorNotification notification)
    {
        RaiseLocalNotification(notification);
    }

    private void OnIncidentCreated(Guid incidentId)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(new Action(async () =>
        {
            await RefreshActiveIncidentCountsAsync();
            if (CurrentPage is LiveAlertsViewModel or DashboardViewModel)
            {
                await ActivatePageSafeAsync(CurrentPage);
            }
        }));
    }

    private void OnIncidentStateChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(new Action(async () => await RefreshActiveIncidentCountsAsync()));
    }

    private void OnModemStatusChanged(object? sender, ModemStatusChangedEventArgs args)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(new Action(() => ApplyModemStatus(args.Status)));
    }

    private void OnOperationGuardStateChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            IsMaintenanceActive = _operationGuard.IsOperationActive;
            MaintenanceDescription = _operationGuard.ActiveDescription ?? "Maintenance operation";
        }));
    }

    private void RaiseLocalNotification(MonitorNotification notification)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            NotificationTitle = notification.Title;
            NotificationMessage = notification.Message;
            NotificationSeverity = notification.Severity;
            NotificationIsSimulated = notification.IsSimulated;
            NotificationTimeText = AfricaLagosTime.Format(notification.TimestampUtc);
            IsNotificationVisible = true;

            if (notification.Severity == Severity.Critical)
            {
                SystemSounds.Exclamation.Play();
            }
            else if (notification.Severity == Severity.Warning)
            {
                SystemSounds.Asterisk.Play();
            }
        }));
    }

    private void ApplyConfiguredMode(bool simulated)
    {
        IsSimulatedMode = simulated;
        ModeLabel = simulated ? "SIMULATED MODE" : "PHYSICAL MODE";
    }

    private void ApplyModemStatus(ModemStatus status)
    {
        ModemConnectionState = status.ConnectionState;
        ModemIsReady = status.IsReadyForSms;
        IsSimulatedMode = status.IsSimulated;
        ModeLabel = status.IsSimulated ? "SIMULATED MODE" : "PHYSICAL MODE";

        ModemStatusText = status.ConnectionState switch
        {
            ModemConnectionState.Connected when status.IsReadyForSms => "MODEM READY",
            ModemConnectionState.Connected => "MODEM CONNECTED — NOT READY",
            ModemConnectionState.Connecting => "MODEM CONNECTING",
            ModemConnectionState.Reconnecting => "MODEM RECONNECTING",
            ModemConnectionState.Faulted => "MODEM FAULT",
            _ => "MODEM DISCONNECTED"
        };

        ModemStatusDetail = status.ConnectionState switch
        {
            ModemConnectionState.Connected =>
                $"{status.SimStatusText ?? "SIM status unknown"} · {status.RegistrationText ?? "registration unknown"} · {(status.SignalQuality.HasValue ? $"signal {status.SignalQuality}/31" : "signal unknown")}{(status.IsSimulated ? " · SIMULATED" : string.Empty)}",
            ModemConnectionState.Faulted => status.LastError ?? "The modem reported a fault.",
            _ => status.LastError ?? "No physical GSM modem is connected."
        };
    }

    private string FormatClock() =>
        $"{AfricaLagosTime.ToLocal(_clock.UtcNow):ddd dd MMM yyyy · HH:mm:ss} Lagos";
}
