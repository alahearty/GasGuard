namespace GasGuard.Contracts.Protocol;

public static class SmsProtocolConstants
{
    public const string Marker = "GASALERT";
    public const int CurrentVersion = 1;
    public const int MaximumBodyLength = 480;
    public const int MaximumDeviceCodeLength = 32;
    public const int MinimumDeviceCodeLength = 3;
    public const int MaximumReading = 1_000_000;
    public const int MaximumUnitLength = 16;
    public const string DefaultUnit = "raw";

    public const string EventStatus = "STATUS";
    public const string EventGasWarning = "GAS_WARNING";
    public const string EventGasCritical = "GAS_CRITICAL";

    public const string LevelNormal = "NORMAL";
    public const string LevelWarning = "WARNING";
    public const string LevelCritical = "CRITICAL";

    public static IReadOnlyList<string> SupportedEvents { get; } =
        [EventStatus, EventGasWarning, EventGasCritical];

    public static IReadOnlyList<string> SupportedLevels { get; } =
        [LevelNormal, LevelWarning, LevelCritical];
}
