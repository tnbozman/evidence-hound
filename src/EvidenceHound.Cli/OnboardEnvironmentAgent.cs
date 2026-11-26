using System.CommandLine;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Onboarding.Capabilities;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound onboard environment pending</c> and <c>decide</c>: the capability map drafted by an agent, one mitigation a command, with no
/// prompt. A decision is written to <c>scope/capabilities.yaml</c> with no approver, which is a draft: a person approves each with
/// <c>hound approve</c>, and a decision a person approved is never replaced here.
/// </summary>
internal static class OnboardEnvironmentAgent
{
    public static void AddTo(Command environment, Terminal terminal, ILogger logger)
    {
        static Option<FileInfo> Framework() => new("--framework") { Description = "The framework snapshot whose mitigations to decide. Default: frameworks/essential-eight-2023.json in the repo." };

        var (pendingRepo, pendingFramework, pendingJson) = (CommonOptions.Repo(), Framework(), CommonOptions.Json());
        var pending = new Command("pending", "List the mitigations not yet decided, the drafts waiting for a person, and the systems in scope.") { pendingRepo, pendingFramework, pendingJson };
        pending.SetAction(async (parseResult, cancellationToken) =>
        {
            var loaded = await LoadAsync(parseResult.GetValue(pendingRepo), parseResult.GetValue(pendingFramework), cancellationToken);
            return await loaded.Match(
                async map =>
                {
                    await terminal.Output.WriteLineAsync((parseResult.GetValue(pendingJson) ? PendingJson(map).ToJsonString(CommandJson.Readable) : PendingText(map)).AsMemory(), cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });

        var mitigation = new Argument<string>("mitigation") { Description = "The mitigation's code, as 'pending' lists it, such as mfa." };
        var providedBy = new Option<string>("--provided-by") { Description = "What provides it: system, manual or gap.", Required = true };
        providedBy.AcceptOnlyFromAmong("system", "manual", "gap");
        var systems = new Option<string[]>("--system") { Description = "A system that provides it, by name. Repeat for more. One not in scope yet is queued for onboarding.", AllowMultipleArgumentsPerToken = false };
        var reason = new Option<string>("--reason") { Description = "Why, in a sentence a reviewer can check.", Required = true };
        var supplier = new Option<string?>("--supplier") { Description = "The third party that supplies it, by ref_id." };
        var owner = new Option<string?>("--owner") { Description = "Who owns it, as an email." };
        var evidence = new Option<string?>("--evidence") { Description = "How its evidence arrives: collector, profile or manual." };
        evidence.AcceptOnlyFromAmong("collector", "profile", "manual");
        var route = new Option<string?>("--route") { Description = "The collector or profile the evidence comes through." };
        var (decideRepo, decideFramework, decideJson) = (CommonOptions.Repo(), Framework(), CommonOptions.Json());
        var decide = new Command("decide", "Record one mitigation's decision as a draft by an agent. A person approves drafts with 'hound approve'.")
        {
            mitigation, providedBy, systems, reason, supplier, owner, evidence, route, decideRepo, decideFramework, decideJson,
        };
        decide.SetAction(async (parseResult, cancellationToken) =>
        {
            var code = parseResult.GetRequiredValue(mitigation);
            var loaded = await LoadAsync(parseResult.GetValue(decideRepo), parseResult.GetValue(decideFramework), cancellationToken);
            var decided = await loaded.BindAsync(async map =>
            {
                var draft = new ScopeCapability(code, Enum.Parse<CapabilityProvision>(parseResult.GetRequiredValue(providedBy), ignoreCase: true), new SourceLine(map.File, 1))
                {
                    Systems = [.. (parseResult.GetValue(systems) ?? []).Select(s => new ScopeRef(s, new SourceLine(map.File, 1)))],
                    Supplier = parseResult.GetValue(supplier) is { } named ? new ScopeRef(named, new SourceLine(map.File, 1)) : null,
                    Owner = parseResult.GetValue(owner),
                    Evidence = parseResult.GetValue(evidence),
                    Route = parseResult.GetValue(route),
                    Reason = parseResult.GetRequiredValue(reason),
                    Signals = [$"Drafted by an agent with hound onboard environment decide on {map.Today:yyyy-MM-dd}"],
                };
                var checkedDraft = Check(map, draft);
                if (checkedDraft is Result<IReadOnlyList<ScopeCapability>>.Ok { Value: var decisions })
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(map.File)!);
                    await File.WriteAllTextAsync(map.File, CapabilityFile.Write(map.Snapshot.FrameworkUrn, decisions, map.Today), cancellationToken);
                }

                return checkedDraft;
            });
            return await decided.Match(
                async _ =>
                {
                    await terminal.Output.WriteLineAsync((parseResult.GetValue(decideJson)
                        ? new JsonObject { ["mitigation"] = code, ["file"] = "scope/capabilities.yaml", ["drafted_by"] = OnboardAgentCommand.DraftedBy, ["approved"] = false }.ToJsonString(CommandJson.Readable)
                        : $"Drafted {code} in scope/capabilities.yaml. A person approves it with 'hound approve'.").AsMemory(), cancellationToken);
                    return ExitCodes.Ok;
                },
                problems => Task.FromResult(Log.Problems(logger, problems)));
        });

        environment.Add(pending);
        environment.Add(decide);
    }

    /// <summary>The decisions with <paramref name="draft"/> added or replacing an earlier draft, in the framework's order; or why it is refused. Pure.</summary>
    internal static Result<IReadOnlyList<ScopeCapability>> Check(Map map, ScopeCapability draft)
    {
        if (!map.Mitigations.Any(m => m.Code == draft.Mitigation))
        {
            return Result.Fail<IReadOnlyList<ScopeCapability>>(new Problem(
                "capability.mitigation_unknown", $"'{draft.Mitigation}' is not a mitigation of {map.Snapshot.FrameworkUrn}. Known: {string.Join(", ", map.Mitigations.Select(m => m.Code))}."));
        }

        if (map.Decisions.FirstOrDefault(d => d.Mitigation == draft.Mitigation) is { ApprovedBy: { Length: > 0 } approver })
        {
            return Result.Fail<IReadOnlyList<ScopeCapability>>(new Problem(
                "capability.already_approved", $"{draft.Mitigation} was approved by {approver}. A person changes an approved decision in scope/capabilities.yaml; an agent does not replace it."));
        }

        if (draft.ProvidedBy == CapabilityProvision.System && draft.Systems.Count == 0)
        {
            return Result.Fail<IReadOnlyList<ScopeCapability>>(new Problem("capability.systems_missing", $"{draft.Mitigation} is provided by a system: name it with --system."));
        }

        if (string.IsNullOrWhiteSpace(draft.Reason))
        {
            return Result.Fail<IReadOnlyList<ScopeCapability>>(new Problem("capability.reason_missing", $"{draft.Mitigation} needs a reason a reviewer can check."));
        }

        var decisions = map.Decisions.Where(d => d.Mitigation != draft.Mitigation).Append(draft).ToArray();
        return Result.Ok<IReadOnlyList<ScopeCapability>>([.. map.Mitigations.SelectMany(m => decisions.Where(d => d.Mitigation == m.Code))]);
    }

    private static JsonObject PendingJson(Map map) => new()
    {
        ["framework"] = map.Snapshot.FrameworkUrn,
        ["systems"] = new JsonArray([.. map.Systems.Select(s => (JsonNode)s)]),
        ["pending"] = new JsonArray([.. map.Mitigations.Where(m => !map.Decisions.Any(d => d.Mitigation == m.Code)).Select(m => (JsonNode)new JsonObject
        {
            ["mitigation"] = m.Code,
            ["requirements"] = m.Requirements.Count,
            ["examples"] = new JsonArray([.. m.Requirements.Take(3).Select(r => (JsonNode)$"{r.RefId ?? r.Urn}: {r.Title}")]),
        })]),
        ["drafts"] = new JsonArray([.. map.Decisions.Where(d => string.IsNullOrWhiteSpace(d.ApprovedBy)).Select(d => (JsonNode)new JsonObject
        {
            ["mitigation"] = d.Mitigation,
            ["provided_by"] = d.ProvidedBy.ToString().ToLowerInvariant(),
            ["systems"] = new JsonArray([.. d.Systems.Select(s => (JsonNode)s.Value)]),
            ["reason"] = d.Reason,
        })]),
        ["approved"] = map.Decisions.Count(d => !string.IsNullOrWhiteSpace(d.ApprovedBy)),
    };

    private static string PendingText(Map map)
    {
        var undecided = map.Mitigations.Where(m => !map.Decisions.Any(d => d.Mitigation == m.Code)).Select(m => $"  {m.Code} ({m.Requirements.Count} requirements)").ToArray();
        var drafts = map.Decisions.Where(d => string.IsNullOrWhiteSpace(d.ApprovedBy)).Select(d => $"  {d.Mitigation}: {d.ProvidedBy.ToString().ToLowerInvariant()}. {d.Reason}").ToArray();
        return $"{undecided.Length} mitigation(s) to decide:\n{string.Join('\n', undecided)}\n{drafts.Length} draft(s) waiting for 'hound approve':\n{string.Join('\n', drafts)}".TrimEnd();
    }

    private static async Task<Result<Map>> LoadAsync(DirectoryInfo? repo, FileInfo? framework, CancellationToken cancellationToken)
    {
        var root = repo?.FullName ?? Directory.GetCurrentDirectory();
        var frameworkFile = framework?.FullName ?? Path.Combine(root, "frameworks", "essential-eight-2023.json");
        if (!File.Exists(frameworkFile))
        {
            return Result.Fail<Map>(new Problem("onboard.framework_missing", $"{frameworkFile} does not exist. Make it with 'hound framework snapshot', or name it with --framework."));
        }

        var parsed = await OnboardEnvironmentCommand.ParseAsync(Path.Combine(root, "scope"), cancellationToken);
        if (parsed.Problems.Count > 0)
        {
            return Result.Fail<Map>([.. parsed.Problems.Select(p => new Problem(p.Code, $"{p.At.File} line {p.At.Line}: {p.Message}"))]);
        }

        return FrameworkSnapshotJson.Read(await File.ReadAllTextAsync(frameworkFile, cancellationToken), frameworkFile).Bind(snapshot =>
            !parsed.Scope.Setup.Frameworks.Any(f => f.Urn == snapshot.FrameworkUrn)
                ? Result.Fail<Map>(new Problem("capability.framework_unknown", $"{snapshot.FrameworkUrn} is not a framework of scope/setup.yaml; add it there first."))
                : parsed.Scope.Capabilities is { } held && held.Framework.Value != snapshot.FrameworkUrn
                    ? Result.Fail<Map>(new Problem("capability.framework_unknown", $"scope/capabilities.yaml maps {held.Framework.Value}, not {snapshot.FrameworkUrn}."))
                    : Result.Ok(new Map(
                        Path.Combine(root, "scope", "capabilities.yaml"),
                        snapshot,
                        CapabilityMap.Mitigations(snapshot),
                        parsed.Scope.Capabilities?.Mitigations ?? [],
                        [.. parsed.Scope.Setup.Domains.SelectMany(d => d.Systems).Select(s => s.Name)],
                        DateOnly.FromDateTime(DateTime.Today))));
    }

    /// <summary>The capability map as the repo holds it, with the framework's mitigations and the systems in scope.</summary>
    internal sealed record Map(string File, FrameworkSnapshot Snapshot, IReadOnlyList<Mitigation> Mitigations, IReadOnlyList<ScopeCapability> Decisions, IReadOnlyList<string> Systems, DateOnly Today);
}
