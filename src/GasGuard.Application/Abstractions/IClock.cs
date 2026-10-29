using GasGuard.Application.Time;

namespace GasGuard.Application.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
    DateTime ToLagos(DateTime utc);
    DateTime LagosToUtc(DateTime local);
    DateOnly ToLagosDate(DateTime utc);
    string LagosTimeZoneName { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime ToLagos(DateTime utc) => AfricaLagosTime.ToLocal(utc);

    public DateTime LagosToUtc(DateTime local) => AfricaLagosTime.LocalToUtc(local);

    public DateOnly ToLagosDate(DateTime utc) => AfricaLagosTime.ToLocalDate(utc);

    public string LagosTimeZoneName => AfricaLagosTime.TimeZoneName;
}
