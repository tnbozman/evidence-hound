using System.CommandLine;
using System.Text.Json.Nodes;
using EvidenceHound.Cli.Setup;
using EvidenceHound.Collectors;
using EvidenceHound.Collectors.M365;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Onboarding.Capabilities;
using EvidenceHound.Runtime.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound onboard environment</c>: one decision per mitigation of the framework, on which system provides it, pre-filled from read-only
/// signals and the systems in scope, decided by the person. Writes <c>scope/capabilities.yaml</c> and the not-applicable rows the map puts in
/// each system's own audit, for review. Never calls CISO Assistant.
/// </summary>
internal static class OnboardEnvironmentCommand
{
    public static Command Build(Terminal terminal, ILoggerFactory loggers, GraphAccess access)
    {
        var logger = loggers.CreateLogger("hound.onboard");
        var repo = new Option<DirectoryInfo>("--repo") { Description = "The config repo to work in. Default: the current folder." };
        var framework = new Option<FileInfo>("--framework") { Description = "The framework snapshot whose mitigations to decide. Default: frameworks/essential-eight-2023.json in the repo." };
        var signals = new Option<FileInfo>("--signals") { Description = "Recorded answers of the read-only Microsoft Graph lookups (subscribed licences and Intune configuration kinds), as JSON." };
        var graph = new Option<string>("--graph") { Description = "Read the licences and Intune configuration kinds live, read-only, as this m365 collector of the deployment config." };
        var deployment = new Option<FileInfo>("--deployment") { Description = "The deployment config: the --graph collector, and the targets whose mappings compute requirements, which are left to them. Default: hound.yaml in the repo." };
        var microsoft = new Option<string>("--microsoft-system") { Description = "The system in scope/setup.yaml that is Microsoft 365, which Microsoft signals suggest. Default: the first whose name has 365." };
        var command = new Command("environment", "Decide which system provides each mitigation, before systems are onboarded, and write scope/capabilities.yaml for review.")
        {
            repo, framework, signals, graph, deployment, microsoft,
        };
        var retired = Retired.On(command);
        OnboardEnvironmentAgent.AddTo(command, terminal, logger);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            await retired.NoteAsync(parseResult, terminal, cancellationToken);
            var root = parseResult.GetValue(repo)?.FullName ?? Directory.GetCurrentDirectory();
            var frameworkFile = parseResult.GetValue(framework)?.FullName ?? Path.Combine(root, "frameworks", "essential-eight-2023.json");
            var source = new SignalSource(parseResult.GetValue(signals), parseResult.GetValue(graph), parseResult.GetValue(deployment)?.FullName ?? Path.Combine(root, "hound.yaml"));
            var result = await RunAsync(root, frameworkFile, source, access, parseResult.GetValue(microsoft), terminal, cancellationToken);
            return result.Match(_ => ExitCodes.Ok, problems => Log.Problems(logger, problems));
        });
        return command;
    }

    private static async Task<Result<Unit>> RunAsync(
        string root, string frameworkFile, SignalSource source, GraphAccess access, string? microsoftSystem, Terminal terminal, CancellationToken cancellationToken)
    {
        if (source.Recorded is not null && source.Collector is not null)
        {
            return Result.Fail<Unit>(new Problem("onboard.signals_twice", "Give --signals (recorded answers) or --graph (read live), not both."));
        }

        if (!File.Exists(frameworkFile))
        {
            return Result.Fail<Unit>(new Problem("onboard.framework_missing", $"{frameworkFile} does not exist. Make it with 'hound framework snapshot', or name it with --framework."));
        }

        var snapshotRead = FrameworkSnapshotJson.Read(await File.ReadAllTextAsync(frameworkFile, cancellationToken), frameworkFile);
        if (snapshotRead is Result<FrameworkSnapshot>.Fail { Errors: var snapshotProblems })
        {
            return Result.Fail<Unit>(snapshotProblems);
        }

        var snapshot = ((Result<FrameworkSnapshot>.Ok)snapshotRead).Value;
        var scopeDir = Path.Combine(root, "scope");
        var capabilitiesPath = Path.Combine(scopeDir, "capabilities.yaml");
        var parsed = await ParseAsync(scopeDir, cancellationToken);
        if (parsed.Problems.Count > 0)
        {
            return Result.Fail<Unit>([.. parsed.Problems.Select(p => new Problem(p.Code, $"{p.At.File} line {p.At.Line}: {p.Message}"))]);
        }

        var scope = parsed.Scope;
        if (!scope.Setup.Frameworks.Any(f => f.Urn == snapshot.FrameworkUrn))
        {
            return Result.Fail<Unit>(new Problem("capability.framework_unknown", $"{snapshot.FrameworkUrn} is not a framework of scope/setup.yaml; add it there first."));
        }

        if (scope.Capabilities is { } held && held.Framework.Value != snapshot.FrameworkUrn)
        {
            return Result.Fail<Unit>(new Problem("capability.framework_unknown", $"scope/capabilities.yaml maps {held.Framework.Value}, not {snapshot.FrameworkUrn}."));
        }

        var targets = await TargetsAsync(source.Config, access, cancellationToken);
        if (targets is Result<IReadOnlyList<ScopeTarget>>.Fail { Errors: var targetProblems })
        {
            return Result.Fail<Unit>(targetProblems);
        }

        var facts = source switch
        {
            { Recorded: { } recorded } => ReadFacts(await File.ReadAllTextAsync(recorded.FullName, cancellationToken), recorded.Name),
            { Collector: { } collector } => await LiveFactsAsync(collector, source.Config, access, cancellationToken),
            _ => Result.Ok(EnvironmentFacts.None),
        };
        if (facts is Result<EnvironmentFacts>.Fail { Errors: var factProblems })
        {
            return Result.Fail<Unit>(factProblems);
        }

        var systems = scope.Setup.Domains.SelectMany(d => d.Systems).ToArray();
        var mitigations = CapabilityMap.Mitigations(snapshot);
        var proposals = CapabilitySignals.Prefill(
            mitigations, systems, ((Result<EnvironmentFacts>.Ok)facts).Value, microsoftSystem ?? systems.FirstOrDefault(s => s.Name.Contains("365", StringComparison.Ordinal))?.Name);
        var decisions = (scope.Capabilities?.Mitigations ?? []).ToList();
        var suppliers = new Dictionary<string, string>(StringComparer.Ordinal);
        var undecided = mitigations.Where(m => !decisions.Any(d => d.Mitigation == m.Code)).ToArray();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var written = new List<string>();
        if (undecided.Length == 0)
        {
            await terminal.Output.WriteLineAsync($"All {mitigations.Count} mitigations are decided in scope/capabilities.yaml.".AsMemory(), cancellationToken);
        }
        else
        {
            var prompt = new OnboardSystemCommand.Prompt(terminal);
            var by = await prompt.AskUntilAnsweredAsync("Your name, recorded as who decided", cancellationToken);
            var role = by is null ? null : await prompt.AskUntilAnsweredAsync("The role that approves these decisions", cancellationToken);
            if (by is null || role is null)
            {
                return Result.Ok(Unit.Value);
            }

            foreach (var mitigation in undecided)
            {
                var index = mitigations.ToList().IndexOf(mitigation) + 1;
                var proposal = proposals.Single(p => p.Mitigation == mitigation.Code);
                var decision = await DecideAsync(prompt, terminal, $"[{index}/{mitigations.Count}] {mitigation.Code}", mitigation, proposal, systems, scope, suppliers, by, role, today, cancellationToken);
                if (decision is null)
                {
                    break;
                }

                decisions.Add(decision);
                decisions = [.. mitigations.SelectMany(m => decisions.Where(d => d.Mitigation == m.Code))];
                await File.WriteAllTextAsync(capabilitiesPath, CapabilityFile.Write(snapshot.FrameworkUrn, decisions, today), cancellationToken);
            }

            written.Add("scope/capabilities.yaml");
        }

        var mapped = scope with { Capabilities = new ScopeCapabilityMap(new ScopeRef(snapshot.FrameworkUrn, new SourceLine(capabilitiesPath, 1)), decisions, new SourceLine(capabilitiesPath, 1)) };
        var rows = CapabilityMap.Rows(mapped, snapshot, ((Result<IReadOnlyList<ScopeTarget>>.Ok)targets).Value);
        foreach (var audit in rows.Audits.Where(a => a.Rows.Count > 0))
        {
            var text = await File.ReadAllTextAsync(audit.File, cancellationToken);
            foreach (var row in audit.Rows)
            {
                var appended = YamlEdit.AppendItem(text, "assessments", YamlMap.Of(
                    (row.Key, new YamlText(row.Requirement)), ("status", new YamlText("to_do")), ("result", new YamlText("not_assessed")),
                    ("provided_by", new YamlFlowList(row.ProvidedBy)), ("observation", new YamlText(row.Observation))));
                if (appended is Result<string>.Fail { Errors: var editProblems })
                {
                    return Result.Fail<Unit>([.. editProblems.Select(p => new Problem(p.Code, $"{audit.File}: {p.Message}"))]);
                }

                text = ((Result<string>.Ok)appended).Value;
            }

            await File.WriteAllTextAsync(audit.File, text, cancellationToken);
            written.Add(Path.GetRelativePath(root, audit.File).Replace('\\', '/'));
        }

        var report = new List<string>();
        if (written.Count > 0)
        {
            report.Add($"Wrote {string.Join(", ", written)}.");
        }

        report.AddRange(CapabilityMap.Pending(mapped).Select(p =>
        {
            var supplier = p.Supplier ?? suppliers.GetValueOrDefault(p.System);
            return $"Next: onboard {p.System} ({string.Join(", ", p.Mitigations)}) with 'hound onboard \"{p.System}\"'; "
                + (supplier is null ? "name its supplier there, which goes through the supplier gate first." : $"its supplier {supplier} goes through the supplier gate first.");
        }));
        report.AddRange(rows.Audits.Where(a => a.Computed.Count > 0).Select(a =>
            $"Left to the mapping of audit {a.AuditRefId}, which computes them: {string.Join(", ", a.Computed)}. If this audit's systems should not be judged on them, take them out of that mapping."));
        report.AddRange(rows.Gaps.Select(g => $"Gap: {g.Mitigation} has no product; no system's audit answers its {g.Requirements.Count} requirement(s)."));
        report.Add("Review the changes in a pull request, then run 'hound scope validate scope' and 'hound scope plan scope'.");
        await terminal.Output.WriteLineAsync(string.Join('\n', report).AsMemory(), cancellationToken);
        return Result.Ok(Unit.Value);
    }

    /// <summary>One mitigation's decision from the person, or null when the input ends or they stop.</summary>
    private static async Task<ScopeCapability?> DecideAsync(
        OnboardSystemCommand.Prompt prompt, Terminal terminal, string heading, Mitigation mitigation, CapabilityProposal proposal, IReadOnlyList<ScopeSystem> systems,
        ScopeDocument scope, Dictionary<string, string> suppliers, string by, string role, DateOnly today, CancellationToken cancellationToken)
    {
        var lines = new List<string> { $"{heading}: {mitigation.Requirements.Count} requirement(s), such as {mitigation.Requirements[0].RefId}: {mitigation.Requirements[0].Title}" };
        lines.Add(proposal.Suggested switch
        {
            CapabilityProvision.System => $"  Suggested: provided by {string.Join(" and ", proposal.Systems)}.",
            CapabilityProvision.Manual => "  Suggested: manual evidence.",
            CapabilityProvision.Gap => "  Suggested: a gap.",
            _ => "  No suggestion.",
        });
        lines.AddRange(proposal.Signals.Select(s => $"  - {s}"));
        await terminal.Error.WriteLineAsync(string.Join('\n', lines).AsMemory(), cancellationToken);
        while (await prompt.AskAsync("a accept, s name the systems, m manual evidence, g gap, q save and stop", cancellationToken) is { } answer)
        {
            var chosen = answer switch
            {
                "a" => CapabilityAnswer.Accept,
                "m" => CapabilityAnswer.Manual,
                "g" => CapabilityAnswer.Gap,
                "s" => await prompt.AskUntilAnsweredAsync("Systems that provide it, separated by commas", cancellationToken) is { } named
                    ? CapabilityAnswer.System([.. named.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)])
                    : null,
                "q" => null,
                _ => CapabilityAnswer.Manual with { Kind = "unknown" },
            };
            if (chosen is null)
            {
                return null;
            }

            var decided = CapabilityAnswers.Decide(proposal, chosen, by, role, today);
            if (decided is Result<ScopeCapability>.Fail { Errors: var problems })
            {
                await terminal.Error.WriteLineAsync(string.Join(' ', problems.Select(p => p.Message)).AsMemory(), cancellationToken);
                continue;
            }

            var decision = ((Result<ScopeCapability>.Ok)decided).Value;
            foreach (var system in decision.Systems.Where(s => !systems.Any(x => x.Name == s.Value) && !suppliers.ContainsKey(s.Value)))
            {
                if (await prompt.AskAsync($"{system.Value} is not onboarded yet. Its supplier, by ref_id (empty if not known)", cancellationToken) is { Length: > 0 } supplier)
                {
                    suppliers[system.Value] = supplier;
                    if (scope.ThirdParties.Any(e => e.RefId == supplier))
                    {
                        decision = decision with { Supplier = new ScopeRef(supplier, decision.At) };
                    }
                }
            }

            return decision;
        }

        return null;
    }

    /// <summary>The deployment's targets and the requirements their mappings compute; none when the repo has no deployment config.</summary>
    private static async Task<Result<IReadOnlyList<ScopeTarget>>> TargetsAsync(string configFile, GraphAccess access, CancellationToken cancellationToken)
    {
        if (!File.Exists(configFile))
        {
            return Result.Ok<IReadOnlyList<ScopeTarget>>([]);
        }

        var loaded = await ConfigLoader.LoadAsync(configFile, access.CollectorTypes, cancellationToken);
        return await loaded.Match(
            async config => (await PlanCommand.LoadMappingsAsync(config.Targets, cancellationToken)).Map(mappings => (IReadOnlyList<ScopeTarget>)
            [
                .. mappings.Select(t => new ScopeTarget(t.Target.ComplianceAssessmentRefId, [.. t.Mapping.Requirements.Select(r => r.Key)], [], new SourceLine(configFile, 0))),
            ]),
            problems => Task.FromResult(Result.Fail<IReadOnlyList<ScopeTarget>>(problems)));
    }

    /// <summary>The two read-only Graph lookups, signed in as the named m365 collector of the deployment config, with its own settings and secrets.</summary>
    private static async Task<Result<EnvironmentFacts>> LiveFactsAsync(string collector, string configFile, GraphAccess access, CancellationToken cancellationToken)
    {
        var loaded = await ConfigLoader.LoadAsync(configFile, access.CollectorTypes, cancellationToken);
        if (loaded is Result<DeploymentConfig>.Fail { Errors: var problems })
        {
            return Result.Fail<EnvironmentFacts>(problems);
        }

        var instance = ((Result<DeploymentConfig>.Ok)loaded).Value.Collectors.FirstOrDefault(c => c.Name == collector);
        if (instance is not { Type: M365Collector.TypeName })
        {
            return Result.Fail<EnvironmentFacts>(new Problem(
                "onboard.graph_collector_unknown", $"{Path.GetFileName(configFile)} has no m365 collector named '{collector}'; --graph names one, such as the one that collects from Microsoft 365."));
        }

        var secrets = ConfigCommands.Secrets(access.Configuration);
        var read = await Result.Combine(instance.Secrets.Select(pair => secrets.Resolve(pair.Value).Map(secret => KeyValuePair.Create(pair.Key, secret))))
            .BindAsync(resolved => access.Lookup.ReadAsync(new CollectorSettings(instance.Settings), resolved.ToDictionary(), access.HttpClients, cancellationToken));
        return read.Map(found => new EnvironmentFacts($"Microsoft Graph ({collector})", found.Licences, found.ConfigurationTypes) { Collector = collector });
    }

    private static Result<EnvironmentFacts> ReadFacts(string json, string name)
    {
        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            string[] List(string key) => [.. (root?[key] as JsonArray ?? []).Select(n => n?.GetValue<string>()).OfType<string>()];
            return root is null
                ? Result.Fail<EnvironmentFacts>(new Problem("onboard.signals_invalid", $"{name} must be a JSON object."))
                : Result.Ok(new EnvironmentFacts((string?)root["source"] ?? name, List("licences"), List("configuration_types")));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return Result.Fail<EnvironmentFacts>(new Problem("onboard.signals_invalid", $"{name} is not valid JSON: {ex.Message}"));
        }
    }

    internal static async Task<ScopeParse> ParseAsync(string scopeDir, CancellationToken cancellationToken)
    {
        async Task<ScopeSource> Read(string path) => new(path, await File.ReadAllTextAsync(path, cancellationToken));
        string[] Files(string folder) => Directory.Exists(Path.Combine(scopeDir, folder)) ? [.. Directory.GetFiles(Path.Combine(scopeDir, folder), "*.yaml").Order(StringComparer.Ordinal)] : [];
        var setup = Path.Combine(scopeDir, "setup.yaml");
        var capabilities = Path.Combine(scopeDir, "capabilities.yaml");
        return ScopeLoader.Parse(
            File.Exists(setup) ? await Read(setup) : new ScopeSource(setup, "organisation:\n  root_folder: Global\n"),
            null,
            [.. await Task.WhenAll(Files("audits").Select(Read))],
            thirdParties: [.. await Task.WhenAll(Files("third-parties").Select(Read))],
            capabilities: File.Exists(capabilities) ? await Read(capabilities) : null);
    }

    /// <summary>Where the Microsoft signals come from: a recorded file, a collector to read live as, or neither.</summary>
    private sealed record SignalSource(FileInfo? Recorded, string? Collector, string Config);
}

/// <summary>What <c>--graph</c> needs from the composition root: the collector types, HTTP clients, the lookup and configuration for secrets.</summary>
internal sealed record GraphAccess(IReadOnlyDictionary<string, CollectorDescription> CollectorTypes, IHttpClientFactory HttpClients, EnvironmentLookup Lookup, IConfiguration Configuration);
