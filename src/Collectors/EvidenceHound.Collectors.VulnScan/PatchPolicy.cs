namespace EvidenceHound.Collectors.VulnScan;

/// <summary>
/// How often a scope must be scanned and how soon its findings must be patched: within <paramref name="PatchWindowDays"/>, or
/// <paramref name="ExploitedWindowHours"/> when the finding is rated critical or an exploit exists.
/// </summary>
public sealed record PatchPolicy(int ScanIntervalDays, int PatchWindowDays, int ExploitedWindowHours)
{
    /// <summary>
    /// Essential Eight maturity level 2 for the scope: internet-facing services daily, two weeks, 48 hours when critical or with an exploit; common
    /// applications weekly and two weeks; other applications and operating systems fortnightly and one month.
    /// </summary>
    public static PatchPolicy For(string scope) => scope switch
    {
        "internet_facing" => new(1, 14, 48),
        "common_apps" => new(7, 14, 14 * 24),
        "other_apps" or "operating_systems" => new(14, 30, 30 * 24),
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Not a scan scope."),
    };
}
