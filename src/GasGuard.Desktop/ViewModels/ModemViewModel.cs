using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Contracts.Modem;
using GasGuard.Desktop.Services;
using GasGuard.Domain;
using Microsoft.Extensions.Logging;

namespace GasGuard.Desktop.ViewModels;

public sealed partial class ModemViewModel : PageViewModelBase
{
    private readonly IGsmModemService _modem;
    private readonly ISimulatedModemControl _simulation;
    private readonly ISettingsService _settings;
    private readonly IDeviceService _devices;
    private readonly ISmsProcessingCoordinator _coordinator;
    private readonly IDemoScenarioService _demoScenario;
    private readonly ISmsEvidenceService _smsEvidence;
    private readonly ILogger<ModemViewModel> _logger;
    private IReadOnlyList<DeviceListItem> _allDevices = [];

    public ModemViewModel(
        IGsmModemService modem,
        ISimulatedModemControl simulation,
        ISettingsService settings,
        IDeviceService devices,
        ISmsProcessingCoordinator coordinator,
        IDemoScenarioService demoScenario,
        ISmsEvidenceService smsEvidence,
        ILogger<ModemViewModel> logger) : base(logger)
    {
        _modem = modem;
        _simulation = simulation;
        _settings = settings;
        _devices = devices;
        _coordinator = coordinator;
        _demoScenario = demoScenario;
        _smsEvidence = smsEvidence;
        _logger = logger;

        Parities = Enum.GetValues<SerialParity>();
        StopBitsOptions = Enum.GetValues<SerialStopBits>();
        HandshakeOptions = Enum.GetValues<SerialHandshake>();
        DemoEvents =
        [
            new("STATUS (NORMAL)", GasEventCode.Status),
            new("GAS_WARNING", GasEventCode.GasWarning),
            new("GAS_CRITICAL", GasEventCode.GasCritical)
        ];
        _selectedDemoEvent = DemoEvents[2];
        SimulatedSendModes =
        [
            new("Accept and submit", SimulatedSendMode.Success),
            new("Reject at modem", SimulatedSendMode.ModemRejected),
            new("Simulate timeout", SimulatedSendMode.Timeout)
        ];
        _selectedSendMode = SimulatedSendModes[0];
        ModemModes =
        [
            new("Simulated modem (DEMO)", true),
            new("Physical USB GSM modem", false)
        ];
        _selectedModemMode = ModemModes[0];

        _modem.StatusChanged += OnModemStatusChanged;
    }

    public override string Title => "GSM Modem";

    public ObservableCollection<string> AvailablePorts { get; } = [];
    public ObservableCollection<ModemDiagnosticEntry> Diagnostics { get; } = [];
    public ObservableCollection<DeviceListItem> DemoDevices { get; } = [];
    public ObservableCollection<SmsEvidenceItem> SmsEvidence { get; } = [];
    public IReadOnlyList<SerialParity> Parities { get; }
    public IReadOnlyList<SerialStopBits> StopBitsOptions { get; }
    public IReadOnlyList<SerialHandshake> HandshakeOptions { get; }
    public IReadOnlyList<ChoiceOption<GasEventCode>> DemoEvents { get; }
    public IReadOnlyList<ChoiceOption<SimulatedSendMode>> SimulatedSendModes { get; }
    public IReadOnlyList<ChoiceOption<bool>> ModemModes { get; }

    [ObservableProperty] private ChoiceOption<bool> _selectedModemMode;
    [ObservableProperty] private string _portName = "SIMULATOR";
    [ObservableProperty] private string? _selectedDetectedPort;
    [ObservableProperty] private string _baudRateText = "115200";
    [ObservableProperty] private string _dataBitsText = "8";
    [ObservableProperty] private SerialParity _selectedParity = SerialParity.None;
    [ObservableProperty] private SerialStopBits _selectedStopBits = SerialStopBits.One;
    [ObservableProperty] private SerialHandshake _selectedHandshake = SerialHandshake.None;
    [ObservableProperty] private string _commandTimeoutText = "8000";
    [ObservableProperty] private string _pollIntervalText = "30";
    [ObservableProperty] private bool _autoConnect = true;
    [ObservableProperty] private bool _autoReconnect = true;
    [ObservableProperty] private bool _deleteSmsAfterPersistence = true;

    [ObservableProperty] private string _connectionStateText = "DISCONNECTED";
    [ObservableProperty] private ModemConnectionState _connectionState = ModemConnectionState.Disconnected;
    [ObservableProperty] private string _simStatusText = "SIM status unknown";
    [ObservableProperty] private string _registrationText = "Network registration unknown";
    [ObservableProperty] private string _signalText = "Signal unknown";
    [ObservableProperty] private string _operatorText = "Network operator unknown";
    [ObservableProperty] private string _modemIdentityText = "Not identified";
    [ObservableProperty] private string _textModeText = "SMS text mode unknown";
    [ObservableProperty] private string _newMessageText = "New-message indication unknown";
    [ObservableProperty] private string _lastPollText = "Never";
    [ObservableProperty] private string _lastErrorText = "None";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isReadyForSms;
    [ObservableProperty] private bool _isSimulated;
    [ObservableProperty] private string _pendingStoredText = "0 stored message(s)";
    [ObservableProperty] private bool _hasDiagnostics;
    [ObservableProperty] private SmsEvidenceItem? _selectedSmsEvidence;
    [ObservableProperty] private bool _hasSmsEvidence;

    [ObservableProperty] private DeviceListItem? _selectedDemoDevice;
    [ObservableProperty] private ChoiceOption<GasEventCode> _selectedDemoEvent;
    [ObservableProperty] private string _demoReadingText = "850";
    [ObservableProperty] private string _customSender = "+2348030000001";
    [ObservableProperty] private string _customBody = "GASALERT|v=1|device=GAS-001|event=GAS_CRITICAL|level=CRITICAL|reading=850|unit=raw|seq=9001";
    [ObservableProperty] private string _customDelayText = "0";
    [ObservableProperty] private ChoiceOption<SimulatedSendMode> _selectedSendMode;

    public bool IsPhysicalMode => SelectedModemMode.Value is false;
    public bool IsSimulatorMode => SelectedModemMode.Value;
    public string ConnectionHeadline => ConnectionState switch
    {
        ModemConnectionState.Connected when IsReadyForSms => "MODEM READY",
        ModemConnectionState.Connected => "MODEM CONNECTED - NOT READY",
        ModemConnectionState.Connecting => "CONNECTING",
        ModemConnectionState.Reconnecting => "RECONNECTING",
        ModemConnectionState.Faulted => "MODEM FAULT",
        _ => "DISCONNECTED"
    };

    partial void OnSelectedModemModeChanged(ChoiceOption<bool> value)
    {
        OnPropertyChanged(nameof(IsPhysicalMode));
        OnPropertyChanged(nameof(IsSimulatorMode));
        if (value.Value)
        {
            PortName = "SIMULATOR";
        }
        else if (PortName == "SIMULATOR")
        {
            PortName = AvailablePorts.FirstOrDefault() ?? string.Empty;
        }
    }

    partial void OnSelectedDetectedPortChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            PortName = value;
        }
    }

    partial void OnSelectedDemoDeviceChanged(DeviceListItem? value)
    {
        if (value is not null)
        {
            CustomSender = value.ExpectedSenderNumber;
            CustomBody = $"GASALERT|v=1|device={value.DeviceCode}|event=GAS_CRITICAL|level=CRITICAL|reading=850|unit=raw|seq=9001";
        }
    }

    protected override async Task LoadAsync()
    {
        var overview = await _settings.GetOverviewAsync();
        ApplySettings(overview.Settings.Modem);
        await LoadPortsAsync();
        await LoadDemoDevicesAsync();

        var status = await _modem.RefreshStatusAsync();
        ApplyStatus(status);
        RefreshDiagnostics();
        UpdatePendingCount();
        await RefreshSmsEvidenceCoreAsync();
        SetError(null);
        SetStatus(null);
    }

    [RelayCommand]
    private async Task SaveSettingsAsync() => await RunOperationAsync("Saving modem settings", async () =>
    {
        if (!TryBuildSettings(out var input, out var error))
        {
            SetError(error);
            return;
        }

        var result = await _settings.SaveModemSettingsAsync(input);
        if (!result.Success)
        {
            SetError(FormatFailure(result));
            return;
        }

        SetError(null);
        SetStatus(result.Message);
    });

    [RelayCommand]
    private async Task ConnectAsync() => await RunOperationAsync("Connecting modem", async () =>
    {
        if (!TryBuildSettings(out var input, out var error))
        {
            SetError(error);
            return;
        }

        var save = await _settings.SaveModemSettingsAsync(input);
        if (!save.Success)
        {
            SetError(FormatFailure(save));
            return;
        }

        var options = ModemOptionsFactory.Create(input);
        var result = await _modem.ConnectAsync(options);
        ApplyStatus(_modem.Status);
        RefreshDiagnostics();
        UpdatePendingCount();

        if (result.Success)
        {
            SetError(null);
            SetStatus(result.Message);
        }
        else
        {
            SetError(result.Message);
        }
    });

    [RelayCommand]
    private async Task DisconnectAsync() => await RunOperationAsync("Disconnecting modem", async () =>
    {
        var result = await _modem.DisconnectAsync();
        ApplyStatus(_modem.Status);
        RefreshDiagnostics();
        SetStatus(result.Message);
    });

    [RelayCommand]
    private async Task RefreshStatusAsync() => await RunOperationAsync("Refreshing modem status", async () =>
    {
        var status = await _modem.RefreshStatusAsync();
        ApplyStatus(status);
        RefreshDiagnostics();
        UpdatePendingCount();
    });

    [RelayCommand]
    private async Task RefreshPortsAsync() => await RunOperationAsync("Refreshing COM ports", async () =>
    {
        await LoadPortsAsync();
        SetStatus(AvailablePorts.Count == 0
            ? "No physical serial ports were detected. The simulated modem remains available for demonstration."
            : $"{AvailablePorts.Count} serial port(s) detected.");
    });

    [RelayCommand]
    private async Task PollNowAsync() => await RunOperationAsync("Polling modem storage", async () =>
    {
        var count = await _coordinator.ReconcileNowAsync();
        ApplyStatus(_modem.Status);
        UpdatePendingCount();
        RefreshDiagnostics();
        await RefreshSmsEvidenceCoreAsync();
        SetStatus($"Reconciliation read {count} stored message(s). Messages are deleted only after successful persistence.");
    });

    [RelayCommand]
    private void RefreshDiagnostics()
    {
        var entries = _modem.GetDiagnostics(200);
        Diagnostics.Clear();
        foreach (var entry in entries)
        {
            Diagnostics.Add(entry);
        }

        HasDiagnostics = Diagnostics.Count > 0;
        UpdatePendingCount();
    }

    [RelayCommand]
    private void ClearDiagnostics()
    {
        _modem.ClearDiagnostics();
        RefreshDiagnostics();
        SetStatus("In-memory modem diagnostics cleared. Persistent communication logs remain in the database.");
    }

    [RelayCommand]
    private async Task RefreshSmsEvidenceAsync() => await RunOperationAsync("Refreshing SMS evidence", async () =>
    {
        await RefreshSmsEvidenceCoreAsync();
        SetStatus("Recent raw SMS evidence refreshed, including rejected and invalid messages.");
    });

    [RelayCommand]
    private async Task LoadDemoDataAsync() => await RunOperationAsync("Loading demo data", async () =>
    {
        var result = await _demoScenario.LoadDemoDataAsync();
        if (!result.Success)
        {
            SetError(result.Message);
            return;
        }

        await LoadDemoDevicesAsync();
        SetError(null);
        SetStatus(result.Message);
    });

    [RelayCommand]
    private async Task InjectDemoAsync() => await RunOperationAsync("Injecting simulated message", async () =>
    {
        if (SelectedDemoDevice is null)
        {
            SetError("Select a registered detector for the simulated message.");
            return;
        }

        if (!int.TryParse(DemoReadingText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var reading))
        {
            SetError("The demonstration raw reading must be a whole number.");
            return;
        }

        var result = await _demoScenario.InjectDemoMessageAsync(
            SelectedDemoDevice.DeviceCode,
            SelectedDemoEvent.Value,
            reading);
        UpdatePendingCount();
        RefreshDiagnostics();
        await Task.Delay(350);
        if (!result.Success)
        {
            SetError(result.Message);
            return;
        }

        await RefreshSmsEvidenceCoreAsync();
        SetError(null);
        SetStatus(result.Message);
    });

    [RelayCommand]
    private async Task InjectCustomAsync() => await RunOperationAsync("Injecting custom simulated SMS", async () =>
    {
        if (!int.TryParse(CustomDelayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var delayMs) ||
            delayMs is < 0 or > 60000)
        {
            SetError("Delay must be a whole number of milliseconds between 0 and 60,000.");
            return;
        }

        if (string.IsNullOrWhiteSpace(CustomSender) || string.IsNullOrWhiteSpace(CustomBody))
        {
            SetError("Enter both a sender and a raw SMS body for the simulated message.");
            return;
        }

        var result = await _simulation.InjectMessageAsync(new SimulatedSmsRequest(
            CustomSender.Trim(),
            CustomBody,
            TimeSpan.FromMilliseconds(delayMs)));
        UpdatePendingCount();
        RefreshDiagnostics();
        await Task.Delay(350);
        await RefreshSmsEvidenceCoreAsync();
        if (result.Success)
        {
            SetError(null);
            SetStatus(result.Message);
        }
        else
        {
            SetError(result.Message);
        }
    });

    [RelayCommand]
    private async Task ApplySendModeAsync() => await RunOperationAsync("Applying simulation send mode", async () =>
    {
        var result = await _simulation.SetSendModeAsync(SelectedSendMode.Value);
        if (result.Success)
        {
            SetError(null);
            SetStatus(result.Message);
        }
        else
        {
            SetError(result.Message);
        }
    });

    [RelayCommand]
    private async Task SimulateDisconnectAsync() => await RunOperationAsync("Simulating modem disconnection", async () =>
    {
        var result = await _simulation.SimulateDisconnectionAsync("Simulated cable disconnection.");
        ApplyStatus(_modem.Status);
        RefreshDiagnostics();
        if (result.Success)
        {
            SetError(null);
            SetStatus(result.Message);
        }
        else
        {
            SetError(result.Message);
        }
    });

    private async Task LoadPortsAsync()
    {
        var ports = await _modem.GetAvailablePortsAsync();
        AvailablePorts.Clear();
        foreach (var port in ports)
        {
            AvailablePorts.Add(port);
        }

        if (IsPhysicalMode && string.IsNullOrWhiteSpace(PortName) && AvailablePorts.Count > 0)
        {
            PortName = AvailablePorts[0];
        }
    }

    private async Task LoadDemoDevicesAsync()
    {
        _allDevices = (await _devices.GetDevicesAsync(false))
            .Where(device => device.IsDemo)
            .ToArray();
        var selectedId = SelectedDemoDevice?.Id;
        DemoDevices.Clear();
        foreach (var device in _allDevices)
        {
            DemoDevices.Add(device);
        }

        SelectedDemoDevice = DemoDevices.FirstOrDefault(x => x.Id == selectedId) ?? DemoDevices.FirstOrDefault();
    }

    private void ApplySettings(ModemSettingsInput settings)
    {
        SelectedModemMode = settings.UseSimulatedModem
            ? ModemModes.First(x => x.Value)
            : ModemModes.First(x => !x.Value);
        PortName = settings.PortName;
        BaudRateText = settings.BaudRate.ToString(CultureInfo.InvariantCulture);
        DataBitsText = settings.DataBits.ToString(CultureInfo.InvariantCulture);
        SelectedParity = settings.Parity;
        SelectedStopBits = settings.StopBits;
        SelectedHandshake = settings.Handshake;
        CommandTimeoutText = settings.CommandTimeoutMs.ToString(CultureInfo.InvariantCulture);
        PollIntervalText = settings.PollIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        AutoConnect = settings.AutoConnect;
        AutoReconnect = settings.AutoReconnect;
        DeleteSmsAfterPersistence = settings.DeleteSmsAfterPersistence;
    }

    private bool TryBuildSettings(out ModemSettingsInput settings, out string? error)
    {
        settings = null!;
        error = null;
        if (!int.TryParse(BaudRateText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var baud) ||
            !int.TryParse(DataBitsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dataBits) ||
            !int.TryParse(CommandTimeoutText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout) ||
            !int.TryParse(PollIntervalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var poll))
        {
            error = "Baud rate, data bits, command timeout and polling interval must be whole numbers.";
            return false;
        }

        settings = new ModemSettingsInput(
            SelectedModemMode.Value,
            SelectedModemMode.Value ? "SIMULATOR" : PortName.Trim(),
            baud,
            dataBits,
            SelectedParity,
            SelectedStopBits,
            SelectedHandshake,
            timeout,
            poll,
            AutoConnect,
            AutoReconnect,
            DeleteSmsAfterPersistence);
        return true;
    }

    private async Task RefreshSmsEvidenceCoreAsync()
    {
        var selectedId = SelectedSmsEvidence?.Id;
        var evidence = await _smsEvidence.GetRecentAsync(50);
        SmsEvidence.Clear();
        foreach (var item in evidence)
        {
            SmsEvidence.Add(item);
        }

        HasSmsEvidence = SmsEvidence.Count > 0;
        SelectedSmsEvidence = SmsEvidence.FirstOrDefault(x => x.Id == selectedId) ?? SmsEvidence.FirstOrDefault();
    }

    private void ApplyStatus(ModemStatus status)
    {
        ConnectionState = status.ConnectionState;
        IsConnected = status.ConnectionState == ModemConnectionState.Connected;
        IsReadyForSms = status.IsReadyForSms;
        IsSimulated = status.IsSimulated;
        SimStatusText = status.SimStatusText ?? "SIM status unknown";
        RegistrationText = status.RegistrationText ?? "Network registration unknown";
        OperatorText = status.NetworkOperator ?? "Network operator unknown";
        SignalText = status.SignalQuality.HasValue
            ? $"{status.SignalQuality}/31 — {status.SignalDescription}"
            : status.SignalDescription ?? "Signal quality unknown";
        ModemIdentityText = status.Model ?? (status.IsSimulated ? "GasGuard Simulated GSM Modem" : "Not identified");
        TextModeText = status.SmsTextModeEnabled ? "SMS text mode enabled" : "SMS text mode not enabled";
        NewMessageText = status.NewMessageIndicationEnabled
            ? "New-message indication enabled"
            : "New-message indication unavailable; polling handles reception";
        LastPollText = status.LastPollAtUtc.HasValue ? UiFormat.Local(status.LastPollAtUtc) : "Never";
        LastErrorText = status.LastError ?? "None";
        ConnectionStateText = ConnectionHeadline;
        OnPropertyChanged(nameof(ConnectionHeadline));
        UpdatePendingCount();
    }

    private void UpdatePendingCount()
    {
        PendingStoredText = IsSimulated
            ? $"{_simulation.PendingStoredMessageCount} stored simulated message(s)"
            : "Physical modem storage is reconciled through unread SMS polling.";
    }

    private void OnModemStatusChanged(object? sender, ModemStatusChangedEventArgs args)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(new Action(() => ApplyStatus(args.Status)));
    }

    private static string FormatFailure(OperationResult result)
    {
        if (result.ValidationErrors is null || result.ValidationErrors.Count == 0)
        {
            return result.Message;
        }

        return $"{result.Message} {string.Join(" ", result.ValidationErrors)}";
    }
}
