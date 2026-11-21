using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

public class ScopeDesiredTests
{
    [Fact]
    public void Scope_declares_each_object_once_by_its_stable_key()
    {
        var declared = ScopeDesired.From(ScopeFixture.Parse().Scope);

        declared.Select(o => (o.Kind, o.Key)).ShouldBe(
        [
            (ScopeKind.Framework, ScopeFixture.FrameworkUrn),
            (ScopeKind.Domain, "Example Pty Ltd"),
            (ScopeKind.Perimeter, "Example Pty Ltd/Example baseline"),
            (ScopeKind.System, "Example 365"),
            (ScopeKind.System, "Example laptops"),
            (ScopeKind.Audit, "EXAMPLE-BASELINE-2026"),
            (ScopeKind.Document, "mfa-policy"),
            (ScopeKind.Control, "AC-MFA"),
            (ScopeKind.Control, "AC-BACKUP"),
            (ScopeKind.Assessment, "EXAMPLE-BASELINE-2026/EX-1.2"),
            (ScopeKind.Assessment, "EXAMPLE-BASELINE-2026/EX-1.1"),
        ]);
    }

    [Fact]
    public void Fields_are_text_and_empty_values_are_left_out()
    {
        var declared = ScopeDesired.From(ScopeFixture.Parse().Scope).ToDictionary(o => (o.Kind, o.Key));

        declared[(ScopeKind.Domain, "Example Pty Ltd")].Fields.ShouldBe(new Dictionary<string, string> { ["description"] = "The whole company." });
        declared[(ScopeKind.Perimeter, "Example Pty Ltd/Example baseline")].Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Example baseline",
            ["ref_id"] = "PER-BASE",
            ["domain"] = "Example Pty Ltd",
        });
        declared[(ScopeKind.System, "Example laptops")].Fields.ShouldBe(new Dictionary<string, string>
        {
            ["type"] = "supporting",
            ["domain"] = "Example Pty Ltd",
            ["parents"] = "Example 365",
        });
        declared[(ScopeKind.Audit, "EXAMPLE-BASELINE-2026")].Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Example baseline 2026",
            ["framework"] = ScopeFixture.FrameworkUrn,
            ["groups"] = "1",
            ["domain"] = "Example Pty Ltd",
            ["perimeter"] = "Example baseline",
            ["eta"] = "2026-06-30",
            ["due_date"] = "2026-07-31",
            ["authors"] = "it@example.com",
            ["reviewers"] = "ceo@example.com",
        });
        declared[(ScopeKind.Document, "mfa-policy")].Fields.ShouldBe(new Dictionary<string, string> { ["name"] = "MFA policy", ["domain"] = "Example Pty Ltd" });
        declared[(ScopeKind.Assessment, "EXAMPLE-BASELINE-2026/EX-1.2")].Fields.ShouldBe(new Dictionary<string, string>
        {
            ["status"] = "done",
            ["result"] = "compliant",
            ["observation"] = "Reviewed by hand.",
            ["controls"] = "AC-MFA",
        });
        declared[(ScopeKind.Assessment, "EXAMPLE-BASELINE-2026/EX-1.1")].Fields.ShouldBe(new Dictionary<string, string>
        {
            ["controls"] = "AC-MFA",
            ["documents"] = "mfa-policy",
        });
    }

    [Fact]
    public void Control_status_and_framework_library_are_written_on_create_only()
    {
        var declared = ScopeDesired.From(ScopeFixture.Parse().Scope).ToDictionary(o => (o.Kind, o.Key));

        var mfa = declared[(ScopeKind.Control, "AC-MFA")];
        mfa.Fields.ShouldBe(new Dictionary<string, string>
        {
            ["name"] = "Multi-factor authentication",
            ["domain"] = "Example Pty Ltd",
            ["owner"] = "it@example.com",
            ["priority"] = "1",
            ["category"] = "technical",
            ["eta"] = "2026-12-31",
            ["systems"] = "Example 365",
            ["documents"] = "mfa-policy",
        });
        mfa.Initial.ShouldBe(new Dictionary<string, string> { ["status"] = "active" });
        declared[(ScopeKind.Framework, ScopeFixture.FrameworkUrn)].Initial.ShouldBe(new Dictionary<string, string> { ["library"] = "urn:example:risk:library:example-baseline" });
    }

    [Fact]
    public void Lists_are_sorted_so_their_order_in_the_file_does_not_matter()
    {
        var controls = ScopeFixture.Controls.Replace("    systems: [Example laptops]", "    systems: [Example laptops, Example 365]", StringComparison.Ordinal);

        var backup = ScopeDesired.From(ScopeFixture.Parse(controls: controls).Scope).Single(o => o.Key == "AC-BACKUP");

        backup.Fields["systems"].ShouldBe("Example 365, Example laptops");
    }

    [Fact]
    public void Query_reads_the_root_folder_frameworks_and_rows_with_hand_written_results()
    {
        var query = ScopeStateQuery.For(ScopeFixture.Parse().Scope);

        (query.RootFolder, query.FrameworkUrns.ShouldHaveSingleItem()).ShouldBe(("Global", ScopeFixture.FrameworkUrn));
        query.Rows.ShouldHaveSingleItem().ShouldBe(new KeyValuePair<string, IReadOnlyList<RequirementKey>>("EXAMPLE-BASELINE-2026", [RequirementKey.Ref("EX-1.2")]), new RowsComparer());
    }

    private sealed class RowsComparer : IEqualityComparer<KeyValuePair<string, IReadOnlyList<RequirementKey>>>
    {
        public bool Equals(KeyValuePair<string, IReadOnlyList<RequirementKey>> x, KeyValuePair<string, IReadOnlyList<RequirementKey>> y) =>
            x.Key == y.Key && x.Value.SequenceEqual(y.Value);

        public int GetHashCode(KeyValuePair<string, IReadOnlyList<RequirementKey>> obj) => obj.Key.GetHashCode(StringComparison.Ordinal);
    }
}
