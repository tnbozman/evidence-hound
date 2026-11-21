using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Status;

/// <summary>Keeps the latest run record per collector.</summary>
public interface IRunRecordStore
{
    Task SaveAsync(RunRecord record, CancellationToken cancellationToken);

    /// <summary>The latest record of every collector that has run, by collector name.</summary>
    Task<Result<IReadOnlyList<RunRecord>>> LatestAsync(CancellationToken cancellationToken);
}

/// <summary>Stores each collector's latest run record as <c>&lt;root&gt;/_status/&lt;collector&gt;.json</c>. Collector names never start with <c>_</c>.</summary>
public sealed class FileRunRecordStore(string root) : IRunRecordStore
{
    private readonly string _folder = Path.Combine(root, "_status");

    public async Task SaveAsync(RunRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, Path.GetFileName(record.Collector) + ".json"), RunRecords.ToJson(record), cancellationToken);
    }

    public async Task<Result<IReadOnlyList<RunRecord>>> LatestAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_folder))
        {
            return Result.Ok<IReadOnlyList<RunRecord>>([]);
        }

        var records = new List<Result<RunRecord>>();
        foreach (var path in Directory.EnumerateFiles(_folder, "*.json").Order(StringComparer.Ordinal))
        {
            records.Add(RunRecords.Parse(await File.ReadAllTextAsync(path, cancellationToken), path));
        }

        return Result.Combine(records).Map(list => (IReadOnlyList<RunRecord>)[.. list.OrderBy(r => r.Collector, StringComparer.Ordinal)]);
    }
}
