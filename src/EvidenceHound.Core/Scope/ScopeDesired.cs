using System.Globalization;
using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;

namespace EvidenceHound.Core.Scope;

/// <summary>
/// The objects a scope declares, as the plan compares them with CISO Assistant. Pure. Each field is text and is left out when empty; lists are
/// sorted and joined with ", " so their order in the file does not matter.
/// </summary>
/// <remarks>
/// Keys: a framework by URN, a domain and a system by name, a perimeter as <c>domain/name</c>, an audit and a control by <c>ref_id</c>, a
/// document by its id (used as the evidence's <c>ref_id</c>), and a hand-written assessment as <c>audit/requirement</c>. A document declared
/// by several audits is one object, from its first declaration.
/// </remarks>
public static class ScopeDesired
{
    public static IReadOnlyList<ScopeObject> From(ScopeDocument scope) => From(scope, ScopeInputs.None);

    public static IReadOnlyList<ScopeObject> From(ScopeDocument scope, ScopeInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(inputs);
        scope = scope with { Audits = [.. scope.Audits.SelectMany(a => a.Cycle is null ? [a] : Periods(a, a.Cycle, inputs.Today))] };
        var setup = scope.Setup;
        return
        [
            .. scope.Settings.FeatureFlags.Select(f => new ScopeObject(ScopeKind.FeatureFlag, f.Name, Fields(("value", f.Enabled ? "true" : "false")))),
            .. scope.Settings.General.Select(g => new ScopeObject(ScopeKind.Setting, g.Name, Fields(("value", g.Json)))),
            .. setup.Frameworks.Select(f => Framework(f, inputs)),
            .. setup.Libraries.Select(l => new ScopeObject(ScopeKind.Framework, l.Value, Fields()) { Initial = Fields(("library", l.Value)) }),
            .. setup.Domains.Select(d => new ScopeObject(ScopeKind.Domain, d.Name, Fields(("description", d.Description)))),
            .. setup.Domains.SelectMany(d => d.Perimeters.Select(p => new ScopeObject(
                ScopeKind.Perimeter, PerimeterKey(d.Name, p.Name), Fields(("name", p.Name), ("ref_id", p.RefId), ("description", p.Description), ("domain", d.Name))))),
            .. setup.Domains.SelectMany(d => d.Systems.Select(s => new ScopeObject(ScopeKind.System, s.Name, Fields(
                ("ref_id", s.RefId), ("type", s.Type == SystemType.Primary ? "primary" : "supporting"), ("description", s.Description), ("owner", s.Owner),
                ("domain", d.Name), ("parents", Joined(s.Parents.Select(p => p.Value))))))),
            .. scope.Audits.Select(a => new ScopeObject(ScopeKind.Audit, a.RefId, Fields(
                ("name", a.Name), ("framework", a.Framework.Value), ("groups", Joined(a.Groups.Select(g => g.Value))), ("domain", a.Domain.Value),
                ("perimeter", a.Perimeter.Value), ("eta", a.Eta), ("due_date", a.DueDate), ("authors", Joined(a.Authors)), ("reviewers", Joined(a.Reviewers)),
                ("status", a.Period?.Status), ("locked", a.Period is { Locked: true } ? "true" : null)))
            {
                Initial = Fields(("baseline", a.Period?.Baseline)),
            }),
            .. scope.Audits.SelectMany(a => a.Documents.Select(d => (Audit: a, Document: d))).DistinctBy(x => x.Document.Id).Select(x => Document(x.Audit, x.Document, inputs)),
            .. scope.Controls.Select(c => new ScopeObject(ScopeKind.Control, c.RefId, Fields(
                ("name", c.Name), ("description", c.Description), ("domain", c.Domain?.Value), ("owner", c.Owner), ("priority", c.Priority), ("category", c.Category),
                ("eta", c.Eta), ("systems", Joined(c.Systems.Select(s => s.Value))), ("documents", Joined(c.Documents.Select(d => d.Value)))))
            {
                Initial = Fields(("status", c.Status), ("reference_control", c.ReferenceControl)),
            }),
            .. scope.Audits.SelectMany(a => Rows(scope, a, inputs)),
            .. Access(scope.Access, setup.RootFolder),
            .. setup.Organisation is { } organisation ? [Organisation(organisation)] : Array.Empty<ScopeObject>(),
            .. scope.ThirdParties.SelectMany(ThirdParty),
            .. Reviews(scope, inputs.Today),
        ];
    }

    /// <summary>Every supplier review the scope declares on <paramref name="today"/>, each with its conclusion when the scope records one.</summary>
    public static IReadOnlyList<ScopeObject> Reviews(ScopeDocument scope, DateOnly? today)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return [.. scope.ThirdParties.SelectMany(e => e.Assessments.SelectMany(a => Reviews(e, a, today)))];
    }

    /// <summary>The key of the organisation's own entity, which CISO Assistant holds as its builtin main entity.</summary>
    public const string OrganisationKey = "organisation";

    private static ScopeObject Organisation(ScopeEntity entity) =>
        new(ScopeKind.MainEntity, OrganisationKey, Fields(
            ("name", entity.Name), ("description", entity.Description), ("mission", entity.Mission), ("country", entity.Country), ("currency", entity.Currency),
            ("identifiers", Identifiers(entity.Identifiers))));

    /// <summary>
    /// A third party as CISO Assistant holds it: the entity, then its solutions, contracts and representatives, each naming the entity by
    /// <c>ref_id</c>. A contract sits in the entity's domain. Identifiers are one JSON field, with sorted keys.
    /// </summary>
    private static IEnumerable<ScopeObject> ThirdParty(ScopeEntity entity) =>
    [
        new(ScopeKind.Entity, entity.RefId, Fields(
            ("name", entity.Name), ("description", entity.Description), ("mission", entity.Mission), ("country", entity.Country), ("currency", entity.Currency),
            ("parent", entity.Parent?.Value), ("domain", entity.Domain?.Value), ("dependency", Whole(entity.Dependency)), ("penetration", Whole(entity.Penetration)),
            ("maturity", Whole(entity.Maturity)), ("trust", Whole(entity.Trust)), ("identifiers", Identifiers(entity.Identifiers)))),
        .. entity.Solutions.Select(s => new ScopeObject(ScopeKind.Solution, s.RefId, Fields(
            ("name", s.Name), ("description", s.Description), ("entity", entity.RefId), ("criticality", Whole(s.Criticality))))),
        .. entity.Contracts.Select(c => new ScopeObject(ScopeKind.Contract, c.RefId, Fields(
            ("name", c.Name), ("description", c.Description), ("entity", entity.RefId), ("domain", entity.Domain?.Value), ("status", c.Status),
            ("start_date", c.Start), ("end_date", c.End), ("annual_expense", Amount(c.AnnualExpense)), ("currency", c.Currency),
            ("solutions", Joined(c.Solutions.Select(s => s.Value)))))),
        .. entity.Representatives.Select(r => new ScopeObject(ScopeKind.Representative, r.Email, Fields(
            ("entity", entity.RefId), ("first_name", r.FirstName), ("last_name", r.LastName), ("role", r.Role), ("phone", r.Phone)))),
    ];

    /// <summary>
    /// A supplier assessment as CISO Assistant holds it. With a review cycle, each review is its own assessment, <c>&lt;ref_id&gt;-yyyy-MM</c> by
    /// its due date: the first is due on <c>due_date</c>, and once a review falls due on <paramref name="today"/> the next is opened, a cycle
    /// later. The questionnaire is set up only when an assessment is created; answers and the tier are never declared. A review carries a
    /// conclusion only when a person recorded one for it in the file with <c>hound approve</c>.
    /// </summary>
    private static IEnumerable<ScopeObject> Reviews(ScopeEntity entity, ScopeEntityAssessment assessment, DateOnly? today)
    {
        var initial = Fields(("questionnaire", assessment.Questionnaire?.Value), ("groups", Joined(assessment.Groups)));
        ScopeObject Review(string key, string name, string? due) => new(ScopeKind.EntityAssessment, key, Fields(
            ("name", name), ("entity", entity.RefId), ("solutions", Joined(assessment.Solutions.Select(s => s.Value))),
            ("domain", assessment.Domain?.Value ?? entity.Domain?.Value), ("perimeter", assessment.Perimeter.Value), ("due_date", due),
            ("criticality", Whole(assessment.Criticality)), ("conclusion", assessment.Conclusions.LastOrDefault(c => c.Review == key)?.Result)))
        {
            Initial = initial,
        };

        if (assessment.ReviewMonths is not { } months || !DateOnly.TryParseExact(assessment.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
        {
            yield return Review(assessment.RefId, assessment.Name, assessment.DueDate);
            yield break;
        }

        for (var due = first; ; due = due.AddMonths(months))
        {
            var label = due.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            yield return Review($"{assessment.RefId}-{label}", $"{assessment.Name} {label}", due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (today is not { } day || day < due)
            {
                yield break;
            }
        }
    }

    /// <summary>Legal identifiers as one JSON object with sorted keys, or null when there are none.</summary>
    public static string? Identifiers(IReadOnlyDictionary<string, string> identifiers)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        return identifiers.Count == 0
            ? null
            : new System.Text.Json.Nodes.JsonObject(identifiers.OrderBy(i => i.Key, StringComparer.Ordinal)
                .Select(i => KeyValuePair.Create(i.Key, (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(i.Value)))).ToJsonString();
    }

    /// <summary>An amount as plain text without trailing zeros, so 12000, 12000.0 and 12000.00 compare equal.</summary>
    public static string? Amount(string? amount) =>
        decimal.TryParse(amount, NumberStyles.Number & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value)
            ? value.ToString("0.############", CultureInfo.InvariantCulture)
            : amount;

    private static string? Whole(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Users by email, each with the roles they hold (directly and through teams) as <c>roles</c>, such as
    /// <c>BI-RL-ADM on Global, BI-RL-APP on Example Pty Ltd</c>; and teams by name. CISO Assistant gives a role on a domain through
    /// membership of that domain's group for it, which is part of the user, so a user's roles are one field of the user.
    /// </summary>
    private static IEnumerable<ScopeObject> Access(ScopeAccess access, string rootFolder)
    {
        var roles = AccessRoles.Assignments(access).ToLookup(a => a.Email, StringComparer.Ordinal);
        return
        [
            .. access.Users.Select(u => new ScopeObject(ScopeKind.User, u.Email, Fields(
                ("first_name", u.FirstName), ("last_name", u.LastName), ("active", u.Active ? "true" : "false"),
                ("roles", Roles(roles[u.Email].Select(Role)))))),
            .. access.Teams.Select(t => new ScopeObject(ScopeKind.Team, t.Name, Fields(
                ("description", t.Description), ("domain", t.Domain?.Value ?? rootFolder), ("leader", t.Leader?.Value), ("members", Joined(t.Members.Select(m => m.Value)))))),
        ];
    }

    /// <summary>One role a user holds, as the <c>roles</c> field lists it: <c>BI-RL-APP on Example Pty Ltd</c>.</summary>
    public static string Role(AccessAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return $"{assignment.RoleCode} on {assignment.Domain}";
    }

    /// <summary>A user's <c>roles</c> field: each role once, in order, so the same roles always have the same text.</summary>
    public static string? Roles(IEnumerable<string> roles) => Joined(roles.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    /// <summary>A framework, with its version and library URN from its snapshot, and its custom library file to upload.</summary>
    private static ScopeObject Framework(ScopeFramework framework, ScopeInputs inputs)
    {
        var snapshot = inputs.Snapshots.GetValueOrDefault(framework.Urn);
        return new ScopeObject(ScopeKind.Framework, framework.Urn, Fields(("version", snapshot?.Version.ToString(CultureInfo.InvariantCulture))))
        {
            Initial = Fields(("library", framework.LibraryUrn ?? snapshot?.LibraryUrn), ("file", framework.LibraryFile)),
            Files = framework.LibraryFile is null ? [] : inputs.Files.GetValueOrDefault(framework.Urn) ?? [],
        };
    }

    /// <summary>
    /// An audit with a cycle as its period audits, from the first period to the one open on <paramref name="today"/>. The open period carries the
    /// file's documents, controls and assessments; an ended one is in review until it closes (signed off in the file, or at once with
    /// <c>close: auto</c>), when it is done and locked. Each period opens from the previous one as its baseline.
    /// </summary>
    private static IEnumerable<ScopeAudit> Periods(ScopeAudit audit, AuditCycle cycle, DateOnly? today)
    {
        var periods = today is { } day ? AuditPeriods.Until(audit.RefId, cycle, day) : [];
        var signed = audit.SignOffs.Select(s => s.Period).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < periods.Count; i++)
        {
            var period = periods[i];
            var open = i == periods.Count - 1;
            var closed = !open && (cycle.Close == "auto" || signed.Contains(period.Label));
            var state = new PeriodState(audit.RefId, open ? "in_progress" : closed ? "done" : "in_review", closed, i > 0 ? periods[i - 1].RefId : null);
            var periodAudit = audit with
            {
                RefId = period.RefId,
                Name = $"{audit.Name} {period.Label}",
                Eta = period.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Period = state,
            };
            yield return open ? periodAudit : periodAudit with { Documents = [], Controls = [], Assessments = [] };
        }
    }

    private static ScopeObject Document(ScopeAudit audit, ScopeEvidenceDocument document, ScopeInputs inputs)
    {
        var files = inputs.Files.GetValueOrDefault(document.Id) ?? [];
        return new ScopeObject(ScopeKind.Document, document.Id, Fields(("name", document.Name), ("domain", audit.Domain.Value), ("hashes", Joined(files.Select(f => f.Sha256).Distinct()))))
        {
            Files = files,
        };
    }

    /// <summary>
    /// The rows of one audit the scope writes: hand-written results, the controls and documents linked to the rows they cite, and, when the
    /// deployment's targets are known, the <c>unlisted</c> rule for every other in-group requirement that no mapping computes. A document is
    /// linked in the audit that declares it and in every audit of the same framework that shares one of its systems.
    /// </summary>
    private static IEnumerable<ScopeObject> Rows(ScopeDocument scope, ScopeAudit audit, ScopeInputs inputs)
    {
        if (audit.Period is { Status: not "in_progress" })
        {
            return [];
        }

        var rows = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
        Dictionary<string, string?> Row(string requirement) =>
            rows.TryGetValue(requirement, out var row) ? row : rows[requirement] = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var hand in audit.Assessments)
        {
            var row = Row(hand.Requirement.Key.Value);
            (row["status"], row["result"], row["observation"]) = (hand.Status, Names.Of(hand.Result), hand.Observation);
        }

        var links = audit.Controls.SelectMany(c => c.Requirements.Select(r => (Field: "controls", Requirement: r.Key.Value, Value: c.Control.Value)))
            .Concat(DocumentsCiting(scope, audit).SelectMany(d => d.Requirements.Select(r => (Field: "documents", Requirement: r.Key.Value, Value: d.Id))));
        foreach (var link in links.GroupBy(l => (l.Requirement, l.Field)))
        {
            Row(link.Key.Requirement)[link.Key.Field] = Joined(link.Select(l => l.Value).Distinct());
        }

        foreach (var (requirement, fields) in Unlisted(audit, inputs))
        {
            var row = Row(requirement);
            foreach (var (name, value) in fields)
            {
                row[name] = value;
            }
        }

        return rows.Select(r => new ScopeObject(ScopeKind.Assessment, $"{audit.RefId}/{r.Key}", Fields([.. r.Value.Select(f => (f.Key, f.Value))])));
    }

    private static IEnumerable<ScopeEvidenceDocument> DocumentsCiting(ScopeDocument scope, ScopeAudit audit)
    {
        var systems = audit.Systems.Select(s => s.Value).ToHashSet(StringComparer.Ordinal);
        return scope.Audits
            .Where(other => other == audit || other.Framework.Value == audit.Framework.Value)
            .SelectMany(other => other.Documents.Where(d => other == audit || d.Systems.Any(s => systems.Contains(s.Value))))
            .DistinctBy(d => d.Id);
    }

    /// <summary>The <c>unlisted</c> rule's fields for each in-group requirement that neither the file lists nor a mapping computes.</summary>
    private static IEnumerable<(string Requirement, (string Name, string? Value)[] Fields)> Unlisted(ScopeAudit audit, ScopeInputs inputs)
    {
        if (inputs.Targets is null || audit.Unlisted is not { } rule || inputs.Snapshots.GetValueOrDefault(audit.Framework.Value) is not { } snapshot)
        {
            return [];
        }

        var groups = audit.Groups.Select(g => g.Value).ToHashSet(StringComparer.Ordinal);
        RequirementKey[] covered =
        [
            .. audit.Assessments.Select(h => h.Requirement.Key),
            .. inputs.Targets.Where(t => t.AuditRefId == (audit.Period?.Series ?? audit.RefId)).SelectMany(t => t.Computed),
        ];
        var repeated = snapshot.Nodes.Where(n => n.RefId is not null).GroupBy(n => n.RefId!, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        return snapshot.Nodes
            .Where(n => groups.Count == 0 || n.Groups.Count == 0 || n.Groups.Any(groups.Contains))
            .Where(n => !covered.Any(k => k.Kind == RequirementKeyKind.Urn ? k.Value == n.Urn : k.Value == n.RefId))
            .Select(n => (n.RefId is { } refId && !repeated.Contains(refId) ? refId : n.Urn, new[] { ("result", (string?)Names.Of(rule.Result)), ("observation", rule.Reason) }));
    }

    /// <summary>A perimeter's key: its domain and name, because names are unique only within a domain.</summary>
    public static string PerimeterKey(string domain, string name) => $"{domain}/{name}";

    /// <summary>A hand-written assessment's key: the audit's <c>ref_id</c> and the requirement as the file names it.</summary>
    public static string AssessmentKey(string auditRefId, RequirementKey requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        return $"{auditRefId}/{requirement.Value}";
    }

    /// <summary>A list as one field value: sorted, joined with ", ", and null when empty.</summary>
    public static string? Joined(IEnumerable<string> values) =>
        values.Order(StringComparer.Ordinal).ToArray() is { Length: > 0 } sorted ? string.Join(", ", sorted) : null;

    private static Dictionary<string, string> Fields(params (string Name, string? Value)[] fields) =>
        fields.Where(f => !string.IsNullOrEmpty(f.Value)).ToDictionary(f => f.Name, f => f.Value!, StringComparer.Ordinal);
}

/// <summary>
/// What the scope's objects need from outside the scope files: the framework snapshots by framework URN, the deployment's targets (null when no
/// deployment config was given, so the rows a mapping computes are unknown and <c>unlisted</c> is not applied), and the files to upload: each
/// document's by its id, and a custom library file by its framework's URN.
/// </summary>
public sealed record ScopeInputs(
    IReadOnlyDictionary<string, FrameworkSnapshot> Snapshots, IReadOnlyList<ScopeTarget>? Targets, IReadOnlyDictionary<string, IReadOnlyList<ScopeFile>> Files)
{
    public static ScopeInputs None { get; } = new(new Dictionary<string, FrameworkSnapshot>(), null, new Dictionary<string, IReadOnlyList<ScopeFile>>());

    /// <summary>The day the plan is made, which decides the open period of each audit with a cycle; null opens none.</summary>
    public DateOnly? Today { get; init; }
}
