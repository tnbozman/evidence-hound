using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Review;

namespace EvidenceHound.Cli;

/// <summary>
/// The person's challenges from review sessions, in <c>review/challenges.json</c>, by item key: the agent reads and answers each before the
/// next session, and an item's challenge goes once the item is accepted.
/// </summary>
internal static class Challenges
{
    private static readonly string Relative = "review/challenges.json";

    public static async Task<Dictionary<string, ReviewChallenge>> ReadAsync(string repo, CancellationToken cancellationToken)
    {
        var path = Path.Combine(repo, Relative);
        if (!File.Exists(path))
        {
            return new(StringComparer.Ordinal);
        }

        var held = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) as JsonObject ?? [];
        return held.Where(c => c.Value is JsonObject).ToDictionary(
            c => c.Key,
            c => new ReviewChallenge(Text(c.Value!["note"]), Text(c.Value!["by"]), Text(c.Value!["on"])),
            StringComparer.Ordinal);
    }

    public static async Task WriteAsync(
        string repo, IReadOnlyList<(string Key, string Note)> made, IEnumerable<string> accepted, string by, DateOnly on, CancellationToken cancellationToken)
    {
        var held = await ReadAsync(repo, cancellationToken);
        foreach (var key in accepted)
        {
            held.Remove(key);
        }

        foreach (var (key, note) in made)
        {
            held[key] = new ReviewChallenge(note, by, on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        var path = Path.Combine(repo, Relative);
        if (held.Count == 0 && !File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = new JsonObject();
        foreach (var (key, challenge) in held.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            json[key] = new JsonObject { ["note"] = challenge.Note, ["by"] = challenge.By, ["on"] = challenge.On };
        }

        await File.WriteAllTextAsync(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", cancellationToken);
    }

    private static string Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
}
