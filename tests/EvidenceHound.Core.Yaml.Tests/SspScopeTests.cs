using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s11-02: <c>scope/ssp.yaml</c> declares one system security plan per programme.</summary>
public class SspScopeTests
{
    [Fact]
    public void A_valid_ssp_is_read_and_has_no_problems()
    {
        var parsed = SspFixture.Parse();

        parsed.Problems.ShouldBeEmpty();
        var ssp = parsed.Scope.Ssps.ShouldHaveSingleItem();
        (ssp.Programme.Value, ssp.Profile, ssp.SystemName, ssp.Perimeter.Value, ssp.Version).ShouldBe(("baseline", "ism", "Example corporate IT", "Example baseline", "0.1"));
        ssp.Roles.Select(r => (r.Role, r.Holder.Value)).ShouldBe([("system_owner", "it@example.com"), ("authorising_official", "ceo@example.com"), ("security_contact", "GRC team")]);
        ssp.Documents.Select(d => d.Value).ShouldBe(["mfa-policy"]);
        ssp.Sections.Select(s => (s.Name, s.File)).ShouldBe([("environment", "ssp/environment.md"), ("physical", "ssp/physical.md"), ("personnel", "ssp/personnel.md")]);
        SspFixture.Problems().ShouldNotContain(p => p.Code.StartsWith("ssp.", StringComparison.Ordinal));
    }

    [Fact]
    public void Programme_and_perimeter_must_exist()
    {
        var problems = SspFixture.Problems(SspFixture.Ssp
            .Replace("programme: baseline", "programme: missing-programme", StringComparison.Ordinal)
            .Replace("perimeter: Example baseline", "perimeter: Nowhere", StringComparison.Ordinal));

        problems.Single(p => p.Code == "ssp.programme_unknown").Message.ShouldContain("missing-programme");
        var perimeter = problems.Single(p => p.Code == "ssp.perimeter_unknown");
        perimeter.Message.ShouldContain("Nowhere");
        perimeter.At.File.ShouldBe(SspFixture.SspPath);
    }

    [Fact]
    public void Roles_and_documents_must_exist()
    {
        var problems = SspFixture.Problems(SspFixture.Ssp
            .Replace("security_contact: GRC team", "security_contact: Nobody team", StringComparison.Ordinal)
            .Replace("documents: [mfa-policy]", "documents: [mfa-policy, no-such-document]", StringComparison.Ordinal));

        problems.Single(p => p.Code == "ssp.role_unknown").Message.ShouldContain("Nobody team");
        problems.Single(p => p.Code == "ssp.document_unknown").Message.ShouldContain("no-such-document");
    }

    [Fact]
    public void Required_section_missing_is_reported()
    {
        var problem = SspFixture.Problems(SspFixture.Ssp.Replace("\n      personnel: ssp/personnel.md", "", StringComparison.Ordinal))
            .Single(p => p.Code == "ssp.section_missing");

        problem.Message.ShouldContain("personnel");
        problem.Message.ShouldContain("ism");
    }

    [Fact]
    public void Every_required_role_is_named()
    {
        SspFixture.Problems(SspFixture.Ssp.Replace("      authorising_official: ceo@example.com\n", "", StringComparison.Ordinal))
            .Single(p => p.Code == "ssp.role_missing").Message.ShouldContain("authorising_official");
    }

    [Fact]
    public void A_programme_has_at_most_one_ssp()
    {
        var twice = SspFixture.Ssp + "\n" + SspFixture.Ssp["ssps:\n".Length..];

        SspFixture.Problems(twice).ShouldContain(p => p.Code == "scope.duplicate" && p.Message.Contains("ssp for programme 'baseline'", StringComparison.Ordinal));
    }

    [Fact]
    public void Profile_is_ism_or_nist_800_171()
    {
        SspFixture.Parse(SspFixture.Ssp.Replace("profile: ism", "profile: iso27001", StringComparison.Ordinal)).Problems.ShouldContain(p => p.Message.Contains("profile", StringComparison.Ordinal));
    }

    [Fact]
    public void Ssps_are_not_in_the_plan()
    {
        var without = ScopeDesired.From(SspFixture.Parse(ssp: "ssps: []\n").Scope);

        ScopeDesired.From(SspFixture.Parse().Scope).Select(o => (o.Kind, o.Key)).ShouldBe(without.Select(o => (o.Kind, o.Key)));
    }
}
