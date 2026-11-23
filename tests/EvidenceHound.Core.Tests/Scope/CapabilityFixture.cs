using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Scope;

/// <summary>
/// s09-28: a fictional four-mitigation framework, three systems each with its own audit, and a capability map: application control from a
/// third-party product, MFA from Microsoft 365, backups from the backup product, and patching a gap.
/// </summary>
internal static class CapabilityFixture
{
    internal const string Framework = "urn:example:risk:framework:example-eight";
    internal static readonly SourceLine At = new("scope/capabilities.yaml", 1);

    internal static readonly FrameworkSnapshot Snapshot = new(
        "urn:example:risk:library:example-eight",
        Framework,
        1,
        [new ImplementationGroup("1", "Level one"), new ImplementationGroup("2", "Level two"), new ImplementationGroup("3", "Level three")],
        [
            Node("ac", "AC-1", "1", "2"),
            Node("ac", "AC-2", "2"),
            Node("mfa", "MFA-1", "1", "2"),
            Node("rb", "RB-1", "1", "2"),
            Node("pa", "PA-1", "1", "2", "3"),
        ]);

    internal static ScopeCapability Decision(string mitigation, CapabilityProvision provision, params string[] systems) =>
        new(mitigation, provision, At) { Systems = [.. systems.Select(s => new ScopeRef(s, At))], ApprovedBy = "GRC lead", ApprovedOn = "2026-11-23" };

    internal static ScopeCapabilityMap Map(params ScopeCapability[] decisions) => new(new ScopeRef(Framework, At), decisions, At);

    internal static ScopeCapabilityMap Standard() => Map(
        Decision("ac", CapabilityProvision.System, "Example app control"),
        Decision("mfa", CapabilityProvision.System, "Example 365"),
        Decision("rb", CapabilityProvision.System, "Example backup"),
        Decision("pa", CapabilityProvision.Gap));

    internal static ScopeDocument ScopeOf(ScopeCapabilityMap map, params ScopeAudit[] audits) =>
        new(
            new ScopeSetup(
                "Global",
                [new ScopeFramework(Framework, null, null, "../frameworks/example-eight.json", At)],
                [new ScopeDomain("Example Pty Ltd", null, [new ScopePerimeter("Corporate IT", null, null, At)], [System("Example 365"), System("Example app control"), System("Example backup")], At)]),
            [],
            audits.Length > 0 ? audits : [Audit("EX365-E8", "Example 365"), Audit("APPCONTROL-E8", "Example app control"), Audit("BACKUP-E8", "Example backup")])
        {
            Capabilities = map,
        };

    internal static ScopeAudit Audit(string refId, string system, params HandAssessment[] assessments) =>
        new(refId, refId, new ScopeRef(Framework, At), [new ScopeRef("1", At), new ScopeRef("2", At)], new ScopeRef("Example Pty Ltd", At), new ScopeRef("Corporate IT", At), new SourceLine($"scope/audits/{refId.ToLowerInvariant()}.yaml", 1))
        {
            Systems = [new ScopeRef(system, At)],
            Assessments = assessments,
        };

    internal static HandAssessment Written(string refId) =>
        new(new RequirementRef(RequirementKey.Ref(refId), At), "done", ComplianceResult.Compliant, "Checked by hand.");

    private static ScopeSystem System(string name) => new(name, null, SystemType.Primary, null, "it@example.com", [], new Dictionary<string, string>(), At);

    private static SnapshotNode Node(string mitigation, string refId, params string[] groups) =>
        new($"urn:example:risk:req_node:example-eight:{mitigation}:{refId.ToLowerInvariant()}", refId, $"urn:example:risk:req_node:example-eight:{mitigation}", groups, $"{refId} requirement");
}
