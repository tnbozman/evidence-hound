using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Review;

namespace EvidenceHound.Cli;

/// <summary>The standing approvals a person signed in a review session, in <c>review/standing-approvals.json</c>.</summary>
internal static class StandingApprovalFile
{
    private static readonly string Relative = "review/standing-approvals.json";

    public static async Task<IReadOnlyList<StandingApproval>> ReadAsync(string repo, CancellationToken cancellationToken)
    {
        var path = Path.Combine(repo, Relative);
        if (!File.Exists(path))
        {
            return [];
        }

        return [.. (JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) as JsonArray ?? []).OfType<JsonObject>().Select(Read).OfType<StandingApproval>()];
    }

    public static async Task AddAsync(string repo, StandingApproval approval, CancellationToken cancellationToken)
    {
        var held = (await ReadAsync(repo, cancellationToken)).Where(a => !(a.Rule == approval.Rule && a.Source == approval.Source)).Append(approval);
        var path = Path.Combine(repo, Relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = new JsonArray([.. held.Select(a => (JsonNode)new JsonObject
        {
            ["rule"] = a.Rule,
            ["source"] = a.Source,
            ["approved_by"] = a.ApprovedBy,
            ["approved_on"] = Day(a.ApprovedOn),
            ["expires"] = Day(a.Expires),
        })]);
        await File.WriteAllTextAsync(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", cancellationToken);
    }

    public static string Describe(StandingApproval approval) =>
        $"{(approval.Rule == StandingApprovals.UnchangedReattestation ? "unchanged re-attestation" : approval.Rule)} of {approval.Source}, signed by {approval.ApprovedBy} on {Day(approval.ApprovedOn)}, until {Day(approval.Expires)}";

    public static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static StandingApproval? Read(JsonObject o) =>
        Text(o["rule"]) is { } rule && Text(o["source"]) is { } source && Text(o["approved_by"]) is { } by && Date(o["approved_on"]) is { } on && Date(o["expires"]) is { } expires
            ? new StandingApproval(rule, source, by, on, expires)
            : null;

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static DateOnly? Date(JsonNode? node) =>
        DateOnly.TryParseExact(Text(node), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}
