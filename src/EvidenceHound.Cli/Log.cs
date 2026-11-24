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

    private static readonly EventId InstanceProblemEvent = new(3001, "InstanceProblem");

    /// <summary>Logs each problem with one collector instance at Warning: the instance will report unable to assess, the others run.</summary>
    public static void InstanceProblems(ILogger logger, string collector, IEnumerable<Problem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        foreach (var problem in problems)
        {
            var named = new Problem(problem.Code, $"{collector}: {problem.Message} Its checks will be unable to assess until this is fixed.");
            logger.Log(LogLevel.Warning, InstanceProblemEvent, new ProblemState(named), null, static (state, _) => state.Problem.Message);
        }
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

    [LoggerMessage(EventId = 1009, Level = LogLevel.Warning, Message = "{Message}")]
    public static partial void Warning(ILogger logger, string message);

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

    [LoggerMessage(EventId = 1300, Level = LogLevel.Information, Message = "Wrote the plan's snapshot to {File}: draft SHA-256 {Draft}, snapshot SHA-256 {Snapshot}")]
    public static partial void SspSnapshotWritten(ILogger logger, string file, string draft, string snapshot);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Version {Version} is signed, but what CISO Assistant holds now is not the reviewed draft; printing a draft. Rebuild the signed version with --from-snapshot.")]
    public static partial void SspSignedButChanged(ILogger logger, string version);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Warning, Message = "No audit report attached: {Reason}")]
    public static partial void SspReportMissing(ILogger logger, string reason);

    [LoggerMessage(EventId = 1303, Level = LogLevel.Information, Message = "Published version {Version} of the plan to evidence {Evidence} as {File}")]
    public static partial void SspPublished(ILogger logger, string version, string evidence, string file);
}
