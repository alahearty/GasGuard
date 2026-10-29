using Microsoft.Extensions.Logging;

namespace GasGuard.Desktop.Logging;

public sealed class DiagnosticLogLevelController
{
    private volatile LogLevel _minimumLevel = LogLevel.Information;

    public LogLevel MinimumLevel
    {
        get => _minimumLevel;
        set => _minimumLevel = value;
    }

    public void Apply(string? value)
    {
        MinimumLevel = Enum.TryParse<LogLevel>(value, true, out var level)
            ? level
            : LogLevel.Information;
    }
}
