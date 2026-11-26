using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.CisoAssistant;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Review;
using EvidenceHound.Runtime;
using EvidenceHound.Runtime.Collectors;
using EvidenceHound.Runtime.Configuration;
using EvidenceHound.Runtime.Scheduling;
using EvidenceHound.Runtime.Status;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound next</c> (s18-01): one read-only answer to "what needs doing now", most urgent first, each with the <c>hound</c> command and
/// skill that handles it and about how long it takes. It changes no file and writes nothing to CISO Assistant; without a connection it
/// lists what it can and says the connection is unknown. It never prints evidence content or a secret.
/// </summary>
internal static class NextCommand
{
    public static Command Build(Terminal terminal, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var logger = loggers.CreateLogger("hound.next");
        var repo = new Option<DirectoryInfo>("--repo") { Description = "The compliance repo. Default: the current folder." };
        var config = new Option<FileInfo?>("--config") { Description = "The deployment config. Default: hound.yaml in the repo." };
        var json = new Option<bool>("--json") { Description = "Answer in JSON, for an agent." };
        var next = new Command("next", "What needs doing now, most urgent first, with the command for each: the review queue, collectors, manual sources, expiring exceptions, scope changes not yet applied and the CISO Assistant connection. Reads only; prompt-free, for an agent.")
        {
            repo, config, json,
        };
        next.SetAction(async (parseResult, cancellationToken) =>
        {
            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var file = parseResult.GetValue(config) ?? new FileInfo(Path.Combine(root, "hound.yaml"));
            var today = AuditSeries.Today(configuration);
            var now = configuration[AuditSeries.TodaySetting] is { Length: > 0 } ? new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero) : DateTimeOffset.UtcNow;
            var gathered = await GatherAsync(root, file, terminal, loggers, configuration, registry, today, now, cancellationToken);
            return await gathered.Match(
                async view =>
                {
                    await terminal.Output.WriteAsync((parseResult.GetValue(json) ? Json(view, today) : Text(view)).AsMemory(), cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        return next;
    }

    private sealed record NextView(IReadOnlyList<NextItem> Items, HoundHealth.Connection Connection);

    private static async Task<Result<NextView>> GatherAsync(
        string root, FileInfo file, Terminal terminal, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var loaded = await ConfigCommands.LoadAsync(file, registry, cancellationToken);
        if (loaded is not Result<DeploymentConfig>.Ok { Value: var deployment })
        {
            return Result.Fail<NextView>(loaded is Result<DeploymentConfig>.Fail { Errors: var errors } ? errors : []);
        }

        var items = new List<NextItem>();
        var queue = await ReviewCommand.QueueAsync(root, file, terminal, registry, today, cancellationToken, configuration);
        if (queue is Result<IReadOnlyList<ReviewItem>>.Fail { Errors: var queueErrors })
        {
            return Result.Fail<NextView>(queueErrors);
        }

        items.AddRange(NextWork.Review(((Result<IReadOnlyList<ReviewItem>>.Ok)queue).Value, today));
        // A manual source waiting for its upload is listed once, for the upload: its collector fails or never ran for the same reason.
        var manual = (await HoundHealth.ManualSourcesAsync(deployment, today, cancellationToken)).Select(m => m.Item).OfType<NextItem>().ToList();
        items.AddRange(manual);
        items.AddRange((await CollectorsAsync(deployment, now, cancellationToken))
            .Where(c => c.Urgency != NextUrgency.Broken || !manual.Any(m => m.Subject == c.Subject)));

        var scope = new DirectoryInfo(Path.Combine(root, "scope"));
        var exceptions = await StatusCommand.ExceptionsAsync(scope, today, cancellationToken);
        items.AddRange(exceptions.Match(
            notes => notes.Select(n => NextWork.Exception(n, today)),
            problems => [ScopeProblems(problems.Count)]));

        var (reached, options) = await HoundHealth.ConnectionAsync(configuration, file, registry, cancellationToken);
        if (reached.Reachable is false || reached.Token == "rejected")
        {
            items.Add(new NextItem(NextUrgency.Broken, "connection", "CISO Assistant", reached.Note, "hound config validate hound.yaml --check-secrets", 10) { Skill = "operate-collectors" });
        }
        else if (reached.Reachable is true && options is not null)
        {
            reached = await PlanAsync(scope, file, options, loggers, registry, today, items, reached, cancellationToken);
        }
        else
        {
            reached = reached with { Note = $"{reached.Note} Scope changes not yet applied are not listed." };
        }

        return Result.Ok(new NextView(NextWork.Order(items), reached));
    }

    /// <summary>Plans the scope against CISO Assistant and adds what is not yet applied; a scope with problems is listed for fixing instead.</summary>
    private static async Task<HoundHealth.Connection> PlanAsync(
        DirectoryInfo scope, FileInfo file, CisoAssistantOptions options, ILoggerFactory loggers, CollectorRegistry registry, DateOnly today, List<NextItem> items, HoundHealth.Connection reached, CancellationToken cancellationToken)
    {
        if (!scope.Exists)
        {
            return reached;
        }

        var checkedScope = await ScopeCommands.ValidateAsync(scope, file, registry, today, cancellationToken);
        if (checkedScope.Problems.Count > 0)
        {
            items.Add(ScopeProblems(checkedScope.Problems.Count));
            return reached;
        }

        var plan = await ScopeCommands.PendingPlanAsync(checkedScope, options, loggers, cancellationToken);
        return plan.Match(
            p =>
            {
                if (NextWork.Scope(p.Changes.Count, p.Drift.Count) is { } pending)
                {
                    items.Add(pending);
                }

                return reached;
            },
            problems => reached with { Note = $"{reached.Note} Could not plan the scope: {string.Join(" ", problems.Select(x => x.Code))}." });
    }

    private static NextItem ScopeProblems(int count) => new(
        NextUrgency.Broken,
        "scope",
        "scope files",
        string.Create(CultureInfo.InvariantCulture, $"The scope has {count} problem(s), so nothing in it can be planned or applied."),
        "hound scope validate scope --config hound.yaml",
        15)
    {
        Skill = "review-before-apply",
    };

    /// <summary>Each configured collector that never ran, failed, or missed its schedule, from the local run records.</summary>
    private static async Task<IReadOnlyList<NextItem>> CollectorsAsync(DeploymentConfig deployment, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var latest = await new FileRunRecordStore(deployment.Evidence.Root).LatestAsync(cancellationToken);
        var byName = latest.Match(records => records.ToDictionary(r => r.Collector, StringComparer.Ordinal), _ => new Dictionary<string, RunRecord>(StringComparer.Ordinal));
        return [.. deployment.Collectors.Select(c =>
        {
            var last = byName.GetValueOrDefault(c.Name);
            var due = last is null ? null : CronSchedule.Parse(c.Schedule, deployment.Scheduler.TimeZone).Match(s => s.NextAfter(last.EndedAt), _ => null);
            return NextWork.Collector(c.Name, last?.EndedAt, last?.Outcome == CollectorOutcome.Failed, [.. last?.Problems.Select(p => p.Message) ?? []], due, now);
        }).OfType<NextItem>()];
    }

    private static string Json(NextView view, DateOnly today) => new JsonObject
    {
        ["today"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["connection"] = new JsonObject
        {
            ["reachable"] = view.Connection.Reachable,
            ["token"] = view.Connection.Token,
            ["token_expiry"] = null,
            ["note"] = view.Connection.Note,
        },
        ["minutes"] = view.Items.Sum(i => i.Minutes),
        ["items"] = new JsonArray([.. view.Items.Select(i => new JsonObject
        {
            ["urgency"] = Name(i.Urgency),
            ["kind"] = i.Kind,
            ["subject"] = i.Subject,
            ["detail"] = i.Detail,
            ["due"] = i.Due?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["minutes"] = i.Minutes,
            ["command"] = i.Command,
            ["skill"] = i.Skill,
        })]),
    }.ToJsonString(CommandJson.Readable) + Environment.NewLine;

    private static string Text(NextView view)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"CISO Assistant: {view.Connection.Note}");
        if (view.Items.Count == 0)
        {
            return text.AppendLine("Nothing needs doing now.").ToString();
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"{view.Items.Count} thing(s) to do, about {view.Items.Sum(i => i.Minutes)} minutes, most urgent first:");
        foreach (var (item, n) in view.Items.Select((item, n) => (item, n + 1)))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{n}. [{Name(item.Urgency)}] {item.Kind} {item.Subject}: {item.Detail}")
                .AppendLine(CultureInfo.InvariantCulture, $"   {item.Command}{(item.Skill is null ? "" : $" (skill {item.Skill})")}, about {item.Minutes} min");
        }

        return text.ToString();
    }

    private static string Name(NextUrgency urgency) => urgency switch
    {
        NextUrgency.Overdue => "overdue",
        NextUrgency.Broken => "broken",
        NextUrgency.DueSoon => "due_soon",
        _ => "waiting",
    };
}
