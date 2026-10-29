namespace GasGuard.Contracts.Modem;

public sealed record ModemStatus
{
    public ModemConnectionState ConnectionState { get; init; } = ModemConnectionState.Disconnected;
    public bool IsSimulated { get; init; }
    public string PortName { get; init; } = string.Empty;
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? Firmware { get; init; }
    public string? SerialNumber { get; init; }
    public SimReadiness SimReadiness { get; init; } = SimReadiness.Unknown;
    public string? SimStatusText { get; init; }
    public int? SignalQuality { get; init; }
    public string? SignalDescription { get; init; }
    public NetworkRegistrationState RegistrationState { get; init; } = NetworkRegistrationState.Unknown;
    public string? RegistrationText { get; init; }
    public string? NetworkOperator { get; init; }
    public bool SmsTextModeEnabled { get; init; }
    public bool NewMessageIndicationEnabled { get; init; }
    public DateTime? LastPollAtUtc { get; init; }
    public DateTime? ConnectedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public string? LastError { get; init; }

    public bool IsReadyForSms =>
        ConnectionState == ModemConnectionState.Connected &&
        SimReadiness == SimReadiness.Ready &&
        RegistrationState is NetworkRegistrationState.RegisteredHome or NetworkRegistrationState.RegisteredRoaming;

    public static ModemStatus Disconnected(string? error = null) => new()
    {
        ConnectionState = ModemConnectionState.Disconnected,
        SimStatusText = "Not connected",
        RegistrationText = "Unknown",
        UpdatedAtUtc = DateTime.UtcNow,
        LastError = error
    };
}
