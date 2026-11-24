using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Tests.Scope;
using static EvidenceHound.Core.Tests.Ssp.SspStatementTests;

namespace EvidenceHound.Core.Tests.Ssp;

/// <summary>s11-04: one SSP layout, NIST SP 800-18 Rev 1 Appendix A, with an ISM annex grouped by Essential Eight strategy and linked to the POA&amp;M.</summary>
public class SspLayoutTests
{
    private static readonly FrameworkSnapshot EssentialEight = ProgrammeOutlineTests.Snapshot("essential-eight-2023.json");

    [Fact]
    public void Sections_follow_sp_800_18_appendix_a()
    {
        var markdown = SspLayout.Markdown(Example(), Texts());

        string[] headings = [.. markdown.Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)).Select(l => l[3..])];
        headings.ShouldBe(
        [
            "Gaps",
            "1. System name and identifier",
            "2. System categorisation",
            "3. System owner",
            "4. Authorising official",
            "5. Other designated contacts",
            "6. Assignment of security responsibility",
            "7. System operational status",
            "8. System type",
            "9. General description and purpose",
            "10. System environment",
            "11. Interconnections and external service providers",
            "12. Applicable laws, regulations and policies",
            "13. Security controls (annex)",
            "14. Completion and approval dates",
            "15. Ongoing system security plan maintenance",
            "Appendix: plan of action and milestones",
        ]);
        markdown.ShouldContain("The overview (sections 1 to 12) and the annex (section 13) meet ISM-0041");
    }

    [Fact]
    public void Repeated_ism_control_appears_once_with_strategies()
    {
        var annex = SspAnnex.Of(Example(), EssentialEight, ["1", "2"]);

        var row = annex.Single(a => a.Control == "ISM-1815");
        row.Parents.ShouldBe(["mfa", "rap", "ac", "uah"]);
        annex.Select(a => a.Control).ShouldBeUnique();
        annex.Count.ShouldBe(EssentialEight.Nodes.Where(n => n.Groups.Contains("1") || n.Groups.Contains("2")).Select(n => n.RefId).Distinct().Count());
    }

    [Fact]
    public void Annex_row_links_its_poam_item()
    {
        var document = Example();
        var annex = SspAnnex.Of(document, EssentialEight, ["1", "2"]);

        annex.Single(a => a.Control == "ISM-1173").Poam.ShouldBeNull();
        annex.Single(a => a.Control == "ISM-1504").Poam.ShouldBe("POAM-ISM-1504");
        Poam.Of(document).Select(p => p.Id).ShouldContain("POAM-ISM-1504");
        // A control with no row in CISO Assistant is not assessed: Not implemented, on no POA&M yet, and shown as such.
        annex.Single(a => a.Control == "ISM-1807").Status.Value.ShouldBe("Not implemented");
    }

    [Fact]
    public void Renders_expected_markdown_from_fixture()
    {
        var document = Example() with { Annex = SspAnnex.Of(Example(), EssentialEight, ["1"]) };

        var markdown = SspLayout.Markdown(document, Texts());

        var golden = Path.Combine(AppContext.BaseDirectory, "golden", "ssp-ism.md");
        markdown.ShouldBe(File.ReadAllText(golden));
    }

    private static Dictionary<string, string> Texts() => new()
    {
        ["purpose"] = "Example corporate IT runs the company's mail, files and laptops.",
        ["environment"] = "One office and its cloud services.",
        ["management"] = "The IT team runs it; changes go through the weekly review.",
        ["laws_and_policies"] = "Privacy Act 1988; the Example Pty Ltd information security policy.",
    };

    private static SspDocument Example()
    {
        var controls = Controls(Control("AC-MFA", "Every user signs in with MFA.", "it@example.com"), Control("AC-ADMIN", null, "it@example.com", "2026-12-31"));
        var repo = new SspSource(SspSource.Repo, "scope/ssp.yaml line 2");
        var ca = new SspSource(SspSource.CisoAssistant, "asset 'Example 365'");
        SspValue Repo(string value) => new(value, repo);
        SspValue Held(string? value) => new(value, ca);
        return new SspDocument(
            "essential-eight",
            "ism",
            Repo("Example corporate IT"),
            Repo("0.1"),
            Repo(EssentialEight.FrameworkUrn),
            Repo("EX-E8-2026"),
            Repo("Corporate IT"),
            [new SspRoleValue("system_owner", Repo("it@example.com")), new SspRoleValue("authorising_official", Repo("ceo@example.com")), new SspRoleValue("security_contact", Repo("IT team"))],
            [new SspSystem(Held("Example 365"), Held("primary"), Held("it@example.com"), Held("Mail, files and identity."))],
            [new SspService(Repo("Example 365"), Held("Managed hosting"), Held("Example Cloud Pty Ltd"), Held("3"))],
            [.. controls.Values],
            [
                SspStatements.Complete(Row("ISM-1173", "compliant", "All admins use phishing-resistant MFA.", "AC-MFA"), controls),
                SspStatements.Complete(Row("ISM-1504", "non_compliant", null, "AC-ADMIN"), controls),
            ],
            [Repo("Identity and access policy")],
            [new SspNamed("purpose", Repo("ssp/purpose.md")), new SspNamed("environment", Repo("ssp/environment.md")), new SspNamed("management", Repo("ssp/management.md")), new SspNamed("laws_and_policies", Repo("ssp/laws-and-policies.md"))])
        {
            Classification = Repo("OFFICIAL"),
            OperationalStatus = Repo("operational"),
            SystemType = Repo("general support system"),
        };
    }
}
