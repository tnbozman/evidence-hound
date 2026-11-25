using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Planning;
using EvidenceHound.Core.Tests.Checks;

namespace EvidenceHound.Core.Tests.Publishing;

/// <summary>s14-01: rows a derived mapping marks <c>draft: true</c> are not published until a person removes the mark.</summary>
public class PublishTests
{
    private static readonly AuditTarget Target = new("EX-ESSENTIALS-2026", new Dictionary<string, string>());

    [Fact]
    public void Draft_rows_are_not_published()
    {
        var mapping = FrameworkMapping.Create("urn:example:risk:framework:example-domains",
        [
            RequirementMapping.Create(RequirementKey.Urn("urn:example:node:reviewed"), [CheckTestData.Id("identity.mfa.enforced")], MappingRule.AllPass).Value(),
            RequirementMapping.Create(RequirementKey.Urn("urn:example:node:draft"), [CheckTestData.Id("identity.mfa.enforced")], MappingRule.AllPass).Value() with { Draft = true },
        ]).Value();
        var check = CheckResult.Create(CheckTestData.Id("identity.mfa.enforced"), CheckOutcome.Pass, "ok", [], CheckTestData.CollectedAt, "m365").Value();
        var results = MappingEngine.ApplyMapping(mapping, [check]);

        var changes = AuditPlanner.PlanChanges([new TargetResults(Target, results)], [], [], new CurrentState([], [], []), "20261125T000000Z").Value();
        var updates = AuditPlanner.PlanUpdates(Target, results, [], []).Value();

        results.Select(r => r.Draft).ShouldBe([false, true]);
        changes.Updates.OfType<AuditUpdate.UpdateRequirementAssessment>().Select(u => u.RequirementRefId).ShouldBe(["urn:example:node:reviewed"]);
        updates.OfType<AuditUpdate.UpdateRequirementAssessment>().Select(u => u.RequirementRefId).ShouldBe(["urn:example:node:reviewed"]);
        var notice = changes.Drift.ShouldHaveSingleItem();
        notice.Code.ShouldBe("publish.draft_row");
        notice.Message.ShouldContain("urn:example:node:draft");
        notice.Message.ShouldContain("draft: true");
        changes.Audits.ShouldHaveSingleItem().Drift.ShouldBe(0);
    }
}
