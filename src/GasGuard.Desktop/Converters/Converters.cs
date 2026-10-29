using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GasGuard.Application.Time;
using GasGuard.Contracts.Modem;
using GasGuard.Domain;

namespace GasGuard.Desktop.Converters;

internal static class BrushPalette
{
    public static readonly SolidColorBrush Success = Freeze("#42C98B");
    public static readonly SolidColorBrush Warning = Freeze("#F2B544");
    public static readonly SolidColorBrush Critical = Freeze("#E55353");
    public static readonly SolidColorBrush Info = Freeze("#5AA9E6");
    public static readonly SolidColorBrush Muted = Freeze("#93A8B7");
    public static readonly SolidColorBrush SuccessSoft = Freeze("#173A2C");
    public static readonly SolidColorBrush WarningSoft = Freeze("#3A3019");
    public static readonly SolidColorBrush CriticalSoft = Freeze("#3A1F24");
    public static readonly SolidColorBrush InfoSoft = Freeze("#1B3347");
    public static readonly SolidColorBrush MutedSoft = Freeze("#22313D");

    private static SolidColorBrush Freeze(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}

public sealed class UtcToLagosConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime utc)
        {
            return "—";
        }

        var format = parameter as string ?? "yyyy-MM-dd HH:mm:ss";
        return AfricaLagosTime.Format(utc, format);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class UtcAgeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime utc)
        {
            return "Never";
        }

        var age = DateTime.UtcNow - utc;
        return AgeText(age);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    public static string AgeText(TimeSpan age)
    {
        if (age.TotalSeconds < 0)
        {
            return "just now";
        }

        if (age.TotalSeconds < 60)
        {
            return "just now";
        }

        if (age.TotalMinutes < 60)
        {
            return $"{(int)age.TotalMinutes} min ago";
        }

        if (age.TotalHours < 24)
        {
            return $"{(int)age.TotalHours} h ago";
        }

        return $"{(int)age.TotalDays} d ago";
    }
}

public sealed class StateTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DeviceOperationalState.Unknown => "UNKNOWN",
        DeviceOperationalState.Normal => "NORMAL",
        DeviceOperationalState.Warning => "WARNING",
        DeviceOperationalState.Critical => "CRITICAL",
        DeviceOperationalState.Offline => "OFFLINE / STALE",
        DeviceOperationalState.CommunicationError => "COMMUNICATION ERROR",
        DeviceReportedState.Unknown => "UNKNOWN",
        DeviceReportedState.Normal => "NORMAL",
        DeviceReportedState.Warning => "WARNING",
        DeviceReportedState.Critical => "CRITICAL",
        Severity.Unknown => "UNKNOWN",
        Severity.Normal => "NORMAL",
        Severity.Warning => "WARNING",
        Severity.Critical => "CRITICAL",
        _ => value?.ToString() ?? "UNKNOWN"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class IncidentStatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        IncidentStatus.Open => "OPEN",
        IncidentStatus.Acknowledged => "ACKNOWLEDGED",
        IncidentStatus.Resolved => "RESOLVED",
        _ => value?.ToString()?.ToUpperInvariant() ?? "UNKNOWN"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isBackground = string.Equals(parameter as string, "background", StringComparison.OrdinalIgnoreCase);
        return value switch
        {
            Severity.Critical => isBackground ? BrushPalette.CriticalSoft : BrushPalette.Critical,
            Severity.Warning => isBackground ? BrushPalette.WarningSoft : BrushPalette.Warning,
            Severity.Normal => isBackground ? BrushPalette.SuccessSoft : BrushPalette.Success,
            _ => isBackground ? BrushPalette.MutedSoft : BrushPalette.Muted
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class StateBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isBackground = string.Equals(parameter as string, "background", StringComparison.OrdinalIgnoreCase);
        return value switch
        {
            DeviceOperationalState.Critical => isBackground ? BrushPalette.CriticalSoft : BrushPalette.Critical,
            DeviceOperationalState.Warning => isBackground ? BrushPalette.WarningSoft : BrushPalette.Warning,
            DeviceOperationalState.Normal => isBackground ? BrushPalette.SuccessSoft : BrushPalette.Success,
            DeviceOperationalState.CommunicationError => isBackground ? BrushPalette.WarningSoft : BrushPalette.Warning,
            DeviceReportedState.Critical => isBackground ? BrushPalette.CriticalSoft : BrushPalette.Critical,
            DeviceReportedState.Warning => isBackground ? BrushPalette.WarningSoft : BrushPalette.Warning,
            DeviceReportedState.Normal => isBackground ? BrushPalette.SuccessSoft : BrushPalette.Success,
            ModemConnectionState.Connected => isBackground ? BrushPalette.SuccessSoft : BrushPalette.Success,
            ModemConnectionState.Connecting or ModemConnectionState.Reconnecting =>
                isBackground ? BrushPalette.WarningSoft : BrushPalette.Warning,
            ModemConnectionState.Faulted => isBackground ? BrushPalette.CriticalSoft : BrushPalette.Critical,
            _ => isBackground ? BrushPalette.MutedSoft : BrushPalette.Muted
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class IncidentStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isBackground = string.Equals(parameter as string, "background", StringComparison.OrdinalIgnoreCase);
        return value switch
        {
            IncidentStatus.Open => isBackground ? BrushPalette.CriticalSoft : BrushPalette.Critical,
            IncidentStatus.Acknowledged => isBackground ? BrushPalette.WarningSoft : BrushPalette.Warning,
            IncidentStatus.Resolved => isBackground ? BrushPalette.SuccessSoft : BrushPalette.Success,
            _ => isBackground ? BrushPalette.MutedSoft : BrushPalette.Muted
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class CommunicationOutcomeBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isBackground = string.Equals(parameter as string, "background", StringComparison.OrdinalIgnoreCase);
        return value switch
        {
            CommunicationOutcome.Failure or CommunicationOutcome.Rejected or CommunicationOutcome.Timeout =>
                isBackground ? BrushPalette.CriticalSoft : BrushPalette.Critical,
            CommunicationOutcome.Success => isBackground ? BrushPalette.SuccessSoft : BrushPalette.Success,
            CommunicationOutcome.Information => isBackground ? BrushPalette.InfoSoft : BrushPalette.Info,
            _ => isBackground ? BrushPalette.MutedSoft : BrushPalette.Muted
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (string.Equals(parameter as string, "inverse", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is not null && (value is not string text || !string.IsNullOrWhiteSpace(text));
        if (string.Equals(parameter as string, "inverse", StringComparison.OrdinalIgnoreCase))
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

public sealed class BytesToSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes)
        {
            return "—";
        }

        string[] suffixes = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var index = 0;
        while (size >= 1024 && index < suffixes.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return $"{size:0.#} {suffixes[index]}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class SpacedEnumConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "—";
        }

        return System.Text.RegularExpressions.Regex.Replace(text, "(?<!^)([A-Z])", " $1");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class BoolToHealthyTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "HEALTHY" : "NOT HEALTHY";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class BoolToHealthyBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isBackground = string.Equals(parameter as string, "background", StringComparison.OrdinalIgnoreCase);
        return value is true
            ? isBackground ? BrushPalette.SuccessSoft : BrushPalette.Success
            : isBackground ? BrushPalette.CriticalSoft : BrushPalette.Critical;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class BoolToYesNoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Yes" : "No";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class BoolToActiveTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "ACTIVE" : "INACTIVE";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class BoolToActiveBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isBackground = string.Equals(parameter as string, "background", StringComparison.OrdinalIgnoreCase);
        return value is true
            ? isBackground ? BrushPalette.SuccessSoft : BrushPalette.Success
            : isBackground ? BrushPalette.MutedSoft : BrushPalette.Muted;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
