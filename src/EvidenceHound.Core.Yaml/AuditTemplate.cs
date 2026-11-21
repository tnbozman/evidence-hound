using System.Globalization;
using System.Text;
using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Yaml;

/// <summary>Writes a starting audit file: every in-group requirement, titled in a comment, to do and not assessed.</summary>
public static class AuditTemplate
{
    /// <summary>
    /// The audit file for <paramref name="refId"/> over the requirements of <paramref name="groups"/>, in framework order. A requirement
    /// is named by its ref_id, or by its URN when the framework repeats the ref or has none. Domain and perimeter are left to fill in.
    /// </summary>
    public static string Write(FrameworkSnapshot snapshot, IReadOnlyList<string> groups, string refId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentException.ThrowIfNullOrWhiteSpace(refId);

        var repeated = snapshot.Nodes.Where(n => n.RefId is not null).GroupBy(n => n.RefId!, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var inGroups = snapshot.Nodes.Where(n => groups.Count == 0 || n.Groups.Count == 0 || n.Groups.Any(groups.Contains)).ToArray();

        var yaml = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"# Written by hound scope new-audit from {snapshot.FrameworkUrn} version {snapshot.Version}.")
            .AppendLine("# Fill in the domain and perimeter, then replace each assessment that a document, a control or a check covers.")
            .AppendLine(CultureInfo.InvariantCulture, $"ref_id: {refId}")
            .AppendLine(CultureInfo.InvariantCulture, $"name: {refId}")
            .AppendLine(CultureInfo.InvariantCulture, $"framework: {snapshot.FrameworkUrn}")
            .AppendLine(CultureInfo.InvariantCulture, $"groups: [{string.Join(", ", groups.Select(g => $"\"{g}\""))}]")
            .AppendLine("domain: TODO        # a domain declared in scope/setup.yaml")
            .AppendLine("perimeter: TODO     # a perimeter of that domain")
            .AppendLine("systems: []")
            .AppendLine("documents: []")
            .AppendLine("controls: []")
            .AppendLine(inGroups.Length == 0 ? "assessments: []" : "assessments:");
        foreach (var node in inGroups)
        {
            yaml.AppendLine(CultureInfo.InvariantCulture, $"  # {node.Title}")
                .AppendLine(node.RefId is { } nodeRef && !repeated.Contains(nodeRef) ? $"  - ref: {Quoted(nodeRef)}" : $"  - urn: {node.Urn}")
                .AppendLine("    status: to_do")
                .AppendLine("    result: not_assessed");
        }

        return yaml
            .AppendLine("unlisted:")
            .AppendLine("  result: not_assessed")
            .ToString()
            .ReplaceLineEndings("\n");
    }

    /// <summary>A ref as YAML: plain when it is letters, digits, dots, dashes and underscores; quoted otherwise.</summary>
    private static string Quoted(string value) =>
        value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') && !char.IsAsciiDigit(value[0]) && value[0] != '-'
            ? value
            : $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
