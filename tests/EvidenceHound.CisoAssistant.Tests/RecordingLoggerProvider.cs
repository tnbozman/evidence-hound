using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>Captures every log message, with its structured values, at every level.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    /// <summary>Each entry's formatted message followed by its structured values.</summary>
    public IReadOnlyCollection<string> Lines => [.. _lines];

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            lines.Enqueue($"{category}: {formatter(state, exception)} | {string.Join("; ", values.Select(v => $"{v.Key}={v.Value}"))}");
        }
    }
}
