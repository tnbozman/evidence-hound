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

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Collected {Count} files from {Collector} into {Location}")]
    public static partial void Collected(ILogger logger, string collector, int count, string location);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Collector {Collector}: {Outcome}")]
    public static partial void CollectorRan(ILogger logger, string collector, Runtime.CollectorOutcome outcome);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "{Collector}: next run {Next}")]
    public static partial void NextRun(ILogger logger, string collector, NextRun next);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "{Collector}: scheduled run finished with exit code {ExitCode}; next run {Next}")]
    public static partial void ScheduledRunFinished(ILogger logger, string collector, int exitCode, NextRun next);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "{Collector}: scheduled run failed unexpectedly; the schedule carries on")]
    public static partial void ScheduledRunFailed(ILogger logger, Exception exception, string collector);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning, Message = "{Collector}: skipped a scheduled run because the previous one is still going")]
    public static partial void ScheduledRunSkipped(ILogger logger, string collector);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "Stopped serving.")]
    public static partial void Stopped(ILogger logger);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Published to CISO Assistant: {Sent} sent, {Skipped} skipped, {Failed} failed")]
    public static partial void Published(ILogger logger, int sent, int skipped, int failed);

    [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "Uploaded {File} to {Folder} (attested: {Attested})")]
    public static partial void Uploaded(ILogger logger, string file, string folder, bool attested);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "{File} is already uploaded as {Existing}; nothing copied")]
    public static partial void AlreadyUploaded(ILogger logger, string file, string existing);

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
