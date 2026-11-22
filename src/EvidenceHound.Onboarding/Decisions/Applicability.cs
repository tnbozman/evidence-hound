using System.Globalization;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Results;
using EvidenceHound.Onboarding.Models;
using EvidenceHound.Onboarding.Skills;

namespace EvidenceHound.Onboarding.Decisions;

/// <summary>How a requirement is met for a system.</summary>
public enum DecisionKind
{
    /// <summary>A profile check reads it from the system's API.</summary>
    ApiCheck,

    /// <summary>Someone provides the evidence by hand.</summary>
    Manual,

    /// <summary>It does not apply to this system: needs a justification and an approver.</summary>
    NotApplicable,

    /// <summary>Another system meets it, such as an identity provider for sign-in.</summary>
    OtherSystem,
}

/// <summary>An in-scope requirement: its key (the ref_id when unique, else the URN), URN, ref_id and title.</summary>
public sealed record Requirement(string Key, string Urn, string? RefId, string Title);

/// <summary>A proposal for one requirement. <see cref="Kind"/> is empty when there is none, as with <c>--no-model</c>.</summary>
public sealed record Proposal(string Key, string Urn, string? RefId, string Title, DecisionKind? Kind, string Reasoning, string? OtherSystem);

/// <summary>What the person decided for one requirement, with the proposal it started from and who accepted it.</summary>
public sealed record Decision(string Key, string Urn, string? RefId, DecisionKind Kind, string Reasoning, DecisionKind? Proposed, string AcceptedBy)
{
    public string? Justification { get; init; }

    public string? Approver { get; init; }

    public string? OtherSystem { get; init; }
}

/// <summary>What the model is told about the system being onboarded.</summary>
public sealed record SystemFacts(string Name, string Product, string Owner);

/// <summary>Deciding applicability: the requirements in scope, the model's proposals for them, and the person's decisions. Pure except <see cref="ProposeAsync"/>.</summary>
public static class Applicability
{
    public const string Step = "applicability";
    public const int DefaultBatch = 40;

    internal const string Instructions = """
        You help a GRC team decide, for one system, how each framework requirement is met. For every requirement in the requirements input,
        propose exactly one kind: api_check when the API summary shows an endpoint that reads what the requirement needs; manual when a person
        must provide the evidence; not_applicable only when the requirement cannot apply to this kind of system; other_system when another
        system meets it (name that system). Give short reasoning that names the endpoint, or why none fits. Never invent endpoints. Use the
        requirement's ref exactly as given inside the square brackets.
        """;

    private static readonly Dictionary<string, DecisionKind> Kinds = new(StringComparer.Ordinal)
    {
        ["api_check"] = DecisionKind.ApiCheck,
        ["manual"] = DecisionKind.Manual,
        ["not_applicable"] = DecisionKind.NotApplicable,
        ["other_system"] = DecisionKind.OtherSystem,
    };

    /// <summary>The reply each batch must match.</summary>
    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("decisions"),
        ["properties"] = new JsonObject
        {
            ["decisions"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("ref", "kind", "reasoning", "other_system"),
                    ["properties"] = new JsonObject
                    {
                        ["ref"] = new JsonObject { ["type"] = "string" },
                        ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Kinds.Keys.Select(k => JsonValue.Create(k))]) },
                        ["reasoning"] = new JsonObject { ["type"] = "string" },
                        ["other_system"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                    },
                },
            },
        },
    };

    /// <summary>The wire name of a kind: <c>api_check</c>, <c>manual</c>, <c>not_applicable</c> or <c>other_system</c>.</summary>
    public static string Name(DecisionKind kind) => Kinds.First(k => k.Value == kind).Key;

    /// <summary>The requirements whose implementation groups include one of <paramref name="groups"/>; every requirement when none are given.</summary>
    public static IReadOnlyList<Requirement> InScope(FrameworkSnapshot framework, IReadOnlyList<string> groups)
    {
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(groups);
        var nodes = framework.Nodes.Where(n => groups.Count == 0 || n.Groups.Intersect(groups, StringComparer.Ordinal).Any()).ToArray();
        var repeated = nodes.Where(n => n.RefId is not null).GroupBy(n => n.RefId, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        return [.. nodes.Select(n => new Requirement(n.RefId is { } r && !repeated.Contains(r) ? r : n.Urn, n.Urn, n.RefId, n.Title))];
    }

    /// <summary>One empty proposal per requirement, for <c>--no-model</c>.</summary>
    public static IReadOnlyList<Proposal> Empty(IReadOnlyList<Requirement> requirements) =>
        [.. requirements.Select(r => new Proposal(r.Key, r.Urn, r.RefId, r.Title, null, "", null))];

    /// <summary>
    /// One proposal per requirement from a reply: those it leaves out get an empty proposal. A reply that names a requirement not asked about,
    /// or one twice, is invalid.
    /// </summary>
    public static Result<IReadOnlyList<Proposal>> Proposals(IReadOnlyList<Requirement> requirements, JsonNode reply)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        var items = (reply?["decisions"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
        var keys = items.Select(i => (string?)i["ref"] ?? "").ToArray();
        var unknown = keys.Where(k => requirements.All(r => r.Key != k)).Distinct(StringComparer.Ordinal).ToArray();
        var repeated = keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (unknown.Length > 0 || repeated.Length > 0)
        {
            return Result.Fail<IReadOnlyList<Proposal>>(LlmProblems.InvalidReply(string.Join(
                "; ", [.. unknown.Select(k => $"'{k}' is not a requirement that was asked about"), .. repeated.Select(k => $"{k} was answered more than once")])));
        }

        var byKey = items.ToDictionary(i => (string)i["ref"]!, StringComparer.Ordinal);
        return Result.Ok<IReadOnlyList<Proposal>>([.. requirements.Select(r => byKey.TryGetValue(r.Key, out var item)
            ? new Proposal(r.Key, r.Urn, r.RefId, r.Title, Kinds.GetValueOrDefault((string?)item["kind"] ?? ""), (string?)item["reasoning"] ?? "", (string?)item["other_system"])
            : new Proposal(r.Key, r.Urn, r.RefId, r.Title, null, "", null))]);
    }

    /// <summary>
    /// The person's decision on a proposal. Not applicable needs a justification and an approver; another system needs that system's name.
    /// </summary>
    public static Result<Decision> Decide(
        Proposal proposal, DecisionKind kind, string reasoning, string acceptedBy, string? justification, string? approver, string? otherSystem)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var problems = new List<Problem>();
        if (kind == DecisionKind.NotApplicable && string.IsNullOrWhiteSpace(justification))
        {
            problems.Add(new Problem("onboard.justification_missing", $"{proposal.Key}: not applicable needs a justification, kept for auditors."));
        }

        if (kind == DecisionKind.NotApplicable && string.IsNullOrWhiteSpace(approver))
        {
            problems.Add(new Problem("onboard.approver_missing", $"{proposal.Key}: not applicable needs the name of the person who approves it."));
        }

        if (kind == DecisionKind.OtherSystem && string.IsNullOrWhiteSpace(otherSystem))
        {
            problems.Add(new Problem("onboard.other_system_missing", $"{proposal.Key}: name the system that meets it."));
        }

        return problems.Count > 0
            ? Result.Fail<Decision>([.. problems])
            : Result.Ok(new Decision(proposal.Key, proposal.Urn, proposal.RefId, kind, reasoning.Trim(), proposal.Kind, acceptedBy.Trim())
            {
                Justification = kind == DecisionKind.NotApplicable ? justification!.Trim() : null,
                Approver = kind == DecisionKind.NotApplicable ? approver!.Trim() : null,
                OtherSystem = kind == DecisionKind.OtherSystem ? otherSystem!.Trim() : null,
            });
    }

    /// <summary>Asks the model for proposals in batches of <paramref name="batch"/> requirements, with the applicability skills as guidance.</summary>
    public static async Task<Result<IReadOnlyList<Proposal>>> ProposeAsync(
        IDraftingModel model, IReadOnlyList<Skill> skills, IReadOnlyList<Requirement> requirements, SystemFacts system, string api, int batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(system);
        var proposals = new List<Proposal>();
        foreach (var chunk in requirements.Chunk(Math.Max(1, batch)))
        {
            var request = StepRequest.Compose(
                Instructions,
                skills,
                [
                    new DraftInput("system", string.Create(CultureInfo.InvariantCulture, $"System: {system.Name}\nProduct: {system.Product}\nOwner: {system.Owner}")),
                    new DraftInput("api", api.Length > 0 ? api : "No API specification was given."),
                    new DraftInput("requirements", string.Join('\n', chunk.Select(r => $"[{r.Key}] {r.Title}"))),
                ],
                Schema(),
                Step);
            var read = (await model.DraftAsync(request, cancellationToken)).Bind(r => Proposals(chunk, r.Value));
            if (read is not Result<IReadOnlyList<Proposal>>.Ok { Value: var got })
            {
                return read;
            }

            proposals.AddRange(got);
        }

        return Result.Ok<IReadOnlyList<Proposal>>(proposals);
    }
}
