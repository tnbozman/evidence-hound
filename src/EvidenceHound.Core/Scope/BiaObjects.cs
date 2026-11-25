using System.Globalization;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Risk;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// Business impact analyses as the plan compares them (s15-04). Pure. A BIA is keyed by <c>ref_id</c>, an asset assessment by
/// <c>bia/system</c> and an escalation threshold by <c>bia/system/after</c> (such as <c>EX-BIA-2026/Example 365/1d</c>). Impacts are level
/// names; their positions on the matrix go in <see cref="ScopeObject.Levels"/> for the writer.
/// </summary>
public static class BiaObjects
{
    public static IEnumerable<ScopeObject> From(IReadOnlyList<ScopeBia> bias, IReadOnlyDictionary<string, LibrarySnapshot> libraries)
    {
        ArgumentNullException.ThrowIfNull(bias);
        ArgumentNullException.ThrowIfNull(libraries);
        var catalogue = RiskCatalogue.Of(libraries.Values);
        foreach (var bia in bias)
        {
            yield return new ScopeObject(ScopeKind.Bia, bia.RefId, Fields(
                ("name", bia.Name), ("description", bia.Description), ("domain", bia.Domain.Value), ("perimeter", bia.Perimeter?.Value),
                ("matrix", bia.Matrix.Value), ("version", bia.Version), ("status", bia.Status), ("due_date", bia.DueDate),
                ("authors", ScopeDesired.Joined(bia.Authors.Select(a => a.Value))), ("reviewers", ScopeDesired.Joined(bia.Reviewers.Select(r => r.Value)))));

            var impacts = catalogue.Matrix(bia.Matrix.Value)?.Impact.ToList() ?? [];
            foreach (var assessment in bia.Assessments.DistinctBy(a => a.System.Value))
            {
                var key = AssessmentKey(bia.RefId, assessment.System.Value);
                yield return new ScopeObject(ScopeKind.AssetAssessment, key, Fields(
                    ("bia", bia.RefId), ("system", assessment.System.Value), ("dependencies", ScopeDesired.Joined(assessment.Dependencies.Select(d => d.Value))),
                    ("controls", ScopeDesired.Joined(assessment.Controls.Select(c => c.Value))), ("recovery_documented", Flag(assessment, "documented", assessment.Documented)),
                    ("recovery_tested", Flag(assessment, "tested", assessment.Tested)), ("recovery_targets_met", Flag(assessment, "targets_met", assessment.TargetsMet)),
                    ("observation", assessment.Observation)));

                foreach (var threshold in assessment.Thresholds.DistinctBy(t => t.After))
                {
                    var level = impacts.IndexOf(threshold.Impact.Value);
                    yield return new ScopeObject(ScopeKind.EscalationThreshold, ThresholdKey(key, threshold.After), Fields(
                        ("assessment", key), ("point_in_time", threshold.After.Seconds.ToString(CultureInfo.InvariantCulture)), ("impact", threshold.Impact.Value),
                        ("qualifications", ScopeDesired.Joined(threshold.Qualifications)), ("justification", threshold.Justification)))
                    {
                        Levels = level < 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["quali_impact"] = level },
                    };
                }
            }
        }
    }

    public static string AssessmentKey(string bia, string system) => $"{bia}/{system}";

    public static string ThresholdKey(string assessment, Duration after) => $"{assessment}/{after}";

    /// <summary>A hand-set flag; none for a flag named by checks, which each run sets (s15-05) and the scope plan leaves alone.</summary>
    private static string? Flag(ScopeAssetAssessment assessment, string flag, bool value) =>
        assessment.Checks.ContainsKey(flag) ? null : value ? "true" : "false";

    private static Dictionary<string, string> Fields(params (string Name, string? Value)[] fields) =>
        fields.Where(f => !string.IsNullOrEmpty(f.Value)).ToDictionary(f => f.Name, f => f.Value!, StringComparer.Ordinal);
}
