using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Onboarding.Capabilities;

/// <summary><c>scope/capabilities.yaml</c> as text, one decision per mitigation in the framework's order. Pure.</summary>
public static class CapabilityFile
{
    public static string Write(string frameworkUrn, IReadOnlyList<ScopeCapability> decisions, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        var text = new StringBuilder()
            .Append("# yaml-language-server: $schema=../schemas/scope.schema.json\n")
            .Append(CultureInfo.InvariantCulture, $"# Which system provides each mitigation, decided with hound onboard environment (last {today:yyyy-MM-dd}). Review it in a pull request.\n")
            .Append("# Results stay in each system's own audit: a mitigation another system provides is not applicable there, naming that system.\n")
            .Append(CultureInfo.InvariantCulture, $"framework: {Quote(frameworkUrn)}\nmitigations:\n");
        foreach (var d in decisions)
        {
            text.Append(CultureInfo.InvariantCulture, $"  - mitigation: {Quote(d.Mitigation)}\n");
            Line(text, "name", d.Name);
            text.Append(CultureInfo.InvariantCulture, $"    provided_by: {d.ProvidedBy.ToString().ToLowerInvariant()}\n");
            if (d.Systems.Count > 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"    systems: [{string.Join(", ", d.Systems.Select(s => Quote(s.Value)))}]\n");
            }

            Line(text, "supplier", d.Supplier?.Value);
            Line(text, "owner", d.Owner);
            Line(text, "evidence", d.Evidence);
            Line(text, "route", d.Route);
            Line(text, "approved_by", d.ApprovedBy);
            Line(text, "approved_on", d.ApprovedOn);
            Line(text, "reason", d.Reason);
            if (d.Signals.Count > 0)
            {
                text.Append("    signals:\n").Append(string.Concat(d.Signals.Select(s => $"      - {Quote(s)}\n")));
            }
        }

        return text.ToString();
    }

    private static void Line(StringBuilder text, string key, string? value)
    {
        if (value is { Length: > 0 })
        {
            text.Append(CultureInfo.InvariantCulture, $"    {key}: {Quote(value)}\n");
        }
    }

    /// <summary>A JSON string is a valid YAML double-quoted scalar.</summary>
    private static string Quote(string value) => JsonSerializer.Serialize(value, Readable);

    /// <summary>Keeps apostrophes and accented letters as they are, so the file reads well in review; quotes and control characters are still escaped.</summary>
    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
