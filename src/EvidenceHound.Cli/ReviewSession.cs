using System.Globalization;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Review;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;
using Prompt = EvidenceHound.Cli.OnboardSystemCommand.Prompt;

namespace EvidenceHound.Cli;

/// <summary>
/// One guided session: the most urgent items that fit the time, each read and answered by the person, then the accepted ones written as
/// <c>hound approve</c> and <c>hound manual approve</c> write them, challenges kept for the agent, and one sign-off record.
/// </summary>
internal sealed class ReviewSession(string repo, FileInfo deployment, Terminal terminal, CollectorRegistry registry, DateOnly today, Core.IClock clock, IConfiguration configuration)
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

    public int Challenged => _challenged.Count;

    /// <summary>The keys the person named with <c>--only</c>: just these are asked. Empty: the whole queue, as time allows.</summary>
    public IReadOnlyList<string> Only { get; init; } = [];

    public bool Wanted(string key) => Only.Count == 0 || Only.Contains(key);

    public async Task<Result<Unit>> RunAsync(int minutes, CancellationToken cancellationToken)
    {
        _recommendations = await Recommendations.ReadAsync(repo, cancellationToken);
        var queue = await ReviewCommand.QueueAsync(repo, deployment, terminal, registry, today, cancellationToken, configuration);
        if (queue is Result<IReadOnlyList<ReviewItem>>.Fail failed)
        {
            return Result.Fail<Unit>(failed.Errors);
        }

        var all = queue.Match(q => q, _ => []);
        if (Only.FirstOrDefault(k => all.All(i => i.Key != k)) is { } unknown)
        {
            return Result.Fail<Unit>(new Problem(
                "review.item_unknown",
                $"'{unknown}' is not waiting on a person. Nearest: {string.Join(", ", ReviewOrder.Nearest(all.Select(i => i.Key), unknown))}. List them all with hound review pending --json."));
        }

        all = [.. all.Where(i => Wanted(i.Key))];
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
        var walk = new ApproveCommand.Walk(repo, terminal, _prompt, today, new ReviewSessionApprovals(this));
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
        var question = item.Kind switch
        {
            ReviewKind.SupplierReview => "Conclude it (ok, warning, blocker; c challenge, s skip, q quit)",
            ReviewKind.Finding => "a acknowledge, c challenge, s skip, q quit",
            _ => "a accept, c challenge, s skip, q quit",
        };
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

    public DateOnly Today => today;

    public bool Decided(string key) => _answers.ContainsKey(key) || _skipped.Contains(key) || _challenged.Any(c => c.Key == key);

    public string? Answer(string key) => _answers.GetValueOrDefault(key);

    public bool Quit => _quit;

    private async Task<string> SignOffAsync(CancellationToken cancellationToken)
    {
        var at = clock.UtcNow;
        var relative = $"review/sign-offs/{at.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}.yaml";
        var file = Path.Combine(repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        static string List(IEnumerable<string> keys) => string.Concat(keys.Select(k => $"\n  - {ReviewCommand.Quote(k)}"));
        var text = $"# One review session: who signed off, when, and every item they accepted.\nperson: {ReviewCommand.Quote(_person!)}\non: \"{today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\"\nat: \"{at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}\"\n"
            + $"accepted:{(_answers.Count == 0 ? " []" : List(_answers.Keys))}\n"
            + $"challenged:{(_challenged.Count == 0 ? " []" : string.Concat(_challenged.Select(c => $"\n  - key: {ReviewCommand.Quote(c.Key)}\n    note: {ReviewCommand.Quote(c.Note)}")))}\n"
            + $"skipped:{(_skipped.Count == 0 ? " []" : List(_skipped))}\n"
            + $"standing:{(_standing.Count == 0 ? " []" : List(_standing))}\n";
        await File.WriteAllTextAsync(file, text, cancellationToken);
        return relative;
    }

    private Task SayAsync(string line, CancellationToken cancellationToken) => terminal.Error.WriteLineAsync(line.AsMemory(), cancellationToken);
}

/// <summary>The session's answers to the walk: what the person accepted, and anything newly unblocked, asked there and then.</summary>
internal sealed class ReviewSessionApprovals(ReviewSession session) : ApproveCommand.IApprovals
{
    public Task<string?> ApproverAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(session.Person);

    public async Task<ApproveCommand.Verdict> DecideAsync(ReviewItem item, string question, string[] answers, CancellationToken cancellationToken)
    {
        if (!session.Decided(item.Key) && !session.Quit && session.Wanted(item.Key) && ReviewOrder.Ready(item, session.Today))
        {
            await session.AskAsync(item, "[now ready]", cancellationToken);
        }

        return new ApproveCommand.Verdict(session.Answer(item.Key) is { } answer && answers.Contains(answer, StringComparer.Ordinal) ? answer : null);
    }
}
