using GasGuard.Domain;

namespace GasGuard.Contracts.Protocol;

public static class GasProtocolMap
{
    public static bool TryMapEvent(string? value, out GasEventCode eventCode, out string normalized)
    {
        normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        eventCode = normalized switch
        {
            SmsProtocolConstants.EventStatus => GasEventCode.Status,
            SmsProtocolConstants.EventGasWarning => GasEventCode.GasWarning,
            SmsProtocolConstants.EventGasCritical => GasEventCode.GasCritical,
            _ => GasEventCode.Unknown
        };
        return eventCode != GasEventCode.Unknown;
    }

    public static Severity ToSeverity(GasEventCode eventCode) => eventCode switch
    {
        GasEventCode.Status => Severity.Normal,
        GasEventCode.GasWarning => Severity.Warning,
        GasEventCode.GasCritical => Severity.Critical,
        _ => Severity.Unknown
    };

    public static DeviceReportedState ToReportedState(GasEventCode eventCode) => eventCode switch
    {
        GasEventCode.Status => DeviceReportedState.Normal,
        GasEventCode.GasWarning => DeviceReportedState.Warning,
        GasEventCode.GasCritical => DeviceReportedState.Critical,
        _ => DeviceReportedState.Unknown
    };

    public static string ExpectedLevel(GasEventCode eventCode) => eventCode switch
    {
        GasEventCode.Status => SmsProtocolConstants.LevelNormal,
        GasEventCode.GasWarning => SmsProtocolConstants.LevelWarning,
        GasEventCode.GasCritical => SmsProtocolConstants.LevelCritical,
        _ => string.Empty
    };

    public static string EventText(GasEventCode eventCode) => eventCode switch
    {
        GasEventCode.Status => SmsProtocolConstants.EventStatus,
        GasEventCode.GasWarning => SmsProtocolConstants.EventGasWarning,
        GasEventCode.GasCritical => SmsProtocolConstants.EventGasCritical,
        _ => "UNKNOWN"
    };
}
