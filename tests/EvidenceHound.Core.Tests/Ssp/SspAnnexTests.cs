using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Scope;
using static EvidenceHound.Core.Tests.Ssp.SspStatementTests;

namespace EvidenceHound.Core.Tests.Ssp;

/// <summary>s11-16: the ISM annex's columns follow the ASD Blueprint SSP annex template.</summary>
public class SspAnnexTests
{
    private static readonly string Doc = "urn:example:risk:function:example-doc-pol:";

    private static readonly FrameworkSnapshot Framework = new(
        "urn:example:risk:library:example",
        "urn:example:risk:framework:example",
        1,
        [new ImplementationGroup("1", "Level one"), new ImplementationGroup("2", "Level two")],
        [
            new SnapshotNode("urn:example:risk:req_node:example:mfa:ex-1", "EX-1", "urn:example:risk:req_node:example:mfa", ["1", "2"], "MFA for remote access…")
            {
                Description = "MFA is used for every remote access path.",
                ReferenceControls = [Doc + "pol.mfa", Doc + "tech.mfa"],
            },
            new SnapshotNode("urn:example:risk:req_node:example:rb:ex-2", "EX-2", "urn:example:risk:req_node:example:rb", ["2"], "Backups are tested.")
            {
                Description = "Backups are tested.",
                ReferenceControls = [Doc + "proc.backup", Doc + "doc.backup_plan", "urn:example:risk:function:other:custom"],
            },
            new SnapshotNode("urn:example:risk:req_node:example:rb:ex-3", "EX-3", "urn:example:risk:req_node:example:rb", ["2"], "Restores are tested."),
        ]);

    [Fact]
    public void Control_type_comes_from_reference_control_prefixes()
    {
        var annex = SspAnnex.Of(Document(Row("EX-1", "compliant", "Done.")), Framework, ["1", "2"]);

        annex.Single(a => a.Control == "EX-1").ControlTypes.ShouldBe(["Policy", "Technical"]);
        annex.Single(a => a.Control == "EX-2").ControlTypes.ShouldBe(["Procedure", "Documentation"]);
        annex.Single(a => a.Control == "EX-3").ControlTypes.ShouldBeEmpty();
        annex.Single(a => a.Control == "EX-1").Description.ShouldBe("MFA is used for every remote access path.");
        annex.Single(a => a.Control == "EX-3").Description.ShouldBe("Restores are tested.");
        annex.Single(a => a.Control == "EX-1").MaturityLevels.ShouldBe(["1", "2"]);
    }

    [Fact]
    public void Supplier_provided_system_makes_responsibility_shared()
    {
        var hosted = Control("AC-HOSTED", "Hosted MFA.", "it@example.com") with { Systems = ["Example 365"] };
        var local = Control("AC-LOCAL", "Local backups.", "it@example.com") with { Systems = ["Example laptops"] };
        var outsourced = Control("AC-MSP", "Run by the MSP.", "it@example.com") with
        {
            ResponsibleEntity = new SspValue("Outsourced service provider", new SspSource(SspSource.Repo, "scope/controls.yaml line 9")),
        };
        var document = Document(Controls(hosted, local, outsourced), Row("EX-1", "compliant", "Done.", "AC-HOSTED"), Row("EX-2", "compliant", "Done.", "AC-LOCAL"), Row("EX-3", "compliant", "Done.", "AC-MSP")) with
        {
            ExternalServices = [Service("Example 365")],
        };

        var annex = SspAnnex.Of(document, Framework, ["1", "2"]);

        annex.Single(a => a.Control == "EX-1").ResponsibleEntity.Value.ShouldBe("Shared");
        annex.Single(a => a.Control == "EX-2").ResponsibleEntity.Value.ShouldBe("Consumer");
        var row = annex.Single(a => a.Control == "EX-3");
        row.ResponsibleEntity.Value.ShouldBe("Outsourced service provider");
        row.ResponsibleEntity.Source.Kind.ShouldBe(SspSource.Repo);
    }

    [Theory]
    [InlineData("compliant", "Effective")]
    [InlineData("partially_compliant", "Ineffective")]
    [InlineData("non_compliant", "Not implemented")]
    [InlineData("not_applicable", "Not applicable")]
    [InlineData("not_assessed", "Not assessed")]
    [InlineData(null, "Not assessed")]
    public void Asd_status_maps_from_result(string? result, string expected)
    {
        var annex = SspAnnex.Of(Document(Row("EX-1", result, "Observed.")), Framework, ["1"]);

        annex.ShouldHaveSingleItem().AsdStatus.Value.ShouldBe(expected);
    }

    [Fact]
    public void A_control_with_no_row_is_not_assessed()
    {
        var annex = SspAnnex.Of(Document(), Framework, ["2"]);

        annex.Select(a => a.AsdStatus.Value).ShouldAllBe(s => s == "Not assessed");
    }

    private static SspService Service(string system)
    {
        var source = new SspSource(SspSource.CisoAssistant, "solution Example hosting");
        return new SspService(new SspValue(system, source), new SspValue("Example hosting", source), new SspValue("Example Cloud Pty Ltd", source), new SspValue("3", source));
    }
}
