using System.Text.Json.Nodes;
using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Onboarding.Output;
using EvidenceHound.Onboarding.Sessions;
using EvidenceHound.Onboarding.Skills;
using EvidenceHound.Onboarding.Suppliers;

namespace EvidenceHound.Onboarding.Tests.Journey;

/// <summary>Supplier first: a system is onboarded only once its supplier is declared and approved.</summary>
public sealed class OnboardSupplierTests
{
    internal static readonly SupplierChoice Choice = new("SUP-EXAMPLE", "Example Assets Ltd", "SOL-EXAMPLE-ASSETS", "Asset inventory", "CTR-EXAMPLE-ASSETS", "Asset inventory subscription");

    private static readonly SourceLine Here = new("scope/third-parties/example-assets.yaml", 1);

    [Fact]
    public void Unapproved_supplier_stops_before_applicability_and_resumes_after()
    {
        SupplierGate.Check([], Choice).ShouldBe(SupplierStatus.NotDeclared);
        SupplierGate.Check([Entity("draft")], Choice).ShouldBe(SupplierStatus.NotApproved);
        SupplierGate.Check([Entity("active") with { Contracts = [] }], Choice).ShouldBe(SupplierStatus.NotApproved);
        SupplierGate.Check([Entity("active")], Choice).ShouldBe(SupplierStatus.Approved);

        var session = new OnboardSession("EXAMPLE-ASSETS", OnboardStage.Supplier) { Supplier = Choice };
        var stopped = session.Advance(SupplierStatus.NotApproved);
        stopped.Stage.ShouldBe(OnboardStage.AwaitingSupplier);
        stopped.Advance(SupplierStatus.NotApproved).Stage.ShouldBe(OnboardStage.AwaitingSupplier);
        stopped.Advance(SupplierStatus.Approved).Stage.ShouldBe(OnboardStage.Applicability);
    }

    [Fact]
    public void Supplier_proposals_carry_reasons_and_sources()
    {
        string[] sources = ["answers", "https://www.example.com/trust"];
        var proposal = SupplierProposal.Read(Reply(), sources).Value();

        proposal.Levels.Select(l => (l.Name, l.Value)).ShouldBe([("dependency", 3), ("penetration", 2), ("maturity", 3), ("trust", 3), ("criticality", 3)]);
        proposal.Levels.ShouldAllBe(l => l.Reason.Length > 0 && l.Sources.Count > 0);
        proposal.Summary.ShouldHaveSingleItem().Source.ShouldBe("https://www.example.com/trust");

        var noReason = Reply();
        noReason["levels"]![0]!["reason"] = "";
        SupplierProposal.Read(noReason, sources).Problems().ShouldHaveSingleItem().Message.ShouldContain("dependency has no reason");

        var madeUp = Reply();
        madeUp["summary"]![0]!["source"] = "https://www.example.org/elsewhere";
        SupplierProposal.Read(madeUp, sources).Problems().ShouldHaveSingleItem().Message.ShouldContain("https://www.example.org/elsewhere");

        var outOfRange = Reply();
        outOfRange["levels"]![2]!["value"] = 0;
        SupplierProposal.Read(outOfRange, sources).Problems().ShouldHaveSingleItem().Message.ShouldContain("maturity");

        var missing = Reply();
        ((JsonArray)missing["levels"]!).RemoveAt(4);
        SupplierProposal.Read(missing, sources).Problems().ShouldHaveSingleItem().Message.ShouldContain("criticality");
    }

    [Fact]
    public void Drafted_supplier_file_is_a_draft_contract_with_each_reason()
    {
        var proposal = SupplierProposal.Read(Reply(), ["answers", "https://www.example.com/trust"]).Value();

        var yaml = SupplierFile.Write(Choice, proposal.Levels, "EXAMPLE-ASSETS", new DateOnly(2026, 11, 22));

        yaml.ShouldContain("ref_id: \"SUP-EXAMPLE\"");
        yaml.ShouldContain("# dependency: The asset inventory depends on it. (answers)\ndependency: 3\n");
        yaml.ShouldContain("    criticality: 3\n");
        yaml.ShouldContain("    status: draft\n");
        yaml.ShouldContain("    solutions: [\"SOL-EXAMPLE-ASSETS\"]\n");
        var parsed = ScopeLoader.Parse(
            new ScopeSource("setup.yaml", "organisation:\n  root_folder: Global\n"), null, [], thirdParties: [new ScopeSource("example-assets.yaml", yaml)]);
        parsed.Problems.ShouldBeEmpty();
        var entity = parsed.Scope.ThirdParties.ShouldHaveSingleItem();
        entity.Dependency.ShouldBe(3);
        entity.Contracts.ShouldHaveSingleItem().Status.ShouldBe("draft");
        SupplierGate.Check(parsed.Scope.ThirdParties, Choice).ShouldBe(SupplierStatus.NotApproved);

        var undecided = SupplierFile.Write(Choice, [], "EXAMPLE-ASSETS", new DateOnly(2026, 11, 22));
        undecided.ShouldContain("# dependency: to decide (0 to 4)");
        undecided.ShouldNotContain("dependency: 3");
    }

    [Fact]
    public void Drafted_supplier_has_a_yearly_review_for_the_grc_team_to_conclude_in_ciso_assistant()
    {
        var yaml = SupplierFile.Write(Choice, [], "EXAMPLE-ASSETS", new DateOnly(2026, 11, 22), new AuditPlace("Example Pty Ltd", "Corporate IT"));

        yaml.ShouldContain("domain: \"Example Pty Ltd\"\n");
        yaml.ShouldContain("assessments:\n  - ref_id: \"EA-SUP-EXAMPLE\"\n");
        var parsed = ScopeLoader.Parse(
            new ScopeSource("setup.yaml", "organisation:\n  root_folder: Global\n"), null, [], thirdParties: [new ScopeSource("example-assets.yaml", yaml)]);
        parsed.Problems.ShouldBeEmpty();
        var review = parsed.Scope.ThirdParties.ShouldHaveSingleItem().Assessments.ShouldHaveSingleItem();
        (review.Name, review.Perimeter.Value, review.DueDate, review.ReviewMonths).ShouldBe(("Example Assets Ltd review", "Corporate IT", "2026-12-22", 12));
        review.Solutions.Select(s => s.Value).ShouldBe(["SOL-EXAMPLE-ASSETS"]);

        // Without a domain and perimeter to put it in, the review is left for the person to add.
        SupplierFile.Write(Choice, [], "EXAMPLE-ASSETS", new DateOnly(2026, 11, 22)).ShouldContain("# assessments: add a review");
    }

    [Fact]
    public void Summary_is_not_evidence_and_kept_documents_are_attached_with_hash()
    {
        var proposal = SupplierProposal.Read(Reply(), ["answers", "https://www.example.com/trust"]).Value();
        var kept = KeptDocument.Of(new Uri("https://www.example.com/trust"), "documents/suppliers/SUP-EXAMPLE/trust.html", "trust page"u8.ToArray(), new DateTimeOffset(2026, 11, 22, 1, 2, 3, TimeSpan.Zero));

        kept.Sha256.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("trust page"u8)));
        kept.Path.ShouldBe("documents/suppliers/SUP-EXAMPLE/trust.html");
        kept.RetrievedAt.ShouldBe(new DateTimeOffset(2026, 11, 22, 1, 2, 3, TimeSpan.Zero));

        // The summary stays in the onboarding session as a draft; the supplier file holds only the levels.
        var session = new OnboardSession("EXAMPLE-ASSETS", OnboardStage.Supplier) { Supplier = Choice, SupplierSummary = proposal.Summary, Documents = [kept] };
        var yaml = SupplierFile.Write(Choice, proposal.Levels, "EXAMPLE-ASSETS", new DateOnly(2026, 11, 22));
        yaml.ShouldNotContain("Data is held");
        var read = OnboardSessionJson.Read(OnboardSessionJson.Write(session)).Value();
        read.SupplierSummary.ShouldHaveSingleItem().Evidence.ShouldBeFalse();
        read.Documents.ShouldHaveSingleItem().Sha256.ShouldBe(kept.Sha256);
        read.Documents[0].Source.ShouldBe(new Uri("https://www.example.com/trust"));
    }

    [Fact]
    public async Task Supplier_step_sends_the_answers_and_documents_with_the_supplier_skills()
    {
        var model = new ApplicabilityTests.ScriptedModel(_ => Reply());
        var skills = SkillSelection.Select(SkillCatalogue.Build(SkillCatalogue.Defaults().Value(), []).Value(), SkillStep.Supplier, null, SkillSelection.DefaultBudgetCharacters).Value();

        var proposal = await SupplierProposal.DraftAsync(
            model, skills, Choice, "Hosts the asset inventory.", [new Models.DraftInput("https://www.example.com/trust", "Certified to ISO/IEC 27001.")], TestContext.Current.CancellationToken);

        proposal.Value().Levels.Count.ShouldBe(5);
        var sent = model.Sent.ShouldHaveSingleItem();
        sent.Step.ShouldBe("supplier");
        sent.Instructions.ShouldContain("<skill name=\"supplier-assessment\"");
        sent.Inputs.Select(i => i.Name).ShouldBe(["answers", "https://www.example.com/trust"]);
        sent.Inputs[0].Content.ShouldContain("Hosts the asset inventory.");
    }

    internal static JsonObject Reply() => (JsonObject)JsonNode.Parse("""
        {
          "levels": [
            {"name":"dependency","value":3,"reason":"The asset inventory depends on it.","sources":["answers"]},
            {"name":"penetration","value":2,"reason":"Read access to device data.","sources":["answers"]},
            {"name":"maturity","value":3,"reason":"Certified to ISO/IEC 27001.","sources":["https://www.example.com/trust"]},
            {"name":"trust","value":3,"reason":"Long-standing supplier.","sources":["answers"]},
            {"name":"criticality","value":3,"reason":"Holds the asset register.","sources":["answers"]}
          ],
          "summary": [
            {"topic":"data location","text":"Data is held in Australia.","source":"https://www.example.com/trust"}
          ]
        }
        """)!;

    private static ScopeEntity Entity(string status) =>
        new("SUP-EXAMPLE", "Example Assets Ltd", Here)
        {
            Solutions = [new ScopeSolution("SOL-EXAMPLE-ASSETS", "Asset inventory", null, 3, Here)],
            Contracts = [new ScopeContract("CTR-EXAMPLE-ASSETS", "Asset inventory subscription", status, Here) { Solutions = [new ScopeRef("SOL-EXAMPLE-ASSETS", Here)] }],
        };
}
