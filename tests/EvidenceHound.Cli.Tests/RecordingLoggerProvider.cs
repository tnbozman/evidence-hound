using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

/// <summary>One captured log entry.</summary>
internal sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception, IReadOnlyList<KeyValuePair<string, object?>> State);

/// <summary>Captures log entries so tests can assert on them.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyCollection<LogEntry> Entries => [.. _entries];

    public IReadOnlyCollection<string> Messages => [.. _entries.Select(e => e.Message)];

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(_entries);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new LogEntry(
                logLevel,
                eventId,
                formatter(state, exception),
                exception,
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? []));
    }
}
