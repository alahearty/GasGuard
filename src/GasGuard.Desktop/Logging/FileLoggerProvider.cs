using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace GasGuard.Desktop.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly DiagnosticLogLevelController _levelController;
    private readonly object _sync = new();
    private StreamWriter? _writer;
    private DateOnly _writerDate;
    private bool _disposed;

    public FileLoggerProvider(string logDirectory, DiagnosticLogLevelController levelController)
    {
        _logDirectory = logDirectory;
        _levelController = levelController;
        Directory.CreateDirectory(_logDirectory);
        DeleteOldLogs();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        if (level < _levelController.MinimumLevel)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var line = new StringBuilder()
            .Append(now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
            .Append(" [")
            .Append(LevelText(level))
            .Append("] ")
            .Append(category)
            .Append(" - ")
            .Append(message);

        if (exception is not null)
        {
            line.AppendLine();
            line.Append(exception);
        }

        var text = line.ToString();
        Debug.WriteLine(text);

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var today = DateOnly.FromDateTime(now);
                if (_writer is null || _writerDate != today)
                {
                    _writer?.Dispose();
                    var path = Path.Combine(_logDirectory, $"gasguard-{today:yyyyMMdd}.log");
                    _writer = new StreamWriter(
                        new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                        new UTF8Encoding(false))
                    {
                        AutoFlush = true
                    };
                    _writerDate = today;
                }

                _writer.WriteLine(text);
            }
            catch (Exception writeException)
            {
                Debug.WriteLine($"GasGuard file logging failed: {writeException}");
            }
        }
    }

    private void DeleteOldLogs()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-30);
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "gasguard-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GasGuard old-log cleanup failed: {ex}");
        }
    }

    private static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRITICAL",
        _ => "NONE"
    };

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && logLevel >= provider._levelController.MinimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
