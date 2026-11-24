using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EvidenceHound.Core.Scope;
using EvidenceHound.Onboarding.Output;

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

/// <summary>What is proposed for a supplier: every level with a reason and sources, and a summary drawn only from the given sources.</summary>
public sealed record SupplierProposal(IReadOnlyList<LevelProposal> Levels, IReadOnlyList<SummaryPoint> Summary)
{
    /// <summary>A level on a 1 to 4 scale that the inputs give no evidence for: left out of the supplier file for the person to decide.</summary>
    public const int NotYetAssessed = 0;

    /// <summary>Each level and its range, as CISO Assistant's stakeholder and solution scales have them.</summary>
    public static readonly IReadOnlyList<(string Name, int Low, int High)> Scales =
        [("dependency", 0, 4), ("penetration", 0, 4), ("maturity", 1, 4), ("trust", 1, 4), ("criticality", 1, 4)];

}

/// <summary>Who drafted a supplier file: a person answering <c>hound onboard</c>, or an agent through <c>hound supplier draft</c>.</summary>
public enum SupplierDrafter
{
    Person,
    Agent,
}

/// <summary>
/// What a draft says beyond the choice and the levels: the third party's description (such as which vendor a reseller stands for), and the
/// documents that were read and kept in the repo, each with its SHA-256.
/// </summary>
public sealed record SupplierNotes(string? Description, IReadOnlyList<(string Path, string Sha256)> Documents)
{
    /// <summary>The contract's terms as the supplier's records give them; each left out when unknown.</summary>
    public ContractTerms? Terms { get; init; }

    /// <summary>The sensitive data the solution handles, as a trust centre or contract says; the authority is left for a person to add.</summary>
    public IReadOnlyList<DraftedData> Data { get; init; } = [];
}

/// <summary>A kind of sensitive data (<c>cui</c>, <c>pii</c>, <c>export_controlled</c> or <c>classified</c>), what the solution does with it, and which.</summary>
public sealed record DraftedData(string Kind, string Role, string? Detail);

/// <summary>A contract's start and end (yyyy-MM-dd), its annual expense as a plain number, and that expense's ISO 4217 currency.</summary>
public sealed record ContractTerms(string? Start, string? End, string? AnnualExpense, string? Currency);

/// <summary>Writes the drafted supplier as <c>scope/third-parties/&lt;entity&gt;.yaml</c>: levels with their reasons as comments, and a draft contract.</summary>
public static class SupplierFile
{
    public static string FileName(SupplierChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return $"{choice.EntityRef.ToLowerInvariant()}.yaml";
    }

    public static string Write(
        SupplierChoice choice, IReadOnlyList<LevelProposal> levels, string? system, DateOnly today, AuditPlace? place = null, SupplierDrafter drafter = SupplierDrafter.Person, SupplierNotes? notes = null)
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
        foreach (var (path, sha256) in notes?.Documents ?? [])
        {
            text.Append(CultureInfo.InvariantCulture, $"# Kept document: {path} (sha256 {sha256})\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"ref_id: {Quote(choice.EntityRef)}\nname: {Quote(choice.EntityName)}\n");
        if (notes?.Description is { Length: > 0 } description)
        {
            text.Append(CultureInfo.InvariantCulture, $"description: {Quote(description)}\n");
        }

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
        if (notes is { Data.Count: > 0 })
        {
            text.Append("    # Add each authority (name, evidence, expires) once a document in the repo shows it.\n    data:\n");
            foreach (var data in notes.Data)
            {
                text.Append(CultureInfo.InvariantCulture, $"      - kind: {data.Kind}\n");
                if (data.Detail is not null)
                {
                    text.Append(CultureInfo.InvariantCulture, $"        detail: {Quote(data.Detail)}\n");
                }

                text.Append(CultureInfo.InvariantCulture, $"        role: {data.Role}\n");
            }
        }

        text.Append(CultureInfo.InvariantCulture, $"contracts:\n  - ref_id: {Quote(choice.ContractRef)}\n    name: {Quote(choice.ContractName)}\n    status: draft\n");
        foreach (var (key, value) in new[] { ("start", notes?.Terms?.Start), ("end", notes?.Terms?.End), ("annual_expense", notes?.Terms?.AnnualExpense), ("currency", notes?.Terms?.Currency) })
        {
            if (value is { Length: > 0 })
            {
                text.Append(CultureInfo.InvariantCulture, $"    {key}: {(key == "annual_expense" ? value : Quote(value))}\n");
            }
        }

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
