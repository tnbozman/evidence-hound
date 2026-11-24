using System.CommandLine;
using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Review;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Prompt = EvidenceHound.Cli.OnboardSystemCommand.Prompt;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound review</c>: the one place a person signs off. <c>hound review pending --json</c> lists everything that waits on a person, in one
/// shape, for their agent to prepare; the session walks them through it. Design: docs/design/guided-review.md.
/// </summary>
internal static class ReviewCommand
{
    public static Command Build(Terminal terminal, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.review");
        var repo = new Option<DirectoryInfo>("--repo") { Description = "The config repo to work in. Default: the current folder." };
        var config = new Option<FileInfo?>("--config") { Description = "The deployment config, for manual evidence proposals. Default: hound.yaml in the repo, when there is one." };
        var json = new Option<bool>("--json") { Description = "Answer in JSON, for an agent." };
        var pending = new Command("pending", "List everything that waits on a person, most urgent first, with the agent's recommendation where one is drafted. Prompt-free, for an agent.")
        {
            repo, config, json,
        };
        pending.SetAction(async (parseResult, cancellationToken) =>
        {
            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var queue = await QueueAsync(root, parseResult.GetValue(config), terminal, registry, AuditSeries.Today(configuration), cancellationToken);
            return await queue.Match(
                async items =>
                {
                    await terminal.Output.WriteLineAsync((parseResult.GetValue(json) ? Json(items) : Text(items)).AsMemory(), cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return new Command("review", "Review and sign off everything that waits on a person, in one guided session.") { pending };
    }

    /// <summary>Every item waiting on a person, most urgent first: what <c>hound approve</c> would ask, and each pending manual evidence proposal.</summary>
    internal static async Task<Result<IReadOnlyList<ReviewItem>>> QueueAsync(
        string repo, FileInfo? config, Terminal terminal, CollectorRegistry registry, DateOnly today, CancellationToken cancellationToken)
    {
        var listing = new ApproveCommand.Listing();
        var walked = await new ApproveCommand.Walk(repo, terminal, new Prompt(terminal), today, listing).RunAsync(cancellationToken);
        var deployment = config ?? new FileInfo(Path.Combine(repo, "hound.yaml"));
        var manual = deployment.Exists
            ? await ManualProposalCommands.PendingAsync(deployment, registry, cancellationToken)
            : Result.Ok<IReadOnlyList<ReviewItem>>([]);
        return walked.Bind(_ => manual).Map(proposals => ReviewOrder.Order([.. listing.Items, .. proposals]));
    }

    private static string Json(IReadOnlyList<ReviewItem> items) => new JsonObject
    {
        ["minutes"] = items.Sum(i => i.Minutes),
        ["items"] = new JsonArray([.. items.Select(i => (JsonNode)new JsonObject
        {
            ["kind"] = ReviewOrder.Code(i.Kind),
            ["key"] = i.Key,
            ["title"] = i.Title,
            ["why"] = i.Why,
            ["effect"] = i.Effect,
            ["minutes"] = i.Minutes,
            ["deadline"] = i.Deadline?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["file"] = i.File,
            ["recommendation"] = i.Recommendation is { } r
                ? new JsonObject { ["answer"] = r.Answer, ["reasons"] = r.Reasons, ["sources"] = new JsonArray([.. r.Sources.Select(s => (JsonNode)s)]) }
                : null,
        })]),
    }.ToJsonString(OnboardAgentCommand.Indented);

    private static string Text(IReadOnlyList<ReviewItem> items) => items.Count == 0
        ? "Nothing is waiting on a person."
        : string.Create(CultureInfo.InvariantCulture, $"{items.Count} item(s), about {items.Sum(i => i.Minutes)} minutes:\n")
            + string.Join('\n', items.Select(i => $"- {i.Title.Split('\n')[0]}"));
}
