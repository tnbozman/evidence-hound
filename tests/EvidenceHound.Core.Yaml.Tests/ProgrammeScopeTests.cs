using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s11-01: programmes in <c>scope/setup.yaml</c> name a framework, the groups reported against and the audit of record.</summary>
public class ProgrammeScopeTests
{
    private static readonly string OtherFramework = "urn:example:risk:framework:example-other";

    [Fact]
    public void A_valid_programme_is_read_and_has_no_problems()
    {
        var parsed = ScopeFixture.Parse(WithProgrammes(Programme()));

        var programme = parsed.Scope.Setup.Programmes.ShouldHaveSingleItem();
        (programme.Id, programme.Name, programme.Framework.Value, programme.Audit.Value, programme.GroupBy).ShouldBe(
            ("baseline", "Example baseline", ScopeFixture.FrameworkUrn, "EXAMPLE-BASELINE-2026", "parent"));
        programme.Groups.ShouldBe(["1", "2"]);
        Codes(WithProgrammes(Programme())).ShouldNotContain(c => c.StartsWith("scope.programme", StringComparison.Ordinal));
    }

    [Fact]
    public void Programme_framework_must_be_declared()
    {
        var problems = ScopeFixture.Problems(WithProgrammes(Programme(framework: OtherFramework)));

        var problem = problems.Single(p => p.Code == "scope.programme_framework_unknown");
        problem.At.File.ShouldBe("scope/setup.yaml");
        problem.Message.ShouldContain(OtherFramework);
    }

    [Fact]
    public void Programme_groups_must_exist_in_snapshot()
    {
        var problem = ScopeFixture.Problems(WithProgrammes(Programme(groups: "[\"1\", \"9\"]"))).Single(p => p.Code == "scope.programme_group_unknown");

        problem.Message.ShouldContain("'9'");
        problem.Hint.ShouldContain("1, 2");
    }

    [Fact]
    public void A_framework_without_a_snapshot_cannot_be_a_programme()
    {
        var setup = ScopeFixture.Setup.Replace("    snapshot: ../frameworks/example-baseline.json\n", "    file: ../frameworks/example-baseline.yaml\n", StringComparison.Ordinal);
        var parsed = ScopeFixture.Parse(Append(setup, Programme()));

        var problems = ScopeValidator.Validate(parsed.Scope, new Dictionary<string, Frameworks.FrameworkSnapshot>(), []);

        problems.ShouldContain(p => p.Code == "scope.programme_needs_snapshot");
        problems.ShouldNotContain(p => p.Code == "scope.programme_group_unknown");
    }

    [Fact]
    public void Programme_audit_must_exist_and_use_its_framework()
    {
        Codes(WithProgrammes(Programme(audit: "EX-MISSING"))).ShouldContain("scope.programme_audit_unknown");

        var otherAudit = ScopeFixture.Audit.Replace($"framework: {ScopeFixture.FrameworkUrn}", $"framework: {OtherFramework}", StringComparison.Ordinal);
        var setup = Append(ScopeFixture.Setup.Replace("domains:", $"  - urn: {OtherFramework}\n    library: urn:example:risk:library:example-other\ndomains:", StringComparison.Ordinal), Programme());
        ScopeFixture.Problems(setup, audit: otherAudit).Select(p => p.Code).ShouldContain("scope.programme_audit_framework");
    }

    [Fact]
    public void Audit_in_two_programmes_is_reported()
    {
        var problem = ScopeFixture.Problems(WithProgrammes(Programme(), Programme(id: "baseline-again"))).Single(p => p.Code == "scope.programme_audit_shared");

        problem.Message.ShouldContain("EXAMPLE-BASELINE-2026");
        problem.Message.ShouldContain("baseline");
    }

    [Theory]
    [InlineData("Baseline")]
    [InlineData("base line")]
    [InlineData("-baseline")]
    public void Programme_id_is_a_lowercase_slug(string id) =>
        Codes(WithProgrammes(Programme(id: id))).ShouldContain("scope.programme_id");

    [Fact]
    public void Programme_ids_are_unique()
    {
        var other = Programme().Replace("EXAMPLE-BASELINE-2026", "EX-OTHER", StringComparison.Ordinal);

        ScopeFixture.Problems(WithProgrammes(Programme(), other)).ShouldContain(p => p.Code == "scope.duplicate" && p.Message.Contains("programme 'baseline'", StringComparison.Ordinal));
    }

    [Fact]
    public void Group_by_accepts_parent_only()
    {
        ScopeFixture.Parse(WithProgrammes(Programme() + "    group_by: strategy\n")).Problems.ShouldContain(p => p.Message.Contains("group_by", StringComparison.Ordinal));
    }

    [Fact]
    public void Programmes_are_not_in_the_plan()
    {
        var without = ScopeDesired.From(ScopeFixture.Parse().Scope);
        var with = ScopeDesired.From(ScopeFixture.Parse(WithProgrammes(Programme())).Scope);

        with.Select(o => (o.Kind, o.Key)).ShouldBe(without.Select(o => (o.Kind, o.Key)));
    }

    [Fact]
    public void Register_must_be_known_with_known_fields()
    {
        var register = "    register:\n      of: ai_systems\n      by: [tier, use]\n";
        var programme = ScopeFixture.Parse(WithProgrammes(Programme() + register)).Scope.Setup.Programmes.ShouldHaveSingleItem();
        (programme.Register!.Of, programme.Register.By).ShouldBe(("ai_systems", (IReadOnlyList<string>)["tier", "use"]));
        Codes(WithProgrammes(Programme() + register)).ShouldNotContain(c => c.StartsWith("scope.programme", StringComparison.Ordinal));

        Codes(WithProgrammes(Programme() + register.Replace("of: ai_systems", "of: robots", StringComparison.Ordinal))).ShouldContain("scope.value_invalid");
        var problem = ScopeFixture.Problems(WithProgrammes(Programme() + register.Replace("[tier, use]", "[tier, colour]", StringComparison.Ordinal)))
            .Single(p => p.Code == "scope.programme_register_unknown");
        problem.Message.ShouldContain("colour");
        problem.Hint.ShouldContain("tier, use, oversight");
    }

    internal static string Programme(string id = "baseline", string framework = ScopeFixture.FrameworkUrn, string groups = "[\"1\", \"2\"]", string audit = "EXAMPLE-BASELINE-2026") =>
        $"""
          - id: {id}
            name: Example baseline
            framework: {framework}
            groups: {groups}
            audit: {audit}

        """;

    internal static string WithProgrammes(params string[] programmes) => Append(ScopeFixture.Setup, string.Concat(programmes));

    private static string Append(string setup, string programmes) => setup + "\nprogrammes:\n" + programmes;

    private static string[] Codes(string setup) => [.. ScopeFixture.Problems(setup).Select(p => p.Code)];
}
