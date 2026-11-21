using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Scope;

public class ScopeApplyTests
{
    private static readonly ScopeKind[] Structure = [ScopeKind.Domain, ScopeKind.Perimeter, ScopeKind.System];
    private static readonly ScopeKind[] Everything = Enum.GetValues<ScopeKind>();
    private static readonly SourceLine At = new("scope/audits/example.yaml", 1);
    private static readonly string E8 = "urn:intuitem:risk:framework:essential-eight-2023";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Changes_are_written_in_plan_order_and_reported()
    {
        var store = new InMemoryScopeStore();
        ScopeObject[] declared = [Domain("Example Pty Ltd"), Perimeter("Example Pty Ltd", "Corporate IT"), System("Example 365", "Example Pty Ltd")];

        var report = await Apply(declared, store);

        report.Changes.Select(c => (c.Change.Kind, c.Outcome)).ShouldBe(
            [(ScopeKind.Domain, ApplyOutcome.Written), (ScopeKind.Perimeter, ApplyOutcome.Written), (ScopeKind.System, ApplyOutcome.Written)]);
        store.Writes.Select(w => w.Change.Key).ShouldBe(["Example Pty Ltd", "Example Pty Ltd/Corporate IT", "Example 365"]);
        (report.Written, report.Failed, report.Skipped).ShouldBe((3, 0, 0));
    }

    [Fact]
    public async Task Failed_domain_skips_its_perimeters_only()
    {
        var store = new InMemoryScopeStore { Refuse = { (ScopeKind.Domain, "Example Pty Ltd") } };
        ScopeObject[] declared =
        [
            Domain("Example Pty Ltd"), Domain("Example Labs"),
            Perimeter("Example Pty Ltd", "Corporate IT"), Perimeter("Example Labs", "Lab network"),
            System("Example 365", "Example Pty Ltd"), System("Example lab servers", "Example Labs"),
        ];

        var report = await Apply(declared, store);

        report.Changes.Where(c => c.Outcome == ApplyOutcome.Failed).Select(c => c.Change.Key).ShouldBe(["Example Pty Ltd"]);
        report.Changes.Where(c => c.Outcome == ApplyOutcome.Skipped).Select(c => c.Change.Key).ShouldBe(["Example Pty Ltd/Corporate IT", "Example 365"]);
        report.Changes.Single(c => c.Change.Key == "Example 365").Detail.ShouldContain("domain 'Example Pty Ltd'");
        store.Writes.Select(w => w.Change.Key).ShouldBe(["Example Labs", "Example Labs/Lab network", "Example lab servers"]);
    }

    [Fact]
    public async Task System_waits_for_its_parent()
    {
        var store = new InMemoryScopeStore { Refuse = { (ScopeKind.System, "Example 365") } };
        ScopeObject[] declared = [Domain("Example Pty Ltd"), System("Example 365", "Example Pty Ltd"), System("Example laptops", "Example Pty Ltd", parents: "Example 365")];

        var report = await Apply(declared, store);

        report.Changes.Single(c => c.Change.Key == "Example laptops").ShouldSatisfyAllConditions(
            c => c.Outcome.ShouldBe(ApplyOutcome.Skipped),
            c => c.Detail.ShouldContain("system 'Example 365'"));
    }

    [Fact]
    public async Task Second_apply_writes_nothing()
    {
        var store = new InMemoryScopeStore();
        ScopeObject[] declared = [Domain("Example Pty Ltd"), Perimeter("Example Pty Ltd", "Corporate IT"), System("Example 365", "Example Pty Ltd")];
        await Apply(declared, store);
        var written = store.Writes.Count;

        var second = await Apply(declared, store);

        second.Changes.ShouldBeEmpty();
        store.Writes.Count.ShouldBe(written);
    }

    [Fact]
    public async Task Kinds_not_applied_yet_are_left_out_and_counted()
    {
        var store = new InMemoryScopeStore();
        ScopeObject[] declared = [Domain("Example Pty Ltd"), new ScopeObject(ScopeKind.Control, "AC-MFA", new Dictionary<string, string> { ["name"] = "MFA" })];

        var report = await Apply(declared, store);

        report.Changes.ShouldHaveSingleItem().Change.Kind.ShouldBe(ScopeKind.Domain);
        report.NotApplied.ShouldBe(1);
        store.Writes.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Writer_receives_the_declared_object_and_the_root_folder()
    {
        var store = new InMemoryScopeStore();
        var domain = Domain("Example Pty Ltd");

        await Apply([domain], store);

        var write = store.Writes.ShouldHaveSingleItem();
        (write.Declared, write.RootFolder).ShouldBe((domain, "Global"));
    }

    [Fact]
    public async Task Locked_audit_is_reported_not_changed()
    {
        var store = new InMemoryScopeStore();
        var audit = new ScopeObject(ScopeKind.Audit, "EX-E8-2026", new Dictionary<string, string> { ["name"] = "Essential Eight ML2 2026" });
        var row = new ScopeObject(ScopeKind.Assessment, "EX-E8-2026/ISM-1486", new Dictionary<string, string> { ["result"] = "compliant" });
        store.Hold(new ActualObject(audit with { Fields = new Dictionary<string, string> { ["name"] = "Essential Eight 2026" } }, "0123456789abcdef") { Locked = true });
        store.Hold(new ActualObject(row with { Fields = new Dictionary<string, string> { ["result"] = "not_assessed" } }, null));

        var report = await Apply([audit, row], store, Everything);

        store.Writes.ShouldBeEmpty();
        report.Changes.ShouldBeEmpty();
        report.Drift.Where(d => d.Reason == DriftReason.Locked).Select(d => (d.Kind, d.Key)).ShouldBe(
            [(ScopeKind.Audit, "EX-E8-2026"), (ScopeKind.Assessment, "EX-E8-2026/ISM-1486")], ignoreOrder: true);
    }

    [Fact]
    public async Task Audit_created_with_selected_groups()
    {
        var store = new InMemoryScopeStore();
        var declared = ScopeDesired.From(Scope([Audit("EX-E8-2026", ["Example 365"])]), Inputs());

        var report = await Apply([.. declared], store, Everything);

        (report.Failed, report.Skipped).ShouldBe((0, 0));
        store.Writes.Select(w => w.Change.Kind).ShouldBe([ScopeKind.Framework, ScopeKind.Domain, ScopeKind.Perimeter, ScopeKind.System, ScopeKind.Audit]);
        store.Writes.Single(w => w.Change.Kind == ScopeKind.Audit).Declared!.Fields.ShouldContainKeyAndValue("groups", "1, 2");
    }

    [Fact]
    public async Task Document_links_to_rows_of_every_audit_sharing_a_system()
    {
        var store = new InMemoryScopeStore();
        var policy = new ScopeEvidenceDocument("identity-policy", "Identity policy", ["documents/identity-policy.pdf"], [Ref("Example 365")], [Requirement("ISM-1504")], At);
        var scope = Scope(
        [
            Audit("EX-E8-2026", ["Example 365"]) with { Documents = [policy] },
            Audit("EX-E8-2027", ["Example 365", "Example laptops"]),
            Audit("EX-E8-LAPTOPS", ["Example laptops"]),
        ]);

        var report = await Apply([.. ScopeDesired.From(scope, Inputs())], store, Everything);

        report.Failed.ShouldBe(0);
        store.Writes.Where(w => w.Change.Kind == ScopeKind.Assessment).Select(w => (w.Change.Key, w.Declared!.Fields["documents"])).ShouldBe(
            [("EX-E8-2026/ISM-1504", "identity-policy"), ("EX-E8-2027/ISM-1504", "identity-policy")], ignoreOrder: true);
    }

    [Fact]
    public async Task Unchanged_file_is_not_uploaded_again()
    {
        var store = new InMemoryScopeStore();
        var policy = new ScopeEvidenceDocument("identity-policy", "Identity policy", ["documents/identity-policy.pdf"], [Ref("Example 365")], [Requirement("ISM-1504")], At);
        var scope = Scope([Audit("EX-E8-2026", ["Example 365"]) with { Documents = [policy] }]);
        await Apply([.. ScopeDesired.From(scope, Inputs(new ScopeFile("documents/identity-policy.pdf", "aaaa")))], store, Everything);
        var written = store.Writes.Count;

        var unchanged = await Apply([.. ScopeDesired.From(scope, Inputs(new ScopeFile("documents/identity-policy.pdf", "aaaa")))], store, Everything);
        var edited = await Apply([.. ScopeDesired.From(scope, Inputs(new ScopeFile("documents/identity-policy.pdf", "bbbb")))], store, Everything);

        unchanged.Changes.ShouldBeEmpty();
        store.Writes.Count.ShouldBe(written + 1);
        var change = edited.Changes.ShouldHaveSingleItem().Change;
        (change.Kind, change.Key, change.Action).ShouldBe((ScopeKind.Document, "identity-policy", ScopeAction.Update));
        change.Fields.ShouldBe([new FieldChange("hashes", "aaaa", "aaaa, bbbb")]);
        store.Writes[^1].Declared!.Files.ShouldBe([new ScopeFile("documents/identity-policy.pdf", "bbbb")]);
    }

    [Fact]
    public async Task Unlisted_not_applicable_skips_rows_a_mapping_computes()
    {
        var store = new InMemoryScopeStore();
        var audit = Audit("EX-E8-2026", ["Example 365"]) with
        {
            Assessments = [new HandAssessment(Requirement("ISM-1486"), "done", ComplianceResult.Compliant, "Checked by hand.")],
            Unlisted = new UnlistedRule(ComplianceResult.NotApplicable, "Only Example 365 is in this audit.", At),
        };
        ScopeTarget[] targets = [new("EX-E8-2026", [RequirementKey.Ref("ISM-1504")], [], At)];

        var report = await Apply([.. ScopeDesired.From(Scope([audit]), Inputs() with { Targets = targets })], store, Everything);

        report.Failed.ShouldBe(0);
        store.Writes.Where(w => w.Change.Kind == ScopeKind.Assessment).Select(w => w.Change.Key).ShouldBe(["EX-E8-2026/ISM-1486", "EX-E8-2026/ISM-1173"], ignoreOrder: true);
        store.Writes.Single(w => w.Change.Key == "EX-E8-2026/ISM-1173").Declared!.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["result"] = "not_applicable",
            ["observation"] = "Only Example 365 is in this audit.",
        });
        ScopeDesired.From(Scope([audit]), Inputs()).ShouldNotContain(o => o.Key == "EX-E8-2026/ISM-1173", "without the deployment's targets, the rows a mapping computes are unknown");
    }

    private static ScopeDocument Scope(IReadOnlyList<ScopeAudit> audits) => new(
        new ScopeSetup(
            "Global",
            [new ScopeFramework(E8, "urn:intuitem:risk:library:essential-eight-2023", null, "frameworks/essential-eight-2023.json", At)],
            [new ScopeDomain("Example Pty Ltd", null, [new ScopePerimeter("Corporate IT", null, null, At)], [new ScopeSystem("Example 365", null, SystemType.Primary, null, null, [], new Dictionary<string, string>(), At)], At)]),
        [],
        audits);

    private static ScopeAudit Audit(string refId, string[] systems) =>
        new(refId, refId, Ref(E8), [Ref("1"), Ref("2")], Ref("Example Pty Ltd"), Ref("Corporate IT"), At) { Systems = [.. systems.Select(Ref)], Unlisted = new UnlistedRule(ComplianceResult.NotAssessed, null, At) };

    /// <summary>A framework of three requirements in groups 1 and 2, and one in group 3 that an ML2 audit leaves out.</summary>
    private static ScopeInputs Inputs(params ScopeFile[] policyFiles) => new(
        new Dictionary<string, FrameworkSnapshot>
        {
            [E8] = new("urn:intuitem:risk:library:essential-eight-2023", E8, 3, [new("1", "ML1"), new("2", "ML2"), new("3", "ML3")],
            [
                new($"{E8}:ism-1504", "ISM-1504", null, ["1", "2", "3"], "MFA for users"),
                new($"{E8}:ism-1173", "ISM-1173", null, ["2", "3"], "MFA for administrators"),
                new($"{E8}:ism-1486", "ISM-1486", null, ["1", "2", "3"], "Web browsers do not process Java"),
                new($"{E8}:ism-1700", "ISM-1700", null, ["3"], "Application control on servers"),
            ]),
        },
        null,
        new Dictionary<string, IReadOnlyList<ScopeFile>> { ["identity-policy"] = policyFiles });

    private static ScopeRef Ref(string value) => new(value, At);

    private static RequirementRef Requirement(string refId) => new(RequirementKey.Ref(refId), At);

    private async Task<ScopeApplyReport> Apply(ScopeObject[] declared, InMemoryScopeStore store, ScopeKind[]? kinds = null)
    {
        var plan = ScopePlanner.Plan(declared, store.Held, prune: false);
        return await ScopeApplier.ApplyAsync(plan, declared, kinds ?? Structure, "Global", store, _ct);
    }

    private static ScopeObject Domain(string name) => new(ScopeKind.Domain, name, new Dictionary<string, string>());

    private static ScopeObject Perimeter(string domain, string name) =>
        new(ScopeKind.Perimeter, ScopeDesired.PerimeterKey(domain, name), new Dictionary<string, string> { ["name"] = name, ["domain"] = domain });

    private static ScopeObject System(string name, string domain, string? parents = null) =>
        new(ScopeKind.System, name, parents is null
            ? new Dictionary<string, string> { ["type"] = "primary", ["domain"] = domain }
            : new Dictionary<string, string> { ["type"] = "supporting", ["domain"] = domain, ["parents"] = parents });

    /// <summary>A CISO Assistant that holds what was written to it, and refuses writes to the objects in <see cref="Refuse"/>.</summary>
    private sealed class InMemoryScopeStore : IScopeWriter
    {
        private readonly Dictionary<(ScopeKind, string), ActualObject> _held = [];

        public HashSet<(ScopeKind, string)> Refuse { get; } = [];

        public List<ScopeWrite> Writes { get; } = [];

        public ActualObject[] Held => [.. _held.Values];

        /// <summary>An object CISO Assistant already holds before the apply.</summary>
        public void Hold(ActualObject held) => _held[(held.Held.Kind, held.Held.Key)] = held;

        public Task<Result<Unit>> WriteAsync(ScopeWrite write, CancellationToken cancellationToken)
        {
            var key = (write.Change.Kind, write.Change.Key);
            if (Refuse.Contains(key))
            {
                return Task.FromResult(Result.Fail<Unit>(new Problem("ciso_assistant.rejected", "CISO Assistant refused the write.")));
            }

            Writes.Add(write);
            if (write.Change.Action == ScopeAction.Delete)
            {
                _held.Remove(key);
            }
            else
            {
                _held[key] = new ActualObject(write.Declared!, write.Change.State);
            }

            return Task.FromResult(Result.Ok(Unit.Value));
        }
    }
}
