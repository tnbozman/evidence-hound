using System.Text;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.VulnScan;

/// <summary>
/// The built-in <c>vuln-scan</c> collector. Each run reads the scanner exports in the drop folder (Nessus v2, or CSV through a column
/// map), takes the export whose scan ran last as the current scan, and emits that export, the parsed scan (<see cref="ScanFile"/>)
/// and a list of every export it read (<see cref="HistoryFile"/>). Any export that cannot be read fails the run.
/// </summary>
public sealed class VulnScanCollector : ICollector
{
    public const string TypeName = "vuln-scan";

    public const string ScanFile = "scan.json";

    public const string HistoryFile = "scan-history.json";

    public string Type => TypeName;

    public string Version => "1.0.0";

    public CollectorDescription Describe() => new(
        [],
        [
            new(VulnScanSettings.DropFolder, "Folder the scanner exports are dropped in. A relative path is resolved against the config file's folder.", Required: true, IsPath: true),
            new(VulnScanSettings.Format, "Export format: nessus (Nessus v2 .nessus files) or csv (with the column settings below).", Required: true),
            new(VulnScanSettings.Scope, $"What the scans cover: {string.Join(", ", VulnScanSource.Scopes)}.", Required: true),
            new(VulnScanSettings.Owner, "Who owns the scans and fixes what they find.", Required: true),
            new(VulnScanSettings.ColumnHost, "CSV only, required: the column with the host name.", Required: false),
            new(VulnScanSettings.ColumnFindingId, "CSV only, required: the column that identifies the finding, such as a plugin ID or CVE.", Required: false),
            new(VulnScanSettings.ColumnSeverity, "CSV only, required: the severity column (info, low, medium, high, critical, or 0 to 4).", Required: false),
            new(VulnScanSettings.ColumnName, "CSV only: the column with the finding's title or software name. Default: the finding ID.", Required: false),
            new(VulnScanSettings.ColumnCve, "CSV only: the column with CVE IDs.", Required: false),
            new(VulnScanSettings.ColumnFirstSeen, "CSV only: the column with when the finding was first seen.", Required: false),
            new(VulnScanSettings.ColumnPatchPublished, "CSV only: the column with when the vendor published the patch.", Required: false),
            new(VulnScanSettings.ColumnExploitAvailable, "CSV only: the column saying whether an exploit exists.", Required: false),
            new(VulnScanSettings.ColumnUnsupported, "CSV only: the column saying whether the software is no longer supported by its vendor.", Required: false),
            new(VulnScanSettings.ColumnScanDate, "CSV only: the column with when the scan saw the finding. Default: when the export file was written.", Required: false),
        ],
        [])
    {
        ChecksFrom = _ => [],
    };

    public async Task<Result<CollectionResult>> CollectAsync(CollectorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return await VulnScanSource.FromSettings(context.Settings)
            .BindAsync(async source => (await ReadAsync(source, cancellationToken)).Bind(files => Assess(source, files)));
    }

    private static Result<CollectionResult> Assess(VulnScanSource source, IReadOnlyList<ExportFile> files) =>
        ScanFolder.Read(files, file => Parse(source, file)).Map(folder =>
        {
            var hashes = files.GroupBy(f => f.FileName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var runs = (folder.Current is { } current ? [current, .. folder.History] : folder.History).Select(r => (r, hashes[r.FileName].ContentHash));
            IReadOnlyList<CollectedEvidence> evidence = folder.Current is { } scan
                ?
                [
                    Export(source, scan.FileName, hashes[scan.FileName].Content),
                    ScanEvidence.Scan(source, scan, hashes[scan.FileName].ContentHash),
                    ScanEvidence.History(source, runs),
                ]
                : [ScanEvidence.History(source, runs)];
            return new CollectionResult([], evidence, []);
        });

    /// <summary>
    /// The export as evidence. CISO Assistant refuses a <c>.nessus</c> attachment, so a Nessus export is stored with <c>.xml</c> added;
    /// the scan record keeps its original name.
    /// </summary>
    private static CollectedEvidence Export(VulnScanSource source, string fileName, ReadOnlyMemory<byte> content) => source.Format == ScanFormat.Nessus
        ? CollectedEvidence.FromBytes(fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".xml", "application/xml", content)
        : CollectedEvidence.FromBytes(fileName, "text/csv", content);

    private static Result<ScanRun> Parse(VulnScanSource source, ExportFile file) => source.Format == ScanFormat.Nessus
        ? NessusParser.Parse(file.FileName, file.Content)
        : CsvScanParser.Parse(file.FileName, Encoding.UTF8.GetString(file.Content.Span), source.Columns!, file.ModifiedAt);

    private static async Task<Result<IReadOnlyList<ExportFile>>> ReadAsync(VulnScanSource source, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source.DropFolder))
        {
            return Result.Fail<IReadOnlyList<ExportFile>>(new Problem(
                "vuln_scan.drop_folder_missing", $"Drop folder '{source.DropFolder}' does not exist. Create it, or check the '{VulnScanSettings.DropFolder}' setting."));
        }

        var files = new List<ExportFile>();
        foreach (var path in Directory.EnumerateFiles(source.DropFolder).Where(p => source.Accepts(Path.GetFileName(p))).Order(StringComparer.Ordinal))
        {
            files.Add(new ExportFile(
                Path.GetFileName(path),
                await File.ReadAllBytesAsync(path, cancellationToken),
                new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)));
        }

        return Result.Ok<IReadOnlyList<ExportFile>>(files);
    }
}
