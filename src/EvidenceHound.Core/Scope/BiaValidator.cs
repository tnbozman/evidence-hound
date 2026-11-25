using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Risk;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// Checks business impact analyses offline (s15-04): the domain, perimeter and matrix; one assessment per system; the systems and controls
/// each names; thresholds unique and in ascending time, with impact names the matrix has. A threshold whose impact is lower than an earlier
/// one is a warning: CISO Assistant carries the highest impact forward, so the file would say less than the chart shows. Pure.
/// </summary>
public static class BiaValidator
{
    public static IReadOnlyList<ScopeProblem> Validate(ScopeDocument scope, IReadOnlyDictionary<string, LibrarySnapshot> libraries)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(libraries);
        if (scope.Bias.Count == 0)
        {
            return [];
        }

        var catalogue = RiskCatalogue.Of(libraries.Values);
        var systems = scope.Setup.Domains.SelectMany(d => d.Systems).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var controls = scope.Controls.Select(c => c.RefId).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. scope.Bias.GroupBy(b => b.RefId, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1))
                .Select(b => new ScopeProblem("bia.duplicate", b.At, $"BIA '{b.RefId}' is declared more than once.", "Give each a different ref_id.")),
            .. scope.Bias.SelectMany(bia => Bia(bia, scope, catalogue.Matrix(bia.Matrix.Value), systems, controls)),
        ];
    }

    private static IEnumerable<ScopeProblem> Bia(ScopeBia bia, ScopeDocument scope, MatrixSnapshot? matrix, HashSet<string> systems, HashSet<string> controls)
    {
        var name = $"BIA '{bia.RefId}'";
        if (scope.Setup.Domains.FirstOrDefault(d => d.Name == bia.Domain.Value) is not { } domain)
        {
            yield return new ScopeProblem("bia.domain_unknown", bia.Domain.At, $"{name} names domain '{bia.Domain.Value}', which setup.yaml does not declare.", "Use a domain from setup.yaml.");
        }
        else if (bia.Perimeter is { } perimeter && domain.Perimeters.All(p => p.Name != perimeter.Value))
        {
            yield return new ScopeProblem("bia.perimeter_unknown", perimeter.At, $"{name} names perimeter '{perimeter.Value}', which domain '{domain.Name}' does not have.", "Use a perimeter of the BIA's domain, or leave it out.");
        }

        if (matrix is null)
        {
            yield return new ScopeProblem(
                "bia.matrix_unknown",
                bia.Matrix.At,
                $"{name} is rated on matrix '{bia.Matrix.Value}', which no library snapshot holds.",
                "List the matrix's library under 'libraries' in setup.yaml with a 'snapshot' (make one with 'hound library snapshot <library file>').");
        }

        foreach (var duplicate in bia.Assessments.GroupBy(a => a.System.Value, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1)))
        {
            yield return new ScopeProblem("bia.system_duplicate", duplicate.At, $"{name} assesses system '{duplicate.System.Value}' more than once.", "Keep one assessment per system and merge its thresholds.");
        }

        foreach (var problem in bia.Assessments.SelectMany(a => Assessment(name, a, matrix, systems, controls)))
        {
            yield return problem;
        }
    }

    private static IEnumerable<ScopeProblem> Assessment(string bia, ScopeAssetAssessment assessment, MatrixSnapshot? matrix, HashSet<string> systems, HashSet<string> controls)
    {
        var name = $"{bia} for '{assessment.System.Value}'";
        foreach (var system in new[] { assessment.System }.Concat(assessment.Dependencies).Where(s => !systems.Contains(s.Value)))
        {
            yield return new ScopeProblem("bia.system_unknown", system.At, $"{name} names system '{system.Value}', which setup.yaml does not declare.", "Use a system's name from setup.yaml.");
        }

        foreach (var control in assessment.Controls.Where(c => !controls.Contains(c.Value)))
        {
            yield return new ScopeProblem("bia.control_unknown", control.At, $"{name} names control '{control.Value}', which controls.yaml does not declare.", "Use a control's ref_id from controls.yaml.");
        }

        var impacts = matrix?.Impact.ToList();
        var (latest, highest) = (0, -1);
        foreach (var threshold in assessment.Thresholds)
        {
            if (threshold.After.Seconds <= latest)
            {
                yield return new ScopeProblem(
                    "bia.threshold_order",
                    threshold.At,
                    $"{name} has a threshold after {threshold.After}, which is not later than the one before it.",
                    "List thresholds once each, from the shortest time to the longest.");
            }

            latest = Math.Max(latest, threshold.After.Seconds);
            if (impacts is null)
            {
                continue;
            }

            var level = impacts.IndexOf(threshold.Impact.Value);
            if (level < 0)
            {
                yield return new ScopeProblem(
                    "bia.impact_unknown",
                    threshold.Impact.At,
                    $"{name} rates impact '{threshold.Impact.Value}', which matrix '{matrix!.Name}' does not have.",
                    $"Use one of {string.Join(", ", impacts)}.");
            }
            else if (level < highest)
            {
                yield return new ScopeProblem(
                    "bia.impact_falls",
                    threshold.At,
                    $"{name} rates the impact after {threshold.After} as '{threshold.Impact.Value}', lower than '{impacts[highest]}' earlier; CISO Assistant keeps the higher one.",
                    "An outage only gets worse with time: rate it at least as high as the threshold before it.")
                { Warning = true };
            }

            highest = Math.Max(highest, level);
        }
    }
}
