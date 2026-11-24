using System.CommandLine;
using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Review;
using EvidenceHound.Runtime;
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
            var queue = await QueueAsync(root, parseResult.GetValue(config), terminal, registry, AuditSeries.Today(configuration), cancellationToken);
            var made = queue.Bind(items => ReviewOrder.Recommend(items, itemKey, parseResult.GetValue(answer), parseResult.GetValue(reason), parseResult.GetValue(source) ?? []));
            return await made.Match(
                async recommendation =>
                {
                    await Recommendations.WriteAsync(root, new Dictionary<string, ReviewRecommendation>(StringComparer.Ordinal) { [itemKey] = recommendation }, [], cancellationToken);
                    await terminal.Output.WriteLineAsync((parseResult.GetValue(json)
                        ? new JsonObject { ["key"] = itemKey, ["answer"] = recommendation.Answer, ["recorded"] = true }.ToJsonString(OnboardAgentCommand.Indented)
                        : $"Recorded your recommendation for {itemKey}: {recommendation.Answer}. The person decides in hound review.").AsMemory(), cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });
        var forTime = new Option<string>("--for") { Description = "How long you have, such as 60m or 2h. The session takes the most urgent items that fit.", DefaultValueFactory = _ => "60m" };
        var noApply = new Option<bool>("--no-apply") { Description = "Stop once the accepted items are written to the repo's files; do not plan or apply them to CISO Assistant." };
        var review = new Command("review", "One guided session: read each item that waits on you, with the agent's recommendation, then accept, challenge or skip it. Accepted items are written and applied in one batch. Needs a person at a terminal.")
        {
            repo, config, forTime, noApply, pending, recommend,
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
            var session = new Session(root, deployment, terminal, registry, AuditSeries.Today(configuration), new SystemClock());
            var done = await session.RunAsync(minutes, cancellationToken);
            if (done is Result<Unit>.Fail { Errors: var problems })
            {
                return Log.Problems(logger, problems);
            }

            if (parseResult.GetValue(noApply) || session.Accepted.Count == 0)
            {
                return ExitCodes.Ok;
            }

            await terminal.Output.WriteLineAsync("\nWhat applying the accepted items changes in CISO Assistant:".AsMemory(), cancellationToken);
            return await ScopeCommands.PlanThenApplyAsync(
                new DirectoryInfo(Path.Combine(root, "scope")), deployment.Exists ? deployment : null, configuration, registry, loggers, terminal.Output,
                async ct => (await new Prompt(terminal).AskAsync("Apply these changes to CISO Assistant now (y, n)", ct))?.ToLowerInvariant() == "y",
                cancellationToken);
        });
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
        string repo, FileInfo? config, Terminal terminal, CollectorRegistry registry, DateOnly today, CancellationToken cancellationToken)
    {
        var listing = new ApproveCommand.Listing();
        var walked = await new ApproveCommand.Walk(repo, terminal, new Prompt(terminal), today, listing).RunAsync(cancellationToken);
        var deployment = config ?? new FileInfo(Path.Combine(repo, "hound.yaml"));
        var standing = await StandingApprovalFile.ReadAsync(repo, cancellationToken);
        var manual = deployment.Exists
            ? await ManualProposalCommands.PendingAsync(deployment, registry, standing, today, cancellationToken)
            : Result.Ok<IReadOnlyList<ReviewItem>>([]);
        var challenges = await Challenges.ReadAsync(repo, cancellationToken);
        var recommendations = await Recommendations.ReadAsync(repo, cancellationToken);
        return walked.Bind(_ => manual).Map(proposals => ReviewOrder.Order([.. listing.Items, .. proposals])
                .Select(i => i with { Challenge = challenges.GetValueOrDefault(i.Key), Recommendation = recommendations.GetValueOrDefault(i.Key) ?? i.Recommendation }).ToArray())
            .Map(items => (IReadOnlyList<ReviewItem>)items);
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
            ["challenge"] = i.Challenge is { } c ? new JsonObject { ["note"] = c.Note, ["by"] = c.By, ["on"] = c.On } : null,
            ["covered_by"] = i.CoveredBy is { } a
                ? new JsonObject { ["rule"] = a.Rule, ["source"] = a.Source, ["approved_by"] = a.ApprovedBy, ["expires"] = StandingApprovalFile.Day(a.Expires) }
                : null,
        })]),
    }.ToJsonString(OnboardAgentCommand.Indented);

    private static string Text(IReadOnlyList<ReviewItem> items) => items.Count == 0
        ? "Nothing is waiting on a person."
        : string.Create(CultureInfo.InvariantCulture, $"{items.Count} item(s), about {items.Sum(i => i.Minutes)} minutes:\n")
            + string.Join('\n', items.Select(i => $"- {i.Title.Split('\n')[0]}"));

    /// <summary>
    /// One guided session: the most urgent items that fit the time, each read and answered by the person, then the accepted ones written as
    /// <c>hound approve</c> and <c>hound manual approve</c> write them, challenges kept for the agent, and one sign-off record.
    /// </summary>
    private sealed class Session(string repo, FileInfo deployment, Terminal terminal, CollectorRegistry registry, DateOnly today, Core.IClock clock)
    {
        private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);
        private readonly List<(string Key, string Note)> _challenged = [];
        private readonly List<string> _skipped = [];
        private readonly List<string> _standing = [];
        private readonly Prompt _prompt = new(terminal);
        private string? _person;
        private bool _quit;
        private Dictionary<string, ReviewRecommendation> _recommendations = new(StringComparer.Ordinal);

        public IReadOnlyList<string> Accepted => [.. _answers.Keys];

        public async Task<Result<Unit>> RunAsync(int minutes, CancellationToken cancellationToken)
        {
            _recommendations = await Recommendations.ReadAsync(repo, cancellationToken);
            var queue = await QueueAsync(repo, deployment, terminal, registry, today, cancellationToken);
            if (queue is Result<IReadOnlyList<ReviewItem>>.Fail failed)
            {
                return Result.Fail<Unit>(failed.Errors);
            }

            var all = queue.Match(q => q, _ => []);
            if (all.Count == 0)
            {
                await terminal.Output.WriteLineAsync("Nothing is waiting on you.".AsMemory(), cancellationToken);
                return Result.Ok(Unit.Value);
            }

            var covered = all.Where(i => i.CoveredBy is not null).ToArray();
            foreach (var item in covered)
            {
                _answers[item.Key] = "attest";
                _standing.Add(item.Key);
            }

            var chosen = ReviewOrder.Within([.. all.Where(i => i.CoveredBy is null)], minutes);
            await SayAsync(string.Create(CultureInfo.InvariantCulture, $"{all.Count} item(s) wait on you, about {all.Sum(i => i.Minutes)} minutes. This session takes {chosen.Count}, about {chosen.Sum(i => i.Minutes)} minutes, most urgent first."), cancellationToken);
            if (covered.Length > 0)
            {
                await SayAsync(string.Create(CultureInfo.InvariantCulture, $"{covered.Length} item(s) are accepted by a standing approval you signed; they are listed in the summary."), cancellationToken);
            }

            _person = await _prompt.AskUntilAnsweredAsync("Your name, recorded with today's date on everything you accept", cancellationToken);
            if (_person is null)
            {
                return Result.Ok(Unit.Value);
            }

            for (var i = 0; i < chosen.Count && !_quit; i++)
            {
                await AskAsync(chosen[i], string.Create(CultureInfo.InvariantCulture, $"[{i + 1}/{chosen.Count}]"), cancellationToken);
            }

            // A re-attestation with the same checks and results can be covered from now on, for a year, if the person signs the rule.
            foreach (var source in chosen.Where(i => i.Renewable is not null && _answers.ContainsKey(i.Key)).Select(i => i.Renewable!).Distinct(StringComparer.Ordinal))
            {
                var answer = await _prompt.AskAsync(
                    $"Accept {source}'s future exports without review for 12 months, when they have the same checks and results as the last attestation (y, n)", cancellationToken);
                if (answer?.ToLowerInvariant() == "y")
                {
                    await StandingApprovalFile.AddAsync(repo, StandingApprovals.Unchanged(source, _person, today), cancellationToken);
                }
            }

            // Apply in one batch: approvals as hound approve writes them (asking about anything they unblock), then manual evidence.
            var walk = new ApproveCommand.Walk(repo, terminal, _prompt, today, new SessionApprovals(this));
            var walked = await walk.RunAsync(cancellationToken);
            if (walked is Result<Unit>.Fail { Errors: var problems })
            {
                return Result.Fail<Unit>(problems);
            }

            var manual = _answers.Keys.Where(k => k.StartsWith("manual_evidence:", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
            if (manual.Count > 0 && deployment.Exists)
            {
                var attested = await ManualProposalCommands.AttestAsync(deployment, registry, manual, _person, clock, cancellationToken);
                if (attested is Result<IReadOnlyList<string>>.Fail { Errors: var errors })
                {
                    return Result.Fail<Unit>(errors);
                }
            }

            await Challenges.WriteAsync(repo, _challenged, _answers.Keys, _person, today, cancellationToken);
            await Recommendations.WriteAsync(repo, new Dictionary<string, ReviewRecommendation>(StringComparer.Ordinal), _answers.Keys, cancellationToken);
            var record = await SignOffAsync(cancellationToken);
            var rules = await StandingApprovalFile.ReadAsync(repo, cancellationToken);
            var summary = string.Create(
                CultureInfo.InvariantCulture,
                $"Accepted {_answers.Count}{(_standing.Count > 0 ? $" ({_standing.Count} by standing approval)" : "")}, challenged {_challenged.Count}, skipped {_skipped.Count}. Sign-off recorded in {record}.{(_challenged.Count > 0 ? " Your agent answers each challenge before the next session." : "")}");
            if (rules.Count > 0)
            {
                summary += $"\nStanding approvals: {string.Join("; ", rules.Select(StandingApprovalFile.Describe))}.";
            }

            await terminal.Output.WriteLineAsync(summary.AsMemory(), cancellationToken);
            return Result.Ok(Unit.Value);
        }

        /// <summary>Shows one item and records the person's answer. Items a walk raises that were not in the queue are asked the same way.</summary>
        public async Task AskAsync(ReviewItem item, string position, CancellationToken cancellationToken)
        {
            item = item with { Recommendation = _recommendations.GetValueOrDefault(item.Key) ?? item.Recommendation };
            await SayAsync($"\n{position} {item.Title}", cancellationToken);
            await SayAsync($"  Why it matters: {item.Why}", cancellationToken);
            if (item.Recommendation is { } r)
            {
                await SayAsync($"  Recommended: {r.Answer}. {r.Reasons}{(r.Sources.Count > 0 ? $" (sources: {string.Join(", ", r.Sources)})" : "")}".TrimEnd(), cancellationToken);
            }

            if (item.Challenge is { } c)
            {
                await SayAsync($"  Challenged by {c.By} on {c.On}: {c.Note}", cancellationToken);
            }

            await SayAsync($"  If you accept: {item.Effect}", cancellationToken);
            string[] accept = item.Kind == ReviewKind.SupplierReview ? ["ok", "warning", "blocker"] : ["a"];
            var question = item.Kind == ReviewKind.SupplierReview ? "Conclude it (ok, warning, blocker; c challenge, s skip, q quit)" : "a accept, c challenge, s skip, q quit";
            while (true)
            {
                var answer = (await _prompt.AskAsync(question, cancellationToken))?.ToLowerInvariant();
                if (answer is null or "q")
                {
                    _quit = true;
                    return;
                }

                if (accept.Contains(answer, StringComparer.Ordinal))
                {
                    _answers[item.Key] = answer == "a" ? "y" : answer;
                    return;
                }

                if (answer == "s")
                {
                    _skipped.Add(item.Key);
                    return;
                }

                if (answer == "c")
                {
                    var note = await _prompt.AskUntilAnsweredAsync("Your question or objection, for the agent to answer", cancellationToken);
                    _challenged.Add((item.Key, note ?? ""));
                    return;
                }
            }
        }

        public string Person => _person!;

        public bool Decided(string key) => _answers.ContainsKey(key) || _skipped.Contains(key) || _challenged.Any(c => c.Key == key);

        public string? Answer(string key) => _answers.GetValueOrDefault(key);

        public bool Quit => _quit;

        private async Task<string> SignOffAsync(CancellationToken cancellationToken)
        {
            var at = clock.UtcNow;
            var relative = $"review/sign-offs/{at.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}.yaml";
            var file = Path.Combine(repo, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            static string List(IEnumerable<string> keys) => string.Concat(keys.Select(k => $"\n  - {Quote(k)}"));
            var text = $"# One review session: who signed off, when, and every item they accepted.\nperson: {Quote(_person!)}\non: \"{today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\"\nat: \"{at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}\"\n"
                + $"accepted:{(_answers.Count == 0 ? " []" : List(_answers.Keys))}\n"
                + $"challenged:{(_challenged.Count == 0 ? " []" : string.Concat(_challenged.Select(c => $"\n  - key: {Quote(c.Key)}\n    note: {Quote(c.Note)}")))}\n"
                + $"skipped:{(_skipped.Count == 0 ? " []" : List(_skipped))}\n"
                + $"standing:{(_standing.Count == 0 ? " []" : List(_standing))}\n";
            await File.WriteAllTextAsync(file, text, cancellationToken);
            return relative;
        }

        private Task SayAsync(string line, CancellationToken cancellationToken) => terminal.Error.WriteLineAsync(line.AsMemory(), cancellationToken);
    }

    /// <summary>The session's answers to the walk: what the person accepted, and anything newly unblocked, asked there and then.</summary>
    private sealed class SessionApprovals(Session session) : ApproveCommand.IApprovals
    {
        public Task<string?> ApproverAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(session.Person);

        public async Task<ApproveCommand.Verdict> DecideAsync(ReviewItem item, string question, string[] answers, CancellationToken cancellationToken)
        {
            if (!session.Decided(item.Key) && !session.Quit)
            {
                await session.AskAsync(item, "[now ready]", cancellationToken);
            }

            return new ApproveCommand.Verdict(session.Answer(item.Key) is { } answer && answers.Contains(answer, StringComparer.Ordinal) ? answer : null);
        }
    }

    /// <summary>A YAML scalar in double quotes when it needs them.</summary>
    private static string Quote(string text) =>
        text.Length > 0 && text.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' or '/') && !char.IsWhiteSpace(text[0]) ? text : JsonValue.Create(text).ToJsonString();
}
