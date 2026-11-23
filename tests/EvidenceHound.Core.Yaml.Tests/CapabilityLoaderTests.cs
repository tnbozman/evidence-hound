using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Yaml.Tests;

/// <summary>s09-28: <c>scope/capabilities.yaml</c> is read with each decision's provider, owner, evidence route, approver and signals.</summary>
public class CapabilityLoaderTests
{
    internal const string Capabilities = """
        framework: urn:intuitem:risk:framework:essential-eight-2023
        mitigations:
          - mitigation: ac
            name: Application control
            provided_by: system
            systems: [Example app control]
            supplier: SUP-EXAPPCTL
            owner: it@example.com
            evidence: profile
            route: example-app-control
            approved_by: GRC lead
            approved_on: "2026-11-23"
            reason: Application control on every laptop is the third-party product, not Intune.
            signals:
              - "Microsoft Graph: no Intune application control policy assigned"
          - mitigation: pa
            provided_by: gap
            approved_by: GRC lead
        """;

    [Fact]
    public void Reads_each_decision_with_its_provider_route_and_approver()
    {
        var parsed = Parse(Capabilities);

        parsed.Problems.ShouldBeEmpty();
        var map = parsed.Scope.Capabilities.ShouldNotBeNull();
        map.Framework.Value.ShouldBe("urn:intuitem:risk:framework:essential-eight-2023");
        map.At.File.ShouldBe("capabilities.yaml");
        var ac = map.Mitigations[0];
        (ac.Mitigation, ac.Name, ac.ProvidedBy, ac.Supplier?.Value, ac.Owner, ac.Evidence, ac.Route, ac.ApprovedBy, ac.ApprovedOn)
            .ShouldBe(("ac", "Application control", CapabilityProvision.System, "SUP-EXAPPCTL", "it@example.com", "profile", "example-app-control", "GRC lead", "2026-11-23"));
        ac.Systems.Select(s => s.Value).ShouldBe(["Example app control"]);
        ac.Signals.ShouldBe(["Microsoft Graph: no Intune application control policy assigned"]);
        map.Mitigations[1].ProvidedBy.ShouldBe(CapabilityProvision.Gap);
        Parse(null).Scope.Capabilities.ShouldBeNull();
    }

    [Fact]
    public void Unknown_keys_and_values_are_reported()
    {
        var parsed = Parse(Capabilities.Replace("provided_by: gap", "provided_by: hope", StringComparison.Ordinal).Replace("evidence: profile", "evidence: vibes\n    colour: red", StringComparison.Ordinal));

        parsed.Problems.Select(p => p.Code).ShouldBe(["scope.key_unknown", "scope.value_invalid", "scope.value_invalid"], ignoreOrder: true);
    }

    private static ScopeParse Parse(string? capabilities) =>
        ScopeLoader.Parse(new ScopeSource("setup.yaml", "organisation:\n  root_folder: Global\n"), null, [], capabilities: capabilities is null ? null : new ScopeSource("capabilities.yaml", capabilities));
}
