using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Sprs;
using EvidenceHound.Core.Tests.Scope;
using static EvidenceHound.Core.Tests.Ssp.SspStatementTests;

namespace EvidenceHound.Core.Tests.Ssp;

/// <summary>
/// s17-03: the one SSP layout with the <c>nist-800-171</c> profile: the annex grouped by family for CISO Assistant's <c>cmmc-2.0</c> library,
/// CMMC asset categories in the inventory, and the requirements CMMC does not allow on a POA&amp;M flagged (32 CFR 170.21(a)(2)).
/// </summary>
public class Nist171SspTests
{
    private static readonly FrameworkSnapshot Cmmc = ProgrammeOutlineTests.Snapshot("cmmc-2.0.json");

    private static readonly string[] Level2 = ["L1", "L2"];

    [Fact]
    public void Groups_requirements_by_family()
    {
        var document = Example() with { Annex = SspAnnex.Of(Example(), Cmmc, Level2) };

        document.Annex.Count.ShouldBe(110);
        document.Annex.Single(a => a.Control == "AC.L2-3.1.3").Family.ShouldBe("Access control (AC)");
        document.Annex.Single(a => a.Control == "SC.L2-3.13.11").Family.ShouldBe("System and communications protection (SC)");
        var markdown = SspLayout.Markdown(document, SspLayoutTests.Texts());
        string[] families = [.. markdown.Split('\n').Where(l => l.StartsWith("### ", StringComparison.Ordinal)).Select(l => l[4..])];
        families.Length.ShouldBe(14);
        families[0].ShouldBe("Access control (AC)");
        markdown.ShouldContain("| Requirement | Title | Level | Status | Statement | Responsible | Evidence | POA&M |\n");
        markdown.IndexOf("| AC.L2-3.1.3 |", StringComparison.Ordinal).ShouldBeLessThan(markdown.IndexOf("### Awareness and training (AT)", StringComparison.Ordinal));
    }

    [Fact]
    public void Asset_categories_come_from_custom_field()
    {
        // CISO Assistant's asset custom fields are an Enterprise feature, so the category is the system's cmmc_asset_category tag in
        // scope/setup.yaml (SspBuilderTests covers the read); the inventory shows it in 32 CFR 170.19's terms.
        var markdown = SspLayout.Markdown(Example(), SspLayoutTests.Texts());

        markdown.ShouldContain("| System | Type | Owner | Description | CMMC asset category |\n");
        markdown.ShouldContain("| Example 365 | primary | it@example.com | Mail, files and identity. | CUI asset |\n");
        markdown.ShouldContain("| Example laptops | supporting | it@example.com | Staff laptops. | - |\n");
        markdown.ShouldContain("- Example laptops: no CMMC asset category;");
        SspLayout.Markdown(SspLayoutTests.Example(), SspLayoutTests.Texts()).ShouldNotContain("CMMC asset category");
        CmmcAssetCategory.Name("security_protection").ShouldBe("Security protection asset");
        CmmcAssetCategory.Name("specialized").ShouldBe("Specialized asset");
        CmmcAssetCategory.Name("the cloud").ShouldBeNull();
    }

    [Fact]
    public void Requirements_cmmc_does_not_allow_on_a_poam_are_flagged()
    {
        CmmcPoam.Reason("AC.L2-3.1.3", Methodology()).ShouldBeNull();
        CmmcPoam.Reason("AC.L2-3.1.20", Methodology()).ShouldNotBeNull().ShouldContain("never");
        CmmcPoam.Reason("PE.L1-3.10.3", Methodology()).ShouldNotBeNull().ShouldContain("never");
        CmmcPoam.Reason("AC.L1-3.1.1", Methodology()).ShouldNotBeNull().ShouldContain("worth 5 points");
        CmmcPoam.Reason("SC.L2-3.13.11", Methodology()).ShouldNotBeNull().ShouldContain("not FIPS-validated");

        var document = Example();
        document.Gaps.ShouldContain(new SspGap("AC.L1-3.1.1", CmmcPoam.Reason("AC.L1-3.1.1", Methodology())!));
        document.Gaps.ShouldNotContain(g => g.Requirement == "AC.L2-3.1.3");
        var items = Poam.Of(document);
        items.Single(i => i.Requirement.Value == "AC.L1-3.1.1").NotAllowed.ShouldNotBeNull();
        items.Single(i => i.Requirement.Value == "AC.L2-3.1.3").NotAllowed.ShouldBeNull();
        Poam.Markdown(items).ShouldContain("CMMC does not allow these on a POA&M (32 CFR 170.21(a)(2)):\n\n- AC.L1-3.1.1: ");
    }

    [Fact]
    public void Renders_expected_markdown_from_fixture()
    {
        var document = Example() with { Annex = SspAnnex.Of(Example(), Cmmc, Level2) };

        var markdown = SspLayout.Markdown(document, SspLayoutTests.Texts());

        var golden = Path.Combine(AppContext.BaseDirectory, "golden", "ssp-nist-800-171.md");
        markdown.ShouldBe(File.ReadAllText(golden));
    }

    /// <summary>The SPRS weights the rules need: a 1-point, a 5-point and the 3.13.11 requirement, and the never-allowed ones.</summary>
    internal static SprsMethodology Methodology() => new(
        "NIST SP 800-171 DoD Assessment Methodology",
        "1.2.1",
        110,
        [
            new SprsWeight("3.1.1", 5),
            new SprsWeight("3.1.3", 1),
            new SprsWeight("3.1.20", 1),
            new SprsWeight("3.10.3", 1),
            new SprsWeight("3.12.4", null),
            new SprsWeight("3.13.11", 5) { Partial = new SprsPartial(3, "cryptography is employed, but mostly not FIPS validated") },
        ]);

    internal static SspDocument Example()
    {
        var controls = Controls(Control("AC-MFA", "Every user signs in with MFA.", "it@example.com"), Control("AC-FLOW", null, "it@example.com", "2026-12-31"));
        var repo = new SspSource(SspSource.Repo, "scope/ssp.yaml line 2");
        var ca = new SspSource(SspSource.CisoAssistant, "asset 'Example 365'");
        SspValue Repo(string value) => new(value, repo);
        SspValue Held(string? value) => new(value, ca);
        SspRequirement[] rows =
        [
            SspStatements.Complete(Row("AC.L1-3.1.1", "non_compliant", "Two shared accounts remain.", "AC-MFA"), controls),
            SspStatements.Complete(Row("AC.L1-3.1.2", "compliant", "Roles limit what each user can do.", "AC-MFA"), controls),
            SspStatements.Complete(Row("AC.L2-3.1.3", "non_compliant", null, "AC-FLOW"), controls),
        ];
        var document = new SspDocument(
            "cmmc-level-2",
            SspProfile.Nist800171,
            Repo("Example CUI enclave"),
            Repo("0.1"),
            Repo(Cmmc.FrameworkUrn),
            Repo("EX-CMMC-2026"),
            Repo("CUI enclave"),
            [new SspRoleValue("system_owner", Repo("it@example.com")), new SspRoleValue("authorising_official", Repo("ceo@example.com")), new SspRoleValue("security_contact", Repo("IT team"))],
            [
                new SspSystem(Held("Example 365"), Held("primary"), Held("it@example.com"), Held("Mail, files and identity.")) { Category = Repo("cui") },
                new SspSystem(Held("Example laptops"), Held("supporting"), Held("it@example.com"), Held("Staff laptops.")),
            ],
            [new SspService(Repo("Example 365"), Held("Managed hosting"), Held("Example Cloud Pty Ltd"), Held("3"))],
            [.. controls.Values],
            rows,
            [Repo("Access control policy")],
            [new SspNamed("purpose", Repo("ssp/purpose.md")), new SspNamed("environment", Repo("ssp/environment.md")), new SspNamed("management", Repo("ssp/management.md")), new SspNamed("laws_and_policies", Repo("ssp/laws-and-policies.md"))])
        {
            Classification = Repo("CUI"),
            OperationalStatus = Repo("operational"),
            SystemType = Repo("general support system"),
        };
        return document with { PoamNotAllowed = CmmcPoam.For(document.Requirements, Methodology()) };
    }
}
