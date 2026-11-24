using System.Text.RegularExpressions;
using EvidenceHound.Core.Frameworks;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// Checks the programmes in <c>scope/setup.yaml</c> (s11-01): a unique slug id, a declared framework with a snapshot, groups the snapshot has,
/// and an audit of record that uses the framework and belongs to no other programme. Pure.
/// </summary>
public static partial class ProgrammeValidator
{
    public static IReadOnlyList<ScopeProblem> Validate(ScopeDocument scope, IReadOnlyDictionary<string, FrameworkSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(snapshots);
        var programmes = scope.Setup.Programmes;
        return
        [
            .. programmes.Where(p => !Slug().IsMatch(p.Id)).Select(p => new ScopeProblem(
                "scope.programme_id", p.At, $"programme id '{p.Id}' is not a lowercase slug.", "Use lowercase letters, digits and single hyphens, such as 'essential-eight'.")),
            .. programmes.GroupBy(p => p.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1).Select(p => new ScopeProblem(
                "scope.duplicate", p.At, $"programme '{g.Key}' is declared more than once; first at {Where(g.First().At)}.", "Give each programme its own id, or remove the copy."))),
            .. programmes.SelectMany(p => FrameworkProblems(p, scope, snapshots)),
            .. programmes.SelectMany(p => AuditProblems(p, scope)),
            .. programmes.GroupBy(p => p.Audit.Value, StringComparer.Ordinal).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1).Select(p => new ScopeProblem(
                "scope.programme_audit_shared",
                p.Audit.At,
                $"audit '{g.Key}' is the audit of record of programme '{g.First().Id}' and of programme '{p.Id}'.",
                "An audit belongs to at most one programme: name another audit, or remove one of the programmes."))),
        ];
    }

    private static IEnumerable<ScopeProblem> FrameworkProblems(ScopeProgramme programme, ScopeDocument scope, IReadOnlyDictionary<string, FrameworkSnapshot> snapshots)
    {
        var framework = programme.Framework;
        if (!scope.Setup.Frameworks.Any(f => f.Urn == framework.Value))
        {
            return [new ScopeProblem(
                "scope.programme_framework_unknown", framework.At, $"programme '{programme.Id}' names framework '{framework.Value}', which this file does not declare.", "Declare it under 'frameworks:', or correct the URN.")];
        }

        if (!snapshots.TryGetValue(framework.Value, out var snapshot))
        {
            return [new ScopeProblem(
                "scope.programme_needs_snapshot",
                framework.At,
                $"programme '{programme.Id}' uses framework '{framework.Value}', which has no snapshot, so its groups and requirements cannot be counted.",
                "Add 'snapshot:' to the framework with a file made by 'hound framework snapshot <library>'.")];
        }

        var known = snapshot.Groups.Select(g => g.RefId).ToArray();
        return programme.Groups.Where(g => !known.Contains(g, StringComparer.Ordinal)).Select(g => new ScopeProblem(
            "scope.programme_group_unknown",
            programme.At,
            $"programme '{programme.Id}' reports against group '{g}', which framework '{framework.Value}' does not have.",
            $"Use one of the framework's groups: {string.Join(", ", known)}."));
    }

    private static IEnumerable<ScopeProblem> AuditProblems(ScopeProgramme programme, ScopeDocument scope)
    {
        var audit = scope.Audits.FirstOrDefault(a => a.RefId == programme.Audit.Value);
        if (audit is null)
        {
            return [new ScopeProblem(
                "scope.programme_audit_unknown", programme.Audit.At, $"programme '{programme.Id}' names audit '{programme.Audit.Value}', which is not declared.", "Name an audit from scope/audits/ by its ref_id.")];
        }

        return audit.Framework.Value == programme.Framework.Value
            ? []
            : [new ScopeProblem(
                "scope.programme_audit_framework",
                programme.Audit.At,
                $"programme '{programme.Id}' uses framework '{programme.Framework.Value}', but its audit '{audit.RefId}' uses '{audit.Framework.Value}'.",
                "Name an audit of the programme's framework, or change the programme's framework.")];
    }

    private static string Where(SourceLine at) => $"{at.File} line {at.Line}";

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Slug();
}
