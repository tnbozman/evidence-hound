using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Risk;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// Checks risk studies offline (s15-02): the matrix and its level names from the library snapshots, the systems, threats and controls each
/// scenario names, residual ratings no higher than current ones (as CISO Assistant's own consistency check), and planned controls that are
/// already active. Pure.
/// </summary>
public static class RiskValidator
{
    public static IReadOnlyList<ScopeProblem> Validate(ScopeDocument scope, IReadOnlyDictionary<string, LibrarySnapshot> libraries)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(libraries);
        if (scope.RiskStudies.Count == 0 && scope.Exceptions.Count == 0)
        {
            return [];
        }

        var catalogue = RiskCatalogue.Of(libraries.Values);
        var systems = scope.Setup.Domains.SelectMany(d => d.Systems).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var controls = scope.Controls.GroupBy(c => c.RefId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return
        [
            .. Duplicates(scope.RiskStudies.Select(s => (s.RefId, s.At)), "risk.study_duplicate", "risk study"),
            .. scope.RiskStudies.SelectMany(study => Study(study, scope, catalogue, systems, controls)),
            .. Duplicates(scope.Exceptions.Select(e => (e.RefId, e.At)), "exception.duplicate", "security exception"),
            .. scope.Exceptions.SelectMany(exception => Exception(exception, scope, systems, controls)),
        ];
    }

    /// <summary>What a security exception names must be declared: its domain, systems, controls, scenarios (<c>study/scenario</c>) and audits.</summary>
    private static IEnumerable<ScopeProblem> Exception(ScopeExceptionRequest exception, ScopeDocument scope, HashSet<string> systems, Dictionary<string, ScopeControl> controls)
    {
        var name = $"security exception '{exception.RefId}'";
        var scenarios = scope.RiskStudies.SelectMany(s => s.Scenarios.Select(c => RiskObjects.ScenarioKey(s.RefId, c.RefId))).ToHashSet(StringComparer.Ordinal);
        var audits = scope.Audits.Select(a => a.RefId).ToHashSet(StringComparer.Ordinal);
        var unknown = new[]
        {
            scope.Setup.Domains.Any(d => d.Name == exception.Domain.Value) ? [] : new[] { ("domain", exception.Domain, "a domain from setup.yaml") },
            exception.Systems.Where(s => !systems.Contains(s.Value)).Select(s => ("system", s, "a system's name from setup.yaml")),
            exception.Controls.Where(c => !controls.ContainsKey(c.Value)).Select(c => ("control", c, "a control's ref_id from controls.yaml")),
            exception.Scenarios.Where(c => !scenarios.Contains(c.Value)).Select(c => ("scenario", c, "<study ref_id>/<scenario ref_id> from scope/risks/")),
            exception.Requirements.Where(r => !audits.Contains(r.Audit)).Select(r => ("audit", new ScopeRef(r.Audit, r.At), "an audit's ref_id from scope/audits/")),
        };
        return unknown.SelectMany(u => u).Select(u =>
            new ScopeProblem($"exception.{u.Item1}_unknown", u.Item2.At, $"{name} names {u.Item1} '{u.Item2.Value}', which the scope does not declare.", $"Use {u.Item3}."));
    }

    private static IEnumerable<ScopeProblem> Study(
        ScopeRiskStudy study, ScopeDocument scope, RiskCatalogue catalogue, HashSet<string> systems, Dictionary<string, ScopeControl> controls)
    {
        if (scope.Setup.Domains.FirstOrDefault(d => d.Name == study.Domain.Value) is not { } domain)
        {
            yield return new ScopeProblem("risk.domain_unknown", study.Domain.At, $"risk study '{study.RefId}' names domain '{study.Domain.Value}', which setup.yaml does not declare.", "Use a domain from setup.yaml.");
        }
        else if (study.Perimeter is { } perimeter && domain.Perimeters.All(p => p.Name != perimeter.Value))
        {
            yield return new ScopeProblem("risk.perimeter_unknown", perimeter.At, $"risk study '{study.RefId}' names perimeter '{perimeter.Value}', which domain '{domain.Name}' does not have.", "Use a perimeter of the study's domain, or leave it out.");
        }

        var matrix = catalogue.Matrix(study.Matrix.Value);
        if (matrix is null)
        {
            yield return new ScopeProblem(
                "risk.matrix_unknown",
                study.Matrix.At,
                $"risk study '{study.RefId}' is rated on matrix '{study.Matrix.Value}', which no library snapshot holds.",
                "List the matrix's library under 'libraries' in setup.yaml with a 'snapshot' (make one with 'hound library snapshot <library file>').");
        }

        foreach (var problem in Duplicates(study.Scenarios.Select(s => (s.RefId, s.At)), "risk.scenario_duplicate", $"scenario of risk study '{study.RefId}'"))
        {
            yield return problem;
        }

        foreach (var problem in study.Scenarios.SelectMany(s => Scenario(s, matrix, catalogue, systems, controls)))
        {
            yield return problem;
        }
    }

    private static IEnumerable<ScopeProblem> Scenario(
        ScopeRiskScenario scenario, MatrixSnapshot? matrix, RiskCatalogue catalogue, HashSet<string> systems, Dictionary<string, ScopeControl> controls)
    {
        var name = $"risk scenario '{scenario.RefId}'";
        foreach (var system in scenario.Systems.Where(s => !systems.Contains(s.Value)))
        {
            yield return new ScopeProblem("risk.system_unknown", system.At, $"{name} names system '{system.Value}', which setup.yaml does not declare.", "Use a system's name from setup.yaml.");
        }

        foreach (var threat in scenario.Threats.Where(t => catalogue.Threat(t.Value) is null))
        {
            yield return new ScopeProblem(
                "risk.threat_unknown",
                threat.At,
                $"{name} names threat '{threat.Value}', which no library snapshot holds.",
                "Name a threat by URN or ref_id from a library listed in setup.yaml with a 'snapshot'.");
        }

        foreach (var control in scenario.ExistingControls.Concat(scenario.PlannedControls).Where(c => !controls.ContainsKey(c.Value)))
        {
            yield return new ScopeProblem("risk.control_unknown", control.At, $"{name} names control '{control.Value}', which controls.yaml does not declare.", "Use a control's ref_id from controls.yaml.");
        }

        foreach (var control in scenario.PlannedControls.Where(c => controls.GetValueOrDefault(c.Value)?.Status == "active"))
        {
            yield return new ScopeProblem(
                "risk.planned_control_active",
                control.At,
                $"{name} plans control '{control.Value}', which controls.yaml says is already active.",
                "Move it to 'existing_controls', and lower the residual rating if it already reduces the risk.")
            { Warning = true };
        }

        if (matrix is null)
        {
            yield break;
        }

        foreach (var problem in new[] { scenario.Current, scenario.Residual, scenario.Inherent }.OfType<RiskRating>().SelectMany(r => Levels(r, matrix, name, scenario.At)))
        {
            yield return problem;
        }

        if (Above(scenario.Residual.Probability, scenario.Current.Probability, matrix.Probability) || Above(scenario.Residual.Impact, scenario.Current.Impact, matrix.Impact))
        {
            yield return new ScopeProblem(
                "risk.residual_above_current",
                scenario.Residual.At ?? scenario.At,
                $"{name} has a residual rating above its current rating; treatment can only lower a risk.",
                "Rate the residual probability and impact at or below the current ones.");
        }
    }

    private static IEnumerable<ScopeProblem> Levels(RiskRating rating, MatrixSnapshot matrix, string name, SourceLine at)
    {
        foreach (var (level, scale, names) in new[] { (rating.Probability, "probability", matrix.Probability), (rating.Impact, "impact", matrix.Impact) })
        {
            if (level is not null && !names.Contains(level, StringComparer.Ordinal))
            {
                yield return new ScopeProblem(
                    "risk.level_unknown",
                    rating.At ?? at,
                    $"{name} rates {scale} '{level}', which matrix '{matrix.Name}' does not have.",
                    $"Use one of {string.Join(", ", names)}, or -- for not rated.");
            }
        }
    }

    private static bool Above(string? residual, string? current, IReadOnlyList<string> names)
    {
        var (r, c) = (Position(residual, names), Position(current, names));
        return r >= 0 && c >= 0 && r > c;
    }

    private static int Position(string? level, IReadOnlyList<string> names) => level is null ? -1 : names.ToList().IndexOf(level);

    private static IEnumerable<ScopeProblem> Duplicates(IEnumerable<(string RefId, SourceLine At)> items, string code, string what) =>
        items.GroupBy(i => i.RefId, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1)).Select(i =>
            new ScopeProblem(code, i.At, $"{what} '{i.RefId}' is declared more than once.", "Give each a different ref_id."));
}
