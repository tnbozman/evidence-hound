using System.IO.Enumeration;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.VulnScan;

/// <summary>The config keys of a vuln-scan source.</summary>
public static class VulnScanSettings
{
    public const string DropFolder = "drop_folder";
    public const string Format = "format";
    public const string Scope = "scope";
    public const string Owner = "owner";
    public const string ColumnHost = "column_host";
    public const string ColumnFindingId = "column_finding_id";
    public const string ColumnSeverity = "column_severity";
    public const string ColumnName = "column_name";
    public const string ColumnCve = "column_cve";
    public const string ColumnFirstSeen = "column_first_seen";
    public const string ColumnPatchPublished = "column_patch_published";
    public const string ColumnExploitAvailable = "column_exploit_available";
    public const string ColumnUnsupported = "column_unsupported";
    public const string ColumnScanDate = "column_scan_date";
}

/// <summary>The export formats a vuln-scan source reads.</summary>
public enum ScanFormat
{
    Nessus,
    Csv,
}

/// <summary>
/// One vuln-scan source, read from a collector instance's settings: the folder scanner exports are dropped in, their format, the
/// Essential Eight scope the scans cover, who owns them, and for CSV the column map.
/// </summary>
public sealed record VulnScanSource(string DropFolder, ScanFormat Format, string Scope, string Owner, ColumnMap? Columns)
{
    /// <summary>The scopes a scan can cover, as the Essential Eight patching requirements group them.</summary>
    public static IReadOnlyList<string> Scopes { get; } = ["internet_facing", "common_apps", "other_apps", "operating_systems"];

    private static readonly string Missing = "vuln_scan.setting_missing";

    private static readonly string Invalid = "vuln_scan.setting_invalid";

    /// <summary>The file pattern the format reads: <c>*.nessus</c> or <c>*.csv</c>.</summary>
    public string Pattern => Format == ScanFormat.Nessus ? "*.nessus" : "*.csv";

    /// <summary>Whether <paramref name="fileName"/> is an export of this source's format, ignoring case.</summary>
    public bool Accepts(string fileName) => FileSystemName.MatchesSimpleExpression(Pattern, fileName, ignoreCase: true);

    public static Result<VulnScanSource> FromSettings(CollectorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var problems = new List<Problem>();
        string Required(string name, string? when = null)
        {
            if (settings.Find(name) is { Length: > 0 } value && value.Trim().Length > 0)
            {
                return value.Trim();
            }

            problems.Add(new Problem(Missing, when is null ? $"Setting '{name}' is required." : $"Setting '{name}' is required when {when}."));
            return string.Empty;
        }

        var folder = Required(VulnScanSettings.DropFolder);
        var formatText = Required(VulnScanSettings.Format);
        var format = formatText switch
        {
            "nessus" => ScanFormat.Nessus,
            "csv" => ScanFormat.Csv,
            "" => null,
            _ => Reject<ScanFormat?>(problems, $"Setting '{VulnScanSettings.Format}' must be nessus or csv, not '{formatText}'.", null),
        };
        var scope = Required(VulnScanSettings.Scope);
        if (scope.Length > 0 && !Scopes.Contains(scope, StringComparer.Ordinal))
        {
            Reject(problems, $"Setting '{VulnScanSettings.Scope}' must be one of {string.Join(", ", Scopes)}, not '{scope}'.", 0);
        }

        var owner = Required(VulnScanSettings.Owner);
        ColumnMap? columns = null;
        if (format == ScanFormat.Csv)
        {
            var when = "format is csv";
            var host = Required(VulnScanSettings.ColumnHost, when);
            var id = Required(VulnScanSettings.ColumnFindingId, when);
            var severity = Required(VulnScanSettings.ColumnSeverity, when);
            string? Optional(string name) => settings.Find(name) is { } text && text.Trim().Length > 0 ? text.Trim() : null;
            columns = new ColumnMap(host, id, severity)
            {
                Name = Optional(VulnScanSettings.ColumnName),
                Cve = Optional(VulnScanSettings.ColumnCve),
                FirstSeen = Optional(VulnScanSettings.ColumnFirstSeen),
                PatchPublished = Optional(VulnScanSettings.ColumnPatchPublished),
                ExploitAvailable = Optional(VulnScanSettings.ColumnExploitAvailable),
                Unsupported = Optional(VulnScanSettings.ColumnUnsupported),
                ScanDate = Optional(VulnScanSettings.ColumnScanDate),
            };
        }

        return problems.Count > 0 ? Result.Fail<VulnScanSource>(problems) : Result.Ok(new VulnScanSource(folder, format!.Value, scope, owner, columns));
    }

    private static T Reject<T>(List<Problem> problems, string message, T fallback)
    {
        problems.Add(new Problem(Invalid, message));
        return fallback;
    }
}
