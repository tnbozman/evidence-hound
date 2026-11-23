using EvidenceHound.Collectors.VulnScan;
using EvidenceHound.Core.Checks;

namespace EvidenceHound.ScanDev;

/// <summary>One invented finding. Its dates are days before today: when the patch was published, and when it was first seen.</summary>
public sealed record DevFinding(
    string Host,
    string Id,
    string Name,
    Severity Severity,
    int? PatchDaysAgo = null,
    int? FirstSeenDaysAgo = null,
    bool Exploit = false,
    bool Unsupported = false);

/// <summary>One scope's scan: the folder its export goes in, its format, how many days ago it ran, the hosts and what it found.</summary>
public sealed record DevScan(string Scope, string Folder, ScanFormat Format, int ScannedDaysAgo, IReadOnlyList<string> Hosts, IReadOnlyList<DevFinding> Findings);

/// <summary>A change to the estate: its name, a sentence, the checks it fails with their outcome, and the change.</summary>
public sealed record Deviation(string Name, string Description, IReadOnlyDictionary<string, CheckOutcome> Fails, Func<ScanDevEstate, ScanDevEstate> Apply);

/// <summary>A vuln-scan source for one scope: its collector name and settings.</summary>
public sealed record ScanSource(string Name, IReadOnlyDictionary<string, string> Settings);

/// <summary>
/// The fictional Example Pty Ltd estate as its scanners see it, described once: internet-facing services scanned daily with Nessus,
/// laptops' applications through a Defender vulnerability management export, other applications and operating systems scanned
/// fortnightly. <see cref="ScanDevWriter"/> writes it as exports. Nothing here is real scan data.
/// </summary>
public sealed record ScanDevEstate(IReadOnlyList<DevScan> Scans)
{
    public static readonly string Owner = "it@example.com";

    private static readonly string[] Laptops = ["laptop-01.example.com", "laptop-02.example.com", "laptop-03.example.com", "laptop-04.example.com"];

    public static ScanDevEstate Compliant { get; } = new(
    [
        new("internet_facing", "internet-facing", ScanFormat.Nessus, 0, ["web-01.example.com", "vpn.example.com", "mail.example.com"],
        [
            new("web-01.example.com", "900201", "Example Web Server 2.4.x < 2.4.63 Multiple Vulnerabilities", Severity.Medium, PatchDaysAgo: 3),
        ]),
        new("common_apps", "laptops", ScanFormat.Csv, 0, Laptops,
        [
            new("laptop-02.example.com", "CVE-2026-0302", "example-browser", Severity.High, FirstSeenDaysAgo: 5),
        ]),
        new("other_apps", "other-apps", ScanFormat.Nessus, 2, ["app-01.example.com", "file-01.example.com"],
        [
            new("app-01.example.com", "900211", "Example Backup Agent < 12.4 Multiple Vulnerabilities", Severity.Medium, PatchDaysAgo: 10),
        ]),
        new("operating_systems", "operating-systems", ScanFormat.Nessus, 1, [.. Laptops, "app-01.example.com", "file-01.example.com"],
        [
            new("file-01.example.com", "900221", "Example OS Cumulative Security Update Missing", Severity.High, PatchDaysAgo: 12),
        ]),
    ]);

    public static IReadOnlyList<Deviation> Deviations { get; } =
    [
        new(
            "internet-facing-scan-stale",
            "The internet-facing scan last ran three days ago, where it must run daily.",
            Expect(("vuln.internet_facing.scanned_in_interval", CheckOutcome.Fail)),
            e => e.Change("internet_facing", s => s with { ScannedDaysAgo = 3 })),
        new(
            "exploited-web-flaw-unpatched",
            "The web server's flaw has a public exploit and its patch came out six days ago, past the 48 hours.",
            Expect(("vuln.internet_facing.patched_in_window", CheckOutcome.Fail)),
            e => e.Change("internet_facing", s => s with { Findings = [.. s.Findings.Select(f => f.Host == "web-01.example.com" ? f with { Exploit = true, PatchDaysAgo = 6 } : f)] })),
        new(
            "unsupported-vpn-appliance",
            "The VPN appliance runs a version its vendor no longer supports.",
            Expect(("vuln.internet_facing.unsupported_removed", CheckOutcome.Fail)),
            e => e.Change("internet_facing", s => s with
            {
                Findings = [.. s.Findings, new("vpn.example.com", "900202", "Example VPN Appliance Unsupported Version Detection", Severity.Critical, Unsupported: true)],
            })),
        new(
            "browser-patch-overdue",
            "A browser flaw on a laptop was first seen twenty days ago, past the two weeks.",
            Expect(("vuln.common_apps.patched_in_window", CheckOutcome.Fail)),
            e => e.Change("common_apps", s => s with { Findings = [.. s.Findings.Select(f => f with { FirstSeenDaysAgo = 20 })] })),
        new(
            "os-update-overdue",
            "The file server's operating system update came out 45 days ago, past the month.",
            Expect(("vuln.operating_systems.patched_in_window", CheckOutcome.Fail)),
            e => e.Change("operating_systems", s => s with { Findings = [.. s.Findings.Select(f => f with { PatchDaysAgo = 45 })] })),
        new(
            "unsupported-laptop-os",
            "One laptop runs an operating system version its vendor no longer supports.",
            Expect(("vuln.operating_systems.unsupported_removed", CheckOutcome.Fail)),
            e => e.Change("operating_systems", s => s with
            {
                Findings = [.. s.Findings, new("laptop-04.example.com", "900222", "Example OS 10 Unsupported Version Detection", Severity.Critical, Unsupported: true)],
            })),
    ];

    /// <summary>The estate for a variant: <c>compliant</c>, or <c>gaps</c> with every deviation.</summary>
    public static ScanDevEstate For(string variant) =>
        variant == "gaps" ? Deviations.Aggregate(Compliant, (estate, deviation) => estate.With(deviation)) : Compliant;

    public ScanDevEstate With(Deviation deviation)
    {
        ArgumentNullException.ThrowIfNull(deviation);
        return deviation.Apply(this);
    }

    /// <summary>The four vuln-scan sources, with their drop folders under <paramref name="root"/>.</summary>
    public IReadOnlyList<ScanSource> Sources(string root) => [.. Scans.Select(s => new ScanSource($"{s.Folder}-scans", Settings(s, $"{root.TrimEnd('/')}/{s.Folder}")))];

    private ScanDevEstate Change(string scope, Func<DevScan, DevScan> change) => this with { Scans = [.. Scans.Select(s => s.Scope == scope ? change(s) : s)] };

    private static Dictionary<string, string> Settings(DevScan scan, string folder)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [VulnScanSettings.DropFolder] = folder,
            [VulnScanSettings.Format] = scan.Format == ScanFormat.Nessus ? "nessus" : "csv",
            [VulnScanSettings.Scope] = scan.Scope,
            [VulnScanSettings.Owner] = Owner,
        };
        if (scan.Format == ScanFormat.Csv)
        {
            foreach (var (key, value) in ScanExports.DefenderColumns)
            {
                settings[key] = value;
            }
        }

        return settings;
    }

    private static Dictionary<string, CheckOutcome> Expect(params (string Check, CheckOutcome Outcome)[] fails) =>
        fails.ToDictionary(f => f.Check, f => f.Outcome, StringComparer.Ordinal);
}
