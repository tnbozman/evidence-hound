using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Runtime.Pipeline;

namespace EvidenceHound.Runtime.Tests;

public class OwnershipTests
{
    private static readonly DateTimeOffset At = new(2026, 6, 30, 2, 0, 0, TimeSpan.Zero);

    private static CheckId Id(string id) => CheckId.Create(id).Value();

    private static readonly FrameworkMapping Mapping = FrameworkMapping.Create("urn:example:x", [
        RequirementMapping.Create("R-1", [Id("identity.mfa.admins"), Id("identity.mfa.users")], MappingRule.AllPass).Value(),
        RequirementMapping.Create("R-2", [Id("identity.mfa.admins"), Id("backup.daily")], MappingRule.AllPass).Value(),
        RequirementMapping.Create("R-3", [Id("patching.os.supported")], MappingRule.AllPass).Value(),
    ]).Value();

    private static CheckResult Check(string id) => CheckResult.Create(Id(id), CheckOutcome.Pass, "ok", [], At, "idp").Value();

    [Fact]
    public void Keeps_only_requirements_whose_every_check_was_reported()
    {
        var owned = Ownership.OwnedResults(Mapping, [Check("identity.mfa.admins"), Check("identity.mfa.users")]);

        owned.Select(r => (r.RequirementRef, r.Result)).ShouldBe([("R-1", ComplianceResult.Compliant)]);
    }

    [Fact]
    public void Owns_nothing_when_no_mapped_check_was_reported()
    {
        Ownership.OwnedResults(Mapping, [Check("network.firewall.enabled")]).ShouldBeEmpty();
    }
}
