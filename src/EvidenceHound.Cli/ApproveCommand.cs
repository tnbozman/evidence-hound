using System.CommandLine;
using System.Globalization;
using EvidenceHound.Cli.Setup;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Onboarding.Decisions;
using EvidenceHound.Onboarding.Sessions;
using EvidenceHound.Onboarding.Suppliers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Prompt = EvidenceHound.Cli.OnboardSystemCommand.Prompt;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound approve</c>: walks a person through everything drafted in the config repo that needs their approval, in turn: environment
/// capabilities, suppliers and their reviews, not-applicable decisions, then each system an agent drafted. It needs a person at a terminal,
/// records who approved each item and when in the repo's files, and never calls CISO Assistant: <c>hound scope apply</c> writes the approved state.
/// </summary>
internal static class ApproveCommand
{
    private static readonly string[] Conclusions = ["ok", "warning", "blocker"];

    public static Command Build(Terminal terminal, ILoggerFactory loggers, IConfiguration configuration)
    {
        var logger = loggers.CreateLogger("hound.approve");
        var repo = new Option<DirectoryInfo>("--repo") { Description = "The config repo to work in. Default: the current folder." };
        var command = new Command("approve", "Approve what is drafted in the config repo, one item at a time. Needs a person at a terminal; an agent or a pipe is refused.") { repo };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            if (!terminal.Interactive)
            {
                Log.Usage(logger, "hound approve needs a person at a terminal: approvals are never piped in or run by an agent. Run it yourself, for example in the VS Code terminal.");
                return ExitCodes.Usage;
            }

            var walk = new Walk(parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory(), terminal, new Prompt(terminal), AuditSeries.Today(configuration));
            var walked = await walk.RunAsync(cancellationToken);
            return walked.Match(_ => ExitCodes.Ok, problems => Log.Problems(logger, problems));
        });
        return command;
    }

    /// <summary>One run through the pending approvals: who is approving, what was asked and approved, and what still waits.</summary>
    private sealed class Walk(string repo, Terminal terminal, Prompt prompt, DateOnly today)
    {
        private readonly List<string> _waiting = [];
        private string? _approver;
        private int _asked;
        private int _approved;
        private bool _stopped;

        private string Day => today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public async Task<Result<Unit>> RunAsync(CancellationToken cancellationToken)
        {
            var walked = await CapabilitiesAsync(cancellationToken);
            walked = await walked.BindAsync(_ => SuppliersAsync(cancellationToken));
            walked = await walked.BindAsync(_ => SystemsAsync(cancellationToken));
            if (walked is Result<Unit>.Ok)
            {
                var waiting = string.Concat(_waiting.Select(w => $"\n{w}"));
                await terminal.Output.WriteLineAsync((_asked == 0
                    ? $"Nothing is waiting for approval.{waiting}"
                    : $"Approved {_approved} of {_asked}.{waiting}\nNext: review the changes in a pull request, then run 'hound scope validate scope', 'hound scope plan scope' and 'hound scope apply scope'. Apply writes what you approved to CISO Assistant.").AsMemory(), cancellationToken);
            }

            return walked;
        }

        /// <summary>Shows one item and asks for one of <paramref name="answers"/>. Null when the person declines; the walk stops on s or when input ends.</summary>
        private async Task<string?> AskAsync(string item, string question, string[] answers, CancellationToken cancellationToken)
        {
            if (_stopped)
            {
                return null;
            }

            if (_approver is null)
            {
                _approver = await prompt.AskUntilAnsweredAsync("Your name, recorded with today's date on everything you approve", cancellationToken);
                if (_approver is null)
                {
                    _stopped = true;
                    return null;
                }
            }

            _asked++;
            await terminal.Error.WriteLineAsync($"\n{item}".AsMemory(), cancellationToken);
            while (true)
            {
                var answer = (await prompt.AskAsync($"{question} ({string.Join(", ", answers)}; n no, s stop)", cancellationToken))?.ToLowerInvariant();
                if (answer is null or "s")
                {
                    _stopped = true;
                    _asked--;
                    return null;
                }

                if (answer == "n")
                {
                    return null;
                }

                if (answers.Contains(answer, StringComparer.Ordinal))
                {
                    _approved++;
                    return answer;
                }
            }
        }

        private Task<string?> ApproveAsync(string item, string question, CancellationToken cancellationToken) => AskAsync(item, question, ["y"], cancellationToken);

        /// <summary>Environment capabilities with no approver: the person's name and today's date are written to scope/capabilities.yaml.</summary>
        private async Task<Result<Unit>> CapabilitiesAsync(CancellationToken cancellationToken)
        {
            var file = Path.Combine(repo, "scope", "capabilities.yaml");
            if (!File.Exists(file))
            {
                return Result.Ok(Unit.Value);
            }

            var yaml = await File.ReadAllTextAsync(file, cancellationToken);
            var map = ScopeLoader.Parse(Setup, null, [], capabilities: new ScopeSource(file, yaml)).Scope.Capabilities;
            foreach (var capability in (map?.Mitigations ?? []).Where(m => string.IsNullOrWhiteSpace(m.ApprovedBy)))
            {
                var provided = capability.ProvidedBy == CapabilityProvision.System ? $"provided by {string.Join(", ", capability.Systems.Select(s => s.Value))}" : $"provided by: {capability.ProvidedBy.ToString().ToLowerInvariant()}";
                var item = $"[capability] {capability.Mitigation} {capability.Name}: {provided}. {capability.Reason}".TrimEnd();
                if (await ApproveAsync(item, "Approve this capability decision", cancellationToken) is null)
                {
                    continue;
                }

                var path = $"mitigations[mitigation={capability.Mitigation}]";
                var edited = YamlEdit.SetScalar(yaml, $"{path}.approved_by", _approver!).Bind(text => YamlEdit.SetScalar(text, $"{path}.approved_on", Day));
                if (edited is not Result<string>.Ok { Value: var changed })
                {
                    return Failed(edited, "scope/capabilities.yaml");
                }

                yaml = changed;
                await File.WriteAllTextAsync(file, yaml, cancellationToken);
            }

            return Result.Ok(Unit.Value);
        }

        /// <summary>
        /// Suppliers: a draft contract is the supplier waiting for approval, and approving makes it active. Then each open review of an approved
        /// supplier is concluded ok, warning or blocker, recorded in the supplier's file for <c>hound scope apply</c> to write.
        /// </summary>
        private async Task<Result<Unit>> SuppliersAsync(CancellationToken cancellationToken)
        {
            var folder = Path.Combine(repo, "scope", "third-parties");
            foreach (var file in Directory.Exists(folder) ? Directory.GetFiles(folder, "*.yaml").Order(StringComparer.Ordinal).ToArray() : [])
            {
                var relative = $"scope/third-parties/{Path.GetFileName(file)}";
                var yaml = await File.ReadAllTextAsync(file, cancellationToken);
                if (Entity(file, yaml) is not { } drafted)
                {
                    continue;
                }

                foreach (var contract in drafted.Contracts.Where(c => c.Status == "draft"))
                {
                    var levels = string.Join(", ", new[] { ("dependency", drafted.Dependency), ("penetration", drafted.Penetration), ("maturity", drafted.Maturity), ("trust", drafted.Trust) }
                        .Where(l => l.Item2 is not null).Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.Item1} {l.Item2}")));
                    var item = $"[supplier] {drafted.RefId} {drafted.Name}: contract {contract.RefId} ({contract.Name}) is a draft, covering {string.Join(", ", contract.Solutions.Select(s => s.Value))}."
                        + $"{(levels.Length > 0 ? $" Levels: {levels}." : "")} Read {relative} before you answer.";
                    if (await ApproveAsync(item, $"Approve {drafted.RefId} and make the contract active", cancellationToken) is null)
                    {
                        continue;
                    }

                    var path = $"contracts[ref_id={contract.RefId}]";
                    var edited = YamlEdit.ReplaceScalar(yaml, $"{path}.status", "active")
                        .Bind(text => YamlEdit.SetScalar(text, $"{path}.approved_by", _approver!))
                        .Bind(text => YamlEdit.SetScalar(text, $"{path}.approved_on", Day));
                    if (edited is not Result<string>.Ok { Value: var changed })
                    {
                        return Failed(edited, relative);
                    }

                    yaml = changed;
                    await File.WriteAllTextAsync(file, yaml, cancellationToken);
                }

                if (Entity(file, yaml) is not { } entity || entity.Contracts.All(c => c.Status != "active"))
                {
                    continue;
                }

                foreach (var assessment in entity.Assessments)
                {
                    var reviews = ScopeDesired.Reviews(new ScopeDocument(new ScopeSetup("Global", [], []), [], []) { ThirdParties = [entity with { Assessments = [assessment] }] }, today);
                    foreach (var review in reviews.Where(r => !r.Fields.ContainsKey("conclusion")))
                    {
                        var item = $"[review] {review.Key} of {entity.RefId} {entity.Name}, due {review.Fields.GetValueOrDefault("due_date") ?? "on no set date"}. Conclude it only once the review is done.";
                        if (await AskAsync(item, "Conclude this review", Conclusions, cancellationToken) is not { } result)
                        {
                            continue;
                        }

                        var edited = YamlEdit.AppendItem(yaml, $"assessments[ref_id={assessment.RefId}].conclusions", YamlMap.Of(
                            ("review", new YamlText(review.Key)), ("result", new YamlText(result)), ("by", new YamlText(_approver!)), ("on", new YamlText(Day))));
                        if (edited is not Result<string>.Ok { Value: var changed })
                        {
                            return Failed(edited, relative);
                        }

                        yaml = changed;
                        await File.WriteAllTextAsync(file, yaml, cancellationToken);
                    }
                }
            }

            return Result.Ok(Unit.Value);
        }

        /// <summary>
        /// Each system an agent drafted: its not-applicable decisions one by one, then the system as a whole, which writes its audit. A system
        /// waits while its supplier is not approved, a not-applicable decision is declined, or requirements are still undecided.
        /// </summary>
        private async Task<Result<Unit>> SystemsAsync(CancellationToken cancellationToken)
        {
            var folder = Path.Combine(repo, "onboarding");
            foreach (var file in Directory.Exists(folder) ? Directory.GetFiles(folder, "session.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray() : [])
            {
                if (OnboardSessionJson.Read(await File.ReadAllTextAsync(file, cancellationToken)) is not Result<OnboardSession>.Ok { Value: var session } || session.Written.Count > 0 || session.Drafts.Count == 0)
                {
                    continue;
                }

                var settings = new OnboardSystemCommand.Settings(
                    session.System, repo, Path.Combine(repo, session.Framework ?? Path.Combine("frameworks", "essential-eight-2023.json")), session.Groups, session.Specs, NoModel: true, Show: false, Config: "");
                foreach (var draft in session.Drafts.Where(d => d.Kind == DecisionKind.NotApplicable).ToArray())
                {
                    var title = session.Proposals.FirstOrDefault(p => p.Key == draft.Key)?.Title;
                    var item = $"[not applicable] {session.System} {draft.Key}: {title}\n  Reasoning: {draft.Reasoning}\n  Justification: {draft.Justification}"
                        + (draft.Sources.Count > 0 ? $"\n  Sources: {string.Join(", ", draft.Sources)}" : "");
                    if (await ApproveAsync(item, "Approve that this requirement is not applicable", cancellationToken) is not null)
                    {
                        session = session.Decide(Applicability.Approve(draft, _approver!, today));
                        await OnboardSystemCommand.SaveAsync(settings, session, cancellationToken);
                    }
                }

                var waits = new List<string>();
                if (session.Supplier is { } supplier && SupplierGate.Check(await OnboardSystemCommand.ThirdPartiesAsync(repo, cancellationToken), supplier) != SupplierStatus.Approved)
                {
                    waits.Add($"for {supplier.EntityRef} to be approved");
                }

                if (session.Drafts.Count(d => d.Kind == DecisionKind.NotApplicable) is > 0 and var open)
                {
                    waits.Add($"for {open} not-applicable decision(s) to be approved, or drafted again with 'hound onboard {session.System} decide'");
                }

                if (session.Pending.Count > 0)
                {
                    waits.Add($"for {session.Pending.Count} requirement(s) still to decide");
                }

                if (waits.Count > 0)
                {
                    _waiting.Add($"{session.System} waits {string.Join(", and ", waits)}.");
                    continue;
                }

                var decisions = session.Decisions;
                var lines = string.Concat(session.Drafts.Select(d => $"\n  {d.Key} {Applicability.Name(d.Kind)}{(d.OtherSystem is { } other ? $" ({other})" : "")}: {d.Reasoning}"));
                var summary = string.Create(CultureInfo.InvariantCulture, $"[system] {session.System} ({session.Product}, owner {session.Owner}): {decisions.Count} decisions drafted by an agent: {Count(decisions, DecisionKind.ApiCheck)} API check(s), {Count(decisions, DecisionKind.Manual)} manual, {Count(decisions, DecisionKind.NotApplicable)} not applicable, {Count(decisions, DecisionKind.OtherSystem)} for another system.{lines}");
                if (await ApproveAsync(summary, $"Approve these decisions and write the audit for {session.System}", cancellationToken) is null)
                {
                    continue;
                }

                foreach (var draft in session.Drafts.ToArray())
                {
                    session = session.Decide(Applicability.Approve(draft, _approver!, today));
                }

                session = session with { Reviewer = _approver };
                await OnboardSystemCommand.SaveAsync(settings, session, cancellationToken);
                if (await OnboardSystemCommand.WriteAsync(settings, session, prompt, terminal, today, cancellationToken) is Result<OnboardSession>.Fail { Errors: var problems })
                {
                    return Result.Fail<Unit>(problems);
                }
            }

            return Result.Ok(Unit.Value);
        }

        private static int Count(IReadOnlyList<Decision> decisions, DecisionKind kind) => decisions.Count(d => d.Kind == kind);

        private static ScopeSource Setup => new("setup.yaml", "organisation:\n  root_folder: Global\n");

        private static ScopeEntity? Entity(string file, string yaml) =>
            ScopeLoader.Parse(Setup, null, [], thirdParties: [new ScopeSource(file, yaml)]).Scope.ThirdParties is [var entity, ..] ? entity : null;

        private static Result<Unit> Failed(Result<string> edited, string file) =>
            Result.Fail<Unit>([.. ((Result<string>.Fail)edited).Errors.Select(p => new Problem(p.Code, $"{file}: {p.Message} Nothing more was approved."))]);
    }
}
