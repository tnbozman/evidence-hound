using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

/// <summary>Captures formatted log messages so tests can assert on them.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => [.. _messages];

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(_messages);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue(formatter(state, exception));
    }
}
