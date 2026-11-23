using EvidenceHound.Core.Results;
using EvidenceHound.Core.Scope;

namespace EvidenceHound.Onboarding.Capabilities;

/// <summary>What read-only lookups found in the environment: subscribed licences and the kinds of Intune configuration present, and where from.</summary>
public sealed record EnvironmentFacts(string Source, IReadOnlyList<string> Licences, IReadOnlyList<string> ConfigurationTypes)
{
    public static EnvironmentFacts None { get; } = new("", [], []);
}

/// <summary>A pre-filled answer for one mitigation: what the signals suggest, if anything, and each signal with its source. Never a decision.</summary>
public sealed record CapabilityProposal(string Mitigation, CapabilityProvision? Suggested, IReadOnlyList<string> Systems, IReadOnlyList<string> Signals)
{
    /// <summary>The model's reasoning when the suggestion is the model's; null when it comes from the signals.</summary>
    public string? Reasoning { get; init; }
}

/// <summary>The person's answer to a proposal.</summary>
public sealed record CapabilityAnswer(string Kind, IReadOnlyList<string> Systems)
{
    public static CapabilityAnswer Accept { get; } = new("accept", []);

    public static CapabilityAnswer Manual { get; } = new("manual", []);

    public static CapabilityAnswer Gap { get; } = new("gap", []);

    public static CapabilityAnswer System(IReadOnlyList<string> systems) => new("system", systems);
}

/// <summary>
/// Pre-fills each mitigation from read-only signals: Microsoft licences and Intune configuration kinds (suggesting the Microsoft system), and
/// systems in scope tagged <c>capabilities: ac, rb</c> (suggested first, as a named product is usually the authoritative one). Pure.
/// </summary>
public static class CapabilitySignals
{
    private static readonly (string Sku, string Name, string[] Mitigations)[] Licences =
    [
        ("AAD_PREMIUM", "Microsoft Entra ID P1", ["mfa"]),
        ("AAD_PREMIUM_P2", "Microsoft Entra ID P2", ["mfa", "rap"]),
        ("INTUNE_A", "Microsoft Intune", ["pos", "rmom", "uah"]),
        ("SPE_E3", "Microsoft 365 E3", ["mfa", "pos", "rmom", "uah"]),
        ("SPE_E5", "Microsoft 365 E5", ["mfa", "rap", "pos", "rmom", "uah", "pa"]),
        ("MDATP_XPLAT", "Microsoft Defender for Endpoint", ["pa"]),
        ("DEFENDER_ENDPOINT_P1", "Microsoft Defender for Endpoint P1", ["pa"]),
        ("WIN_DEF_ATP", "Microsoft Defender for Endpoint P2", ["pa"]),
    ];

    private static readonly (string Type, string Text, string[] Mitigations)[] Configurations =
    [
        ("#microsoft.graph.windows10EndpointProtectionConfiguration", "Intune holds an endpoint protection configuration, which can carry application control", ["ac"]),
        ("#microsoft.graph.windowsDefenderAdvancedThreatProtectionConfiguration", "Intune holds a Defender for Endpoint onboarding configuration", ["pa"]),
        ("#microsoft.graph.windowsUpdateForBusinessConfiguration", "Intune holds a Windows Update for Business ring", ["pos"]),
    ];

    public static IReadOnlyList<CapabilityProposal> Prefill(IReadOnlyList<Mitigation> mitigations, IReadOnlyList<ScopeSystem> systems, EnvironmentFacts facts, string? microsoftSystem)
    {
        ArgumentNullException.ThrowIfNull(mitigations);
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(facts);
        return [.. mitigations.Select(m =>
        {
            var tagged = systems
                .Where(s => s.Tags.GetValueOrDefault("capabilities") is { } tags && tags.Split(',', StringSplitOptions.TrimEntries).Contains(m.Code, StringComparer.Ordinal))
                .ToArray();
            var microsoft = Licences.Where(l => l.Mitigations.Contains(m.Code) && facts.Licences.Contains(l.Sku, StringComparer.Ordinal))
                .Select(l => $"{facts.Source}: licence {l.Sku} ({l.Name}) is subscribed")
                .Concat(Configurations.Where(c => c.Mitigations.Contains(m.Code) && facts.ConfigurationTypes.Contains(c.Type, StringComparer.Ordinal)).Select(c => $"{facts.Source}: {c.Text}"))
                .ToArray();
            string[] signals = [.. tagged.Select(s => $"scope/setup.yaml: {s.Name} is tagged capabilities: {s.Tags["capabilities"]}"), .. microsoft];
            string[] suggested = tagged.Length > 0 ? [.. tagged.Select(s => s.Name)] : microsoft.Length > 0 && microsoftSystem is not null ? [microsoftSystem] : [];
            return new CapabilityProposal(m.Code, suggested.Length > 0 ? CapabilityProvision.System : null, suggested, signals);
        })];
    }
}

/// <summary>Turns a proposal and the person's answer into a decision for <c>scope/capabilities.yaml</c>. Pure.</summary>
public static class CapabilityAnswers
{
    public static Result<ScopeCapability> Decide(CapabilityProposal proposal, CapabilityAnswer answer, string acceptedBy, string approvedBy, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(answer);
        if (string.IsNullOrWhiteSpace(acceptedBy) || string.IsNullOrWhiteSpace(approvedBy))
        {
            return Result.Fail<ScopeCapability>(new Problem("capability.approver_missing", $"mitigation '{proposal.Mitigation}' needs who decided it and the role that approved it."));
        }

        (CapabilityProvision Provision, IReadOnlyList<string> Systems, string Reason)? decided = answer.Kind switch
        {
            "accept" when proposal.Suggested is { } suggested => (
                suggested,
                suggested == CapabilityProvision.System ? proposal.Systems : [],
                proposal.Reasoning is { } why ? $"Accepted by {acceptedBy} from the model's proposal: {why}" : $"Accepted by {acceptedBy} from the signals."),
            "system" when answer.Systems.Count > 0 => (CapabilityProvision.System, answer.Systems, $"Decided by {acceptedBy}: provided by {string.Join(" and ", answer.Systems)}."),
            "manual" => (CapabilityProvision.Manual, [], $"Decided by {acceptedBy}: manual evidence."),
            "gap" => (CapabilityProvision.Gap, [], $"Decided by {acceptedBy}: nothing provides it yet."),
            _ => null,
        };
        if (decided is not { } d)
        {
            return Result.Fail<ScopeCapability>(new Problem(
                "capability.nothing_to_accept", $"mitigation '{proposal.Mitigation}' has no suggestion to accept; name the systems that provide it, or choose manual or gap."));
        }

        var at = new SourceLine(CapabilityMap.FileName, 0);
        return Result.Ok(new ScopeCapability(proposal.Mitigation, d.Provision, at)
        {
            Systems = [.. d.Systems.Select(s => new ScopeRef(s, at))],
            ApprovedBy = approvedBy,
            ApprovedOn = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            Reason = d.Reason,
            Signals = proposal.Signals,
        });
    }
}
