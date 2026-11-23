using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceHound.Collectors.VulnScan.Tests;

public sealed class VulnScanCollectorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 2, 0, 0, TimeSpan.Zero);

    private readonly string _drop = Directory.CreateTempSubdirectory("hound-vuln-scan-tests-").FullName;

    [Fact]
    public async Task Nessus_export_becomes_the_scan_and_its_evidence()
    {
        Drop(Fixtures.Nessus, Fixtures.Bytes(Fixtures.Nessus), Now.AddDays(-7));
        Drop("notes.txt", Encoding.UTF8.GetBytes("not a scan"), Now.AddDays(-1));

        var result = (await CollectAsync(Settings())).Value();

        result.Checks.Select(c => (c.CheckId.Value, c.Outcome)).ShouldBe([
            ("vuln.internet_facing.scanned_in_interval", CheckOutcome.Fail),
            ("vuln.internet_facing.patched_in_window", CheckOutcome.Fail),
            ("vuln.internet_facing.unsupported_removed", CheckOutcome.Fail),
        ]);
        result.Checks.ShouldAllBe(c => c.Source == "vuln-example" && c.CollectedAt == Now);
        result.Files.Select(f => f.FileName).ShouldBe([Fixtures.Nessus, VulnScanCollector.ScanFile, VulnScanCollector.HistoryFile, PatchChecks.AssessmentFile]);
        var scan = Json(result, VulnScanCollector.ScanFile);
        scan["scope"]!.GetValue<string>().ShouldBe("internet_facing");
        scan["owner"]!.GetValue<string>().ShouldBe("Sam Example");
        scan["file"]!.GetValue<string>().ShouldBe(Fixtures.Nessus);
        scan["scan_date"]!.GetValue<string>().ShouldBe("2026-11-16T10:25:00+00:00");
        scan["hosts"]!.AsArray().Count.ShouldBe(3);
        var first = scan["findings"]![0]!;
        first["host"]!.GetValue<string>().ShouldBe("web-01.example.com");
        first["severity"]!.GetValue<string>().ShouldBe("high");
        first["exploit_available"]!.GetValue<bool>().ShouldBeTrue();
        first["patch_published"]!.GetValue<string>().ShouldBe("2026-11-10T00:00:00+00:00");
        first["first_seen"].ShouldBeNull();
        var assessment = Json(result, PatchChecks.AssessmentFile);
        assessment["overdue"]!.AsArray().Select(o => o!["finding_id"]!.GetValue<string>()).ShouldBe(["900101", "900103"]);
        assessment["overdue"]![0]!["counted_from"]!.GetValue<string>().ShouldBe("patch_published");
        assessment["overdue"]![0]!["window_hours"]!.GetValue<int>().ShouldBe(48);
        assessment["unsupported"]!.AsArray().Select(o => o!["host"]!.GetValue<string>()).ShouldBe(["vpn.example.com"]);
    }

    [Fact]
    public async Task Windows_and_interval_can_be_set()
    {
        Drop(Fixtures.Nessus, Fixtures.Bytes(Fixtures.Nessus), Now.AddDays(-7));
        var settings = Settings();
        settings[VulnScanSettings.ScanIntervalDays] = "7";
        settings[VulnScanSettings.PatchWindowDays] = "30";
        settings[VulnScanSettings.ExploitedPatchWindowHours] = "720";

        var result = (await CollectAsync(settings)).Value();

        result.Checks.Select(c => c.Outcome).ShouldBe([CheckOutcome.Pass, CheckOutcome.Fail, CheckOutcome.Fail]);
        Json(result, PatchChecks.AssessmentFile)["overdue"]!.AsArray().Select(o => o!["finding_id"]!.GetValue<string>()).ShouldBe(["900103"]);
    }

    [Fact]
    public async Task Policy_settings_must_be_whole_numbers_above_zero()
    {
        var settings = Settings();
        settings[VulnScanSettings.ScanIntervalDays] = "0";
        settings[VulnScanSettings.ExploitedPatchWindowHours] = "two days";

        (await CollectAsync(settings)).Problems().Select(p => p.Message).ShouldBe([
            "Setting 'scan_interval_days' must be a whole number greater than zero, not '0'.",
            "Setting 'exploited_patch_window_hours' must be a whole number greater than zero, not 'two days'.",
        ]);
    }

    [Fact]
    public async Task Older_exports_are_listed_as_history()
    {
        Drop("internet-facing-2026-11-09.nessus", Encoding.UTF8.GetBytes(Fixtures.Text(Fixtures.Nessus).Replace("Nov 16", "Nov 09", StringComparison.Ordinal).Replace("HOST_END_TIMESTAMP", "IGNORED", StringComparison.Ordinal)), Now.AddDays(-14));
        Drop(Fixtures.Nessus, Fixtures.Bytes(Fixtures.Nessus), Now.AddDays(-7));

        var result = (await CollectAsync(Settings())).Value();

        Json(result, VulnScanCollector.ScanFile)["file"]!.GetValue<string>().ShouldBe(Fixtures.Nessus);
        var exports = Json(result, VulnScanCollector.HistoryFile)["exports"]!.AsArray();
        exports.Select(e => e!["file"]!.GetValue<string>()).ShouldBe([Fixtures.Nessus, "internet-facing-2026-11-09.nessus"]);
        exports[1]!["scan_date"]!.GetValue<string>().ShouldBe("2026-11-09T10:25:00+00:00");
        exports[1]!["findings"]!.GetValue<int>().ShouldBe(3);
        exports[1]!["sha256"]!.GetValue<string>().Length.ShouldBe(64);
    }

    [Fact]
    public async Task Defender_csv_reads_through_the_column_map()
    {
        Drop(Fixtures.Defender, Fixtures.Bytes(Fixtures.Defender), Now.AddDays(-3));

        var result = (await CollectAsync(DefenderSettings())).Value();

        var scan = Json(result, VulnScanCollector.ScanFile);
        scan["scope"]!.GetValue<string>().ShouldBe("common_apps");
        scan["findings"]!.AsArray().Count.ShouldBe(3);
        result.Files[0].MediaType.ShouldBe("text/csv");
    }

    [Fact]
    public async Task Empty_drop_folder_has_no_scan_yet()
    {
        var result = (await CollectAsync(Settings())).Value();

        result.Files.Select(f => f.FileName).ShouldBe([VulnScanCollector.HistoryFile]);
        result.Checks.Select(c => c.Outcome).ShouldBe([CheckOutcome.Fail, CheckOutcome.Error, CheckOutcome.Error]);
        Json(result, VulnScanCollector.HistoryFile)["exports"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Malformed_export_fails_the_run_naming_file_and_line()
    {
        Drop(Fixtures.Nessus, Fixtures.Bytes(Fixtures.Nessus), Now.AddDays(-7));
        Drop("broken.nessus", Encoding.UTF8.GetBytes("<NessusClientData_v2>\n<Report>\n"), Now.AddDays(-1));

        var problem = (await CollectAsync(Settings())).Problems().ShouldHaveSingleItem();

        problem.Code.ShouldBe("vuln_scan.export_invalid");
        problem.Message.ShouldStartWith("broken.nessus line ");
    }

    [Fact]
    public async Task Missing_drop_folder_fails()
    {
        var settings = Settings();
        settings[VulnScanSettings.DropFolder] = Path.Combine(_drop, "nowhere");

        (await CollectAsync(settings)).Problems().ShouldHaveSingleItem().Code.ShouldBe("vuln_scan.drop_folder_missing");
    }

    [Fact]
    public async Task Every_bad_setting_is_reported_at_once()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [VulnScanSettings.DropFolder] = _drop,
            [VulnScanSettings.Format] = "pdf",
            [VulnScanSettings.Scope] = "everything",
        };

        var problems = (await CollectAsync(settings)).Problems();

        problems.Select(p => p.Message).ShouldBe([
            "Setting 'format' must be nessus or csv, not 'pdf'.",
            "Setting 'scope' must be one of internet_facing, common_apps, other_apps, operating_systems, not 'everything'.",
            "Setting 'owner' is required.",
        ]);
    }

    [Fact]
    public async Task Csv_needs_the_host_finding_and_severity_columns()
    {
        var settings = DefenderSettings();
        settings.Remove("column_host");
        settings.Remove("column_severity");

        var problems = (await CollectAsync(settings)).Problems();

        problems.Select(p => p.Message).ShouldBe(["Setting 'column_host' is required when format is csv.", "Setting 'column_severity' is required when format is csv."]);
        problems.ShouldAllBe(p => p.Code == "vuln_scan.setting_missing");
    }

    [Fact]
    public void Describes_all_twelve_checks_and_an_instance_emits_its_scope_s_three()
    {
        var description = new VulnScanCollector().Describe();

        description.Checks.Count.ShouldBe(12);
        description.ChecksFrom!(new CollectorSettings(Settings())).ShouldBe(PatchChecks.Ids("internet_facing"));
    }

    [Fact]
    public void Describes_every_setting_and_the_column_map()
    {
        var names = new VulnScanCollector().Describe().Settings.Select(s => s.Name).ToArray();

        names.ShouldBe([
            "drop_folder", "format", "scope", "owner", "scan_interval_days", "patch_window_days", "exploited_patch_window_hours",
            "column_host", "column_finding_id", "column_severity", "column_name", "column_cve", "column_first_seen",
            "column_patch_published", "column_exploit_available", "column_unsupported", "column_scan_date",
        ]);
    }

    public void Dispose() => Directory.Delete(_drop, recursive: true);

    private Dictionary<string, string> Settings() => new(StringComparer.Ordinal)
    {
        [VulnScanSettings.DropFolder] = _drop,
        [VulnScanSettings.Format] = "nessus",
        [VulnScanSettings.Scope] = "internet_facing",
        [VulnScanSettings.Owner] = "Sam Example",
    };

    private Dictionary<string, string> DefenderSettings() => new(StringComparer.Ordinal)
    {
        [VulnScanSettings.DropFolder] = _drop,
        [VulnScanSettings.Format] = "csv",
        [VulnScanSettings.Scope] = "common_apps",
        [VulnScanSettings.Owner] = "Sam Example",
        ["column_host"] = "DeviceName",
        ["column_finding_id"] = "CveId",
        ["column_severity"] = "VulnerabilitySeverityLevel",
        ["column_name"] = "SoftwareName",
        ["column_cve"] = "CveId",
        ["column_first_seen"] = "FirstSeenTimestamp",
        ["column_exploit_available"] = "ExploitabilityLevel",
        ["column_scan_date"] = "LastSeenTimestamp",
    };

    private void Drop(string name, byte[] content, DateTimeOffset written)
    {
        var path = Path.Combine(_drop, name);
        File.WriteAllBytes(path, content);
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
    }

    private static JsonNode Json(CollectionResult result, string file)
    {
        using var stream = result.Files.Single(f => f.FileName == file).Open();
        return JsonNode.Parse(stream)!;
    }

    private static Task<Result<CollectionResult>> CollectAsync(Dictionary<string, string> settings) => new VulnScanCollector().CollectAsync(
        new CollectorContext("vuln-example", new CollectorSettings(settings), new Dictionary<string, Secret>(), new FixedClock(Now), NullLogger.Instance, new NoHttpClients()),
        TestContext.Current.CancellationToken);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class NoHttpClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("The vuln-scan collector makes no HTTP requests.");
    }
}
