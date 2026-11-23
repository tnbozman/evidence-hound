using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Onboarding.Models;
using EvidenceHound.Onboarding.Skills;

namespace EvidenceHound.Onboarding.Capabilities;

/// <summary>
/// The model's proposals for the capability map, made after the signals and shown beside them: one request with the mitigations, the
/// systems in scope and the signals, guided by the environment skills. A proposal is never a decision.
/// </summary>
public static class CapabilityProposals
{
    public const string Step = "environment";

    internal const string Instructions = """
        You help a GRC team decide, for each mitigation in the mitigations input, which product in their environment provides it: the product
        whose own settings and records prove it. Propose provided_by system and name the systems (by their names in the systems input) when one
        enforces it; manual when evidence must be gathered by hand; gap when nothing provides it yet. A licence alone does not mean a product is
        in use: say so in the reasoning. Give short reasoning that cites the signal or the system description. Use each mitigation's code
        exactly as given inside the square brackets, at most once.
        """;

    private static readonly Dictionary<string, CapabilityProvision> Kinds = new(StringComparer.Ordinal)
    {
        ["system"] = CapabilityProvision.System,
        ["manual"] = CapabilityProvision.Manual,
        ["gap"] = CapabilityProvision.Gap,
    };

    /// <summary>The reply the model must match.</summary>
    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("mitigations"),
        ["properties"] = new JsonObject
        {
            ["mitigations"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("mitigation", "provided_by", "systems", "reasoning"),
                    ["properties"] = new JsonObject
                    {
                        ["mitigation"] = new JsonObject { ["type"] = "string" },
                        ["provided_by"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Kinds.Keys.Select(k => JsonValue.Create(k))]) },
                        ["systems"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                        ["reasoning"] = new JsonObject { ["type"] = "string" },
                    },
                },
            },
        },
    };

    public static async Task<Result<IReadOnlyList<CapabilityProposal>>> ProposeAsync(
        IDraftingModel model,
        IReadOnlyList<Skill> skills,
        IReadOnlyList<Mitigation> mitigations,
        IReadOnlyList<ScopeSystem> systems,
        IReadOnlyList<CapabilityProposal> prefilled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(mitigations);
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(prefilled);
        var request = StepRequest.Compose(
            Instructions,
            skills,
            [
                new DraftInput("mitigations", string.Join('\n', mitigations.Select(m =>
                    $"[{m.Code}] {m.Requirements.Count} requirement(s), such as {m.Requirements[0].RefId}: {m.Requirements[0].Title}"))),
                new DraftInput("systems", systems.Count == 0 ? "No systems are in scope yet." : string.Join('\n', systems.Select(s =>
                    $"{s.Name} ({s.Type.ToString().ToLowerInvariant()}): {s.Description ?? "no description"}"))),
                new DraftInput("signals", prefilled.Any(p => p.Signals.Count > 0)
                    ? string.Join('\n', prefilled.SelectMany(p => p.Signals.Select(signal => $"[{p.Mitigation}] {signal}")))
                    : "No signals were read."),
            ],
            Schema(),
            Step);
        return (await model.DraftAsync(request, cancellationToken)).Bind(reply => Merge(prefilled, reply.Value));
    }

    /// <summary>
    /// Each mitigation's proposal from the reply, with its reasoning; the signals' suggestion stays where the reply has none. A reply about a
    /// mitigation not asked about, or one twice, is invalid.
    /// </summary>
    public static Result<IReadOnlyList<CapabilityProposal>> Merge(IReadOnlyList<CapabilityProposal> prefilled, JsonNode reply)
    {
        ArgumentNullException.ThrowIfNull(prefilled);
        var items = (reply?["mitigations"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
        var codes = items.Select(i => (string?)i["mitigation"] ?? "").ToArray();
        var unknown = codes.Where(c => prefilled.All(p => p.Mitigation != c)).Distinct(StringComparer.Ordinal).ToArray();
        var repeated = codes.GroupBy(c => c, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (unknown.Length > 0 || repeated.Length > 0)
        {
            return Result.Fail<IReadOnlyList<CapabilityProposal>>(LlmProblems.InvalidReply(string.Join(
                "; ", [.. unknown.Select(c => $"'{c}' is not a mitigation that was asked about"), .. repeated.Select(c => $"{c} was answered more than once")])));
        }

        var byCode = items.ToDictionary(i => (string)i["mitigation"]!, StringComparer.Ordinal);
        return Result.Ok<IReadOnlyList<CapabilityProposal>>([.. prefilled.Select(p =>
        {
            if (!byCode.TryGetValue(p.Mitigation, out var item) || !Kinds.TryGetValue((string?)item["provided_by"] ?? "", out var kind))
            {
                return p;
            }

            string[] named = [.. (item["systems"] as JsonArray ?? []).Select(s => ((string?)s ?? "").Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal)];
            var reasoning = ((string?)item["reasoning"] ?? "").Trim();
            return kind == CapabilityProvision.System && named.Length == 0
                ? p with { Suggested = null, Systems = [], Reasoning = reasoning }
                : p with { Suggested = kind, Systems = kind == CapabilityProvision.System ? named : [], Reasoning = reasoning };
        })]);
    }
}
