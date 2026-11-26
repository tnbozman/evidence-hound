using EvidenceHound.Cli.Setup;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Onboarding.Output;
using EvidenceHound.Onboarding.Sessions;

namespace EvidenceHound.Cli;

/// <summary>
/// The files <c>hound onboard &lt;system&gt;</c> writes into the compliance repo once the person has decided: the system's audit, its manual
/// rows, the system in <c>setup.yaml</c>, the onboarding record and the saved session. The questions are in OnboardSystemCommand.cs.
/// </summary>
internal static partial class OnboardSystemCommand
{
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
        var domain = await prompt.AskOneOfAsync("Domain for the system's audit", [.. domains.Select(d => d.Name)], "a domain in scope/setup.yaml", cancellationToken);
        var perimeters = domains.FirstOrDefault(d => d.Name == domain)?.Perimeters.Select(p => p.Name).ToArray() ?? [];
        var perimeter = await prompt.AskOneOfAsync("Perimeter", perimeters, $"a perimeter of {domain}", cancellationToken);
        if (domain is null || perimeter is null)
        {
            return Result.Fail<OnboardSession>(new Problem(
                "onboard.place_missing",
                "scope/setup.yaml declares no domain with a perimeter for the system's audit. Add one (docs/scope.md, setup.yaml), then run hound approve again."));
        }

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

        var series = await OrganisationSeriesAsync(settings.Repo, cancellationToken);
        var systemRef = (await DomainsAsync(settings, cancellationToken)).SelectMany(d => d.Systems).FirstOrDefault(x => x.Name == session.System)?.RefId
            ?? RefIds.Suggest(RefIdKind.System, session.System);
        var place = new AuditPlace(domain, perimeter) { Series = series, SystemRefId = systemRef };
        await WriteFileAsync(settings.Repo, auditFile, OnboardOutput.Audit(session, snapshot.FrameworkUrn, settings.FrameworkName, place, today), cancellationToken);
        var auditRef = series is null ? OnboardOutput.AuditRefId(session.System, settings.FrameworkName) : OnboardOutput.SeriesRefId(series, systemRef);
        var seriesNote = series is null
            ? $"No audit series in scope/audits, so the system's audit is {auditRef} without a cycle (docs/scope.md, Audit periods)."
            : $"The system's audit is {auditRef}, a series of {series.RefId} by {series.Period}.";
        if (await AuditTargets.AddAsync(settings.Repo, auditRef, snapshot.FrameworkUrn, [session.System], cancellationToken) is { } targeted)
        {
            seriesNote += $" {targeted.Note}";
            if (targeted.Written)
            {
                written.Add("hound.yaml");
            }
        }

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
            $"Wrote {string.Join(", ", written)}. {seriesNote} {mappingNote}{gate}\nNext: review the changes in a pull request, then run 'hound scope validate scope', 'hound scope plan scope' and 'hound scope apply scope'.".AsMemory(),
            cancellationToken);
        return Result.Ok(session);
    }

    private static Task<IReadOnlyList<ScopeDomain>> DomainsAsync(Settings settings, CancellationToken cancellationToken) => DomainsAsync(settings.Repo, cancellationToken);

    /// <summary>
    /// The organisation's audit series in the repo: an audit with a cycle whose ref ID is <c>&lt;ORG&gt;-&lt;FRAMEWORK&gt;</c>, the one the
    /// config targets when there are several. Null when there is none.
    /// </summary>
    private static async Task<OrganisationSeries?> OrganisationSeriesAsync(string repo, CancellationToken cancellationToken)
    {
        var cycles = await AuditSeries.CyclesAsync(Path.Combine(repo, "scope"), cancellationToken);
        var organisation = cycles.Where(c => c.Key.Split('-').Length == 2).ToArray();
        var config = Path.Combine(repo, "hound.yaml");
        var targeted = File.Exists(config) ? TargetedAudit().Match(await File.ReadAllTextAsync(config, cancellationToken)) : null;
        var chosen = organisation.Length == 1
            ? organisation[0]
            : organisation.FirstOrDefault(c => targeted is { Success: true } && c.Key == targeted.Groups[1].Value);
        if (chosen.Value is { } cycle)
        {
            return new OrganisationSeries(chosen.Key, cycle.Period, cycle.Starts);
        }

        // s18-08: without an organisation audit, the series setup.yaml names.
        var setup = Path.Combine(repo, "scope", "setup.yaml");
        return File.Exists(setup)
            && ScopeLoader.Parse(new ScopeSource(setup, await File.ReadAllTextAsync(setup, cancellationToken)), null, []).Scope.Setup.AuditSeries is { } series
            ? new OrganisationSeries(series.RefId, series.Cycle.Period, series.Cycle.Starts)
            : null;
    }

    [System.Text.RegularExpressions.GeneratedRegex("compliance_assessment:\\s*\"?([A-Z0-9-]+)")]
    private static partial System.Text.RegularExpressions.Regex TargetedAudit();

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
                ("ref_id", new YamlText(RefIds.Suggest(RefIdKind.System, session.System))),
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
}
