using System.Text.Json;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Status;

/// <summary>One run's effect on one audit: when, which run, and how many rows it changed, verified unchanged and restored from drift.</summary>
public sealed record HistoryEntry(DateTimeOffset Date, string RunId, int Changed, int Unchanged, int Drift);

/// <summary>An append-only history of runs per audit.</summary>
public interface IRunHistory
{
    Task AppendAsync(string auditRefId, HistoryEntry entry, CancellationToken cancellationToken);

    /// <summary>The latest entry of every audit that has one, by audit ref_id.</summary>
    Task<Result<IReadOnlyList<(string Audit, HistoryEntry Entry)>>> LatestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Appends one JSON line per run to <c>&lt;root&gt;/_history/&lt;audit&gt;.jsonl</c>: <c>date</c>, <c>run_id</c>, <c>changed</c>,
/// <c>unchanged</c> and <c>drift</c>. Lines are never rewritten.
/// </summary>
public sealed class FileRunHistory(string root) : IRunHistory
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    private readonly string _folder = Path.Combine(root, "_history");

    public async Task AppendAsync(string auditRefId, HistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(auditRefId);
        ArgumentNullException.ThrowIfNull(entry);
        Directory.CreateDirectory(_folder);
        await File.AppendAllTextAsync(Path.Combine(_folder, Path.GetFileName(auditRefId) + ".jsonl"), JsonSerializer.Serialize(entry, Options) + "\n", cancellationToken);
    }

    public async Task<Result<IReadOnlyList<(string Audit, HistoryEntry Entry)>>> LatestAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_folder))
        {
            return Result.Ok<IReadOnlyList<(string Audit, HistoryEntry Entry)>>([]);
        }

        var latest = new List<Result<(string Audit, HistoryEntry Entry)>>();
        foreach (var path in Directory.EnumerateFiles(_folder, "*.jsonl").Order(StringComparer.Ordinal))
        {
            var last = (await File.ReadAllLinesAsync(path, cancellationToken)).LastOrDefault(line => line.Length > 0);
            if (last is not null)
            {
                latest.Add(Parse(last, path).Map(entry => (Path.GetFileNameWithoutExtension(path), entry)));
            }
        }

        return Result.Combine(latest);
    }

    private static Result<HistoryEntry> Parse(string line, string source)
    {
        try
        {
            return JsonSerializer.Deserialize<HistoryEntry>(line, Options) is { } entry
                ? Result.Ok(entry)
                : Invalid(source, "the line holds no entry.");
        }
        catch (JsonException ex)
        {
            return Invalid(source, ex.Message);
        }
    }

    private static Result<HistoryEntry> Invalid(string source, string why) =>
        Result.Fail<HistoryEntry>(new Problem("status.history_invalid", $"Run history {source} cannot be read: {why}"));
}
