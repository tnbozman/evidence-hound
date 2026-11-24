using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;
using static EvidenceHound.Core.Tests.Ssp.SspStatementTests;

namespace EvidenceHound.Core.Tests.Ssp;

/// <summary>
/// s11-06: the brief a person's own agent drafts control descriptions from: the controls with no description that leave an implemented
/// requirement without a statement, with names only, never evidence content. hound has no model; it only hands over the facts.
/// </summary>
public class SspSuggestTests
{
    [Fact]
    public void Only_gap_controls_are_listed()
    {
        var controls = Controls(
            Control("AC-MFA", "Every user signs in with MFA.", "it@example.com"),
            Control("AC-ADMIN", null, "it@example.com"),
            Control("AC-PLANNED", null, "it@example.com", "2026-12-31"),
            Control("AC-OBSERVED", null, "it@example.com"));
        var document = Document(
            controls,
            SspStatements.Complete(Row("ISM-1173", "compliant", null, "AC-MFA"), controls),
            SspStatements.Complete(Row("ISM-1175", "compliant", null, "AC-ADMIN"), controls),
            SspStatements.Complete(Row("ISM-1176", "partially_compliant", null, "AC-ADMIN"), controls),
            SspStatements.Complete(Row("ISM-1504", "non_compliant", null, "AC-PLANNED"), controls),
            SspStatements.Complete(Row("ISM-1505", "compliant", "Checked by hand each quarter.", "AC-OBSERVED"), controls));

        var briefs = SspSuggest.Brief(document);

        var admin = briefs.ShouldHaveSingleItem();
        admin.RefId.ShouldBe("AC-ADMIN");
        admin.Name.ShouldBe("AC-ADMIN name");
        admin.Owner.ShouldBe("it@example.com");
        admin.Requirements.Select(r => (r.Ref, r.Status)).ShouldBe([("ISM-1175", "Implemented"), ("ISM-1176", "Partially implemented")]);
    }

    [Fact]
    public void The_brief_holds_names_not_evidence_content()
    {
        var controls = Controls(Control("AC-ADMIN", null, "it@example.com") with { Systems = ["Example 365"] });
        var row = SspStatements.Complete(Row("ISM-1175", "compliant", null, "AC-ADMIN"), controls);
        var document = Document(controls, row) with
        {
            Annex = [new SspAnnexRow("ISM-1175", "Privileged access", ["rap"], Value("applicable"), Value("Implemented"), Value(null), Value(null), Value("policy"), null) { Description = "Privileged users use separate accounts." }],
        };

        var json = JsonNode.Parse(SspSuggest.Json("essential-eight", SspSuggest.Brief(document)))!;

        json["programme"]!.GetValue<string>().ShouldBe("essential-eight");
        json["instructions"]!.GetValue<string>().ShouldContain("hound ssp describe");
        var control = json["controls"]!.AsArray().ShouldHaveSingleItem()!.AsObject();
        control.Select(p => p.Key).ShouldBe(["ref_id", "name", "owner", "systems", "requirements", "describe_with"]);
        control["systems"]!.AsArray().Select(s => s!.GetValue<string>()).ShouldBe(["Example 365"]);
        control["describe_with"]!.GetValue<string>().ShouldBe("hound ssp describe --control AC-ADMIN --text \"<description>\"");
        var requirement = control["requirements"]!.AsArray().ShouldHaveSingleItem()!.AsObject();
        requirement.Select(p => p.Key).ShouldBe(["ref", "description", "status", "evidence"]);
        requirement["description"]!.GetValue<string>().ShouldBe("Privileged users use separate accounts.");
        requirement["evidence"]!.GetValue<string>().ShouldBe("policy");
    }

    private static SspValue Value(string? value) => new(value, new SspSource(SspSource.CisoAssistant, "x"));
}
