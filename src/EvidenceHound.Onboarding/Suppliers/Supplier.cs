using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;
using EvidenceHound.Onboarding.Models;
using EvidenceHound.Onboarding.Output;
using EvidenceHound.Onboarding.Skills;

namespace EvidenceHound.Onboarding.Suppliers;

/// <summary>Who provides the system: the supplier entity, the solution it provides and the contract covering it, by ref_id and name.</summary>
public sealed record SupplierChoice(string EntityRef, string EntityName, string SolutionRef, string SolutionName, string ContractRef, string ContractName);

/// <summary>Whether a supplier may have a system onboarded.</summary>
public enum SupplierStatus
{
    NotDeclared,
    NotApproved,
    Approved,
}

/// <summary>
/// The supplier gate. Until supplier assessments and approval arrive (s09-20), a supplier counts as approved when it is declared under
/// <c>scope/third-parties/</c> with an active contract covering the solution.
/// </summary>
public static class SupplierGate
{
    public static SupplierStatus Check(IReadOnlyList<ScopeEntity> thirdParties, SupplierChoice choice)
    {
        ArgumentNullException.ThrowIfNull(thirdParties);
        ArgumentNullException.ThrowIfNull(choice);
        var entity = thirdParties.FirstOrDefault(e => e.RefId == choice.EntityRef);
        if (entity is null)
        {
            return SupplierStatus.NotDeclared;
        }

        return entity.Contracts.Any(c => c.Status == "active" && c.Solutions.Any(s => s.Value == choice.SolutionRef))
            ? SupplierStatus.Approved
            : SupplierStatus.NotApproved;
    }
}

/// <summary>A proposed level (dependency, penetration, maturity, trust, or the solution's criticality), with its reason and sources.</summary>
public sealed record LevelProposal(string Name, int Value, string Reason, IReadOnlyList<string> Sources);

/// <summary>
/// One point of the supplier summary, such as certifications or where data is held, with its source. A draft kept in the onboarding
/// record; never evidence.
/// </summary>
public sealed record SummaryPoint(string Topic, string Text, string Source)
{
    /// <summary>Always false: the summary is a draft, and only the documents the person keeps are evidence.</summary>
    public bool Evidence { get; private init; }
}

/// <summary>A vendor document the person kept: where it came from, when, its SHA-256, and where it was saved in the config repo.</summary>
public sealed record KeptDocument(Uri Source, string Path, string Sha256, DateTimeOffset RetrievedAt)
{
    public static KeptDocument Of(Uri source, string path, ReadOnlySpan<byte> content, DateTimeOffset retrievedAt) =>
        new(source, path, Convert.ToHexStringLower(SHA256.HashData(content)), retrievedAt);
}

/// <summary>The model's proposal for a supplier: every level with a reason and sources, and a summary drawn only from the given sources.</summary>
public sealed record SupplierProposal(IReadOnlyList<LevelProposal> Levels, IReadOnlyList<SummaryPoint> Summary)
{
    public const string Step = "supplier";
    public const string Answers = "answers";

    /// <summary>Each level and its range, as CISO Assistant's stakeholder and solution scales have them.</summary>
    /// <summary>A level on a 1 to 4 scale that the inputs give no evidence for: left out of the supplier file for the person to decide.</summary>
    public const int NotYetAssessed = 0;

    public static readonly IReadOnlyList<(string Name, int Low, int High)> Scales =
        [("dependency", 0, 4), ("penetration", 0, 4), ("maturity", 1, 4), ("trust", 1, 4), ("criticality", 1, 4)];

    internal const string Instructions = """
        You help a GRC team record a supplier. From the person's answers and the vendor documents given as inputs, propose dependency,
        penetration, maturity and trust for the supplier and criticality for its solution, each with a short reason and the names of the
        inputs it rests on ("answers" or a document's name). Then summarise what the documents say about certifications, where data is held
        and the support lifecycle, each point with its source. Use only the inputs; leave the summary empty when no document says anything.
        Maturity, trust and criticality run from 1 to 4; give 0 when not yet assessed, and say in the reason what evidence to ask for.
        """;

    public static JsonObject Schema()
    {
        var text = new JsonObject { ["type"] = "string" };
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("levels", "summary"),
            ["properties"] = new JsonObject
            {
                ["levels"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray("name", "value", "reason", "sources"),
                        ["properties"] = new JsonObject
                        {
                            ["name"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Scales.Select(s => JsonValue.Create(s.Name))]) },
                            ["value"] = new JsonObject { ["type"] = "integer" },
                            ["reason"] = text.DeepClone(),
                            ["sources"] = new JsonObject { ["type"] = "array", ["items"] = text.DeepClone() },
                        },
                    },
                },
                ["summary"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray("topic", "text", "source"),
                        ["properties"] = new JsonObject { ["topic"] = text.DeepClone(), ["text"] = text.DeepClone(), ["source"] = text.DeepClone() },
                    },
                },
            },
        };
    }

    /// <summary>
    /// Reads a reply. Every level must appear once, within its scale, with a reason and at least one source; every source must be one of
    /// <paramref name="sources"/>, so nothing rests on a document the person did not give.
    /// </summary>
    public static Result<SupplierProposal> Read(JsonNode reply, IReadOnlyCollection<string> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var levels = (reply?["levels"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(l => new LevelProposal((string?)l["name"] ?? "", Int(l["value"]), ((string?)l["reason"] ?? "").Trim(), [.. (l["sources"] as JsonArray ?? []).Select(s => (string?)s ?? "")]))
            .ToArray();
        var summary = (reply?["summary"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(s => new SummaryPoint((string?)s["topic"] ?? "", ((string?)s["text"] ?? "").Trim(), (string?)s["source"] ?? ""))
            .ToArray();
        var problems = new List<string>();
        foreach (var (name, low, high) in Scales)
        {
            var named = levels.Where(l => l.Name == name).ToArray();
            if (named.Length != 1)
            {
                problems.Add(named.Length == 0 ? $"{name} is missing" : $"{name} is given {named.Length} times");
            }
            else if ((named[0].Value < low || named[0].Value > high) && !(low > NotYetAssessed && named[0].Value == NotYetAssessed))
            {
                problems.Add($"{name} is {named[0].Value}, outside {low} to {high}");
            }
            else if (named[0].Reason.Length == 0)
            {
                problems.Add($"{name} has no reason");
            }
            else if (named[0].Sources.Count == 0)
            {
                problems.Add($"{name} has no source");
            }
        }

        problems.AddRange(levels.SelectMany(l => l.Sources).Concat(summary.Select(s => s.Source)).Where(s => !sources.Contains(s)).Distinct(StringComparer.Ordinal)
            .Select(s => $"'{s}' is not one of the sources given"));
        return problems.Count > 0
            ? Result.Fail<SupplierProposal>(LlmProblems.InvalidReply(string.Join("; ", problems)))
            : Result.Ok(new SupplierProposal([.. Scales.Select(s => levels.Single(l => l.Name == s.Name))], summary));
    }

    /// <summary>Asks the model for a supplier proposal from the person's answers and the documents they kept, with the supplier skills.</summary>
    public static async Task<Result<SupplierProposal>> DraftAsync(
        IDraftingModel model, IReadOnlyList<Skill> skills, SupplierChoice choice, string provides, IReadOnlyList<DraftInput> documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(documents);
        var answers = $"Supplier: {choice.EntityName}\nSolution: {choice.SolutionName}\nContract: {choice.ContractName}\nWhat it provides: {provides}";
        var request = StepRequest.Compose(Instructions, skills, [new DraftInput(Answers, answers), .. documents], Schema(), Step);
        var reply = await model.DraftAsync(request, cancellationToken);
        return reply.Bind(r => Read(r.Value, [Answers, .. documents.Select(d => d.Name)]));
    }

    private static int Int(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<int>(out var number) ? number : -1;
}

/// <summary>Who drafted a supplier file: a person answering <c>hound onboard</c>, or an agent through <c>hound supplier draft</c>.</summary>
public enum SupplierDrafter
{
    Person,
    Agent,
}

/// <summary>Writes the drafted supplier as <c>scope/third-parties/&lt;entity&gt;.yaml</c>: levels with their reasons as comments, and a draft contract.</summary>
public static class SupplierFile
{
    public static string FileName(SupplierChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return $"{choice.EntityRef.ToLowerInvariant()}.yaml";
    }

    public static string Write(SupplierChoice choice, IReadOnlyList<LevelProposal> levels, string? system, DateOnly today, AuditPlace? place = null, SupplierDrafter drafter = SupplierDrafter.Person)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(levels);
        var text = new StringBuilder();
        var agent = drafter == SupplierDrafter.Agent;
        var forSystem = string.IsNullOrWhiteSpace(system) ? "" : $" for {system}";
        text.Append(CultureInfo.InvariantCulture, $"# Drafted by {(agent ? "an agent with hound supplier draft" : "hound onboard")}{forSystem} on {today:yyyy-MM-dd}. Review each level and its reason.\n");
        text.Append(agent
            ? "# A person approves the supplier and concludes its review with hound approve, in a terminal.\n"
            : "# Once the supplier is approved, make the contract active and run hound onboard again.\n");
        text.Append(CultureInfo.InvariantCulture, $"ref_id: {Quote(choice.EntityRef)}\nname: {Quote(choice.EntityName)}\n");
        if (place is not null)
        {
            text.Append(CultureInfo.InvariantCulture, $"domain: {Quote(place.Domain)}\n");
        }

        foreach (var (name, low, high) in SupplierProposal.Scales.Where(s => s.Name != "criticality"))
        {
            Level(text, "", name, low, high, levels);
        }

        text.Append(CultureInfo.InvariantCulture, $"solutions:\n  - ref_id: {Quote(choice.SolutionRef)}\n    name: {Quote(choice.SolutionName)}\n");
        Level(text, "    ", "criticality", 1, 4, levels);
        text.Append(CultureInfo.InvariantCulture, $"contracts:\n  - ref_id: {Quote(choice.ContractRef)}\n    name: {Quote(choice.ContractName)}\n    status: draft\n");
        text.Append(CultureInfo.InvariantCulture, $"    solutions: [{Quote(choice.SolutionRef)}]\n");
        if (place is null)
        {
            text.Append("# assessments: add a review (ref_id, name, solutions, perimeter, due_date, review_every) for the GRC team to conclude in CISO Assistant.\n");
            return text.ToString();
        }

        text.Append(agent
            ? "# The supplier's review: a person concludes it with hound approve, and a system it supplies is registered only once it is concluded ok.\n"
            : "# The supplier's review: the GRC team concludes it in CISO Assistant, and a system it supplies is registered only once it is concluded ok.\n");
        text.Append(CultureInfo.InvariantCulture, $"assessments:\n  - ref_id: {Quote($"EA-{choice.EntityRef}")}\n    name: {Quote($"{choice.EntityName} review")}\n");
        text.Append(CultureInfo.InvariantCulture, $"    solutions: [{Quote(choice.SolutionRef)}]\n    perimeter: {Quote(place.Perimeter)}\n");
        text.Append(CultureInfo.InvariantCulture, $"    due_date: \"{today.AddMonths(1):yyyy-MM-dd}\"\n    review_every: 12 months\n");
        return text.ToString();
    }

    private static void Level(StringBuilder text, string indent, string name, int low, int high, IReadOnlyList<LevelProposal> levels)
    {
        if (levels.FirstOrDefault(l => l.Name == name) is { } assessed && low > SupplierProposal.NotYetAssessed && assessed.Value == SupplierProposal.NotYetAssessed)
        {
            text.Append(CultureInfo.InvariantCulture, $"{indent}# {name}: not yet assessed ({low} to {high}): {OneLine(assessed.Reason)} ({string.Join(", ", assessed.Sources)})\n");
        }
        else if (levels.FirstOrDefault(l => l.Name == name) is { } level)
        {
            text.Append(CultureInfo.InvariantCulture, $"{indent}# {name}: {OneLine(level.Reason)} ({string.Join(", ", level.Sources)})\n{indent}{name}: {level.Value}\n");
        }
        else
        {
            text.Append(CultureInfo.InvariantCulture, $"{indent}# {name}: to decide ({low} to {high})\n");
        }
    }

    private static string OneLine(string text) => string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>A JSON string is a valid YAML double-quoted scalar.</summary>
    private static string Quote(string value) => JsonSerializer.Serialize(value);
}
