using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Scope;

/// <summary>s08-06: a scope plan's effect on compliance, per audit: counts per implementation group, and each row whose result changes.</summary>
public sealed class ComplianceImpactTests
{
    private const string Audit = "EX-AUDIT-2026";
    private const string Framework = "urn:example:risk:framework:example-baseline";

    private static readonly FrameworkSnapshot Snapshot = new(
        "urn:example:risk:library:example-baseline",
        Framework,
        1,
        [new ImplementationGroup("1", "Level one"), new ImplementationGroup("2", "Level two")],
        [
            new SnapshotNode("urn:example:risk:req_node:example-baseline:ex-1:ex-1.1", "EX-1.1", "urn:example:risk:req_node:example-baseline:ex-1", ["1", "2"], "Every account uses multi-factor authentication"),
            new SnapshotNode("urn:example:risk:req_node:example-baseline:ex-1:ex-1.2", "EX-1.2", "urn:example:risk:req_node:example-baseline:ex-1", ["2"], "Administrators use phishing-resistant MFA"),
            new SnapshotNode("urn:example:risk:req_node:example-baseline:ex-2:ex-2.1", "EX-2.1", "urn:example:risk:req_node:example-baseline:ex-2", ["1", "2"], "Backups are tested quarterly"),
        ]);

    private static readonly ScopeObject DeclaredAudit = new(ScopeKind.Audit, Audit, new Dictionary<string, string> { ["framework"] = Framework, ["groups"] = "1, 2" });

    /// <summary>What CISO Assistant holds: EX-1.1 compliant, EX-1.2 not assessed (no result yet), EX-2.1 non-compliant.</summary>
    private static readonly IReadOnlyList<ActualObject> Held =
    [
        Row("EX-1.1", "compliant"),
        new ActualObject(new ScopeObject(ScopeKind.Assessment, $"{Audit}/EX-1.2", new Dictionary<string, string>()), null),
        Row("EX-2.1", "non_compliant"),
    ];

    [Fact]
    public void Counts_compliant_per_group_before_and_after()
    {
        var impact = ComplianceImpact.Of(Plan(HandWritten("EX-1.2", "not_assessed", "compliant")), [DeclaredAudit, Declared("EX-1.2", hand: true)], Held, Inputs());

        var audit = impact.ShouldHaveSingleItem();
        audit.Audit.ShouldBe(Audit);
        audit.Groups.ShouldBe([new ImpactGroup("1", 1, 1, 2), new ImpactGroup("2", 1, 2, 3)]);
    }

    [Fact]
    public void Changed_row_names_title_strategy_and_reason()
    {
        var plan = Plan(HandWritten("EX-1.2", "not_assessed", "compliant"), Unlisted("EX-2.1", "non_compliant", "not_applicable"));

        var impact = ComplianceImpact.Of(plan, [DeclaredAudit, Declared("EX-1.2", hand: true), Declared("EX-2.1", hand: false)], Held, Inputs());

        impact.ShouldHaveSingleItem().Rows.ShouldBe(
        [
            new ImpactRow("EX-1.2", "Administrators use phishing-resistant MFA", "EX-1", "not_assessed", "compliant", "hand-written in the audit file", Regresses: false),
            new ImpactRow("EX-2.1", "Backups are tested quarterly", "EX-2", "non_compliant", "not_applicable", "the unlisted rule", Regresses: false),
        ]);
    }

    [Fact]
    public void Regression_is_flagged_and_listed_first()
    {
        var plan = Plan(HandWritten("EX-1.2", "not_assessed", "compliant"), Unlisted("EX-1.1", "compliant", "not_assessed"));

        var impact = ComplianceImpact.Of(plan, [DeclaredAudit, Declared("EX-1.1", hand: false), Declared("EX-1.2", hand: true)], Held, Inputs()).ShouldHaveSingleItem();

        impact.Rows.Select(r => (r.Requirement, r.Regresses)).ShouldBe([("EX-1.1", true), ("EX-1.2", false)]);
        impact.Regressions.ShouldBe(1);
        impact.Groups.ShouldBe([new ImpactGroup("1", 1, 0, 2), new ImpactGroup("2", 1, 1, 3)]);
    }

    [Fact]
    public void Run_owned_rows_are_left_out()
    {
        var plan = Plan(HandWritten("EX-2.1", "non_compliant", "compliant"));
        var targets = new ScopeTarget(Audit, [RequirementKey.Ref("EX-2.1")], [], new SourceLine("hound.yaml", 1));

        ComplianceImpact.Of(plan, [DeclaredAudit, Declared("EX-2.1", hand: true)], Held, Inputs([targets])).ShouldBeEmpty();
    }

    [Fact]
    public void Plan_with_no_result_changes_has_no_impact()
    {
        var linkOnly = new ScopeChange(ScopeKind.Assessment, $"{Audit}/EX-1.1", ScopeAction.Update, [new FieldChange("controls", null, "AC-MFA")], null);

        ComplianceImpact.Of(Plan(linkOnly), [DeclaredAudit], Held, Inputs()).ShouldBeEmpty();
    }

    private static ScopePlan Plan(params ScopeChange[] changes) => new(changes, []);

    private static ScopeChange HandWritten(string requirement, string old, string @new) =>
        new(ScopeKind.Assessment, $"{Audit}/{requirement}", ScopeAction.Update, [new FieldChange("status", "to_do", "done"), new FieldChange("result", old, @new)], null);

    private static ScopeChange Unlisted(string requirement, string old, string @new) =>
        new(ScopeKind.Assessment, $"{Audit}/{requirement}", ScopeAction.Update, [new FieldChange("result", old, @new)], null);

    private static ScopeObject Declared(string requirement, bool hand) =>
        new(ScopeKind.Assessment, $"{Audit}/{requirement}", hand
            ? new Dictionary<string, string> { ["status"] = "done", ["result"] = "compliant", ["observation"] = "Checked at the March review." }
            : new Dictionary<string, string> { ["result"] = "not_assessed" });

    private static ActualObject Row(string requirement, string result) =>
        new(new ScopeObject(ScopeKind.Assessment, $"{Audit}/{requirement}", new Dictionary<string, string> { ["result"] = result }), null);

    private static ScopeInputs Inputs(IReadOnlyList<ScopeTarget>? targets = null) =>
        new(new Dictionary<string, FrameworkSnapshot> { [Framework] = Snapshot }, targets ?? [], new Dictionary<string, IReadOnlyList<ScopeFile>>());
}
