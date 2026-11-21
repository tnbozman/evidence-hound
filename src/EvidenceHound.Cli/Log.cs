using System.Collections;
using EvidenceHound.Core.Results;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>The CLI's log messages, in one place so every command reports the same way.</summary>
internal static partial class Log
{
    private static readonly EventId ProblemEvent = new(3000, "Problem");

    /// <summary>Logs each expected failure at Error with its code, and returns the configuration exit code.</summary>
    public static int Problems(ILogger logger, IEnumerable<Problem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        foreach (var problem in problems)
        {
            logger.Log(LogLevel.Error, ProblemEvent, new ProblemState(problem), null, static (state, _) => state.Problem.Message);
        }

        return ExitCodes.Configuration;
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Error, Message = "{Message}")]
    public static partial void Usage(ILogger logger, string message);

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "{Count} collectors configured")]
    public static partial void CollectorsConfigured(ILogger logger, int count);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Published to CISO Assistant: {Sent} sent, {Skipped} skipped, {Failed} failed")]
    public static partial void Published(ILogger logger, int sent, int skipped, int failed);

    [LoggerMessage(EventId = 9000, Level = LogLevel.Critical, Message = "Unexpected failure running '{Command}'. Rerun with Logging__LogLevel__Default=Debug for the stack trace.")]
    public static partial void Unexpected(ILogger logger, Exception exception, string command);

    [LoggerMessage(EventId = 9001, Level = LogLevel.Debug, Message = "Details of the unexpected failure:")]
    public static partial void Details(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Warning, Message = "Cancelled.")]
    public static partial void Cancelled(ILogger logger);

    /// <summary>Structured state for a problem: <c>Code</c> and <c>Message</c>.</summary>
    private sealed class ProblemState(Problem problem) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly KeyValuePair<string, object?>[] _items =
            [new("Code", problem.Code), new("Message", problem.Message), new("{OriginalFormat}", "{Message}")];

        public Problem Problem { get; } = problem;

        public int Count => _items.Length;

        public KeyValuePair<string, object?> this[int index] => _items[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)_items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}
