using Microsoft.Extensions.Logging;

namespace EvidenceHound.Collectors.Testing;

/// <summary>Keeps the text of everything logged: messages, structured values, scopes and exceptions, at every level.</summary>
internal sealed class CapturingLogger : ILogger
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        _entries.AddRange(Texts(state));
        return null;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _entries.Add(formatter(state, exception));
        _entries.AddRange(Texts(state));
        if (exception is not null)
        {
            _entries.Add(exception.ToString());
        }
    }

    private static IEnumerable<string> Texts<TState>(TState state) =>
        state is IEnumerable<KeyValuePair<string, object?>> values
            ? values.Select(pair => pair.Value?.ToString() ?? string.Empty)
            : [state?.ToString() ?? string.Empty];
}
