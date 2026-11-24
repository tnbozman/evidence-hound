namespace EvidenceHound.Core.Scope;

/// <summary>
/// Each requirement's status, statement and responsible party, mapped the same way every time (s11-03). The status follows the result; a
/// requirement not met whose controls have an ETA is Planned. The statement is the linked controls' descriptions in ref order, then the
/// observation. A row in place without a statement, a row in place or planned without an owner, and a row not applicable without a
/// justification are gaps; a row not yet implemented is not, as the POA&amp;M carries it. Pure.
/// </summary>
public static class SspStatements
{
    public static readonly string Implemented = "Implemented";
    public static readonly string PartiallyImplemented = "Partially implemented";
    public static readonly string Planned = "Planned";
    public static readonly string NotImplemented = "Not implemented";
    public static readonly string NotApplicable = "Not applicable";

    /// <summary>The statuses a POA&amp;M lists.</summary>
    public static IReadOnlyList<string> Unmet { get; } = [PartiallyImplemented, Planned, NotImplemented];

    public static SspRequirement Complete(SspRequirement row, IReadOnlyDictionary<string, SspControl> controls)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(controls);
        var linked = Linked(row, controls);
        var status = Status(row.Result.Value, linked);
        if (status == NotApplicable)
        {
            return row with
            {
                Status = new SspValue(status, row.Result.Source),
                Statement = row.Observation,
                Responsible = new SspValue(null, row.Result.Source),
                Gaps = string.IsNullOrWhiteSpace(row.Observation.Value) ? ["not applicable without a justification"] : [],
            };
        }

        string[] parts =
        [
            .. linked.Where(c => !string.IsNullOrWhiteSpace(c.Description.Value)).Select(c => $"{c.RefId.Value}: {c.Description.Value}"),
            .. string.IsNullOrWhiteSpace(row.Observation.Value) ? [] : new[] { row.Observation.Value },
        ];
        var owners = linked.Select(c => c.Owner.Value).OfType<string>()
            .SelectMany(o => o.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var controlRefs = string.Join(", ", linked.Select(c => c.RefId.Value));
        var statementSource = new SspSource(
            SspSource.CisoAssistant,
            linked.Length == 0 ? row.Result.Source.Reference : $"applied controls {controlRefs}; {row.Result.Source.Reference}");
        return row with
        {
            Status = new SspValue(status, row.Result.Source),
            Statement = new SspValue(parts.Length == 0 ? null : string.Join('\n', parts), statementSource),
            Responsible = new SspValue(owners.Length == 0 ? null : string.Join(", ", owners), linked.Length == 0 ? row.Result.Source : new SspSource(SspSource.CisoAssistant, $"applied controls {controlRefs}")),
            Gaps =
            [
                .. parts.Length == 0 && InPlace(status) ? ["no implementation statement"] : Array.Empty<string>(),
                .. owners.Length == 0 && status != NotImplemented ? ["no responsible party"] : Array.Empty<string>(),
            ],
        };
    }

    /// <summary>Something is in place to describe: the row needs an implementation statement. A row not yet implemented is on the POA&amp;M instead.</summary>
    private static bool InPlace(string status) => status == Implemented || status == PartiallyImplemented;

    /// <summary>The row's controls as the plan holds them, in ref order; a control CISO Assistant does not hold is left out.</summary>
    internal static SspControl[] Linked(SspRequirement row, IReadOnlyDictionary<string, SspControl> controls) =>
        [.. (row.Controls.Value ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(c => controls.GetValueOrDefault(c)).OfType<SspControl>()];

    private static string Status(string? result, SspControl[] linked) => result switch
    {
        "compliant" => Implemented,
        "partially_compliant" => PartiallyImplemented,
        "not_applicable" => NotApplicable,
        _ => linked.Any(c => !string.IsNullOrWhiteSpace(c.Eta.Value)) ? Planned : NotImplemented,
    };
}
