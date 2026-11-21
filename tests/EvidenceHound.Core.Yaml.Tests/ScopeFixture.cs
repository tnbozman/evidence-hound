using EvidenceHound.Core.Frameworks;
using EvidenceHound.Core.Mapping;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>A small fictional scope for Example Pty Ltd and the example baseline framework, with ways to break it.</summary>
internal static class ScopeFixture
{
    public const string FrameworkUrn = "urn:example:risk:framework:example-baseline";

    public const string Setup = """
        organisation:
          root_folder: Global
        frameworks:
          - urn: urn:example:risk:framework:example-baseline
            library: urn:example:risk:library:example-baseline
            snapshot: ../frameworks/example-baseline.json
        domains:
          - name: Example Pty Ltd
            description: The whole company.
            perimeters:
              - name: Example baseline
                ref_id: PER-BASE
            systems:
              - name: Example 365
                type: primary
                description: Mail, files and identity.
                owner: it@example.com
                tags:
                  essentials_domain: identity
              - name: Example laptops
                type: supporting
                parents: [Example 365]
                tags:
                  cui: "true"
        """;

    public const string Controls = """
        controls:
          - ref_id: AC-MFA
            name: Multi-factor authentication
            domain: Example Pty Ltd
            owner: it@example.com
            status: active
            priority: "1"
            category: technical
            eta: "2026-12-31"
            systems: [Example 365]
            documents: [mfa-policy]
          - ref_id: AC-BACKUP
            name: Daily backups
            domain: Example Pty Ltd
            status: active
            systems: [Example laptops]
        """;

    public const string Audit = """
        ref_id: EXAMPLE-BASELINE-2026
        name: Example baseline 2026
        framework: urn:example:risk:framework:example-baseline
        groups: ["1"]
        domain: Example Pty Ltd
        perimeter: Example baseline
        eta: "2026-06-30"
        due_date: "2026-07-31"
        authors: [it@example.com]
        reviewers: [ceo@example.com]
        field_visibility:
          observation: {auditor: edit, respondent: read}
        systems: [Example 365, Example laptops]
        documents:
          - id: mfa-policy
            name: MFA policy
            files: [policies/mfa.pdf]
            systems: [Example 365]
            requirements: [EX-1.1]
        controls:
          - ref_id: AC-MFA
            requirements: [EX-1.1, EX-1.2]
        assessments:
          - ref: EX-1.2
            status: done
            result: compliant
            observation: Reviewed by hand.
        unlisted:
          result: not_assessed
        """;

    public const string AuditPath = "scope/audits/example-baseline-2026.yaml";

    /// <summary>Groups 1 and 2; EX-1.1 and EX-1.2 are in group 1, EX-2.1 only in group 2.</summary>
    public static readonly FrameworkSnapshot Snapshot = new(
        "urn:example:risk:library:example-baseline",
        FrameworkUrn,
        1,
        [new ImplementationGroup("1", "Level one"), new ImplementationGroup("2", "Level two")],
        [
            new SnapshotNode("urn:example:risk:req_node:example-baseline:ex-1.1", "EX-1.1", null, ["1", "2"], "Every account uses multi-factor authentication"),
            new SnapshotNode("urn:example:risk:req_node:example-baseline:ex-1.2", "EX-1.2", null, ["1", "2"], "Phishing-resistant MFA for admins"),
            new SnapshotNode("urn:example:risk:req_node:example-baseline:ex-2.1", "EX-2.1", null, ["2"], "Patches applied within 48 hours"),
        ]);

    public static readonly IReadOnlyDictionary<string, FrameworkSnapshot> Snapshots = new Dictionary<string, FrameworkSnapshot> { [FrameworkUrn] = Snapshot };

    public static ScopeParse Parse(string setup = Setup, string controls = Controls, string audit = Audit) =>
        ScopeLoader.Parse(new ScopeSource("scope/setup.yaml", setup), new ScopeSource("scope/controls.yaml", controls), [new ScopeSource(AuditPath, audit)]);

    /// <summary>Parses and validates, with an optional target, and returns every problem.</summary>
    public static IReadOnlyList<ScopeProblem> Problems(string setup = Setup, string controls = Controls, string audit = Audit, params ScopeTarget[] targets)
    {
        var parsed = Parse(setup, controls, audit);
        return [.. parsed.Problems, .. ScopeValidator.Validate(parsed.Scope, Snapshots, targets)];
    }

    public static ScopeTarget Target(string audit = "EXAMPLE-BASELINE-2026", params string[] computedRefs) =>
        new(audit, [.. computedRefs.Select(r => RequirementKey.Ref(r))], ["AC-MFA"], new SourceLine("hound.yaml", 30));
}
