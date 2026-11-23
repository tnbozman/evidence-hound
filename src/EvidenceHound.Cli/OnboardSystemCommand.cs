using System.CommandLine;
using System.Globalization;
using EvidenceHound.Cli.Setup;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
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
internal static class OnboardSystemCommand
{
    internal const string Choices = "a accept, c API check, m manual, n not applicable, o another system, l accept all remaining proposals, s save and stop";

    public static void Attach(Command onboard, Terminal terminal, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger("hound.onboard");
        var system = new Argument<string?>("system") { Description = "The system to onboard, as an ID such as EXAMPLE-FIREWALL.", Arity = ArgumentArity.ZeroOrOne };
        var repo = new Option<DirectoryInfo>("--repo") { Description = "The config repo to work in. Default: the current folder." };
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
            await prompt.AskAsync("Your name, recorded as who accepted each decision", cancellationToken),
            await prompt.AskEmailAsync("System owner's email, as a CISO Assistant user (hound matches the owner by email)", cancellationToken),
            await prompt.AskAsync("Product, as the vendor names it", cancellationToken),
            await prompt.AskRefIdAsync(RefIdKind.Supplier, "Supplier ref_id", cancellationToken),
            await prompt.AskAsync("Supplier name", cancellationToken),
            await prompt.AskRefIdAsync(RefIdKind.Solution, "Solution ref_id (what the supplier provides)", cancellationToken),
            await prompt.AskAsync("Solution name", cancellationToken),
            await prompt.AskRefIdAsync(RefIdKind.Contract, "Contract ref_id", cancellationToken),
            await prompt.AskAsync("Contract name", cancellationToken),
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

        var provides = await prompt.AskAsync($"What does {choice.EntityName} provide for {settings.System}", cancellationToken) ?? "";
        var kept = new List<KeptDocument>();
        while (await prompt.AskAsync("A vendor document to keep (https URL or file; empty to finish)", cancellationToken) is { Length: > 0 } where)
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

        // The levels are the person's to decide in the supplier file, or their coding assistant's to draft with 'hound supplier draft'.
        LevelProposal[] levels = [];
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var place = (await DomainsAsync(settings, cancellationToken)) is [{ Perimeters: [var perimeter, ..] } domain, ..] ? new AuditPlace(domain.Name, perimeter.Name) : null;
        await File.WriteAllTextAsync(file, SupplierFile.Write(choice, levels, settings.System, DateOnly.FromDateTime(DateTime.Today), place), cancellationToken);
        await terminal.Error.WriteLineAsync($"Drafted {file} with a draft contract{(place is null ? "" : " and a review for the GRC team to conclude in CISO Assistant")}.".AsMemory(), cancellationToken);
        return Result.Ok((session with { SupplierLevels = levels, SupplierProvides = provides.Length > 0 ? provides : null, Documents = [.. session.Documents, .. kept] }).Advance(SupplierStatus.NotApproved));
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

    /// <summary>
    /// Writes the decided session into the working copy: the system's audit, manual rows added to the framework's mapping, and the
    /// onboarding record. Never calls CISO Assistant; the person reviews the files, then validates, plans and applies.
    /// </summary>
    internal static async Task<Result<OnboardSession>> WriteAsync(Settings settings, OnboardSession session, Prompt prompt, Terminal terminal, DateOnly today, CancellationToken cancellationToken)
    {
        var framework = FrameworkSnapshotJson.Read(await File.ReadAllTextAsync(settings.Framework, cancellationToken), settings.Framework);
        if (framework is Result<FrameworkSnapshot>.Fail { Errors: var frameworkProblems })
        {
            return Result.Fail<OnboardSession>(frameworkProblems);
        }

        var auditFile = Path.Combine("scope", "audits", OnboardOutput.AuditFileName(settings.System, settings.FrameworkName)).Replace('\\', '/');
        if (File.Exists(Path.Combine(settings.Repo, auditFile)))
        {
            return Result.Fail<OnboardSession>(new Problem("onboard.output_exists", $"{auditFile} already exists. Move it aside, or merge the decisions in the session by hand."));
        }

        var setupPath = Path.Combine(settings.Repo, "scope", "setup.yaml");
        var domains = await DomainsAsync(settings, cancellationToken);
        var domain = await prompt.AskWithDefaultAsync("Domain for the system's audit", (domains is [var first, ..] ? first.Name : ""), cancellationToken);
        var perimeter = await prompt.AskWithDefaultAsync(
            "Perimeter", (domains.FirstOrDefault(d => d.Name == domain)?.Perimeters is [var firstPerimeter, ..] ? firstPerimeter.Name : ""), cancellationToken);
        var snapshot = ((Result<FrameworkSnapshot>.Ok)framework).Value;
        var declared = await DeclareSystemAsync(setupPath, domains, domain, session, cancellationToken);
        if (declared is Result<bool>.Fail { Errors: var declareProblems })
        {
            return Result.Fail<OnboardSession>(declareProblems);
        }

        var written = new List<string> { auditFile };
        if (((Result<bool>.Ok)declared).Value)
        {
            written.Add("scope/setup.yaml");
        }

        await WriteFileAsync(settings.Repo, auditFile, OnboardOutput.Audit(session, snapshot.FrameworkUrn, settings.FrameworkName, new AuditPlace(domain, perimeter), today), cancellationToken);

        var mappingFile = Path.Combine("mappings", $"{settings.FrameworkName}.yaml").Replace('\\', '/');
        var mappingNote = $"No {mappingFile} to add manual rows to; add the manual requirements to your mapping by hand.";
        if (File.Exists(Path.Combine(settings.Repo, mappingFile)))
        {
            var (text, added) = OnboardOutput.Mapping(await File.ReadAllTextAsync(Path.Combine(settings.Repo, mappingFile), cancellationToken), session);
            mappingNote = added.Count == 0 ? $"{mappingFile} already names every manual requirement." : $"Added {added.Count} manual requirement(s) to {mappingFile}: {string.Join(", ", added)}.";
            if (added.Count > 0)
            {
                await WriteFileAsync(settings.Repo, mappingFile, text, cancellationToken);
                written.Add(mappingFile);
            }
        }

        var recordFile = $"onboarding/{OnboardOutput.RecordFileName(settings.System, settings.FrameworkName, today)}";
        await WriteFileAsync(settings.Repo, recordFile, OnboardOutput.Record(session, await SkillsUsedAsync(settings, cancellationToken), settings.FrameworkName, today), cancellationToken);
        written.Add(recordFile);
        session = session with { Written = written };
        await SaveAsync(settings, session, cancellationToken);
        var entity = session.Supplier is { } supplier ? (await ThirdPartiesAsync(settings.Repo, cancellationToken)).FirstOrDefault(e => e.RefId == supplier.EntityRef) : null;
        var review = entity?.Assessments.Select(a => a.RefId).FirstOrDefault();
        var concluded = entity is not null && entity.Contracts.Any(c => c.Status == "active") && entity.Assessments.Any(a => a.Conclusions is [.., { Result: "ok" }]);
        var gate = session.Supplier is { } named
            ? concluded
                ? $"\n{named.EntityRef} is approved and its review is concluded ok in its file, so 'hound scope plan' registers {settings.System} with it."
                : $"\n{settings.System} is registered only once {named.EntityRef} is approved: {(review is null ? $"add a review to {named.EntityRef}'s file and conclude it" : $"conclude {review}")} as ok with 'hound approve' before 'hound scope plan'."
            : "";
        await terminal.Output.WriteLineAsync(
            $"Wrote {string.Join(", ", written)}. {mappingNote}{gate}\nNext: review the changes in a pull request, then run 'hound scope validate scope', 'hound scope plan scope' and 'hound scope apply scope'.".AsMemory(),
            cancellationToken);
        return Result.Ok(session);
    }

    private static Task<IReadOnlyList<ScopeDomain>> DomainsAsync(Settings settings, CancellationToken cancellationToken) => DomainsAsync(settings.Repo, cancellationToken);

    internal static async Task<IReadOnlyList<ScopeDomain>> DomainsAsync(string repo, CancellationToken cancellationToken)
    {
        var setupPath = Path.Combine(repo, "scope", "setup.yaml");
        return File.Exists(setupPath)
            ? ScopeLoader.Parse(new ScopeSource(setupPath, await File.ReadAllTextAsync(setupPath, cancellationToken)), null, []).Scope.Setup.Domains
            : [];
    }

    /// <summary>
    /// Declares the system in <paramref name="domain"/> of <c>scope/setup.yaml</c> with the solution it is, or adds the solution to the system
    /// already declared, so the supplier gate in <c>scope plan</c> covers it. True when the file changed.
    /// </summary>
    private static async Task<Result<bool>> DeclareSystemAsync(string setupPath, IReadOnlyList<ScopeDomain> domains, string domain, OnboardSession session, CancellationToken cancellationToken)
    {
        if (!File.Exists(setupPath))
        {
            return Result.Ok(false);
        }

        var yaml = await File.ReadAllTextAsync(setupPath, cancellationToken);
        var existing = domains.SelectMany(d => d.Systems.Select(s => (Domain: d.Name, System: s))).FirstOrDefault(x => x.System.Name == session.System);
        Result<string> edited;
        if (existing.System is not null)
        {
            edited = session.Supplier is { } supplier
                ? YamlEdit.SetScalar(yaml, $"domains[name={existing.Domain}].systems[name={session.System}].solution", supplier.SolutionRef)
                : Result.Ok(yaml);
        }
        else
        {
            var description = session.Supplier is { } from ? $"{session.Product}, from {from.EntityName}." : session.Product ?? "";
            edited = YamlEdit.AppendItem(yaml, $"domains[name={domain}].systems", YamlMap.Of(
                ("name", new YamlText(session.System)),
                ("type", new YamlText("primary")),
                ("description", new YamlText(description)),
                ("owner", session.Owner is { } owner && owner.Contains('@', StringComparison.Ordinal) ? new YamlText(owner) : null),
                ("solution", session.Supplier is { } chosen ? new YamlText(chosen.SolutionRef) : null)));
        }

        return await edited.Match(
            async text =>
            {
                if (text == yaml)
                {
                    return Result.Ok(false);
                }

                await File.WriteAllTextAsync(setupPath, text, cancellationToken);
                return Result.Ok(true);
            },
            problems => Task.FromResult(Result.Fail<bool>([.. problems.Select(p => new Problem(p.Code, $"scope/setup.yaml: {p.Message} The system's audit was not written."))])));
    }

    private static async Task WriteFileAsync(string repo, string relative, string text, CancellationToken cancellationToken)
    {
        var path = Path.Combine(repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text, cancellationToken);
    }

    /// <summary>The skills the session's requests used, from its skills.json.</summary>
    private static async Task<IReadOnlyList<SkillUse>> SkillsUsedAsync(Settings settings, CancellationToken cancellationToken)
    {
        var path = Path.Combine(settings.Folder, "requests", "skills.json");
        if (!File.Exists(path))
        {
            return [];
        }

        var list = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) as System.Text.Json.Nodes.JsonArray ?? [];
        return [.. list.OfType<System.Text.Json.Nodes.JsonObject>().Select(s => new SkillUse((string?)s["name"] ?? "", (string?)s["source"] ?? "", (string?)s["version"] ?? "", (string?)s["sha256"] ?? ""))];
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

    internal static async Task<IReadOnlyList<Core.Scope.ScopeEntity>> ThirdPartiesAsync(string repo, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(repo, "scope", "third-parties");
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.yaml").Order(StringComparer.Ordinal).ToArray() : [];
        var sources = await Task.WhenAll(files.Select(async f => new ScopeSource(f, await File.ReadAllTextAsync(f, cancellationToken))));
        return ScopeLoader.Parse(new ScopeSource("setup.yaml", "organisation:\n  root_folder: Global\n"), null, [], thirdParties: sources).Scope.ThirdParties;
    }

    internal static async Task SaveAsync(Settings settings, OnboardSession session, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(settings.Folder);
        await File.WriteAllTextAsync(settings.SessionFile, OnboardSessionJson.Write(session), cancellationToken);
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

        /// <summary>A ref ID, with its kind's prefix shown in the prompt and only the rest typed; asked again until it fits. Null once the input has ended.</summary>
        public Task<string?> AskRefIdAsync(RefIdKind kind, string question, CancellationToken cancellationToken) =>
            RefIdPrompt.AskAsync(terminal, kind, question, "", cancellationToken);

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
