using System.CommandLine;
using System.Globalization;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Onboarding.Decisions;
using EvidenceHound.Onboarding.Output;
using EvidenceHound.Onboarding.Sessions;
using EvidenceHound.Onboarding.Specs;
using EvidenceHound.Onboarding.Suppliers;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound onboard &lt;system&gt;</c>: supplier first, then one decision per in-scope requirement. The session is saved after every answer in
/// <c>onboarding/&lt;system&gt;/session.json</c> and resumes where it stopped. It writes only into the config repo's working copy.
/// </summary>
internal static partial class OnboardSystemCommand
{
    internal const string Choices = "a accept, c API check, m manual, n not applicable, o another system, l accept all remaining proposals, s save and stop";

    public static void Attach(Command onboard, Terminal terminal, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger("hound.onboard");
        var system = new Argument<string?>("system") { Description = "The system to onboard, as an ID such as EXAMPLE-FIREWALL.", Arity = ArgumentArity.ZeroOrOne };
        var repo = CommonOptions.Repo();
        var framework = new Option<FileInfo>("--framework") { Description = "The framework snapshot to decide against. Default: frameworks/essential-eight-2023.json in the repo." };
        var groups = new Option<string[]>("--group") { Description = "An implementation group in scope, such as 2 for maturity level two. Repeat for more; none means every requirement.", AllowMultipleArgumentsPerToken = false };
        var specs = new Option<FileInfo[]>("--spec") { Description = "The system's API specification (OpenAPI, JSON or YAML). Repeat for more." };
        var retired = Retired.On(onboard);
        OnboardAgentCommand.Attach(onboard, system, terminal, loggers);
        foreach (var symbol in new Symbol[] { system, repo, framework, groups, specs })
        {
            switch (symbol)
            {
                case Argument argument:
                    onboard.Arguments.Add(argument);
                    break;
                case Option option:
                    onboard.Options.Add(option);
                    break;
            }
        }

        onboard.SetAction(async (parseResult, cancellationToken) =>
        {
            if (parseResult.GetValue(system) is not { Length: > 0 } name)
            {
                Log.Usage(logger, "Name the system to onboard: hound onboard <system> --framework <file> --spec <file>.");
                return ExitCodes.Usage;
            }

            await retired.NoteAsync(parseResult, terminal, cancellationToken);
            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var options = new Settings(
                name,
                root,
                parseResult.GetValue(framework)?.FullName ?? Path.Combine(root, "frameworks", "essential-eight-2023.json"),
                parseResult.GetValue(groups) ?? [],
                [.. (parseResult.GetValue(specs) ?? []).Select(s => s.FullName)]);
            return await RunAsync(options, terminal, logger, cancellationToken);
        });
    }

    internal sealed record Settings(string System, string Repo, string Framework, IReadOnlyList<string> Groups, IReadOnlyList<string> Specs)
    {
        public string Folder => Path.Combine(Repo, "onboarding", System);

        public string SessionFile => Path.Combine(Folder, "session.json");

        /// <summary>The framework's name as skills name it: the snapshot file's name, such as essential-eight-2023.</summary>
        public string FrameworkName => Path.GetFileNameWithoutExtension(Framework);
    }

    private static async Task<int> RunAsync(Settings settings, Terminal terminal, ILogger logger, CancellationToken cancellationToken)
    {
        if (!File.Exists(Path.Combine(settings.Repo, "scope", "setup.yaml")))
        {
            return Log.Problems(logger, [new Problem("onboard.not_a_repo", $"{settings.Repo} is not a compliance repo: it has no scope/setup.yaml. Run hound onboard in the repo 'hound init' wrote, or name it with --repo.")]);
        }

        var loaded = File.Exists(settings.SessionFile)
            ? OnboardSessionJson.Read(await File.ReadAllTextAsync(settings.SessionFile, cancellationToken))
            : Result.Ok(new OnboardSession(settings.System, OnboardStage.Supplier));
        if (loaded is Result<OnboardSession>.Fail { Errors: var sessionProblems })
        {
            return Log.Problems(logger, sessionProblems);
        }

        var prompt = new Prompt(terminal);
        var session = ((Result<OnboardSession>.Ok)loaded).Value;
        if (session.Written.Count > 0)
        {
            await terminal.Output.WriteLineAsync($"{settings.System} is onboarded and its files are already written: {string.Join(", ", session.Written)}. Move {Path.GetRelativePath(settings.Repo, settings.SessionFile)} aside to onboard it again.".AsMemory(), cancellationToken);
            return ExitCodes.Ok;
        }

        if (session.Drafts.Count > 0)
        {
            await terminal.Output.WriteLineAsync(
                $"{settings.System} has {session.Drafts.Count} decision(s) drafted by an agent. A person approves them, and the system, with 'hound approve' in a terminal; nothing is written until then.".AsMemory(),
                cancellationToken);
            return ExitCodes.Ok;
        }

        if (session.Supplier is null)
        {
            await terminal.Error.WriteAsync((OnboardGuide.Intro(settings.System) + (settings.Specs.Count == 0 ? OnboardGuide.NoSpec : "")).AsMemory(), cancellationToken);
            var started = await StartAsync(session, prompt, cancellationToken);
            if (started is null)
            {
                return ExitCodes.Ok;
            }

            session = started;
            await SaveAsync(settings, session, cancellationToken);
        }

        if (session.Stage is OnboardStage.Supplier or OnboardStage.AwaitingSupplier)
        {
            var supplier = await SupplierAsync(settings, session, prompt, terminal, cancellationToken);
            if (supplier is Result<OnboardSession>.Fail { Errors: var supplierProblems })
            {
                return Log.Problems(logger, supplierProblems);
            }

            session = ((Result<OnboardSession>.Ok)supplier).Value;
            await SaveAsync(settings, session, cancellationToken);
            if (session.Stage == OnboardStage.AwaitingSupplier)
            {
                var choice = session.Supplier!;
                await terminal.Output.WriteLineAsync(
                    $"{choice.EntityRef} is not approved yet. Review scope/third-parties/{SupplierFile.FileName(choice)}; once the supplier is approved, make contract {choice.ContractRef} active and run 'hound onboard {settings.System}' again to decide applicability.".AsMemory(),
                    cancellationToken);
                return ExitCodes.Ok;
            }
        }

        var decided = await ApplicabilityAsync(settings, session, prompt, terminal, cancellationToken);
        if (decided is Result<OnboardSession>.Ok { Value: { Stage: OnboardStage.Decided } done })
        {
            decided = await WriteAsync(settings, done, prompt, terminal, DateOnly.FromDateTime(DateTime.Today), cancellationToken);
        }

        return decided.Match(_ => ExitCodes.Ok, problems => Log.Problems(logger, problems));
    }

    /// <summary>The questions that start a session: who reviews, who owns the system, the product, and who supplies it. Null when input ended.</summary>
    private static async Task<OnboardSession?> StartAsync(OnboardSession session, Prompt prompt, CancellationToken cancellationToken)
    {
        string?[] answers =
        [
            await prompt.AskAsync(0, cancellationToken),
            await prompt.AskEmailAsync(1, cancellationToken),
            await prompt.AskAsync(2, cancellationToken),
            await prompt.AskRefIdAsync(RefIdKind.Supplier, 3, cancellationToken),
            await prompt.AskAsync(4, cancellationToken),
            await prompt.AskRefIdAsync(RefIdKind.Solution, 5, cancellationToken),
            await prompt.AskAsync(6, cancellationToken),
            await prompt.AskRefIdAsync(RefIdKind.Contract, 7, cancellationToken),
            await prompt.AskAsync(8, cancellationToken),
        ];
        if (answers.Any(a => string.IsNullOrWhiteSpace(a)))
        {
            return null;
        }

        return session with
        {
            Reviewer = answers[0],
            Owner = answers[1],
            Product = answers[2],
            Supplier = new SupplierChoice(answers[3]!, answers[4]!, answers[5]!, answers[6]!, answers[7]!, answers[8]!),
        };
    }

    private static async Task<Result<OnboardSession>> SupplierAsync(
        Settings settings, OnboardSession session, Prompt prompt, Terminal terminal, CancellationToken cancellationToken)
    {
        var choice = session.Supplier!;
        var status = SupplierGate.Check(await ThirdPartiesAsync(settings.Repo, cancellationToken), choice);
        if (status != SupplierStatus.NotDeclared || session.Stage == OnboardStage.AwaitingSupplier)
        {
            return Result.Ok(session.Advance(status));
        }

        var file = Path.Combine(settings.Repo, "scope", "third-parties", SupplierFile.FileName(choice));
        if (File.Exists(file))
        {
            return Result.Fail<OnboardSession>(new Problem("onboard.supplier_file_exists", $"{file} exists but does not declare {choice.EntityRef}. Correct its ref_id, or choose another supplier ref_id."));
        }

        await prompt.HintAsync(9, cancellationToken);
        var provides = await prompt.AskAsync(OnboardGuide.Question(9, choice.EntityName, settings.System), cancellationToken) ?? "";
        var kept = new List<KeptDocument>();
        await prompt.HintAsync(10, cancellationToken);
        while (await prompt.AskAsync(OnboardGuide.Question(10), cancellationToken) is { Length: > 0 } where)
        {
            var document = await KeepAsync(settings.Repo, choice, where, cancellationToken);
            if (document is Result<KeptDocument>.Ok { Value: var got })
            {
                kept.Add(got);
                await terminal.Error.WriteLineAsync($"Kept {got.Path} (SHA-256 {got.Sha256}).".AsMemory(), cancellationToken);
            }
            else
            {
                await terminal.Error.WriteLineAsync(string.Join("; ", ((Result<KeptDocument>.Fail)document).Errors.Select(e => e.Message)).AsMemory(), cancellationToken);
            }
        }

        // The levels are the person's to decide, here or later in the supplier file; a coding assistant drafts them with 'hound supplier draft'.
        var levels = await LevelsAsync(prompt, terminal, session.Reviewer ?? "the reviewer", cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var place = (await DomainsAsync(settings, cancellationToken)) is [{ Perimeters: [var perimeter, ..] } domain, ..] ? new AuditPlace(domain.Name, perimeter.Name) : null;
        await File.WriteAllTextAsync(file, SupplierFile.Write(choice, levels, settings.System, DateOnly.FromDateTime(DateTime.Today), place), cancellationToken);
        await terminal.Error.WriteLineAsync($"Drafted {file} with a draft contract{(place is null ? "" : " and a review for the GRC team to conclude in CISO Assistant")}.".AsMemory(), cancellationToken);
        return Result.Ok((session with { SupplierLevels = levels, SupplierProvides = provides.Length > 0 ? provides : null, Documents = [.. session.Documents, .. kept] }).Advance(SupplierStatus.NotApproved));
    }

    /// <summary>
    /// Each level, asked after its scale is printed: a whole number on the scale and why, or empty to leave it to decide in the file. Stops
    /// asking once the input has ended.
    /// </summary>
    private static async Task<IReadOnlyList<LevelProposal>> LevelsAsync(Prompt prompt, Terminal terminal, string reviewer, CancellationToken cancellationToken)
    {
        var levels = new List<LevelProposal>();
        foreach (var (name, low, high) in SupplierProposal.Scales)
        {
            await terminal.Error.WriteAsync(SupplierScales.Describe(name).AsMemory(), cancellationToken);
            while (await prompt.AskAsync($"{name} ({low} to {high}; empty to decide later)", cancellationToken) is var answer)
            {
                if (answer is null)
                {
                    return levels;
                }

                if (answer.Length == 0)
                {
                    break;
                }

                if (!int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < low || value > high)
                {
                    await terminal.Error.WriteLineAsync($"'{answer}' is not a whole number from {low} to {high}.".AsMemory(), cancellationToken);
                    continue;
                }

                if (await prompt.AskUntilAnsweredAsync($"Why {value}, in a sentence (kept as the reason)", cancellationToken) is not { } reason)
                {
                    return levels;
                }

                levels.Add(new LevelProposal(name, value, reason, [$"decided by {reviewer}"]));
                break;
            }
        }

        return levels;
    }

    /// <summary>Saves a vendor document under documents/suppliers/&lt;supplier&gt;/ with its hash.</summary>
    private static async Task<Result<KeptDocument>> KeepAsync(string repo, SupplierChoice choice, string where, CancellationToken cancellationToken)
    {
        byte[] content;
        Uri source;
        if (Uri.TryCreate(where, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
        {
            if (url.Scheme != Uri.UriSchemeHttps)
            {
                return Result.Fail<KeptDocument>(new Problem("onboard.document_not_https", $"{where}: only https documents are fetched."));
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail<KeptDocument>(new Problem("onboard.document_unreachable", $"{where}: {(int)response.StatusCode} {response.ReasonPhrase}."));
            }

            content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            source = url;
        }
        else if (File.Exists(where))
        {
            content = await File.ReadAllBytesAsync(where, cancellationToken);
            source = new Uri(Path.GetFullPath(where));
        }
        else
        {
            return Result.Fail<KeptDocument>(new Problem("onboard.document_not_found", $"{where} is neither an https URL nor a file."));
        }

        var name = Path.GetFileName(source.AbsolutePath) is { Length: > 0 } last ? last : "document.html";
        var relative = Path.Combine("documents", "suppliers", choice.EntityRef.ToLowerInvariant(), name).Replace('\\', '/');
        var path = Path.Combine(repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
        return Result.Ok(KeptDocument.Of(source, relative, content, DateTimeOffset.UtcNow));
    }

    private static async Task<Result<OnboardSession>> ApplicabilityAsync(
        Settings settings, OnboardSession session, Prompt prompt, Terminal terminal, CancellationToken cancellationToken)
    {
        var summary = await ApiAsync(settings.Specs.Count > 0 ? settings.Specs : session.Specs, cancellationToken);
        if (summary is not Result<string>.Ok { Value: var api })
        {
            return Result.Fail<OnboardSession>(((Result<string>.Fail)summary).Errors);
        }

        if (api.Length > 0)
        {
            await terminal.Error.WriteLineAsync($"What the API offers:\n{api}\n".AsMemory(), cancellationToken);
        }

        if (session.Proposals.Count == 0)
        {
            var framework = FrameworkSnapshotJson.Read(await File.ReadAllTextAsync(settings.Framework, cancellationToken), settings.Framework);
            if (framework is not Result<FrameworkSnapshot>.Ok { Value: var snapshot })
            {
                return Result.Fail<OnboardSession>(((Result<FrameworkSnapshot>.Fail)framework).Errors);
            }

            var requirements = Applicability.InScope(snapshot, settings.Groups);
            session = session with
            {
                Framework = Path.GetRelativePath(settings.Repo, settings.Framework).Replace('\\', '/'),
                Groups = settings.Groups,
                Specs = [.. settings.Specs],
                Proposals = Applicability.Empty(requirements),
            };
            await SaveAsync(settings, session, cancellationToken);
        }

        session = await DecideAsync(settings, session, prompt, terminal, cancellationToken);
        var decisions = session.Decisions;
        await terminal.Output.WriteLineAsync((session.Stage == OnboardStage.Decided
            ? string.Create(CultureInfo.InvariantCulture, $"All {decisions.Count} requirements decided: {Count(decisions, DecisionKind.ApiCheck)} API check(s), {Count(decisions, DecisionKind.Manual)} manual, {Count(decisions, DecisionKind.NotApplicable)} not applicable, {Count(decisions, DecisionKind.OtherSystem)} for another system. The session is in {Path.GetRelativePath(settings.Repo, settings.SessionFile)}.")
            : string.Create(CultureInfo.InvariantCulture, $"Saved: {decisions.Count} of {session.Proposals.Count} requirements decided. Run the same command to carry on.")).AsMemory(), cancellationToken);
        return Result.Ok(session);
    }

    /// <summary>What the system's API offers, summarised from its specifications; empty when none are given.</summary>
    internal static async Task<Result<string>> ApiAsync(IReadOnlyList<string> specs, CancellationToken cancellationToken)
    {
        var summaries = new List<ApiSummary>();
        foreach (var spec in specs)
        {
            if (!File.Exists(spec))
            {
                return Result.Fail<string>(new Problem("onboard.spec_missing", $"{spec} does not exist."));
            }

            var read = SpecReader.Read(await File.ReadAllTextAsync(spec, cancellationToken), Path.GetFileName(spec));
            if (read is Result<ApiSummary>.Fail { Errors: var problems })
            {
                return Result.Fail<string>(problems);
            }

            summaries.Add(((Result<ApiSummary>.Ok)read).Value);
        }

        return Result.Ok(string.Join("\n\n", summaries.Select(s => s.Text())));
    }

    private static int Count(IReadOnlyList<Decision> decisions, DecisionKind kind) => decisions.Count(d => d.Kind == kind);

    private static async Task<OnboardSession> DecideAsync(Settings settings, OnboardSession session, Prompt prompt, Terminal terminal, CancellationToken cancellationToken)
    {
        var reviewer = session.Reviewer ?? session.Owner ?? "";
        while (session.Pending is [var proposal, ..])
        {
            var position = session.Proposals.ToList().IndexOf(proposal) + 1;
            await terminal.Error.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"[{position}/{session.Proposals.Count}] {proposal.RefId ?? proposal.Key}: {proposal.Title}").AsMemory(), cancellationToken);
            await terminal.Error.WriteLineAsync((proposal.Kind is { } proposed
                ? $"  Proposed: {Applicability.Name(proposed)}{(proposal.OtherSystem is { } other ? $" ({other})" : "")}. {proposal.Reasoning}"
                : "  No proposal.").AsMemory(), cancellationToken);
            var answer = await prompt.AskAsync($"Decide ({Choices})", cancellationToken);
            var kind = answer switch
            {
                "a" => proposal.Kind,
                "c" => DecisionKind.ApiCheck,
                "m" => DecisionKind.Manual,
                "n" => DecisionKind.NotApplicable,
                "o" => DecisionKind.OtherSystem,
                _ => null,
            };
            if (answer is null or "s")
            {
                return session;
            }

            if (answer == "l")
            {
                foreach (var accepted in session.Pending.Where(p => p.Kind is { } k && k != DecisionKind.NotApplicable).ToArray())
                {
                    session = session.Decide(((Result<Decision>.Ok)Applicability.Decide(accepted, accepted.Kind!.Value, accepted.Reasoning, reviewer, null, null, accepted.OtherSystem)).Value);
                }

                await SaveAsync(settings, session, cancellationToken);
                continue;
            }

            if (kind is null)
            {
                await terminal.Error.WriteLineAsync((answer == "a" ? "There is no proposal to accept; choose c, m, n or o." : $"Choose one of: {Choices}.").AsMemory(), cancellationToken);
                continue;
            }

            var decision = await DecisionAsync(proposal, kind.Value, answer == "a", reviewer, prompt, cancellationToken);
            if (decision is null)
            {
                return session;
            }

            session = session.Decide(decision);
            await SaveAsync(settings, session, cancellationToken);
        }

        return session;
    }

    /// <summary>Asks what the decision needs: reasoning, and a justification and approver for not applicable, or the other system. Null when input ended.</summary>
    private static async Task<Decision?> DecisionAsync(Proposal proposal, DecisionKind kind, bool accepting, string reviewer, Prompt prompt, CancellationToken cancellationToken)
    {
        var reasoning = accepting ? proposal.Reasoning : await prompt.AskAsync(proposal.Reasoning.Length > 0 ? $"Reasoning [{proposal.Reasoning}]" : "Reasoning", cancellationToken);
        if (reasoning is null)
        {
            return null;
        }

        reasoning = reasoning.Length > 0 ? reasoning : proposal.Reasoning;
        string? justification = null, approver = null, other = accepting ? proposal.OtherSystem : null;
        if (kind == DecisionKind.NotApplicable)
        {
            justification = await prompt.AskUntilAnsweredAsync("Justification, kept for auditors", cancellationToken);
            approver = justification is null ? null : await prompt.AskUntilAnsweredAsync("Approved by", cancellationToken);
            if (approver is null)
            {
                return null;
            }
        }
        else if (kind == DecisionKind.OtherSystem && string.IsNullOrWhiteSpace(other))
        {
            other = await prompt.AskUntilAnsweredAsync("Which system meets it", cancellationToken);
            if (other is null)
            {
                return null;
            }
        }

        return Applicability.Decide(proposal, kind, reasoning, reviewer, justification, approver, other).Match<Decision?>(d => d, _ => null);
    }

    /// <summary>Questions on stderr, answers from the terminal's input; null once the input has ended, so a closed input stops rather than loops.</summary>
    internal sealed class Prompt(Terminal terminal)
    {
        public async Task<string?> AskAsync(string question, CancellationToken cancellationToken)
        {
            await terminal.Error.WriteAsync($"{question}: ".AsMemory(), cancellationToken);
            await terminal.Error.FlushAsync(cancellationToken);
            return await terminal.Input.ReadLineAsync(cancellationToken) is { } line ? Answers.Clean(line) : null;
        }

        /// <summary>The hint of an opening question (<see cref="OnboardGuide.Opening"/>): what it is for and an example, on a line before the question.</summary>
        public Task HintAsync(int opening, CancellationToken cancellationToken) =>
            terminal.Error.WriteLineAsync($"  {OnboardGuide.Opening[opening].Hint}".AsMemory(), cancellationToken);

        /// <summary>An opening question, after its hint.</summary>
        public async Task<string?> AskAsync(int opening, CancellationToken cancellationToken)
        {
            await HintAsync(opening, cancellationToken);
            return await AskAsync(OnboardGuide.Question(opening), cancellationToken);
        }

        public async Task<string?> AskEmailAsync(int opening, CancellationToken cancellationToken)
        {
            await HintAsync(opening, cancellationToken);
            return await AskEmailAsync(OnboardGuide.Question(opening), cancellationToken);
        }

        /// <summary>A ref ID, with its kind's prefix shown in the prompt and only the rest typed; asked again until it fits. Null once the input has ended.</summary>
        public async Task<string?> AskRefIdAsync(RefIdKind kind, int opening, CancellationToken cancellationToken)
        {
            await HintAsync(opening, cancellationToken);
            return await RefIdPrompt.AskAsync(terminal, kind, OnboardGuide.Question(opening), "", cancellationToken);
        }

        /// <summary>
        /// One of <paramref name="options"/>, listed in the question, the first by default; any other answer is asked again, so a "y" typed out
        /// of habit is never written (s15-D7). The default once the input has ended, as an empty answer would; null with no options.
        /// </summary>
        public async Task<string?> AskOneOfAsync(string question, IReadOnlyList<string> options, string what, CancellationToken cancellationToken)
        {
            if (options.Count == 0)
            {
                return null;
            }

            while (await AskAsync($"{question}, one of: {string.Join(", ", options)} [{options[0]}]", cancellationToken) is { } answer)
            {
                if (answer.Length == 0)
                {
                    return options[0];
                }

                if (options.FirstOrDefault(o => string.Equals(o, answer, StringComparison.OrdinalIgnoreCase)) is { } chosen)
                {
                    return chosen;
                }

                await terminal.Error.WriteLineAsync($"'{answer}' is not {what}. Press Enter for {options[0]}, or type one of the names listed.".AsMemory(), cancellationToken);
            }

            return options[0];
        }

        public async Task<string> AskWithDefaultAsync(string question, string @default, CancellationToken cancellationToken)
        {
            var answer = await AskAsync(@default.Length > 0 ? $"{question} [{@default}]" : question, cancellationToken);
            return string.IsNullOrEmpty(answer) ? @default : answer;
        }

        /// <summary>Asks until the answer is an email, because CISO Assistant matches people by email; null once the input has ended.</summary>
        public async Task<string?> AskEmailAsync(string question, CancellationToken cancellationToken)
        {
            while (await AskAsync(question, cancellationToken) is { } answer)
            {
                if (answer.Contains('@', StringComparison.Ordinal))
                {
                    return answer;
                }

                await terminal.Error.WriteLineAsync($"'{answer}' is not an email. Give the email of a user in CISO Assistant.".AsMemory(), cancellationToken);
            }

            return null;
        }

        public async Task<string?> AskUntilAnsweredAsync(string question, CancellationToken cancellationToken)
        {
            while (await AskAsync(question, cancellationToken) is { } answer)
            {
                if (answer.Length > 0)
                {
                    return answer;
                }
            }

            return null;
        }
    }
}
