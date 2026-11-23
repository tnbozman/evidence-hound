using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Yaml;
using EvidenceHound.Onboarding.Decisions;
using EvidenceHound.Onboarding.Output;
using EvidenceHound.Onboarding.Sessions;
using EvidenceHound.Onboarding.Suppliers;

namespace EvidenceHound.Onboarding.Tests.Journey;

/// <summary>A decided session becomes files for a pull request: the system's audit, mapping additions and the onboarding record.</summary>
public sealed class OnboardOutputTests
{
    private static readonly DateOnly Today = new(2026, 11, 23);
    private static readonly AuditPlace Place = new("Example Pty Ltd", "Corporate IT");

    [Fact]
    public void Not_applicable_becomes_scope_assessment_with_justification()
    {
        var yaml = OnboardOutput.Audit(Decided(), "urn:example:framework", "example-framework", Place, Today);

        var audit = ScopeLoader.Parse(new ScopeSource("setup.yaml", "organisation:\n  root_folder: Global\n"), null, [new ScopeSource("audit.yaml", yaml)]);
        audit.Problems.ShouldBeEmpty();
        var parsed = audit.Scope.Audits.ShouldHaveSingleItem();
        parsed.RefId.ShouldBe("EXAMPLE-ASSETS-EXAMPLE-FRAMEWORK");
        parsed.Framework.Value.ShouldBe("urn:example:framework");
        parsed.Groups.Select(g => g.Value).ShouldBe(["2"]);
        parsed.Domain.Value.ShouldBe("Example Pty Ltd");
        parsed.Perimeter.Value.ShouldBe("Corporate IT");
        parsed.Systems.Select(x => x.Value).ShouldBe(["EXAMPLE-ASSETS"]);

        // Requirements checked through the API are not listed: they start not assessed until the collector's results are published.
        parsed.Unlisted.ShouldNotBeNull().Result.ShouldBe(ComplianceResult.NotAssessed);

        var notApplicable = parsed.Assessments.Single(a => a.Requirement.Key.Value == "EX-2");
        notApplicable.Result.ShouldBe(ComplianceResult.NotApplicable);
        notApplicable.Status.ShouldBe("done");
        notApplicable.Observation.ShouldBe("Not applicable: Only the supplier administers it. Approved by Sam Example; accepted by Alex Example when onboarding EXAMPLE-ASSETS on 2026-11-23 (onboarding/example-assets-example-framework-2026-11-23.md).");

        // Met by another system: open until that system's audit shows it met, never not applicable.
        var elsewhere = parsed.Assessments.Single(a => a.Requirement.Key.Value == "EX-3");
        elsewhere.Result.ShouldBe(ComplianceResult.NotAssessed);
        elsewhere.Status.ShouldBe("to_do");
        elsewhere.ProvidedBy.ShouldBe(["Example backup service"]);
        elsewhere.Observation.ShouldStartWith("Met by Example backup service; open until the audit of Example backup service shows it met: Backups are taken by the backup service.");

        var manual = parsed.Assessments.Single(a => a.Requirement.Key.Value == "EX-1");
        manual.Status.ShouldBe("to_do");
        manual.Result.ShouldBe(ComplianceResult.NotAssessed);
        manual.Observation.ShouldStartWith("Manual evidence: Ask the owner for the patch report.");

        // A requirement checked through the API is answered by the collector, not by hand.
        parsed.Assessments.ShouldNotContain(a => a.Requirement.Key.Value == "EX-4");
    }

    [Fact]
    public void Mapping_additions_do_not_change_existing_rows()
    {
        const string mapping = """
            framework_urn: urn:example:framework
            requirements:
              # Patching.
              - ref: EX-4
                rule: all_pass
                checks: [devices.supported_os]
            manual_only:
              - ref: EX-9
                source: An existing manual source.
            description: Kept after the list.
            """;

        var (text, added) = OnboardOutput.Mapping(mapping, Decided());

        added.ShouldBe(["EX-1"]);
        text.ShouldContain("  - ref: EX-9\n    source: An existing manual source.\n  # EXAMPLE-ASSETS, added by hound onboard: manual evidence.\n  - ref: EX-1\n    source: \"Ask the owner for the patch report.\"\ndescription: Kept after the list.");
        Removed(mapping, text).ShouldBeEmpty();

        // A requirement keyed by URN is already covered when the mapping names its ref_id (for example with rows: all).
        var urnKeyed = Decided() with { Decisions = [Decided().Decisions[0] with { Key = "urn:example:req:x", RefId = "EX-9" }] };
        OnboardOutput.Mapping(mapping, urnKeyed).Added.ShouldBeEmpty();

        // Run again: nothing more to add.
        OnboardOutput.Mapping(text, Decided()).Added.ShouldBeEmpty();
        OnboardOutput.Mapping(text, Decided()).Text.ShouldBe(text);

        // A mapping without manual_only gets one at the end.
        var (fresh, _) = OnboardOutput.Mapping("framework_urn: urn:example:framework\nrequirements: []\n", Decided());
        fresh.ShouldEndWith("manual_only:\n  # EXAMPLE-ASSETS, added by hound onboard: manual evidence.\n  - ref: EX-1\n    source: \"Ask the owner for the patch report.\"\n");
    }

    [Fact]
    public void Record_lists_every_decision_and_model()
    {
        var session = Decided() with { Model = new ModelUsed("anthropic", "claude-opus-5-5") };
        SkillUse[] skills = [new("applicability", "default", "1.0.0", "abc123")];

        var record = OnboardOutput.Record(session, skills, "example-framework", Today);

        record.ShouldContain("# Onboarding EXAMPLE-ASSETS: example-framework, 2026-11-23");
        record.ShouldContain("Model: anthropic claude-opus-5-5. Every request and reply is in [requests/](EXAMPLE-ASSETS/requests/).");
        record.ShouldContain("| applicability | default | 1.0.0 | `abc123` |");
        foreach (var decision in session.Decisions)
        {
            record.ShouldContain($"| {decision.Key} |");
        }

        record.ShouldContain("| EX-2 | (none) | not_applicable | No privileged users in the product. | Alex Example | Only the supplier administers it. Approved by Sam Example. |");
        record.ShouldContain("| EX-4 | api_check | api_check | GET /v1/devices shows the OS version. | Alex Example |  |");
        record.ShouldContain("Supplier: Example Assets Ltd (SUP-EXAMPLE), solution Asset inventory, contract CTR-EXAMPLE-ASSETS.");
        record.ShouldContain("This summary is a draft from the documents below; it is not evidence.");
        record.ShouldContain("| https://www.example.com/trust | documents/suppliers/sup-example/trust.html | `");

        OnboardOutput.Record(Decided(), [], "example-framework", Today).ShouldContain("Model: none (--no-model); every proposal was left to the reviewer.");
    }

    [Fact]
    public void Second_framework_writes_its_own_audit_and_record()
    {
        var first = OnboardOutput.AuditFileName("EXAMPLE-ASSETS", "example-framework");
        var second = OnboardOutput.AuditFileName("EXAMPLE-ASSETS", "other-framework");

        first.ShouldBe("example-assets-example-framework.yaml");
        second.ShouldNotBe(first);
        OnboardOutput.RecordFileName("EXAMPLE-ASSETS", "other-framework", Today).ShouldNotBe(OnboardOutput.RecordFileName("EXAMPLE-ASSETS", "example-framework", Today));
    }

    /// <summary>The lines of <paramref name="before"/> missing from <paramref name="after"/>, in order.</summary>
    private static List<string> Removed(string before, string after)
    {
        var remaining = after.Split('\n').ToList();
        var removed = new List<string>();
        foreach (var line in before.Split('\n'))
        {
            var at = remaining.IndexOf(line);
            if (at < 0)
            {
                removed.Add(line);
            }
            else
            {
                remaining.RemoveRange(0, at + 1);
            }
        }

        return removed;
    }

    internal static OnboardSession Decided()
    {
        var framework = new Core.Frameworks.FrameworkSnapshot(
            "urn:example:library",
            "urn:example:framework",
            1,
            [new Core.Frameworks.ImplementationGroup("2", "Level two")],
            [
                new Core.Frameworks.SnapshotNode("urn:example:req:a", "EX-1", null, ["2"], "Patch internet-facing services."),
                new Core.Frameworks.SnapshotNode("urn:example:req:b", "EX-2", null, ["2"], "MFA for privileged users."),
                new Core.Frameworks.SnapshotNode("urn:example:req:c", "EX-3", null, ["2"], "Back up important data daily."),
                new Core.Frameworks.SnapshotNode("urn:example:req:d", "EX-4", null, ["2"], "Supported operating systems."),
            ]);
        var proposals = Applicability.Empty(Applicability.InScope(framework, ["2"])).ToArray();
        proposals[3] = proposals[3] with { Kind = DecisionKind.ApiCheck, Reasoning = "GET /v1/devices shows the OS version." };
        var session = new OnboardSession("EXAMPLE-ASSETS", OnboardStage.Applicability)
        {
            Reviewer = "Alex Example",
            Owner = "Alex Example",
            Product = "Example Asset API",
            Groups = ["2"],
            Supplier = OnboardSupplierTests.Choice,
            SupplierLevels = [new LevelProposal("dependency", 3, "The asset inventory depends on it.", ["answers"])],
            SupplierSummary = [new SummaryPoint("data location", "Data is held in Australia.", "https://www.example.com/trust")],
            Documents = [KeptDocument.Of(new Uri("https://www.example.com/trust"), "documents/suppliers/sup-example/trust.html", "trust page"u8, new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero))],
            Proposals = proposals,
        };
        return session
            .Decide(Applicability.Decide(proposals[0], DecisionKind.Manual, "Ask the owner for the patch report.", "Alex Example", null, null, null).Value())
            .Decide(Applicability.Decide(proposals[1], DecisionKind.NotApplicable, "No privileged users in the product.", "Alex Example", "Only the supplier administers it.", "Sam Example", null).Value())
            .Decide(Applicability.Decide(proposals[2], DecisionKind.OtherSystem, "Backups are taken by the backup service.", "Alex Example", null, null, "Example backup service").Value())
            .Decide(Applicability.Decide(proposals[3], DecisionKind.ApiCheck, proposals[3].Reasoning, "Alex Example", null, null, null).Value());
    }
}
