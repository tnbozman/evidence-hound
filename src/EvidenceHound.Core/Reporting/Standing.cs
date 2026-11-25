using System.Globalization;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Reporting;

/// <summary>
/// A programme's standing (s15-14): the highest group met, and how many rows are compliant, applicable, assessed and not assessed. A row
/// not assessed is a gap: it blocks the level and counts as not met, so a few automated passes never read as compliance.
/// </summary>
public sealed record Standing(ImplementationGroup? Met, int Total, int Applicable, int Compliant, int Assessed, int NotAssessed)
{
    /// <summary>The standing of <paramref name="programme"/>'s rows, over its groups in order.</summary>
    public static Standing Of(ProgrammePosture programme)
    {
        ArgumentNullException.ThrowIfNull(programme);
        var rows = programme.Requirements;
        ImplementationGroup? met = null;
        var below = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in programme.Groups)
        {
            below.Add(group.RefId);
            var atOrBelow = rows.Where(r => r.Groups.Any(below.Contains)).ToList();
            if (atOrBelow.Count == 0 || atOrBelow.Any(r => r.Result is not (ComplianceResult.Compliant or ComplianceResult.NotApplicable)))
            {
                break;
            }

            met = group;
        }

        var notApplicable = rows.Count(r => r.Result == ComplianceResult.NotApplicable);
        var notAssessed = rows.Count(r => r.Result == ComplianceResult.NotAssessed);
        return new Standing(met, rows.Count, rows.Count - notApplicable, rows.Count(r => r.Result == ComplianceResult.Compliant), rows.Count - notAssessed, notAssessed);
    }

    /// <summary>One line: the level met, compliant of applicable with not assessed counted as not met, and how many are not assessed.</summary>
    public string Text() => string.Create(
        CultureInfo.InvariantCulture,
        $"Level met: {Met?.Name ?? "none (level 0)"}. Compliant {Compliant} of {Applicable} applicable ({Percent(Compliant, Applicable)}), counting rows not assessed as not met; assessed {Assessed} of {Total}; not assessed: {NotAssessed}.");

    /// <summary>Rounded down, so a part short of the whole never reads 100%.</summary>
    private static string Percent(int part, int whole) => whole == 0 ? "-" : string.Create(CultureInfo.InvariantCulture, $"{100L * part / whole}%");
}
