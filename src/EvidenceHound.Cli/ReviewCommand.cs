using System.CommandLine;
using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Review;
using EvidenceHound.Core.Scope;
using EvidenceHound.Runtime;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        var repo = CommonOptions.Repo();
        var config = new Option<FileInfo?>("--config") { Description = "The deployment config, for manual evidence proposals. Default: hound.yaml in the repo, when there is one." };
        var json = CommonOptions.Json();
        var pending = new Command("pending", "List everything that waits on a person, most urgent first, with the agent's recommendation where one is drafted. Prompt-free, for an agent.")
        {
            repo, config, json,
        };
        pending.SetAction(async (parseResult, cancellationToken) =>
        {
            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var queue = await QueueAsync(root, parseResult.GetValue(config), terminal, registry, AuditSeries.Today(configuration), cancellationToken, configuration);
            return await queue.Match(
                async items =>
                {
                    await terminal.Output.WriteLineAsync((parseResult.GetValue(json) ? Json(items) : Text(items)).AsMemory(), cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        var answer = new Option<string?>("--answer") { Description = "What you recommend: accept or decline; for a supplier review, ok, warning or blocker." };
        var reason = new Option<string?>("--reason") { Description = "Why, in words the person can check." };
        var source = new Option<string[]>("--source") { Description = "A file, page or extract the recommendation rests on. Repeat for more.", AllowMultipleArgumentsPerToken = false };
        var key = new Argument<string>("key") { Description = "The item's key, as hound review pending --json lists it." };
        var recommend = new Command("recommend", "Record your recommendation for an item waiting on a person, with its reason and sources, for the session to show. Prompt-free, for an agent; it decides nothing.")
        {
            key, repo, config, answer, reason, source, json,
        };
        recommend.SetAction(async (parseResult, cancellationToken) =>
        {
            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var itemKey = parseResult.GetValue(key)!;
            var queue = await QueueAsync(root, parseResult.GetValue(config), terminal, registry, AuditSeries.Today(configuration), cancellationToken, configuration);
            var made = queue.Bind(items => ReviewOrder.Recommend(items, itemKey, parseResult.GetValue(answer), parseResult.GetValue(reason), parseResult.GetValue(source) ?? []));
            return await made.Match(
                async recommendation =>
                {
                    await Recommendations.WriteAsync(root, new Dictionary<string, ReviewRecommendation>(StringComparer.Ordinal) { [itemKey] = recommendation }, [], cancellationToken);
                    await terminal.Output.WriteLineAsync((parseResult.GetValue(json)
                        ? new JsonObject { ["key"] = itemKey, ["answer"] = recommendation.Answer, ["recorded"] = true }.ToJsonString(CommandJson.Readable)
                        : $"Recorded your recommendation for {itemKey}: {recommendation.Answer}. The person decides in hound review.").AsMemory(), cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        var prepare = new Command("prepare", "Get the week's session ready in one step: refresh the queue, list the items still without a recommendation and the skill that drafts each, and save the scope plan the session applies. Reads only; prompt-free, for an agent.")
        {
            repo, config, json,
        };
        prepare.SetAction(async (parseResult, cancellationToken) =>
        {
            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var file = parseResult.GetValue(config) ?? new FileInfo(Path.Combine(root, "hound.yaml"));
            var today = AuditSeries.Today(configuration);
            var queue = await QueueAsync(root, parseResult.GetValue(config), terminal, registry, today, cancellationToken, configuration);
            if (queue is Result<IReadOnlyList<ReviewItem>>.Fail { Errors: var errors })
            {
                return Log.Problems(logger, errors);
            }

            var items = ((Result<IReadOnlyList<ReviewItem>>.Ok)queue).Value;
            var connection = await CisoAssistantConnection.ResolveAsync(configuration, file.Exists ? file : null, registry, cancellationToken);
            var plan = await connection.Match(
                options => ScopeCommands.PrepareAsync(
                    new DirectoryInfo(Path.Combine(root, "scope")), file.Exists ? file : null, options, ScopeCommands.PreparedPlan(root), today, registry, loggers, cancellationToken),
                problems => Task.FromResult((0, (string?)null, $"Not planned: {string.Join(" ", problems.Select(p => p.Message))}")));
            await terminal.Output.WriteLineAsync((parseResult.GetValue(json) ? PreparedJson(items, plan) : PreparedText(items, plan)).AsMemory(), cancellationToken);
            return ExitCodes.Ok;
        });
        var forTime = new Option<string>("--for") { Description = "How long you have, such as 60m or 2h. The session takes the most urgent items that fit.", DefaultValueFactory = _ => "60m" };
        var only = new Option<string[]>("--only") { Description = "Ask only this item, by its key from hound review pending --json, once you have talked it through with your agent. Repeat for more." };
        var noApply = new Option<bool>("--no-apply") { Description = "Stop once the accepted items are written to the repo's files; do not plan or apply them to CISO Assistant." };
        var review = new Command("review", "One guided session: read each item that waits on you, with the agent's recommendation, then accept, challenge or skip it. Accepted items are written and applied in one batch. Needs a person at a terminal.")
        {
            repo, config, forTime, only, noApply, pending, prepare, recommend,
        };
        review.SetAction(async (parseResult, cancellationToken) =>
        {
            if (!terminal.Interactive)
            {
                Log.Usage(logger, "hound review needs a person at a terminal: sign-off is never piped in or run by an agent. Run it yourself, for example in the VS Code terminal.");
                return ExitCodes.Usage;
            }

            if (Minutes(parseResult.GetValue(forTime)) is not { } minutes)
            {
                return Log.Problems(logger, [new Problem("review.time_invalid", $"--for '{parseResult.GetValue(forTime)}' is not a time; write it as minutes or hours, such as 60m or 2h.")]);
            }

            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var deployment = parseResult.GetValue(config) ?? new FileInfo(Path.Combine(root, "hound.yaml"));
            var session = new ReviewSession(root, deployment, terminal, registry, AuditSeries.Today(configuration), new SystemClock(), configuration)
            {
                Only = [.. parseResult.GetValue(only) ?? []],
            };
            var started = DateTimeOffset.UtcNow;
            var done = await session.RunAsync(minutes, cancellationToken);
            if (done is Result<Unit>.Fail { Errors: var problems })
            {
                return Log.Problems(logger, problems);
            }

            var (exit, applied) = parseResult.GetValue(noApply) ? (ExitCodes.Ok, "nothing applied (--no-apply)") : await ApplyAsync(root, deployment, session, cancellationToken);
            await terminal.Output.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"\nSession summary: accepted {session.Accepted.Count}, challenged {session.Challenged}; CISO Assistant: {applied}; took {Math.Max(1, (int)Math.Ceiling((DateTimeOffset.UtcNow - started).TotalMinutes))} minute(s).").AsMemory(), cancellationToken);
            return exit;
        });

        // The prepared plan when the scope files are as it read them (no accepted item changed them); otherwise plan again, which includes
        // both the accepted items and whatever the prepared plan held.
        async Task<(int Exit, string Applied)> ApplyAsync(string root, FileInfo deployment, ReviewSession session, CancellationToken cancellationToken)
        {
            var prepared = ScopeCommands.PreparedPlan(root);
            var state = await ScopeCommands.SavedStateAsync(prepared, registry, cancellationToken);
            async Task<bool> Confirm(CancellationToken ct) => (await new Prompt(terminal).AskAsync("Apply these changes to CISO Assistant now (y, n)", ct))?.ToLowerInvariant() == "y";
            if (state is { FilesUnchanged: true } unchanged)
            {
                var connection = await CisoAssistantConnection.ResolveAsync(configuration, deployment.Exists ? deployment : null, registry, cancellationToken);
                if (connection is not Result<CisoAssistantOptions>.Ok { Value: var options })
                {
                    return (Log.Problems(logger, connection is Result<CisoAssistantOptions>.Fail { Errors: var e } ? e : []), "nothing applied");
                }

                var changes = (await File.ReadAllTextAsync(prepared, cancellationToken)) is var text && SavedPlanFile.Read(text, prepared) is Result<SavedPlan>.Ok { Value: var saved } ? saved.Plan.Changes.Count : 0;
                await terminal.Output.WriteLineAsync($"\nWhat the plan prepared on {unchanged.Day} changes in CISO Assistant:".AsMemory(), cancellationToken);
                var exit = await ScopeCommands.ApplyPreparedAsync(prepared, options, registry, loggers, terminal.Output, Confirm, cancellationToken);
                return (exit, exit == ExitCodes.Ok && !File.Exists(prepared)
                    ? string.Create(CultureInfo.InvariantCulture, $"{changes} change(s) applied to CISO Assistant")
                    : exit == ExitCodes.Ok ? "nothing applied (you said no)" : "nothing applied (see above)");
            }

            if (state is not null)
            {
                File.Delete(prepared);
            }

            if (session.Accepted.Count == 0)
            {
                return (ExitCodes.Ok, "nothing to apply");
            }

            await terminal.Output.WriteLineAsync("\nWhat applying the accepted items changes in CISO Assistant:".AsMemory(), cancellationToken);
            var planned = await ScopeCommands.PlanThenApplyAsync(
                new DirectoryInfo(Path.Combine(root, "scope")), deployment.Exists ? deployment : null, configuration, registry, loggers, terminal.Output, Confirm, cancellationToken);
            return (planned, planned == ExitCodes.Ok ? "the accepted changes, as planned above" : "nothing applied (see above)");
        }
        return review;
    }

    /// <summary>Minutes from 60m, 2h or a plain number of minutes.</summary>
    internal static int? Minutes(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        var t when t.EndsWith('h') && int.TryParse(t[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var h) && h > 0 => h * 60,
        var t when t.EndsWith('m') && int.TryParse(t[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var m) && m > 0 => m,
        var t when int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 => n,
        _ => null,
    };

    /// <summary>Every item waiting on a person, most urgent first: what <c>hound approve</c> would ask, and each pending manual evidence proposal.</summary>
    internal static async Task<Result<IReadOnlyList<ReviewItem>>> QueueAsync(
        string repo, FileInfo? config, Terminal terminal, CollectorRegistry registry, DateOnly today, CancellationToken cancellationToken, IConfiguration? configuration = null)
    {
        var listing = new ApproveCommand.Listing();
        var walked = await new ApproveCommand.Walk(repo, terminal, new Prompt(terminal), today, listing).RunAsync(cancellationToken);
        var deployment = config ?? new FileInfo(Path.Combine(repo, "hound.yaml"));
        var standing = await StandingApprovalFile.ReadAsync(repo, cancellationToken);
        var manual = deployment.Exists
            ? await ManualProposalCommands.PendingAsync(deployment, registry, standing, today, cancellationToken)
            : Result.Ok<IReadOnlyList<ReviewItem>>([]);
        var findings = await FindingsAsync(repo, today, registry, cancellationToken);
        var reports = configuration is null ? [] : await IncidentReportsAsync(repo, configuration, registry, today, cancellationToken);
        var challenges = await Challenges.ReadAsync(repo, cancellationToken);
        var recommendations = await Recommendations.ReadAsync(repo, cancellationToken);
        return walked.Bind(_ => manual).Map(proposals => ReviewOrder.Order([.. listing.Items, .. proposals, .. findings, .. reports]).Where(i => ReviewOrder.Ready(i, today))
                .Select(i => i with { Challenge = challenges.GetValueOrDefault(i.Key), Recommendation = recommendations.GetValueOrDefault(i.Key) ?? i.Recommendation }).ToArray())
            .Map(items => (IReadOnlyList<ReviewItem>)items);
    }

    /// <summary>
    /// Each report an open incident in CISO Assistant still owes under <c>scope/reporting.yaml</c> (s15-07), due by its deadline: listed every
    /// week until the incident's timeline records it. None when the scope declares no obligations or CISO Assistant is not configured.
    /// </summary>
    private static async Task<IReadOnlyList<ReviewItem>> IncidentReportsAsync(
        string repo, IConfiguration configuration, CollectorRegistry registry, DateOnly today, CancellationToken cancellationToken)
    {
        var scope = new DirectoryInfo(Path.Combine(repo, "scope"));
        var reporting = await RepoScope.ReportingAsync(scope, cancellationToken);
        if (reporting.Obligations.Count == 0 || CisoAssistantConnection.FromSettings(configuration) is not Result<CisoAssistantOptions>.Ok { Value: var options })
        {
            return [];
        }

        var checkedScope = await RepoScope.ValidateAsync(scope, null, registry, today, cancellationToken);
        var systems = checkedScope.Scope.Setup.Domains.SelectMany(d => d.Systems).ToList();
        using var services = new ServiceCollection().AddLogging().AddCisoAssistantClient(options).BuildServiceProvider();
        var open = await services.GetRequiredService<IIncidentSource>().OpenAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return open.Match(
            incidents => (IReadOnlyList<ReviewItem>)[.. incidents.SelectMany(i => ReportingObligations.Owed(reporting.Obligations, systems, i, now)
                .Where(r => r.ReportedAt is null)
                .Select(r => new ReviewItem(
                    ReviewKind.Finding,
                    $"incident_report:{i.RefId}/{r.Obligation.Id}",
                    $"[incident] {i.RefId} {i.Name}: {r.Obligation.Name} to {r.Obligation.ReportTo}, {(r.Overdue ? "overdue since" : "due by")} {r.Due.ToUniversalTime():yyyy-MM-dd HH:mm} UTC",
                    $"Owed because {r.Why}. The clock started when the incident was discovered.",
                    $"Nothing changes; your sign-off records that you saw it this week. Make the report, then add a timeline entry to the incident: '{ReportingObligations.ReportedPrefix}{r.Obligation.Id}, <their reference>'.",
                    2)
                {
                    Deadline = DateOnly.FromDateTime(r.Due.UtcDateTime),
                    File = Path.Combine(scope.FullName, "reporting.yaml"),
                }))],
            _ => []);
    }

    /// <summary>The scope's warnings, such as sensitive data held where nothing approves it: each listed every week until it is fixed.</summary>
    private static async Task<IReadOnlyList<ReviewItem>> FindingsAsync(string repo, DateOnly today, CollectorRegistry registry, CancellationToken cancellationToken)
    {
        var scope = new DirectoryInfo(Path.Combine(repo, "scope"));
        if (!File.Exists(Path.Combine(scope.FullName, "setup.yaml")))
        {
            return [];
        }

        var checkedScope = await RepoScope.ValidateAsync(scope, null, registry, today, cancellationToken);
        return [.. checkedScope.Warnings.Select(w => new ReviewItem(
            ReviewKind.Finding,
            string.Create(CultureInfo.InvariantCulture, $"finding:{w.Code}/{Path.GetFileName(w.At.File)}:{w.At.Line}"),
            $"[finding] {w.Message}",
            ReviewOrder.FindingWhy(w.Code),
            $"Nothing changes; your sign-off records that you saw it this week. To clear it: {w.Hint}",
            2)
        {
            File = w.At.File,
        })];
    }

    private static string Json(IReadOnlyList<ReviewItem> items) => new JsonObject
    {
        ["minutes"] = items.Sum(i => i.Minutes),
        ["high"] = items.Count(i => ReviewOrder.Risk(i) == "high"),
        ["summary"] = new JsonArray([.. ReviewOrder.Summary(items).Select(g => (JsonNode)new JsonObject
        {
            ["kind"] = ReviewOrder.Code(g.Kind),
            ["recommendation"] = g.Recommendation,
            ["count"] = g.Count,
            ["minutes"] = g.Minutes,
            ["high"] = g.High,
        })]),
        ["items"] = new JsonArray([.. items.Select(i => (JsonNode)new JsonObject
        {
            ["kind"] = ReviewOrder.Code(i.Kind),
            ["key"] = i.Key,
            ["title"] = i.Title,
            ["why"] = i.Why,
            ["effect"] = i.Effect,
            ["minutes"] = i.Minutes,
            ["risk"] = ReviewOrder.Risk(i),
            ["deadline"] = i.Deadline?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["file"] = i.File,
            ["recommendation"] = i.Recommendation is { } r
                ? new JsonObject { ["answer"] = r.Answer, ["reasons"] = r.Reasons, ["sources"] = new JsonArray([.. r.Sources.Select(s => (JsonNode)s)]) }
                : null,
            ["challenge"] = i.Challenge is { } c ? new JsonObject { ["note"] = c.Note, ["by"] = c.By, ["on"] = c.On } : null,
            ["covered_by"] = i.CoveredBy is { } a
                ? new JsonObject { ["rule"] = a.Rule, ["source"] = a.Source, ["approved_by"] = a.ApprovedBy, ["expires"] = StandingApprovalFile.Day(a.Expires) }
                : null,
        })]),
    }.ToJsonString(CommandJson.Readable);

    private static string PreparedJson(IReadOnlyList<ReviewItem> items, (int Changes, string? Saved, string Note) plan) => new JsonObject
    {
        ["items"] = items.Count,
        ["minutes"] = items.Sum(i => i.Minutes),
        ["without_recommendation"] = new JsonArray([.. Bare(items).Select(i => (JsonNode)new JsonObject
        {
            ["key"] = i.Key,
            ["kind"] = ReviewOrder.Code(i.Kind),
            ["title"] = i.Title,
            ["skill"] = ReviewOrder.Skill(i),
        })]),
        ["challenged"] = new JsonArray([.. items.Where(i => i.Challenge is not null).Select(i => (JsonNode)i.Key)]),
        ["plan"] = new JsonObject { ["changes"] = plan.Changes, ["saved"] = plan.Saved, ["note"] = plan.Note },
    }.ToJsonString(CommandJson.Readable);

    private static string PreparedText(IReadOnlyList<ReviewItem> items, (int Changes, string? Saved, string Note) plan)
    {
        var bare = Bare(items);
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"{items.Count} item(s) wait on the person, about {items.Sum(i => i.Minutes)} minutes; {bare.Count} still need a recommendation:"),
        };
        lines.AddRange(bare.Select(i => $"- {i.Key}: skill {ReviewOrder.Skill(i)}"));
        lines.Add(plan.Saved is null ? $"Scope plan: {plan.Note}" : string.Create(CultureInfo.InvariantCulture, $"Scope plan: {plan.Changes} change(s), saved in {plan.Saved}. {plan.Note}"));
        return string.Join('\n', lines);
    }

    /// <summary>Items the agent still has to recommend on; a standing approval or the item's own draft already answers the rest.</summary>
    private static List<ReviewItem> Bare(IReadOnlyList<ReviewItem> items) => [.. items.Where(i => i.Recommendation is null && i.CoveredBy is null)];

    private static string Text(IReadOnlyList<ReviewItem> items) => items.Count == 0
        ? "Nothing is waiting on a person."
        : string.Create(CultureInfo.InvariantCulture, $"{items.Count} item(s), about {items.Sum(i => i.Minutes)} minutes:\n")
            + string.Join('\n', items.Select(i => $"- {i.Title.Split('\n')[0]}"));

    /// <summary>A YAML scalar in double quotes when it needs them.</summary>
    internal static string Quote(string text) =>
        text.Length > 0 && text.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' or '/') && !char.IsWhiteSpace(text[0]) ? text : JsonValue.Create(text).ToJsonString();
}
