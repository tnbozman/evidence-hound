namespace EvidenceHound.Collectors;

/// <summary>What a collector reports and needs: the check IDs it can emit, its plain settings and its secrets.</summary>
public sealed record CollectorDescription(
    IReadOnlyList<string> Checks,
    IReadOnlyList<SettingDescription> Settings,
    IReadOnlyList<SettingDescription> Secrets);

/// <summary>One setting or secret a collector reads, by its config key. Config without a required one is invalid.</summary>
public sealed record SettingDescription(string Name, string Description, bool Required);
