using EvidenceHound.Core.Publishing;

namespace EvidenceHound.Core.Tests.Publishing;

public class SelectorTests
{
    [Fact]
    public void Ref_id_selector_matches_the_dispatcher_field()
    {
        Selector.ByRefId("AC-MFA").ToQueryString().ShouldBe("ref_id=AC-MFA");
    }

    [Fact]
    public void Requirement_selector_names_the_audit_and_the_requirement_in_order()
    {
        Selector.RequirementInAudit("EXAMPLE-BASELINE-2026", "EX-1.1").ToQueryString()
            .ShouldBe("compliance_assessment__ref_id=EXAMPLE-BASELINE-2026&requirement__ref_id=EX-1.1");
    }

    [Fact]
    public void Values_are_url_encoded()
    {
        Selector.ByName("Example & Co/backups 1").ToQueryString().ShouldBe("name=Example%20%26%20Co%2Fbackups%201");
    }

    [Fact]
    public void Describes_itself_for_messages()
    {
        Selector.RequirementInAudit("A-1", "R 2").ToString().ShouldBe("compliance_assessment__ref_id=A-1, requirement__ref_id=R 2");
    }
}
