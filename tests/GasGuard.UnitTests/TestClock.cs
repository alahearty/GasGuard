using GasGuard.Application.Abstractions;
using GasGuard.Application.Time;

namespace GasGuard.UnitTests;

internal sealed class TestClock : IClock
{
    public TestClock(DateTime? utcNow = null)
    {
        UtcNow = utcNow ?? new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    }

    public DateTime UtcNow { get; set; }
    public DateTime ToLagos(DateTime utc) => AfricaLagosTime.ToLocal(utc);
    public DateTime LagosToUtc(DateTime local) => AfricaLagosTime.LocalToUtc(local);
    public DateOnly ToLagosDate(DateTime utc) => AfricaLagosTime.ToLocalDate(utc);
    public string LagosTimeZoneName => AfricaLagosTime.TimeZoneName;
}
