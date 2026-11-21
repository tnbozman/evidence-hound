using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// Writes every log entry to one writer (stderr in production) as <c>level: message</c>, so stdout stays free for command output.
/// An exception is summarised on the same line, except at Debug and Trace, where the full exception with its stack trace follows.
/// </summary>
internal sealed class StderrLoggerProvider(TextWriter writer) : ILoggerProvider
{
    private readonly Lock _gate = new();

    public ILogger CreateLogger(string categoryName) => new StderrLogger(this);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        lock (_gate)
        {
            writer.WriteLine(line);
        }
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        _ => "crit",
    };

    private sealed class StderrLogger(StderrLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var detail = exception switch
            {
                null => "",
                _ when logLevel <= LogLevel.Debug => Environment.NewLine + exception,
                _ => $" ({exception.GetType().Name}: {exception.Message})",
            };
            provider.Write($"{LevelName(logLevel)}: {formatter(state, exception)}{detail}");
        }
    }
}
