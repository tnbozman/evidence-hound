using EvidenceHound.Core.Frameworks;
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
            ["systems"] = "Example 365, Example laptops",
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
    public void Query_reads_the_root_folder_and_frameworks()
    {
        var scope = ScopeFixture.Parse().Scope;

        var query = ScopeStateQuery.For(scope, ScopeDesired.From(scope));

        (query.RootFolder, query.FrameworkUrns.ShouldHaveSingleItem()).ShouldBe(("Global", ScopeFixture.FrameworkUrn));
    }

    [Fact]
    public void Other_libraries_are_loaded_by_urn_and_read_with_the_frameworks()
    {
        var setup = ScopeFixture.Setup.Replace("domains:", "libraries:\n  - urn:example:risk:library:mapping-example\ndomains:", StringComparison.Ordinal);
        var scope = ScopeFixture.Parse(setup: setup).Scope;

        var declared = ScopeDesired.From(scope);
        var query = ScopeStateQuery.For(scope, declared);

        declared.Single(o => o.Key == "urn:example:risk:library:mapping-example").ShouldSatisfyAllConditions(
            o => o.Kind.ShouldBe(ScopeKind.Framework),
            o => o.Initial.ShouldBe(new Dictionary<string, string> { ["library"] = "urn:example:risk:library:mapping-example" }));
        query.FrameworkUrns.ShouldBe([ScopeFixture.FrameworkUrn, "urn:example:risk:library:mapping-example"]);
    }

    [Fact]
    public void Framework_takes_its_version_and_library_from_the_snapshot()
    {
        var setup = ScopeFixture.Setup.Replace("    library: urn:example:risk:library:example-baseline\n", "", StringComparison.Ordinal);

        var framework = ScopeDesired.From(ScopeFixture.Parse(setup: setup).Scope, ScopeInputs.None with { Snapshots = ScopeFixture.Snapshots })[0];

        framework.Fields.ShouldBe(new Dictionary<string, string> { ["version"] = ScopeFixture.Snapshot.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        framework.Initial.ShouldBe(new Dictionary<string, string> { ["library"] = ScopeFixture.Snapshot.LibraryUrn });
    }

    [Fact]
    public void Custom_library_is_declared_with_its_file_and_version()
    {
        const string urn = "urn:example:risk:library:example-risk";
        var setup = ScopeFixture.Setup.Replace("domains:", $"libraries:\n  - urn: {urn}\n    file: ../libraries/example-risk.yaml\ndomains:", StringComparison.Ordinal);
        var scope = ScopeFixture.Parse(setup: setup).Scope;
        var file = new ScopeFile("/repo/libraries/example-risk.yaml", "abc");
        var inputs = ScopeInputs.None with
        {
            Files = new Dictionary<string, IReadOnlyList<ScopeFile>> { [urn] = [file] },
            Libraries = new Dictionary<string, LibrarySnapshot> { [urn] = new(urn, 2, [], [], []) },
        };

        var declared = ScopeDesired.From(scope, inputs);

        var library = declared.Single(o => o.Key == urn);
        library.Kind.ShouldBe(ScopeKind.Framework);
        library.Fields.ShouldBe(new Dictionary<string, string> { ["version"] = "2" });
        library.Initial.ShouldBe(new Dictionary<string, string> { ["library"] = urn, ["file"] = "../libraries/example-risk.yaml" });
        library.Files.ShouldBe([file]);
        ScopeStateQuery.For(scope, declared).FrameworkUrns.ShouldContain(urn);
    }

    [Fact]
    public void Framework_says_what_libraries_it_brings_with_it()
    {
        // s15-01: CISO Assistant loads a library's dependencies with it, so a plan says which threat catalogues arrive with a framework.
        var snapshot = ScopeFixture.Snapshot with { Dependencies = ["urn:example:risk:library:example-catalog", "urn:example:risk:library:example-doc-pol"] };
        var inputs = ScopeInputs.None with { Snapshots = new Dictionary<string, FrameworkSnapshot> { [ScopeFixture.FrameworkUrn] = snapshot } };

        var framework = ScopeDesired.From(ScopeFixture.Parse().Scope, inputs)[0];

        framework.Initial["brings"].ShouldBe("urn:example:risk:library:example-catalog, urn:example:risk:library:example-doc-pol");
    }

    [Fact]
    public void Query_reads_the_rows_the_scope_writes()
    {
        var scope = ScopeFixture.Parse().Scope;

        var query = ScopeStateQuery.For(scope, ScopeDesired.From(scope));

        query.Rows.ShouldHaveSingleItem().ShouldBe(
            new KeyValuePair<string, IReadOnlyList<RequirementKey>>("EXAMPLE-BASELINE-2026", [RequirementKey.Ref("EX-1.2"), RequirementKey.Ref("EX-1.1")]), new RowsComparer());
    }

    private sealed class RowsComparer : IEqualityComparer<KeyValuePair<string, IReadOnlyList<RequirementKey>>>
    {
        public bool Equals(KeyValuePair<string, IReadOnlyList<RequirementKey>> x, KeyValuePair<string, IReadOnlyList<RequirementKey>> y) =>
            x.Key == y.Key && x.Value.SequenceEqual(y.Value);

        public int GetHashCode(KeyValuePair<string, IReadOnlyList<RequirementKey>> obj) => obj.Key.GetHashCode(StringComparison.Ordinal);
    }
}
