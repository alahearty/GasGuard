using GasGuard.Application.Time;

namespace GasGuard.Desktop.ViewModels;

internal static class UiFormat
{
    public static string Local(DateTime? utc, string format = "yyyy-MM-dd HH:mm:ss") =>
        utc.HasValue ? AfricaLagosTime.Format(utc.Value, format) : "—";

    public static string Age(DateTime? utc, DateTime nowUtc)
    {
        if (!utc.HasValue)
        {
            return "never";
        }

        var age = nowUtc - utc.Value;
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

    public static string TimeZoneLabel => "Africa/Lagos (UTC+01:00)";
}
