using EvidenceHound.Core.Reporting;
using EvidenceHound.Core.Sprs;
using EvidenceHound.Core.Tests.Ssp;

namespace EvidenceHound.Core.Tests.Sprs;

/// <summary>
/// s17-06: the yearly affirmation pack gathers what the affirming official reads before affirming continuing compliance (32 CFR 170.22):
/// the SPRS score, the open POA&amp;M items with their dates, evidence freshness and lapsed manual sources. It decides nothing.
/// </summary>
public class AffirmationPackTests
{
    private static readonly DateOnly Today = new(2026, 12, 2);

    [Fact]
    public void Lists_open_poam_items_and_lapsed_sources()
    {
        var plan = Nist171SspTests.Example();

        var pack = AffirmationPack.Markdown(plan, Nist171SspTests.Methodology(), Posture(), Today);

        pack.ShouldStartWith("# Annual affirmation pack: cmmc-level-2, 2026-12-02\n");
        pack.ShouldContain("SPRS score: 97 (from 110, NIST SP 800-171 DoD Assessment Methodology 1.2.1).");
        pack.ShouldContain("| AC.L1-3.1.1 | Not implemented | AC-MFA | it@example.com | no date | not allowed on a CMMC POA&M |\n");
        pack.ShouldContain("| AC.L2-3.1.3 | Planned | AC-FLOW | it@example.com | 2026-12-31 | - |\n");
        pack.ShouldContain("| example-m365 | 2026-12-01 | succeeded | fresh |\n");
        pack.ShouldContain("| example-backup-restore-test | 2026-10-01 | succeeded | stale |\n");
        pack.ShouldContain("| EV-BACKUP-RESTORE: Daily backups | Backup owners | 2026-11-15 | 17 |\n");
        pack.ShouldNotContain("EV-MFA-EXCEPTIONS");
        pack.ShouldContain("Record the affirmation in the weekly review");
    }

    [Fact]
    public void Says_so_when_nothing_is_open_or_lapsed()
    {
        var plan = Nist171SspTests.Example() with { Requirements = [], PoamNotAllowed = new Dictionary<string, string>() };
        var posture = Posture() with { Due = [], Collectors = [] };

        var pack = AffirmationPack.Markdown(plan, Nist171SspTests.Methodology(), posture, Today);

        pack.ShouldContain("No open POA&M items.");
        pack.ShouldContain("No collectors in the snapshot.");
        pack.ShouldContain("No lapsed manual sources.");
    }

    private static PostureSnapshot Posture() => new(
        new ReportingPeriod(PeriodKind.Month, 2026, 11),
        new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero),
        [],
        [],
        [],
        [],
        [],
        [],
        [
            new CollectorHealth("example-m365", new DateTimeOffset(2026, 12, 1, 2, 0, 0, TimeSpan.Zero), "succeeded", 10, 0, []),
            new CollectorHealth("example-backup-restore-test", new DateTimeOffset(2026, 10, 1, 2, 0, 0, TimeSpan.Zero), "succeeded", 1, 0, []),
        ],
        [
            new DueItem(DueKind.ManualEvidence, "EV-BACKUP-RESTORE: Daily backups", "Backup owners", new DateOnly(2026, 11, 15), "pending"),
            new DueItem(DueKind.ManualEvidence, "EV-MFA-EXCEPTIONS: Multi-factor authentication", "IT team", new DateOnly(2026, 11, 1), "completed"),
            new DueItem(DueKind.SupplierReview, "Example Cloud Pty Ltd review", "IT team", new DateOnly(2026, 11, 1), "pending"),
        ]);
}
