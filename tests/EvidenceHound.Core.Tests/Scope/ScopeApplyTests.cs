using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Scope;

public class ScopeApplyTests
{
    private static readonly ScopeKind[] Structure = [ScopeKind.Domain, ScopeKind.Perimeter, ScopeKind.System];

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

    private async Task<ScopeApplyReport> Apply(ScopeObject[] declared, InMemoryScopeStore store)
    {
        var plan = ScopePlanner.Plan(declared, store.Held, prune: false);
        return await ScopeApplier.ApplyAsync(plan, declared, Structure, "Global", store, _ct);
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
