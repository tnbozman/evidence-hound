using System.Text.Json;
using System.Text.Json.Serialization;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Results;
using EvidenceHound.Runtime.Pipeline;

namespace EvidenceHound.Runtime.Storage;

/// <summary>s14-05: one collector's latest check results and evidence, and the stored run its evidence files are in.</summary>
public sealed record LatestResults(string Collector, CollectorResults Results, string Location);

/// <summary>s14-05: where each collector's latest results are saved, so a row can join checks from several collectors.</summary>
public interface ILatestResults
{
    /// <summary>The latest saved results of every configured collector except <paramref name="collector"/>.</summary>
    Task<IReadOnlyList<LatestResults>> OthersAsync(string collector, CancellationToken cancellationToken);

    Task SaveAsync(LatestResults latest, CancellationToken cancellationToken);
}

/// <summary>Nothing saved: each collector only fills the rows it fully reports.</summary>
public sealed class NoLatestResults : ILatestResults
{
    public static readonly NoLatestResults Instance = new();

    public Task<IReadOnlyList<LatestResults>> OthersAsync(string collector, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LatestResults>>([]);

    public Task SaveAsync(LatestResults latest, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Saves each collector's latest results as <c>&lt;root&gt;/_latest/&lt;collector&gt;.json</c>. Only the configured collectors are read, so a
/// collector removed from the config stops counting. A file that cannot be read is skipped: its rows wait for that collector's next run.
/// </summary>
public sealed class FileLatestResults(string root, IReadOnlyCollection<string> configured) : ILatestResults
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        WriteIndented = true,
    };

    private string Folder => Path.Combine(root, "_latest");

    public async Task<IReadOnlyList<LatestResults>> OthersAsync(string collector, CancellationToken cancellationToken)
    {
        var others = new List<LatestResults>();
        foreach (var name in configured.Where(n => n != collector && Safe(n)).Order(StringComparer.Ordinal))
        {
            var path = Path.Combine(Folder, name + ".json");
            if (File.Exists(path) && await ReadAsync(path, cancellationToken) is { } latest)
            {
                others.Add(latest);
            }
        }

        return others;
    }

    public async Task SaveAsync(LatestResults latest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(latest);
        if (!Safe(latest.Collector))
        {
            return;
        }

        Directory.CreateDirectory(Folder);
        var saved = new Saved(
            latest.Collector,
            latest.Location,
            [.. latest.Results.Checks.Select(c => new SavedCheck(c.CheckId.Value, c.Outcome, c.Observation, [.. c.EvidenceRefs], c.CollectedAt, c.Source))],
            [.. latest.Results.Evidence.Select(e => new SavedEvidence(e.Id, e.FileName, e.MediaType, e.Sha256, e.SizeBytes, e.CollectedAt))]);
        var path = Path.Combine(Folder, latest.Collector + ".json");
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(saved, Json), cancellationToken);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    private static bool Safe(string name) => name.Length > 0 && Path.GetFileName(name) == name && name is not ("." or "..");

    private static async Task<LatestResults?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var saved = JsonSerializer.Deserialize<Saved>(await File.ReadAllTextAsync(path, cancellationToken), Json);
            if (saved is null)
            {
                return null;
            }

            var checks = Result.Combine(saved.Checks.Select(c => CheckId.Create(c.CheckId)
                .Bind(id => CheckResult.Create(id, c.Outcome, c.Observation, c.EvidenceRefs, c.CollectedAt, c.Source))));
            var evidence = Result.Combine(saved.Evidence.Select(e => Evidence.Create(e.Id, e.FileName, e.MediaType, e.Sha256, e.SizeBytes, e.CollectedAt)));
            return checks.Bind(c => evidence.Map(e => new LatestResults(saved.Collector, new CollectorResults(c, e, []), saved.Location)))
                .Match<LatestResults?>(latest => latest, _ => null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private sealed record Saved(string Collector, string Location, IReadOnlyList<SavedCheck> Checks, IReadOnlyList<SavedEvidence> Evidence);

    private sealed record SavedCheck(string CheckId, CheckOutcome Outcome, string Observation, IReadOnlyList<string> EvidenceRefs, DateTimeOffset CollectedAt, string Source);

    private sealed record SavedEvidence(string Id, string FileName, string MediaType, string Sha256, long SizeBytes, DateTimeOffset CollectedAt);
}
