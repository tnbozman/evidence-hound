using System.Globalization;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Scope;

/// <summary>A deployment target as scope validation sees it: its audit, the requirements its mapping computes and its controls.</summary>
public sealed record ScopeTarget(string AuditRefId, IReadOnlyList<RequirementKey> Computed, IReadOnlyList<string> ControlRefIds, SourceLine At);

/// <summary>Checks a parsed scope against itself, the framework snapshots and the deployment's targets. Pure.</summary>
public static class ScopeValidator
{
    private static readonly string[] FieldsHoundWrites = ["result", "observation", "evidences"];

    /// <summary>
    /// Every problem at once: duplicate declarations, references to undeclared things, groups and requirements the framework does not
    /// have or the audit does not assess, hand-written results a mapping also computes, fields hound cannot write, a missing
    /// <c>unlisted</c>, and targets outside the scope. <paramref name="snapshots"/> is keyed by framework URN; a declared framework
    /// without one is reported, and its audits are not checked against it.
    /// </summary>
    public static IReadOnlyList<ScopeProblem> Validate(
        ScopeDocument scope, IReadOnlyDictionary<string, FrameworkSnapshot> snapshots, IReadOnlyList<ScopeTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(targets);

        var index = new ScopeIndex(scope);
        return
        [
            .. Duplicates(scope),
            .. scope.Setup.Frameworks.Where(f => !snapshots.ContainsKey(f.Urn)).Select(SnapshotMissing),
            .. scope.Setup.Domains.SelectMany(d => d.Systems).SelectMany(s => s.Parents).Where(p => !index.Systems.Contains(p.Value)).Select(p => Unknown(p, "system", "scope/setup.yaml")),
            .. scope.Controls.SelectMany(c => ControlProblems(c, index)),
            .. scope.Audits.SelectMany(a => AuditProblems(a, index, snapshots, targets)),
            .. targets.SelectMany(t => TargetProblems(t, index)),
            .. AccessValidator.Validate(scope),
            .. SettingsValidator.Validate(scope, CisoAssistantSettings.Current),
            .. SupplierValidator.References(scope),
        ];
    }

    private static IEnumerable<ScopeProblem> Duplicates(ScopeDocument scope) =>
    [
        .. Repeated(scope.Setup.Domains, d => d.Name, d => d.At, "domain"),
        .. Repeated(scope.Setup.Domains.SelectMany(d => d.Systems), s => s.Name, s => s.At, "system"),
        .. scope.Setup.Domains.SelectMany(d => Repeated(d.Perimeters, p => p.Name, p => p.At, "perimeter")),
        .. Repeated(scope.Controls, c => c.RefId, c => c.At, "control"),
        .. Repeated(scope.Audits, a => a.RefId, a => a.At, "audit"),
        .. scope.Audits.SelectMany(a => Repeated(a.Documents, d => d.Id, d => d.At, "document")),
    ];

    private static IEnumerable<ScopeProblem> Repeated<T>(IEnumerable<T> items, Func<T, string> key, Func<T, SourceLine> at, string kind) =>
        items
            .GroupBy(key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Skip(1).Select(item => new ScopeProblem(
                "scope.duplicate",
                at(item),
                $"{kind} '{group.Key}' is declared more than once; first at {Where(at(group.First()))}.",
                $"Give each {kind} its own name, or remove the copy.")));

    private static ScopeProblem SnapshotMissing(ScopeFramework framework) => new(
        "scope.framework_snapshot_missing",
        framework.At,
        $"framework '{framework.Urn}' has no snapshot, so its groups and requirements cannot be checked.",
        "Add 'snapshot:' with a file made by 'hound framework snapshot <library>', or 'file:' with the library itself.");

    private static IEnumerable<ScopeProblem> ControlProblems(ScopeControl control, ScopeIndex index) =>
    [
        .. control.Domain is { } domain && !index.Domains.Contains(domain.Value) ? [Unknown(domain, "domain", "scope/setup.yaml")] : Array.Empty<ScopeProblem>(),
        .. control.Systems.Where(s => !index.Systems.Contains(s.Value)).Select(s => Unknown(s, "system", "scope/setup.yaml")),
        .. control.Documents.Where(d => !index.Documents.Contains(d.Value)).Select(d => new ScopeProblem(
            "scope.reference_unknown", d.At, $"document '{d.Value}' is not declared in any audit.", "Declare it under 'documents' in the audit it evidences.")),
    ];

    private static IEnumerable<ScopeProblem> AuditProblems(
        ScopeAudit audit, ScopeIndex index, IReadOnlyDictionary<string, FrameworkSnapshot> snapshots, IReadOnlyList<ScopeTarget> targets)
    {
        var declared = index.Frameworks.Contains(audit.Framework.Value);
        var snapshot = declared ? snapshots.GetValueOrDefault(audit.Framework.Value) : null;
        return
        [
            .. declared ? [] : new[] { Unknown(audit.Framework, "framework", "scope/setup.yaml") },
            .. index.Domains.Contains(audit.Domain.Value) ? [] : new[] { Unknown(audit.Domain, "domain", "scope/setup.yaml") },
            .. index.Perimeters.Contains((audit.Domain.Value, audit.Perimeter.Value)) ? [] : new[]
            {
                new ScopeProblem(
                    "scope.reference_unknown",
                    audit.Perimeter.At,
                    $"perimeter '{audit.Perimeter.Value}' is not declared in domain '{audit.Domain.Value}'.",
                    "Declare it under the domain's 'perimeters' in scope/setup.yaml."),
            },
            .. audit.Systems.Concat(audit.Documents.SelectMany(d => d.Systems)).Where(s => !index.Systems.Contains(s.Value)).Select(s => Unknown(s, "system", "scope/setup.yaml")),
            .. audit.Controls.Where(c => !index.Controls.Contains(c.Control.Value)).Select(c => Unknown(c.Control, "control", "scope/controls.yaml")),
            .. snapshot is null ? [] : FrameworkProblems(audit, snapshot),
            .. ResultConflicts(audit, snapshot, targets),
            .. FieldVisibilityProblems(audit),
            .. UnlistedProblems(audit),
            .. CycleProblems(audit),
        ];
    }

    /// <summary>A cycle starts on the first of a month; each sign-off names one of its periods, and is dated after that period ended.</summary>
    private static IEnumerable<ScopeProblem> CycleProblems(ScopeAudit audit)
    {
        if (audit.Cycle is not { } cycle)
        {
            return audit.SignOffs.Take(1).Select(s => new ScopeProblem(
                "scope.sign_off_without_cycle",
                s.At ?? audit.At,
                $"audit '{audit.RefId}' records sign-offs but has no 'cycle'.",
                "Add a 'cycle', or remove the sign-offs."));
        }

        if (!DateOnly.TryParseExact(cycle.Starts, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first) || first.Day != 1)
        {
            return
            [
                new ScopeProblem(
                    "scope.cycle_start_invalid",
                    cycle.At,
                    $"the cycle of audit '{audit.RefId}' starts on '{cycle.Starts}', which is not the first day of a month.",
                    "Start the cycle on the first of a month, such as \"2026-07-01\"."),
            ];
        }

        return audit.SignOffs.SelectMany(s => SignOffProblems(audit, cycle, s));
    }

    private static IEnumerable<ScopeProblem> SignOffProblems(ScopeAudit audit, AuditCycle cycle, SignOff signOff)
    {
        var at = signOff.At ?? audit.At;
        if (!DateOnly.TryParseExact(signOff.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            yield return new ScopeProblem("scope.sign_off_date_invalid", at, $"sign-off date '{signOff.Date}' is not a date.", "Write it as YYYY-MM-DD.");
            yield break;
        }

        var periods = AuditPeriods.Until(audit.RefId, cycle, date.AddYears(1));
        if (periods.FirstOrDefault(p => p.Label == signOff.Period) is not { } period)
        {
            yield return new ScopeProblem(
                "scope.sign_off_unknown_period",
                at,
                $"sign-off period '{signOff.Period}' is not a period of audit '{audit.RefId}'.",
                periods.Count > 0 ? $"Name a period such as '{periods[^1].Label}'." : "Name a period that starts on or after the cycle's start.");
        }
        else if (date <= period.End)
        {
            yield return new ScopeProblem(
                "scope.sign_off_early",
                at,
                $"period '{signOff.Period}' is signed off on {signOff.Date}, before it ends on {period.End:yyyy-MM-dd}.",
                "Sign a period off after it has ended.");
        }
    }

    private static IEnumerable<ScopeProblem> FrameworkProblems(ScopeAudit audit, FrameworkSnapshot snapshot)
    {
        var defined = snapshot.Groups.Select(g => g.RefId).ToArray();
        var groups = audit.Groups.Select(g => g.Value).ToHashSet(StringComparer.Ordinal);
        var cited = audit.Documents.SelectMany(d => d.Requirements)
            .Concat(audit.Controls.SelectMany(c => c.Requirements))
            .Concat(audit.Assessments.Select(a => a.Requirement));
        return
        [
            .. audit.Groups.Where(g => !defined.Contains(g.Value, StringComparer.Ordinal)).Select(g => new ScopeProblem(
                "scope.group_unknown",
                g.At,
                $"group '{g.Value}' is not defined by framework '{snapshot.FrameworkUrn}'.",
                $"Groups it defines: {string.Join(", ", defined)}.")),
            .. cited.Select(r => RequirementProblem(r, snapshot, groups)).OfType<ScopeProblem>(),
        ];
    }

    private static ScopeProblem? RequirementProblem(RequirementRef requirement, FrameworkSnapshot snapshot, HashSet<string> groups)
    {
        var nodes = snapshot.Resolve(requirement.Key);
        return nodes.Count == 0
            ? new ScopeProblem(
                "scope.requirement_unknown",
                requirement.At,
                $"requirement '{requirement.Key}' is not in framework '{snapshot.FrameworkUrn}'.",
                "Check the ref_id or URN against the framework snapshot.")
            : nodes.Any(n => InGroups(n, groups))
                ? null
                : new ScopeProblem(
                    "scope.requirement_not_assessed",
                    requirement.At,
                    $"requirement '{requirement.Key}' is not assessed by this audit: it is outside groups {string.Join(", ", groups.Order(StringComparer.Ordinal))}.",
                    "Add its group to the audit's 'groups', or stop citing the requirement here.");
    }

    private static bool InGroups(SnapshotNode node, HashSet<string> groups) =>
        groups.Count == 0 || node.Groups.Count == 0 || node.Groups.Any(groups.Contains);

    private static IEnumerable<ScopeProblem> ResultConflicts(ScopeAudit audit, FrameworkSnapshot? snapshot, IReadOnlyList<ScopeTarget> targets) =>
        targets
            .Where(t => t.AuditRefId == audit.RefId)
            .SelectMany(target =>
            {
                var computed = target.Computed.SelectMany(key => Names(key, snapshot)).ToHashSet(StringComparer.Ordinal);
                return audit.Assessments
                    .Where(a => Names(a.Requirement.Key, snapshot).Any(computed.Contains))
                    .Select(a => new ScopeProblem(
                        "scope.result_conflict",
                        a.Requirement.At,
                        $"requirement '{a.Requirement.Key}' has a hand-written result, but the mapping of the target for audit '{target.AuditRefId}' also computes it.",
                        "Remove the assessment or take the requirement out of the mapping, so that only one of them writes the row."));
            });

    /// <summary>The URNs a key names when the framework is known, otherwise the key itself.</summary>
    private static IEnumerable<string> Names(RequirementKey key, FrameworkSnapshot? snapshot) =>
        snapshot?.Resolve(key) is { Count: > 0 } nodes ? nodes.Select(n => n.Urn) : [key.Value];

    private static IEnumerable<ScopeProblem> FieldVisibilityProblems(ScopeAudit audit) =>
        FieldsHoundWrites
            .Select(field => (Field: field, Access: audit.FieldVisibility.GetValueOrDefault(field)))
            .Where(f => f.Access is { Auditor: not null and not "edit" })
            .Select(f => new ScopeProblem(
                "scope.field_not_writable",
                f.Access!.At,
                $"field '{f.Field}' is '{f.Access.Auditor}' for the auditor, so CISO Assistant would silently ignore what hound writes to it.",
                "Set 'auditor: edit' for result, observation and evidences."));

    private static IEnumerable<ScopeProblem> UnlistedProblems(ScopeAudit audit) => audit.Unlisted switch
    {
        null =>
        [
            new ScopeProblem(
                "scope.unlisted_missing",
                audit.At,
                $"audit '{audit.RefId}' does not say what happens to the in-group requirements it does not list.",
                "Add 'unlisted:' with 'result: not_assessed', or 'result: not_applicable' and a 'reason' for an audit scoped to one system."),
        ],
        { Result: ComplianceResult.NotApplicable, Reason: null } rule =>
        [
            new ScopeProblem(
                "scope.unlisted_reason_missing",
                rule.At,
                "unlisted requirements are marked not applicable without a reason.",
                "Add 'reason:' saying why the requirements this file does not list do not apply."),
        ],
        _ => [],
    };

    private static IEnumerable<ScopeProblem> TargetProblems(ScopeTarget target, ScopeIndex index) =>
    [
        .. index.Audits.Contains(target.AuditRefId) ? [] : new[]
        {
            new ScopeProblem(
                "config.target_not_in_scope",
                target.At,
                $"target audit '{target.AuditRefId}' is not declared in the scope.",
                "Add a file for it under scope/audits/, or point the target at an audit the scope declares."),
        },
        .. target.ControlRefIds.Where(c => !index.Controls.Contains(c)).Select(c => new ScopeProblem(
            "config.target_not_in_scope",
            target.At,
            $"target control '{c}' is not declared in the scope.",
            "Declare it in scope/controls.yaml, or remove it from the target's controls.")),
    ];

    private static ScopeProblem Unknown(ScopeRef reference, string kind, string file) => new(
        "scope.reference_unknown", reference.At, $"{kind} '{reference.Value}' is not declared.", $"Declare it in {file}, or correct the name.");

    private static string Where(SourceLine at) => $"{at.File} line {at.Line}";

    /// <summary>The names the scope declares, for reference checks.</summary>
    private sealed class ScopeIndex(ScopeDocument scope)
    {
        public HashSet<string> Frameworks { get; } = [.. scope.Setup.Frameworks.Select(f => f.Urn)];

        public HashSet<string> Domains { get; } = [.. scope.Setup.Domains.Select(d => d.Name)];

        public HashSet<(string Domain, string Perimeter)> Perimeters { get; } = [.. scope.Setup.Domains.SelectMany(d => d.Perimeters.Select(p => (d.Name, p.Name)))];

        public HashSet<string> Systems { get; } = [.. scope.Setup.Domains.SelectMany(d => d.Systems.Select(s => s.Name))];

        public HashSet<string> Controls { get; } = [.. scope.Controls.Select(c => c.RefId)];

        public HashSet<string> Audits { get; } = [.. scope.Audits.Select(a => a.RefId)];

        public HashSet<string> Documents { get; } = [.. scope.Audits.SelectMany(a => a.Documents.Select(d => d.Id))];
    }
}
