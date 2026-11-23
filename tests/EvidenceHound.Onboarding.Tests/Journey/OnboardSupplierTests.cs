using EvidenceHound.Core.Scope;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Onboarding.Output;
using EvidenceHound.Onboarding.Sessions;
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
    public void Maturity_and_trust_without_evidence_are_not_yet_assessed_and_left_out_of_the_file()
    {
        LevelProposal[] levels =
        [
            .. Levels.Where(l => l.Name is not ("maturity" or "trust")),
            new("maturity", SupplierProposal.NotYetAssessed, "No assurance documents were given.", ["https://www.example.com/trust"]),
            new("trust", SupplierProposal.NotYetAssessed, "Long-standing supplier.", ["answers"]),
        ];

        var yaml = SupplierFile.Write(Choice, levels, "EXAMPLE-ASSETS", new DateOnly(2026, 11, 23));
        yaml.ShouldContain("# maturity: not yet assessed (1 to 4): No assurance documents were given. (https://www.example.com/trust)\n");
        yaml.ShouldNotContain("maturity: 0");
        yaml.ShouldNotContain("trust: 0");
        var parsed = ScopeLoader.Parse(
            new ScopeSource("setup.yaml", "organisation:\n  root_folder: Global\n"), null, [], thirdParties: [new ScopeSource("example-assets.yaml", yaml)]);
        parsed.Problems.ShouldBeEmpty();
        var entity = parsed.Scope.ThirdParties.ShouldHaveSingleItem();
        (entity.Maturity, entity.Trust).ShouldBe((null, null));
    }

    [Fact]
    public void Drafted_supplier_file_is_a_draft_contract_with_each_reason()
    {
        var yaml = SupplierFile.Write(Choice, Levels, "EXAMPLE-ASSETS", new DateOnly(2026, 11, 22));

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
        var kept = KeptDocument.Of(new Uri("https://www.example.com/trust"), "documents/suppliers/SUP-EXAMPLE/trust.html", "trust page"u8.ToArray(), new DateTimeOffset(2026, 11, 22, 1, 2, 3, TimeSpan.Zero));

        kept.Sha256.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("trust page"u8)));
        kept.Path.ShouldBe("documents/suppliers/SUP-EXAMPLE/trust.html");
        kept.RetrievedAt.ShouldBe(new DateTimeOffset(2026, 11, 22, 1, 2, 3, TimeSpan.Zero));

        // The summary stays in the onboarding session as a draft; the supplier file holds only the levels.
        var session = new OnboardSession("EXAMPLE-ASSETS", OnboardStage.Supplier) { Supplier = Choice, SupplierSummary = [new SummaryPoint("data location", "Data is held in Australia.", "https://www.example.com/trust")], Documents = [kept] };
        var yaml = SupplierFile.Write(Choice, Levels, "EXAMPLE-ASSETS", new DateOnly(2026, 11, 22));
        yaml.ShouldNotContain("Data is held");
        var read = OnboardSessionJson.Read(OnboardSessionJson.Write(session)).Value();
        read.SupplierSummary.ShouldHaveSingleItem().Evidence.ShouldBeFalse();
        read.Documents.ShouldHaveSingleItem().Sha256.ShouldBe(kept.Sha256);
        read.Documents[0].Source.ShouldBe(new Uri("https://www.example.com/trust"));
    }

    internal static readonly LevelProposal[] Levels =
    [
        new("dependency", 3, "The asset inventory depends on it.", ["answers"]),
        new("penetration", 2, "Read access to device data.", ["answers"]),
        new("maturity", 3, "Certified to ISO/IEC 27001.", ["https://www.example.com/trust"]),
        new("trust", 3, "Long-standing supplier.", ["answers"]),
        new("criticality", 3, "Holds the asset register.", ["answers"]),
    ];

    private static ScopeEntity Entity(string status) =>
        new("SUP-EXAMPLE", "Example Assets Ltd", Here)
        {
            Solutions = [new ScopeSolution("SOL-EXAMPLE-ASSETS", "Asset inventory", null, 3, Here)],
            Contracts = [new ScopeContract("CTR-EXAMPLE-ASSETS", "Asset inventory subscription", status, Here) { Solutions = [new ScopeRef("SOL-EXAMPLE-ASSETS", Here)] }],
        };
}
