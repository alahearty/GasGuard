using System.Globalization;

namespace GasGuard.Application.Time;

public static class AfricaLagosTime
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    public static string TimeZoneName => Zone.Id;

    public static DateTime ToLocal(DateTime utc)
    {
        var normalizedUtc = utc.Kind switch
        {
            DateTimeKind.Utc => utc,
            DateTimeKind.Local => utc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc)
        };

        return TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, Zone);
    }

    public static DateTime LocalToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }

    public static DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    public static string Format(DateTime utc, string format = "yyyy-MM-dd HH:mm:ss") =>
        ToLocal(utc).ToString(format, CultureInfo.InvariantCulture);

    public static string FormatWithZone(DateTime utc) =>
        $"{Format(utc)} {Zone.StandardName} (UTC+01:00)";

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Africa/Lagos", "W. Central Africa Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "Africa/Lagos",
            TimeSpan.FromHours(1),
            "West Africa Standard Time",
            "West Africa Standard Time");
    }
}
