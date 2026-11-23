using System.Globalization;
using System.Text.RegularExpressions;
using EvidenceHound.Core.Packaging;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Runtime.Storage;

/// <summary>
/// Stores runs as <c>&lt;root&gt;/&lt;collector&gt;/&lt;UTC timestamp&gt;/</c> with the files and <c>_manifest.json</c>, written last,
/// so a folder without a manifest is an incomplete run.
/// </summary>
public sealed partial class FileEvidenceStore(string root) : IEvidenceStore
{
    private static readonly string FolderFormat = "yyyyMMdd'T'HHmmssfff'Z'";

    public async Task<Result<StoredRun>> WriteRunAsync(EvidenceManifest manifest, IReadOnlyList<CollectedFile> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(files);
        if (!CollectorName().IsMatch(manifest.Collector))
        {
            return Result.Fail<StoredRun>(new Problem("evidence.collector_name_invalid", $"Collector name '{manifest.Collector}' cannot be used as a folder name."));
        }

        var location = Path.Combine(root, manifest.Collector, manifest.CollectedAt.UtcDateTime.ToString(FolderFormat, CultureInfo.InvariantCulture));
        if (Directory.Exists(location))
        {
            return Result.Fail<StoredRun>(new Problem("evidence.run_exists", $"A run of {manifest.Collector} at {manifest.CollectedAt:O} is already stored in {location}."));
        }

        Directory.CreateDirectory(location);
        foreach (var file in files)
        {
            await File.WriteAllBytesAsync(Path.Combine(location, file.FileName), file.Content, cancellationToken);
        }

        await File.WriteAllTextAsync(Path.Combine(location, Manifests.FileName), Manifests.ToJson(manifest), cancellationToken);
        return Result.Ok(new StoredRun(manifest.Collector, manifest.CollectedAt, location, Complete: true));
    }

    public Task<IReadOnlyList<StoredRun>> ListRunsAsync(string collector, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(root, collector);
        IReadOnlyList<StoredRun> runs = !CollectorName().IsMatch(collector) || !Directory.Exists(folder)
            ? []
            : [.. Directory.EnumerateDirectories(folder)
                .Select(location => (Location: location, Parsed: DateTimeOffset.TryParseExact(
                    Path.GetFileName(location), FolderFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at), At: at))
                .Where(entry => entry.Parsed)
                .Select(entry => new StoredRun(collector, entry.At, entry.Location, File.Exists(Path.Combine(entry.Location, Manifests.FileName))))
                .OrderBy(run => run.StartedAt)];
        return Task.FromResult(runs);
    }

    public async Task<byte[]?> ReadFileAsync(StoredRun run, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(fileName);
        var path = Path.Combine(run.Location, fileName);
        return Path.GetFileName(fileName) == fileName && fileName is not ("." or "..") && File.Exists(path)
            ? await File.ReadAllBytesAsync(path, cancellationToken)
            : null;
    }

    public Task DeleteRunAsync(StoredRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        Directory.Delete(run.Location, recursive: true);
        return Task.CompletedTask;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CollectorName();
}
