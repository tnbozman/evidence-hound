using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.VulnScan;

/// <summary>
/// Pure: the scans in a drop folder. The same content dropped twice is read once, under its newest name. The export whose scan ran
/// last is the current scan; the others are its history, newest first. One export that cannot be read fails the whole folder, so
/// nothing is published from a partial read.
/// </summary>
public sealed record ScanFolder(ScanRun? Current, IReadOnlyList<ScanRun> History)
{
    public static Result<ScanFolder> Read(IReadOnlyList<ExportFile> files, Func<ExportFile, Result<ScanRun>> parse)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(parse);
        var distinct = files
            .OrderByDescending(f => f.ModifiedAt).ThenBy(f => f.FileName, StringComparer.Ordinal)
            .DistinctBy(f => f.ContentHash, StringComparer.Ordinal);
        return Result.Combine(distinct.Select(parse)).Map(runs =>
        {
            var newest = runs.OrderByDescending(r => r.ScanDate).ThenBy(r => r.FileName, StringComparer.Ordinal).ToArray();
            return new ScanFolder(newest.FirstOrDefault(), [.. newest.Skip(1)]);
        });
    }
}
