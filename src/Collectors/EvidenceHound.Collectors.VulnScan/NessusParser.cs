using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using EvidenceHound.Core.Results;

namespace EvidenceHound.Collectors.VulnScan;

/// <summary>
/// Pure: reads a Nessus v2 export (<c>.nessus</c>). Each <c>ReportHost</c> is a host, named by its <c>host-fqdn</c> tag when it has one;
/// each <c>ReportItem</c> above severity 0 is a finding. The scan date is when the last host finished (<c>HOST_END_TIMESTAMP</c>,
/// else <c>HOST_END</c>, read as UTC). Document type definitions are refused.
/// </summary>
public static partial class NessusParser
{
    private static readonly string Root = "NessusClientData_v2";

    private static readonly string[] HostEndFormats = ["ddd MMM d HH:mm:ss yyyy", "ddd MMM dd HH:mm:ss yyyy"];

    public static Result<ScanRun> Parse(string fileName, ReadOnlyMemory<byte> content)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return Load(fileName, content).Bind(document => Read(fileName, document));
    }

    private static Result<XDocument> Load(string fileName, ReadOnlyMemory<byte> content)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, settings);
            return Result.Ok(XDocument.Load(reader, LoadOptions.SetLineInfo));
        }
        catch (XmlException e)
        {
            return Result.Fail<XDocument>(ScanProblems.Invalid(fileName, e.LineNumber, $"not well-formed XML ({e.Message})."));
        }
    }

    private static Result<ScanRun> Read(string fileName, XDocument document)
    {
        var root = document.Root!;
        if (root.Name.LocalName != Root)
        {
            return Result.Fail<ScanRun>(ScanProblems.Invalid(fileName, Line(root), $"not a Nessus v2 export (root element is {root.Name.LocalName}, not {Root})."));
        }

        var hosts = root.Elements("Report").Elements("ReportHost").ToArray();
        var findings = new List<Result<ScanFinding>>();
        var names = new List<string>();
        var ends = new List<DateTimeOffset>();
        foreach (var host in hosts)
        {
            var tags = host.Element("HostProperties")?.Elements("tag").ToDictionary(t => (string?)t.Attribute("name") ?? string.Empty, t => t.Value.Trim(), StringComparer.Ordinal)
                ?? [];
            var name = tags.GetValueOrDefault("host-fqdn") is { Length: > 0 } fqdn ? fqdn : (string?)host.Attribute("name") ?? string.Empty;
            names.Add(name);
            if (HostEnd(tags) is { } end)
            {
                ends.Add(end);
            }

            findings.AddRange(host.Elements("ReportItem").Where(i => (string?)i.Attribute("severity") != "0").Select(i => Finding(fileName, name, i)));
        }

        return Result.Combine(findings).Bind(all => ends.Count > 0
            ? Result.Ok(new ScanRun(fileName, ends.Max(), [.. names.Distinct(StringComparer.Ordinal)], all))
            : Result.Fail<ScanRun>(ScanProblems.Invalid(fileName, Line(root), "no host has a HOST_END or HOST_END_TIMESTAMP tag, so the scan date is unknown.")));
    }

    private static Result<ScanFinding> Finding(string fileName, string host, XElement item)
    {
        var id = (string?)item.Attribute("pluginID");
        if (string.IsNullOrWhiteSpace(id))
        {
            return Result.Fail<ScanFinding>(ScanProblems.Invalid(fileName, Line(item), "ReportItem has no pluginID."));
        }

        var severity = (string?)item.Attribute("severity") switch
        {
            "1" => Severity.Low,
            "2" => Severity.Medium,
            "3" => Severity.High,
            "4" => Severity.Critical,
            _ => (Severity?)null,
        };
        if (severity is not { } known)
        {
            return Result.Fail<ScanFinding>(ScanProblems.Invalid(fileName, Line(item), $"ReportItem {id} has severity '{(string?)item.Attribute("severity")}', not 0 to 4."));
        }

        var name = (string?)item.Attribute("pluginName") ?? id;
        var patch = item.Element("patch_publication_date")?.Value.Trim();
        DateTimeOffset? published = null;
        if (patch is { Length: > 0 })
        {
            if (!DateTimeOffset.TryParseExact(patch, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            {
                return Result.Fail<ScanFinding>(ScanProblems.Invalid(fileName, Line(item.Element("patch_publication_date")!), $"patch_publication_date '{patch}' is not yyyy/MM/dd."));
            }

            published = date;
        }

        return Result.Ok(new ScanFinding(
            host,
            id.Trim(),
            name,
            known,
            [.. item.Elements("cve").Select(c => c.Value.Trim()).Where(c => c.Length > 0)],
            null,
            published,
            IsTrue(item.Element("exploit_available")),
            IsTrue(item.Element("unsupported_by_vendor")) || UnsupportedName().IsMatch(name)));
    }

    private static DateTimeOffset? HostEnd(Dictionary<string, string> tags)
    {
        if (tags.GetValueOrDefault("HOST_END_TIMESTAMP") is { } stamp && long.TryParse(stamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        return tags.GetValueOrDefault("HOST_END") is { } text
            && DateTimeOffset.TryParseExact(Spaces().Replace(text, " "), HostEndFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var end)
            ? end
            : null;
    }

    private static bool IsTrue(XElement? element) => element is not null && bool.TryParse(element.Value.Trim(), out var value) && value;

    private static int Line(XObject node) => node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 1;

    [GeneratedRegex(@"\bUnsupported\b|\bSEoL\b", RegexOptions.CultureInvariant)]
    private static partial Regex UnsupportedName();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}
