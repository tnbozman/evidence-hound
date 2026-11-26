using System.Text.Json.Nodes;
using EvidenceHound.Core.Review;

namespace EvidenceHound.Cli;

/// <summary>
/// The agent's recommendations for items waiting on a person, in <c>review/recommendations.json</c>, by item key. The session shows each
/// with its item; one goes once its item is accepted.
/// </summary>
internal static class Recommendations
{
    private static readonly string Relative = "review/recommendations.json";

    public static async Task<Dictionary<string, ReviewRecommendation>> ReadAsync(string repo, CancellationToken cancellationToken)
    {
        var path = Path.Combine(repo, Relative);
        if (!File.Exists(path))
        {
            return new(StringComparer.Ordinal);
        }

        var held = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) as JsonObject ?? [];
        return held.Where(r => r.Value is JsonObject).ToDictionary(
            r => r.Key,
            r => new ReviewRecommendation(
                Text(r.Value!["answer"]) ?? "", Text(r.Value!["reasons"]) ?? "",
                [.. (r.Value!["sources"] as JsonArray ?? []).Select(Text).OfType<string>()]),
            StringComparer.Ordinal);
    }

    /// <summary>Records <paramref name="made"/> and drops the recommendations of the accepted items.</summary>
    public static async Task WriteAsync(string repo, IReadOnlyDictionary<string, ReviewRecommendation> made, IEnumerable<string> accepted, CancellationToken cancellationToken)
    {
        var held = await ReadAsync(repo, cancellationToken);
        foreach (var key in accepted)
        {
            held.Remove(key);
        }

        foreach (var (key, recommendation) in made)
        {
            held[key] = recommendation;
        }

        var path = Path.Combine(repo, Relative);
        if (held.Count == 0 && !File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = new JsonObject();
        foreach (var (key, r) in held.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            json[key] = new JsonObject { ["answer"] = r.Answer, ["reasons"] = r.Reasons, ["sources"] = new JsonArray([.. r.Sources.Select(s => (JsonNode)s)]) };
        }

        await File.WriteAllTextAsync(path, json.ToJsonString(CommandJson.Escaped) + "\n", cancellationToken);
    }

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
