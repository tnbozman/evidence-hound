namespace EvidenceHound.Collectors;

/// <summary>
/// What a collector reports and needs: the check IDs it can emit, its plain settings and its secrets. A collector whose check IDs
/// come from config (such as the manual collector) names that setting in <paramref name="ChecksSetting"/>: the setting holds a
/// comma-separated list of the check IDs each instance may emit.
/// </summary>
public sealed record CollectorDescription(
    IReadOnlyList<string> Checks,
    IReadOnlyList<SettingDescription> Settings,
    IReadOnlyList<SettingDescription> Secrets,
    string? ChecksSetting = null)
{
    /// <summary>
    /// For a collector whose checks depend on more than one setting (such as the rest collector's profile): the check IDs an instance
    /// with these settings can emit.
    /// </summary>
    public Func<CollectorSettings, IReadOnlyList<string>>? ChecksFrom { get; init; }
}

/// <summary>
/// One setting or secret a collector reads, by its config key. Config without a required one is invalid. A setting with
/// <paramref name="IsPath"/> holds a file or folder path; the config loader resolves it against the config file's folder.
/// </summary>
public sealed record SettingDescription(string Name, string Description, bool Required, bool IsPath = false);
