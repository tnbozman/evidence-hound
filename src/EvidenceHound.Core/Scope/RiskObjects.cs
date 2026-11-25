using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Risk;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// Risk studies and scenarios as the plan compares them (s15-02). Pure. A study is keyed by <c>ref_id</c>, a scenario by
/// <c>study/ref_id</c>. Ratings are level names, so a reviewer reads "current_probability: Likely → Possible"; their positions on the
/// matrix go in <see cref="ScopeObject.Levels"/> for the writer. Not rated is left out. Risk levels are never declared.
/// </summary>
public static class RiskObjects
{
    /// <summary>
    /// The studies, their scenarios (each naming the security exceptions that cover it), a risk acceptance request for each scenario treated by
    /// <c>accept</c>, and the security exceptions. Neither an acceptance's state nor an exception's status is declared: an approver sets them in
    /// CISO Assistant (s15-03).
    /// </summary>
    public static IEnumerable<ScopeObject> From(
        IReadOnlyList<ScopeRiskStudy> studies, IReadOnlyDictionary<string, LibrarySnapshot> libraries, IReadOnlyList<ScopeExceptionRequest>? exceptions = null)
    {
        ArgumentNullException.ThrowIfNull(studies);
        ArgumentNullException.ThrowIfNull(libraries);
        exceptions ??= [];
        foreach (var exception in exceptions)
        {
            yield return Exception(exception);
        }

        var catalogue = RiskCatalogue.Of(libraries.Values);
        foreach (var study in studies)
        {
            yield return new ScopeObject(ScopeKind.RiskStudy, study.RefId, Fields(
                ("name", study.Name), ("description", study.Description), ("domain", study.Domain.Value), ("perimeter", study.Perimeter?.Value),
                ("matrix", study.Matrix.Value), ("status", study.Status), ("eta", study.Eta), ("due_date", study.DueDate),
                ("authors", ScopeDesired.Joined(study.Authors.Select(a => a.Value))), ("reviewers", ScopeDesired.Joined(study.Reviewers.Select(r => r.Value)))));

            var matrix = catalogue.Matrix(study.Matrix.Value);
            foreach (var scenario in study.Scenarios)
            {
                var key = ScenarioKey(study.RefId, scenario.RefId);
                var covering = exceptions.Where(e => e.Scenarios.Any(s => s.Value == key)).Select(e => e.RefId);
                yield return Scenario(study, scenario, matrix, catalogue, ScopeDesired.Joined(covering));
                if (scenario.Treatment == "accept")
                {
                    yield return new ScopeObject(ScopeKind.RiskAcceptance, key, Fields(
                        ("name", $"Accept: {scenario.Name}"), ("domain", study.Domain.Value), ("scenario", key), ("justification", scenario.Justification)));
                }
            }
        }
    }

    /// <summary>A scenario's key: its study's ref_id and its own.</summary>
    public static string ScenarioKey(string study, string scenario) => $"{study}/{scenario}";

    /// <summary>The ref_id CISO Assistant holds a scenario's risk acceptance under: <c>ACC-</c>, then the scenario's key with <c>-</c> for <c>/</c>.</summary>
    public static string AcceptanceRefId(string key) => $"ACC-{key.Replace('/', '-')}";

    private static ScopeObject Exception(ScopeExceptionRequest exception) =>
        new(ScopeKind.SecurityException, exception.RefId, Fields(
            ("name", exception.Name), ("description", exception.Description), ("domain", exception.Domain.Value), ("severity", exception.Severity),
            ("owners", ScopeDesired.Joined(exception.Owners.Select(o => o.Value))), ("expiration_date", exception.ExpirationDate), ("observation", exception.Observation),
            ("systems", ScopeDesired.Joined(exception.Systems.Select(s => s.Value))), ("controls", ScopeDesired.Joined(exception.Controls.Select(c => c.Value))),
            ("requirements", ScopeDesired.Joined(exception.Requirements.Select(r => $"{r.Audit}/{r.Requirement}")))));

    private static ScopeObject Scenario(ScopeRiskStudy study, ScopeRiskScenario scenario, MatrixSnapshot? matrix, RiskCatalogue catalogue, string? exceptions)
    {
        var ratings = new (string Field, string Number, string? Level, Func<MatrixSnapshot, IReadOnlyList<string>> Scale)[]
        {
            ("current_probability", "current_proba", scenario.Current.Probability, m => m.Probability),
            ("current_impact", "current_impact", scenario.Current.Impact, m => m.Impact),
            ("residual_probability", "residual_proba", scenario.Residual.Probability, m => m.Probability),
            ("residual_impact", "residual_impact", scenario.Residual.Impact, m => m.Impact),
            ("inherent_probability", "inherent_proba", scenario.Inherent?.Probability, m => m.Probability),
            ("inherent_impact", "inherent_impact", scenario.Inherent?.Impact, m => m.Impact),
        };
        return new ScopeObject(ScopeKind.RiskScenario, ScenarioKey(study.RefId, scenario.RefId), Fields(
            [
                ("ref_id", scenario.RefId), ("name", scenario.Name), ("description", scenario.Description), ("study", study.RefId),
                ("systems", ScopeDesired.Joined(scenario.Systems.Select(s => s.Value))),
                ("threats", ScopeDesired.Joined(scenario.Threats.Select(t => catalogue.Threat(t.Value)?.Urn ?? t.Value))),
                ("existing_controls", ScopeDesired.Joined(scenario.ExistingControls.Select(c => c.Value))),
                ("planned_controls", ScopeDesired.Joined(scenario.PlannedControls.Select(c => c.Value))),
                .. ratings.Select(r => (r.Field, r.Level)),
                ("treatment", scenario.Treatment), ("justification", scenario.Justification), ("exceptions", exceptions),
            ]))
        {
            Levels = matrix is null
                ? new Dictionary<string, int>()
                : ratings.Where(r => r.Level is not null && r.Scale(matrix).Contains(r.Level, StringComparer.Ordinal))
                    .ToDictionary(r => r.Number, r => r.Scale(matrix).ToList().IndexOf(r.Level!), StringComparer.Ordinal),
        };
    }

    private static Dictionary<string, string> Fields(params (string Name, string? Value)[] fields) =>
        fields.Where(f => !string.IsNullOrEmpty(f.Value)).ToDictionary(f => f.Name, f => f.Value!, StringComparer.Ordinal);
}
