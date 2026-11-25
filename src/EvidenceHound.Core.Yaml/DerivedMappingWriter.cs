using System.Globalization;
using System.Text;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Yaml;

/// <summary>s14-01: writes a derived mapping as a mapping file whose rows are all <c>draft: true</c>, with the rows that got no checks as comments.</summary>
public static class DerivedMappingWriter
{
    public static string Write(DerivedMapping derived, string origin)
    {
        ArgumentNullException.ThrowIfNull(derived);
        ArgumentNullException.ThrowIfNull(origin);
        var text = new StringBuilder()
            .AppendLine("# yaml-language-server: $schema=../schemas/mapping.schema.json")
            .AppendLine(CultureInfo.InvariantCulture, $"# Drafted by `hound mapping derive` with the transition mapping {origin}.")
            .AppendLine("# Every row is `draft: true`: runs do not publish it. Review each row (do these checks show this action is met in this domain?),")
            .AppendLine("# change what is wrong, then delete its `draft: true` line. Rows the source mapping gave no checks are listed at the end.")
            .AppendLine(CultureInfo.InvariantCulture, $"framework_urn: {derived.FrameworkUrn}");
        var mapped = derived.Requirements.Where(r => r.Checks.Count > 0).ToList();
        text.AppendLine(mapped.Count == 0 ? "requirements: []" : "requirements:");
        foreach (var row in mapped)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  # {row.RefId} ({row.Domain}), from {string.Join(", ", row.SourceRefs)}")
                .AppendLine(CultureInfo.InvariantCulture, $"  - urn: {row.Urn}")
                .AppendLine("    draft: true")
                .AppendLine("    rule: all_pass")
                .AppendLine("    checks:");
            foreach (var check in row.Checks)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"      - {check}");
            }
        }

        var open = derived.Requirements.Where(r => r.Checks.Count == 0).ToList();
        if (open.Count > 0)
        {
            text.AppendLine("# No checks from the source mapping: map a manual source's checks to each row, or list it under manual_only with a")
                .AppendLine("# suggested source (see docs/mapping-files.md).");
            foreach (var row in open)
            {
                var linked = row.SourceRefs.Count == 0 ? "" : $"; linked from {string.Join(", ", row.SourceRefs)}";
                text.AppendLine(CultureInfo.InvariantCulture, $"#   - urn: {row.Urn}   # {row.RefId} ({row.Domain}): no source checks{linked}");
            }
        }

        return text.ToString();
    }
}
